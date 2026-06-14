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
        left_bottom_x = result.left_bottom_x
        left_bottom_y = result.left_bottom_y

        print(left_bottom_x, left_bottom_y)

        cx, cy, robot_angle = self.compute_robot_pose([left_bottom_x, left_bottom_y], angle, pcb_width, pcb_height)

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
        cv2.putText(image, f"Angle: {angle:.4f} -- X: {left_bottom_x:.4f} -- Y: {left_bottom_y:.4f}", (draw_x, draw_Y), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)

        return DataResponse(Result=True,
                            Score=result.score,
                            ResImg=self._convert_2_base64(image),
                            ImageX=left_bottom_x,
                            ImageY=left_bottom_y,
                            ImageAngle=angle,
                            RobotX=cx-self.offset_x,
                            RobotY=cy+self.offset_y,
                            RobotAngle=robot_angle
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


if __name__ == '__main__':
    pass