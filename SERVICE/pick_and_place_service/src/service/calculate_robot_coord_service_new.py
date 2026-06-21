import math
import cv2
import numpy as np
import base64
import json
import ast

from src.dtos.meta import DataResponse, ErrorCode
from src.service.base_service import BaseService


class CalculateRobotCoordService(BaseService):
    def __init__(self):
        super().__init__()

        self.templates = []
        self.offsets = []

        # Độ dài vector phụ trên ảnh để suy ra hướng/góc robot.
        # Không liên quan đơn vị robot, chỉ cần đủ dài để tránh nhiễu.
        self.direction_length_pixel = 100.0

    # ==========================================================
    # LOAD TEMPLATE
    # ==========================================================

    def update_templates(self, templates, offsets):
        self.templates = []
        self.offsets = []

        if templates is None or len(templates) == 0:
            return DataResponse(Result=False, Message="Template list is empty")

        if offsets is None or len(offsets) != len(templates):
            return DataResponse(Result=False, Message="Offset list invalid or not matching templates")

        for template in templates:
            if template is None:
                continue

            if len(template.shape) == 3:
                gray = cv2.cvtColor(template, cv2.COLOR_BGR2GRAY)
            else:
                gray = template.copy()

            self.templates.append(gray)

        for offset in offsets:
            self.offsets.append(offset)

        if len(self.templates) == 0:
            return DataResponse(Result=False, Message="No valid template loaded")

        return DataResponse(Result=True, Message="Load Templates Successfully!")

    # ==========================================================
    # BASIC UTILS
    # ==========================================================

    @staticmethod
    def _convert_2_base64(image):
        success, encoded_image = cv2.imencode(".png", image)

        if not success:
            return None

        image_bytes = encoded_image.tobytes()
        img_base64 = base64.b64encode(image_bytes).decode("utf-8")

        return img_base64

    @staticmethod
    def _normalize_angle(angle):
        while angle > 180.0:
            angle -= 360.0

        while angle <= -180.0:
            angle += 360.0

        return angle

    @staticmethod
    def _rotate_vector(x, y, angle_deg):
        theta = math.radians(angle_deg)

        cos_t = math.cos(theta)
        sin_t = math.sin(theta)

        rx = x * cos_t - y * sin_t
        ry = x * sin_t + y * cos_t

        return rx, ry

    def _transform_point_to_robot(self, point):
        """
        Wrapper cho self.calib.transform để tránh lỗi format.

        Input:
            point = [image_x, image_y]

        Output:
            robot_x, robot_y
        """

        if point is None or len(point) < 2:
            raise ValueError("Invalid image point")

        robot_point = self.calib.transform([float(point[0]), float(point[1])])

        robot_arr = np.array(robot_point, dtype=float).reshape(-1)

        if len(robot_arr) < 2:
            raise ValueError(f"Invalid calibration result: {robot_point}")

        return float(robot_arr[0]), float(robot_arr[1])

    # ==========================================================
    # OFFSET PARSER
    # ==========================================================

    @staticmethod
    def _try_parse_string_offset(offset):
        """
        Offset đôi khi từ C# gửi sang có thể là string JSON.
        Hàm này hỗ trợ:
            '{"OffsetX": 1, "OffsetY": 2, "OffsetRZ": 3}'
            "{'OffsetX': 1, 'OffsetY': 2, 'OffsetRZ': 3}"
        """

        if not isinstance(offset, str):
            return offset

        text = offset.strip()

        if text == "":
            raise ValueError("Offset string is empty")

        try:
            return json.loads(text)
        except Exception:
            pass

        try:
            return ast.literal_eval(text)
        except Exception:
            pass

        raise ValueError(f"Cannot parse offset string: {offset}")

    @staticmethod
    def _get_offset_value(offset, names, index):
        """
        Hỗ trợ offset dạng:
            dict:
                {"OffsetX": ..., "OffsetY": ..., "OffsetRZ": ...}

            object:
                offset.OffsetX, offset.OffsetY, offset.OffsetRZ

            list:
                [OffsetX, OffsetY, OffsetRZ]
        """

        if isinstance(offset, dict):
            for name in names:
                if name in offset:
                    return float(offset[name])

        for name in names:
            if hasattr(offset, name):
                return float(getattr(offset, name))

        if isinstance(offset, (list, tuple)) and len(offset) > index:
            return float(offset[index])

        raise ValueError(f"Cannot get offset value. Offset={offset}, names={names}, index={index}")

    def _parse_robot_offset_vector(self, robot_offset_vector):
        robot_offset_vector = self._try_parse_string_offset(robot_offset_vector)

        offset_x = self._get_offset_value(
            robot_offset_vector,
            ["OffsetX", "offsetX", "offset_x"],
            0
        )

        offset_y = self._get_offset_value(
            robot_offset_vector,
            ["OffsetY", "offsetY", "offset_y"],
            1
        )

        offset_rz = self._get_offset_value(
            robot_offset_vector,
            ["OffsetRZ", "offsetRZ", "offset_rz"],
            2
        )

        return offset_x, offset_y, offset_rz

    # ==========================================================
    # IMAGE ANGLE -> ROBOT ANGLE
    # ==========================================================

    def image_angle_to_robot_angle_by_calib(self, center_point, image_angle):
        """
        Tính góc object trong hệ robot bằng calibration.

        Không hardcode:
            robot_angle = image_angle
            robot_angle = 180 - image_angle

        Cách làm:
            1. Lấy center ảnh C.
            2. Tạo điểm hướng P theo image_angle trên ảnh.
            3. Transform C và P sang robot.
            4. atan2(P_robot - C_robot).
        """

        if center_point is None or len(center_point) < 2:
            raise ValueError("center_point invalid")

        cx = float(center_point[0])
        cy = float(center_point[1])

        theta = math.radians(float(image_angle))
        length = float(self.direction_length_pixel)

        direction_image_x = cx + length * math.cos(theta)
        direction_image_y = cy + length * math.sin(theta)

        center_robot_x, center_robot_y = self._transform_point_to_robot([cx, cy])
        direction_robot_x, direction_robot_y = self._transform_point_to_robot(
            [direction_image_x, direction_image_y]
        )

        dx = direction_robot_x - center_robot_x
        dy = direction_robot_y - center_robot_y

        if abs(dx) < 1e-9 and abs(dy) < 1e-9:
            raise ValueError("Cannot calculate robot angle because direction vector is zero")

        robot_angle = math.degrees(math.atan2(dy, dx))

        return self._normalize_angle(robot_angle)

    # ==========================================================
    # CORE CALCULATION
    # ==========================================================

    def compute_robot_pose(self, center_point, image_angle, robot_offset_vector):
        """
        center_point:
            Center object hiện tại detect được trên ảnh, đơn vị pixel.

        image_angle:
            Góc object hiện tại trên ảnh, degree.

        robot_offset_vector:
            Offset đã lưu từ C#.

            BẮT BUỘC phải là offset local:
                OffsetX: offset local X từ center object tới điểm gắp
                OffsetY: offset local Y từ center object tới điểm gắp
                OffsetRZ: robotRZ - objectRobotAngle

        return:
            pick_x, pick_y, robot_theta, object_robot_angle
        """

        if center_point is None or len(center_point) < 2:
            raise ValueError("center_point invalid")

        # 1. Center ảnh hiện tại -> robot
        center_robot_x, center_robot_y = self._transform_point_to_robot(center_point)

        # 2. Góc object hiện tại trong hệ robot
        object_robot_angle = self.image_angle_to_robot_angle_by_calib(
            center_point,
            image_angle
        )

        # 3. Lấy offset local từ dữ liệu C#
        offset_local_x, offset_local_y, offset_rz = self._parse_robot_offset_vector(
            robot_offset_vector
        )

        # 4. Xoay offset local sang hệ robot global
        dx_robot, dy_robot = self._rotate_vector(
            offset_local_x,
            offset_local_y,
            object_robot_angle
        )

        # 5. Tính tọa độ điểm gắp
        pick_x = center_robot_x + dx_robot
        pick_y = center_robot_y + dy_robot

        # 6. Tính robot RZ
        robot_theta = object_robot_angle + offset_rz
        robot_theta = self._normalize_angle(robot_theta)

        return pick_x, pick_y, robot_theta, object_robot_angle

    # ==========================================================
    # MAIN API
    # ==========================================================

    def cal_robot_coord(self, image):
        if image is None:
            return DataResponse(Result=False, Message="Image is None")

        if len(self.templates) == 0:
            return DataResponse(Result=False, Message="No template loaded")

        if len(self.offsets) != len(self.templates):
            return DataResponse(Result=False, Message="Template and offset count not matching")

        if len(image.shape) == 3:
            img_gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
            draw_image = image.copy()
        else:
            img_gray = image.copy()
            draw_image = cv2.cvtColor(image, cv2.COLOR_GRAY2BGR)

        matches = []
        temp_idx = -1

        for i, template in enumerate(self.templates):
            cur_matches = self.image_matcher.match(img_gray, template)

            if cur_matches is None or len(cur_matches) == 0:
                continue

            matches = cur_matches
            temp_idx = i
            break

        if len(matches) == 0 or temp_idx < 0:
            return DataResponse(Result=False, Message="Không tìm thấy PCB / template")

        robot_offset_vector = self.offsets[temp_idx]
        result = matches[0]

        angle = float(result.angle)
        center_x = float(result.center_x)
        center_y = float(result.center_y)

        try:
            pick_x, pick_y, robot_angle, object_robot_angle = self.compute_robot_pose(
                [center_x, center_y],
                angle,
                robot_offset_vector
            )
        except Exception as ex:
            return DataResponse(
                Result=False,
                Message=f"Compute robot pose error: {str(ex)}"
            )

        # ======================================================
        # DRAW RESULT
        # ======================================================

        polygon = np.array(
            [
                [result.left_top_x, result.left_top_y],
                [result.right_top_x, result.right_top_y],
                [result.right_bottom_x, result.right_bottom_y],
                [result.left_bottom_x, result.left_bottom_y],
            ],
            dtype=np.int32
        )

        cv2.polylines(draw_image, [polygon], True, (0, 255, 0), 2)

        cv2.circle(
            draw_image,
            (int(center_x), int(center_y)),
            5,
            (0, 0, 255),
            -1
        )

        draw_x = int(np.min(polygon[:, 0]))
        draw_y = int(np.min(polygon[:, 1]))

        draw_x = max(draw_x - 50, 0)
        draw_y = max(draw_y - 60, 30)

        score = float(result.score)

        cv2.putText(
            draw_image,
            f"Score: {score:.4f}",
            (draw_x, draw_y),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.8,
            (0, 255, 0),
            2
        )

        cv2.putText(
            draw_image,
            f"ImgX: {center_x:.3f} ImgY: {center_y:.3f} ImgA: {angle:.3f}",
            (draw_x, draw_y + 35),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.8,
            (0, 255, 0),
            2
        )

        cv2.putText(
            draw_image,
            f"ObjRobotA: {object_robot_angle:.3f}",
            (draw_x, draw_y + 70),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.8,
            (0, 255, 0),
            2
        )

        cv2.putText(
            draw_image,
            f"RobotX: {pick_x:.3f} RobotY: {pick_y:.3f} RZ: {robot_angle:.3f}",
            (draw_x, draw_y + 105),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.8,
            (0, 255, 0),
            2
        )

        return DataResponse(
            Result=True,
            Message="Calculate robot coordinate successfully",
            Score=score,
            ResImg=self._convert_2_base64(draw_image),
            ImageX=center_x,
            ImageY=center_y,
            ImageAngle=angle,
            RobotX=pick_x,
            RobotY=pick_y,
            RobotAngle=robot_angle
        )


if __name__ == "__main__":
    pass