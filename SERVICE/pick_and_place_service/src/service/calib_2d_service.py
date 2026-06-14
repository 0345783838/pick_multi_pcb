import math
import time
import cv2
import numpy as np
from src.dtos.meta import DataResponse, ErrorCode, Calib2DResponse
from src.service.base_service import BaseService
import base64
import ast


class Calib2dService(BaseService):
    def __init__(self):
        super().__init__()
        pass

    @staticmethod
    def _convert_2_base64(image):
        success, encoded_image = cv2.imencode('.png', image)
        if not success:
            return None
        image_bytes = encoded_image.tobytes()
        img_base64 = base64.b64encode(image_bytes).decode("utf-8")

        return img_base64

    @staticmethod
    def _get_box_centers(boxes):
        centers = []
        for box in boxes:
            x_center = (box[0] + box[2]) / 2
            y_center = (box[1] + box[3]) / 2
            centers.append([x_center, y_center])
        return np.array(centers)

    @staticmethod
    def euclidean_distance(pointA, pointB):
        return np.linalg.norm(pointA - pointB)

    def calib_2d(self, data):
        M, inliers = self.calib.fit(data.pixel_points, data.robot_points)
        if M is None:
            return Calib2DResponse(Result=False)

        return Calib2DResponse(Result=True)

    def transform(self, pixel_point):
        if self.calib.matrix is None:
            return Calib2DResponse(Result=False,
                                   Message="Calibration not found")

        result = self.calib.transform(pixel_point.pixel)
        return Calib2DResponse(Result=True,
                               RobotX=result[0],
                               RobotY=result[1],
                               Message="OK")

    def save_matrix(self):
        res, message = self.calib.save(self.calib_file_path)
        if not res:
            return Calib2DResponse(Result=False, Message=message)
        return Calib2DResponse(Result=True)

    def load_matrix(self):
        res, message = self.calib.load(self.calib_file_path)
        if not res:
            return Calib2DResponse(Result=False, Message=message)
        return Calib2DResponse(Result=True)

    def check_calib_ready(self):
        return Calib2DResponse(Result=self.calib.is_calibrated())


if __name__ == '__main__':
    pass
