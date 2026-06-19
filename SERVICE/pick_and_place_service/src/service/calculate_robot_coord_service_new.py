import math
import time
import cv2
import numpy as np
from src.dtos.meta import DataResponse, ErrorCode
from src.service.base_service import BaseService
import base64
import ast


class CalculateRobotCoordService(BaseService):
    def __init__(self):
        super().__init__()
        self.templates = []
        pass

    def update_templates(self, templates):
        self.templates = []
        for template in templates:
            gray = cv2.cvtColor(template, cv2.COLOR_BGR2GRAY)
            self.templates.append(gray)

        return DataResponse(Result=True, Message='Load Templates Successfully!')

    @staticmethod
    def _convert_2_base64(image):
        success, encoded_image = cv2.imencode('.png', image)
        if not success:
            return None
        image_bytes = encoded_image.tobytes()
        img_base64 = base64.b64encode(image_bytes).decode("utf-8")

        return img_base64

    # def cal_robot_coord(self, image, pcb_width, pcb_height):
    #     img_gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    #     matches = self.image_matcher.match(img_gray, self.template)
    #     if len(matches) == 0:
    #         matches = self.image_matcher.match(img_gray, self.template_2)
    #         if len(matches) == 0:
    #             return DataResponse(Result=False, Message='Không tìm thấy góc PCB')
    #
    #     result = matches[0]
    #
    #     angle = result.angle
    #     left_bottom_x = result.left_bottom_x
    #     left_bottom_y = result.left_bottom_y
    #
    #     print(left_bottom_x, left_bottom_y)
    #
    #     cx, cy, robot_angle = self.compute_robot_pose([left_bottom_x, left_bottom_y], angle, pcb_width, pcb_height)
    #
    #     cv2.polylines(image, [np.array(
    #         [[result.left_top_x, result.left_top_y], [result.right_top_x, result.right_top_y],
    #          [result.right_bottom_x, result.right_bottom_y], [result.left_bottom_x, result.left_bottom_y]], np.int32)],
    #                   True, (0, 255, 0), 1)
    #
    #
    #     # Get draw text pos
    #     if result.left_top_x < result.right_top_x:
    #         draw_x = int(result.left_top_x) - 100
    #     else:
    #         draw_x = int(result.right_top_x) - 100
    #
    #     if result.left_top_y < result.right_top_y:
    #         draw_Y = int(result.left_top_y) - 50
    #     else:
    #         draw_Y = int(result.right_top_y) - 50
    #
    #     cv2.putText(image, f"Score: {result.score:.4f}", (draw_x, draw_Y-50), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
    #     cv2.putText(image, f"Angle: {angle:.4f} -- X: {left_bottom_x:.4f} -- Y: {left_bottom_y:.4f}", (draw_x, draw_Y), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
    #
    #     return DataResponse(Result=True,
    #                         Score=result.score,
    #                         ResImg=self._convert_2_base64(image),
    #                         ImageX=left_bottom_x,
    #                         ImageY=left_bottom_y,
    #                         ImageAngle=angle,
    #                         RobotX=cx-self.offset_x,
    #                         RobotY=cy+self.offset_y,
    #                         RobotAngle=robot_angle
    #                         )

    def cal_robot_coord(self, image, pcb_width, pcb_height):
        img_gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)

        matches = []
        for template in self.templates:
            matches = self.image_matcher.match(img_gray, template)
            if len(matches) == 0:
                continue

        if len(matches) == 0:
            return DataResponse(Result=False, Message='Không tìm thấy góc PCB')

        result = matches[0]

        angle = result.angle
        bottom_left_x = result.left_bottom_x
        bottom_left_y = result.left_bottom_y

        print(bottom_left_x, bottom_left_y)

        cx, cy, robot_angle = self.compute_robot_pose_from_bottom_left([bottom_left_x, bottom_left_y], angle, pcb_width, pcb_height)

        cv2.polylines(image, [np.array(
            [[result.left_top_x, result.left_top_y], [result.right_top_x, result.right_top_y],
             [result.right_bottom_x, result.right_bottom_y], [result.left_bottom_x, result.left_bottom_y]], np.int32)],
                      True, (0, 255, 0), 1)


        # Get draw text pos
        if result.left_top_x < result.right_top_x:
            draw_x = int(result.left_top_x) - 100
        else:
            draw_x = int(result.right_top_x) - 100

        if result.left_top_y < result.right_top_y:
            draw_Y = int(result.left_top_y) - 50
        else:
            draw_Y = int(result.right_top_y) - 50

        cv2.putText(image, f"Score: {result.score:.4f}", (draw_x, draw_Y-50), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
        cv2.putText(image, f"Angle: {angle:.4f} -- X: {bottom_left_x:.4f} -- Y: {bottom_left_y:.4f}", (draw_x, draw_Y), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)

        target_x = cx - self.offset_x
        target_y = cy + self.offset_y

        tcp_x, tcp_y = self.compensate_tool_offset(
            target_x,
            target_y,
            robot_angle
        )

        return DataResponse(Result=True,
                            Score=result.score,
                            ResImg=self._convert_2_base64(image),
                            ImageX=bottom_left_x,
                            ImageY=bottom_left_y,
                            ImageAngle=angle,
                            RobotX=tcp_x,
                            RobotY=tcp_y,
                            RobotAngle=robot_angle + self.tool_offset_rz
                            )

    def compute_robot_pose(self, corner, theta_deg, pcb_w, pcb_h):
        corner_robot = self.calib.transform(corner)

        theta = math.radians(theta_deg)

        dx = pcb_w / 2
        dy = pcb_h / 2

        dx_r = dx * math.cos(theta) - dy * math.sin(theta)
        dy_r = dx * math.sin(theta) + dy * math.cos(theta)

        center_x = corner_robot[0] + dx_r
        center_y = corner_robot[1] + dy_r

        robot_theta = theta_deg

        return center_x, center_y, robot_theta

    def compute_robot_pose_from_top_right(self, corner, theta_deg, pcb_w, pcb_h):
        corner_robot = self.calib.transform(corner)

        theta = math.radians(theta_deg)

        # Vector từ TOP_RIGHT về tâm PCB
        dx = -pcb_w / 2
        dy = -pcb_h / 2

        dx_r = dx * math.cos(theta) - dy * math.sin(theta)
        dy_r = dx * math.sin(theta) + dy * math.cos(theta)

        center_x = corner_robot[0] + dx_r
        center_y = corner_robot[1] + dy_r

        robot_theta = theta_deg

        return center_x, center_y, robot_theta

    def compute_robot_pose_from_bottom_left(self, corner, theta_deg, pcb_w, pcb_h):
        corner_robot = self.calib.transform(corner)

        theta = math.radians(theta_deg)

        # Vector từ BOTTOM_LEFT về tâm PCB
        dx = -pcb_w/2
        dy = -pcb_h/2

        dx_r = dx * math.cos(theta) - dy * math.sin(theta)
        dy_r = dx * math.sin(theta) + dy * math.cos(theta)

        center_x = corner_robot[0] + dx_r
        center_y = corner_robot[1] + dy_r

        robot_theta = theta_deg

        return center_x, center_y, robot_theta

    def compensate_tool_offset(self, target_x, target_y, robot_angle_deg):
        """
        target_x, target_y: tọa độ điểm cần gắp thực tế, ví dụ tâm PCB
        robot_angle_deg: góc robot/tool đang xoay
        tool_offset_x: khoảng lệch từ TCP tới điểm gắp theo chiều dài tool
        tool_offset_y: khoảng lệch theo chiều rộng tool, trường hợp này = 0

        Trả về tọa độ TCP cần chạy tới.
        """
        theta = np.deg2rad(robot_angle_deg)

        offset_robot_x = self.tool_offset_x * np.cos(theta) - self.tool_offset_y * np.sin(theta)
        offset_robot_y = self.tool_offset_x * np.sin(theta) + self.tool_offset_y * np.cos(theta)

        tcp_x = target_x - offset_robot_x
        tcp_y = target_y - offset_robot_y

        return tcp_x, tcp_y


if __name__ == '__main__':
    pass