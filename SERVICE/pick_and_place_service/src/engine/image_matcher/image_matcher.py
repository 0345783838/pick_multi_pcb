import ctypes
import time
from typing import List
import numpy as np
import cv2
from pydantic import BaseModel
import os


class MatchResult(ctypes.Structure):
    _fields_ = [
        ('leftTopX', ctypes.c_double),
        ('leftTopY', ctypes.c_double),
        ('leftBottomX', ctypes.c_double),
        ('leftBottomY', ctypes.c_double),
        ('rightTopX', ctypes.c_double),
        ('rightTopY', ctypes.c_double),
        ('rightBottomX', ctypes.c_double),
        ('rightBottomY', ctypes.c_double),
        ('centerX', ctypes.c_double),
        ('centerY', ctypes.c_double),
        ('angle', ctypes.c_double),
        ('score', ctypes.c_double)
    ]


class MatchResultPt(BaseModel):
    left_top_x: float
    left_top_y: float
    left_bottom_x: float
    left_bottom_y: float
    right_top_x: float
    right_top_y: float
    right_bottom_x: float
    right_bottom_y: float
    center_x: float
    center_y: float
    angle: float
    score: float


# 定义Matcher类
class ImageMatcher:
    def __init__(self, dll_path, max_count: int = 10, score_threshold: float = 0.5, iou_threshold: float = 0.4,
                 angle: int = 0, min_area: int = 256, scale: list = [0.3, 2.5, 0.1], sure_match_theshold: float = 0.9):
        lib_path = os.path.dirname(dll_path)
        os.environ["PATH"] = lib_path + ";" + os.environ["PATH"]
        self.lib = ctypes.CDLL(dll_path)
        self.lib.matcher.argtypes = [ctypes.c_int, ctypes.c_float, ctypes.c_float, ctypes.c_float, ctypes.c_float]
        self.lib.matcher.restype = ctypes.c_void_p
        self.lib.setTemplate.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ubyte), ctypes.c_int, ctypes.c_int,
                                         ctypes.c_int]
        self.lib.match.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ubyte), ctypes.c_int, ctypes.c_int,
                                   ctypes.c_int, ctypes.POINTER(MatchResult), ctypes.c_int]

        if max_count <= 0:
            raise ValueError("max_count must be greater than 0")
        self.max_count = max_count
        self.score_threshold = score_threshold
        self.iou_threshold = iou_threshold
        self.angle = angle
        self.min_area = min_area
        self.matcher = self.lib.matcher(max_count, score_threshold, iou_threshold, angle, min_area)
        self.scale = np.arange(scale[0], scale[1] + scale[2], scale[2])
        self.scale = self._sort_scale(self.scale)
        self.sure_match_theshold = sure_match_theshold

        self.results = (MatchResult * self.max_count)()

    @staticmethod
    def _sort_scale(scales):
        scales = np.round(scales, 2)
        arr_sorted = np.sort(scales)  # Sắp xếp toàn bộ mảng
        arr_desc = arr_sorted[arr_sorted <= 1.0][::-1]  # Phần nhỏ hơn 1.0, sắp xếp giảm dần
        arr_asc = arr_sorted[arr_sorted > 1.0]  # Phần lớn hơn hoặc bằng 1.0, sắp xếp tăng dần

        # Trộn xen kẽ hai danh sách
        new_arr = []
        len_desc, len_asc = len(arr_desc), len(arr_asc)

        # Trộn xen kẽ
        for i in range(max(len_desc, len_asc)):
            if i < len_desc:
                new_arr.append(arr_desc[i])  # Thêm từ mảng giảm dần
            if i < len_asc:
                new_arr.append(arr_asc[i])  # Thêm từ mảng tăng dần

        # Chuyển kết quả thành numpy array
        new_arr = np.array(new_arr)
        # new_arr = arr_sorted
        return new_arr

    def set_template(self, image):
        height, width = image.shape[0], image.shape[1]
        channels = 1
        data = image.ctypes.data_as(ctypes.POINTER(ctypes.c_ubyte))
        return self.lib.setTemplate(self.matcher, data, width, height, channels)

    def match(self, image, template):
        image_cp = image.copy()
        template_cp = template.copy()
        if image_cp.ndim == 3:
            if image_cp.shape[2] == 3:
                image_cp = cv2.cvtColor(image_cp, cv2.COLOR_BGR2GRAY)
            elif image_cp.shape[2] == 1:
                image_cp = image_cp[:, :, 0]
            else:
                raise ValueError("Invalid image_cp shape")
        if template_cp.ndim == 3:
            if template_cp.shape[2] == 3:
                template_cp = cv2.cvtColor(template_cp, cv2.COLOR_BGR2GRAY)
            elif template_cp.shape[2] == 1:
                template_cp = template_cp[:, :, 0]
            else:
                raise ValueError("Invalid template_cp shape")

        height, width = image_cp.shape[0], image_cp.shape[1]
        channels = 1
        data = image_cp.ctypes.data_as(ctypes.POINTER(ctypes.c_ubyte))
        temp_height, temp_width = template_cp.shape[0], template_cp.shape[1]

        sure_res = []
        accepted_res = []
        max_avg = 0
        for s in self.scale:
            if s == 1.0:
                self.set_template(template_cp)
                matches = self.lib.match(self.matcher, data, width, height, channels, self.results, self.max_count)
            else:
                new_width = int(temp_width * s)
                new_height = int(temp_height * s)
                if new_width > width or new_height > height:
                    continue
                new_template = cv2.resize(template_cp, (new_width, new_height), interpolation=cv2.INTER_LINEAR)
                self.set_template(new_template)
                matches = self.lib.match(self.matcher, data, width, height, channels, self.results, self.max_count)

            if matches > 0:
                score = 0
                taken_idx = []
                for idx in range(min(matches, self.max_count)):
                    match = self.results[idx]
                    if match.score >= self.sure_match_theshold:
                        match_ob = MatchResultPt(left_top_x=match.leftTopX,
                                                 left_top_y=match.leftTopY,
                                                 left_bottom_x=match.leftBottomX,
                                                 left_bottom_y=match.leftBottomY,
                                                 right_top_x=match.rightTopX,
                                                 right_top_y=match.rightTopY,
                                                 right_bottom_x=match.rightBottomX,
                                                 right_bottom_y=match.rightBottomY,
                                                 center_x=match.centerX,
                                                 center_y=match.centerY,
                                                 angle=match.angle,
                                                 score=match.score)
                        sure_res.append(match_ob)
                        self._fill_polygon_black(image_cp, [(match.leftTopX, match.leftTopY),
                                                         (match.rightTopX, match.rightTopY),
                                                         (match.rightBottomX, match.rightBottomY),
                                                         (match.leftBottomX, match.leftBottomY)])
                        data = image_cp.ctypes.data_as(ctypes.POINTER(ctypes.c_ubyte))
                        taken_idx.append(idx)
                    elif match.score >= self.score_threshold:
                        score += match.score

                if len(taken_idx) == min(matches, self.max_count):
                    continue
                cur_avg = score / (min(matches, self.max_count) - len(taken_idx))

                if cur_avg > max_avg:
                    max_avg = cur_avg
                    accepted_temp = []
                    for idx in range(min(matches, self.max_count)):
                        if idx in taken_idx:
                            continue

                        accepted_match = self.results[idx]
                        accepted_match_obj = MatchResultPt(left_top_x=accepted_match.leftTopX,
                                                           left_top_y=accepted_match.leftTopY,
                                                           left_bottom_x=accepted_match.leftBottomX,
                                                           left_bottom_y=accepted_match.leftBottomY,
                                                           right_top_x=accepted_match.rightTopX,
                                                           right_top_y=accepted_match.rightTopY,
                                                           right_bottom_x=accepted_match.rightBottomX,
                                                           right_bottom_y=accepted_match.rightBottomY,
                                                           center_x=accepted_match.centerX,
                                                           center_y=accepted_match.centerY,
                                                           angle=accepted_match.angle,
                                                           score=accepted_match.score)
                        accepted_temp.append(accepted_match_obj)
                    accepted_res = accepted_temp
            else:
                continue

        if len(sure_res) == 0:
            return accepted_res

        if len(accepted_res) == 0:
            return sure_res

        final_res = self._filter_accepted_res(sure_res, accepted_res)

        return final_res

    def _filter_accepted_res(self, sure_res: List[MatchResultPt], accepted_res: List[MatchResultPt]) -> List[MatchResultPt]:
        """
        Filter accepted_res with sure_res by iou
        :param sure_res:
        :param accepted_res:
        :return:
        """
        res = sure_res.copy()
        for accepted in accepted_res:
            is_accepted = False
            for sure in sure_res:
                iou = self.calc_iou(accepted, sure)
                if iou > 0.7:
                    is_accepted = True
                    break
            if not is_accepted:
                res.append(accepted)
        return res

    @staticmethod
    def calc_iou(box_a: MatchResultPt, box_b: MatchResultPt) -> float:
        """
        Calculate IoU of two boxes
        :param box_a:
        :param box_b:
        :return:
        """
        x1 = max(box_a.left_top_x, box_b.left_top_x)
        y1 = max(box_a.left_top_y, box_b.left_top_y)
        x2 = min(box_a.right_bottom_x, box_b.right_bottom_x)
        y2 = min(box_a.right_bottom_y, box_b.right_bottom_y)

        intersection_area = max(0, x2 - x1) * max(0, y2 - y1)

        box_a_area = (box_a.right_bottom_x - box_a.left_top_x) * (box_a.right_bottom_y - box_a.left_top_y)
        box_b_area = (box_b.right_bottom_x - box_b.left_top_x) * (box_b.right_bottom_y - box_b.left_top_y)

        iou = intersection_area / float(box_a_area + box_b_area - intersection_area)

        return iou

    @staticmethod
    def _fill_polygon_black(image, points):
        """
        Fills a polygon defined by the given points in the image with black color.

        :param image: Input image (numpy array)
        :param points: List of 4 points defining the polygon [(x1, y1), (x2, y2), (x3, y3), (x4, y4)]
        :return: Image with the specified polygon filled in black
        """
        # Create a mask of the same size as the image, initialized to zero
        mask = np.zeros_like(image, dtype=np.uint8)

        # Convert the list of points to a numpy array with coordinates as integers
        polygon = np.array(points, dtype=np.int32)

        # Fill the polygon on the mask with white color (255)
        cv2.fillPoly(mask, [polygon], (255, 255, 255))

        # Set the pixels of the image corresponding to the polygon to black
        image[np.where(mask == 255)] = 0

        return image


if __name__ == "__main__":
    max_count = 1
    score_threshold = 0.7
    iou_threshold = 0.4
    angle = 180
    min_area = 1000
    scale = [0.9, 1.1, 0.01]

    dll_path = r'F:\working\CORE\template_matching\config\dll\templatematching_ctype.dll'
    matcher = ImageMatcher(dll_path, max_count, score_threshold, iou_threshold, angle, min_area, scale)
    big_image = cv2.imread(r'C:\Users\CH Computer\Downloads\pcb\Image_20260305144732540.bmp',
                           cv2.IMREAD_GRAYSCALE)
    template = cv2.imread(r'F:\working\pick_and_place\pick_and_place\pick_and_place\SERVICE\pick_and_place_service\config\data\template.png', cv2.IMREAD_GRAYSCALE)
    # 设置模板
    time_st = time.time()
    matches = matcher.match(big_image, template)
    print(time.time() - time_st)
    if len(matches) == 0:
        print("ko match")
    else:
        for i in range(len(matches)):
            result = matches[i]
            if result.score > matcher.score_threshold:

                # Crop the matched region
                height, width = template.shape[:2]
                dst_pts = np.array([[0, 0], [width - 1, 0], [width - 1, height - 1], [0, height - 1]], dtype=np.float32)
                src_pts = np.array([[result.left_top_x, result.left_top_y], [result.right_top_x, result.right_top_y],
                                    [result.right_bottom_x, result.right_bottom_y],
                                    [result.left_bottom_x, result.left_bottom_y]], dtype=np.float32)

                M = cv2.getPerspectiveTransform(src_pts, dst_pts)
                dst = cv2.warpPerspective(big_image, M, (width, height))

                # Draw a rectangle around the matched region
                cv2.polylines(big_image, [np.array(
                    [[result.left_top_x, result.left_top_y], [result.right_top_x, result.right_top_y],
                     [result.right_bottom_x, result.right_bottom_y], [result.left_bottom_x, result.left_bottom_y]], np.int32)],
                              True, (0, 255, 0), 1)
                cv2.putText(big_image, str(result.score), (int(result.left_top_x), int(result.left_top_y)),
                            cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 1)

    print("OKE")
