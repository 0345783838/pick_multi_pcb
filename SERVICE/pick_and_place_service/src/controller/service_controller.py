import time
from fastapi import APIRouter, File, UploadFile, HTTPException, Form, Query
from fastapi.responses import StreamingResponse
from pydantic import BaseModel
from src.service.calculate_robot_coord_service import CalculateRobotCoordService
from src.service.calib_2d_service import Calib2dService
from typing import List
import numpy as np
import json
import cv2
import io


# diamond_router = APIRouter(dependencies=[Depends(check_auth)])
robot_router = APIRouter()
cal_robot_coord_service = CalculateRobotCoordService()
calib_2d_robot = Calib2dService()


class PointPairs(BaseModel):
    pixel_points: List[List[float]]
    robot_points: List[List[float]]


class PixelPoint(BaseModel):
    pixel: List[float]


class FilePath(BaseModel):
    path: str


class PcbSize(BaseModel):
    width: float
    height: float


@robot_router.post(path='/cal_robot_coord')
def cal_robot_coord(image: UploadFile = File(...), pcb_size: str = Form(...)):
    if not image.file:
        raise HTTPException(status_code=400, detail="Invalid input")

    pcb_size_json = PcbSize(**json.loads(pcb_size))
    pcb_width = pcb_size_json.width
    pcb_height = pcb_size_json.height

    img_str = image.file.read()
    if img_str is None or img_str == b'':
        # Cannot read image
        return HTTPException(status_code=400, detail="Invalid input")
    try:
        np_img = np.fromstring(img_str, np.uint8)
        img = cv2.imdecode(np_img, flags=1)
        if img is None:
            raise HTTPException(status_code=400, detail="Invalid input")
    except Exception as ex:
        # Cannot decode image
        raise HTTPException(status_code=400, detail="Invalid input")
    res = cal_robot_coord_service.cal_robot_coord(img, pcb_width, pcb_height)
    return res


@robot_router.post(path='/load_templates')
def load_templates(images: List[UploadFile] = File(...)):
    imgs = []
    for image in images:
        img_str = image.file.read()
        if img_str is None or img_str == b'':
            # Cannot read image
            return HTTPException(status_code=400, detail="Invalid input")
        try:
            np_img = np.fromstring(img_str, np.uint8)
            img = cv2.imdecode(np_img, flags=1)
            if img is None:
                raise HTTPException(status_code=400, detail="Invalid input")
            imgs.append(img)
        except Exception as ex:
            raise HTTPException(status_code=400, detail="Invalid input")

    res = cal_robot_coord_service.update_templates(imgs)
    return res

@robot_router.post(path='/calib_2d')
def calib_2d(data: PointPairs):
    res = calib_2d_robot.calib_2d(data)
    return res


@robot_router.post("/transform_pixel_to_robot")
def transform(pixel_point: PixelPoint):
    res = calib_2d_robot.transform(pixel_point)
    return res


@robot_router.post("/save_calib_matrix")
def save_calib_matrix():
    res = calib_2d_robot.save_matrix()
    return res


@robot_router.post("/load_calib_matrix")
def load_calib_matrix():
    res = calib_2d_robot.load_matrix()
    return res


@robot_router.get(path='/check_service_status')
def check_service_status():
    return {"status": "running"}


@robot_router.post("/check_calib_ready_status")
def check_calib_ready_status():
    res = calib_2d_robot.check_calib_ready()
    return res
