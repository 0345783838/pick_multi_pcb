import time
from fastapi import APIRouter, File, UploadFile, HTTPException, Form, Query
from fastapi.responses import StreamingResponse
from pydantic import BaseModel
from src.service.calculate_robot_coord_service_new import CalculateRobotCoordService
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

@robot_router.post(path='/cal_robot_coord')
def cal_robot_coord(image: UploadFile = File(...)):
    if not image.file:
        raise HTTPException(status_code=400, detail="Invalid input")

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
    res = cal_robot_coord_service.cal_robot_coord(img)
    return res


@robot_router.post(path='/load_templates')
async def load_templates(
    images: List[UploadFile] = File(...),
    offsets: str = Form(...)
):
    imgs = []

    # Parse offsets từ string JSON: "[[3,5,6],[2,6,8]]"
    try:
        offset_list = json.loads(offsets)
    except Exception:
        raise HTTPException(
            status_code=400,
            detail="Invalid offsets format. Expected JSON string like [[3,5,6],[2,6,8]]"
        )

    # Check offsets có đúng dạng list không
    if not isinstance(offset_list, list):
        raise HTTPException(
            status_code=400,
            detail="Offsets must be a list"
        )

    # Check số lượng ảnh và offset có khớp không
    if len(images) != len(offset_list):
        raise HTTPException(
            status_code=400,
            detail=f"Images count and offsets count not match. images={len(images)}, offsets={len(offset_list)}"
        )

    # Check mỗi offset phải có 3 giá trị: [x, y, rz]
    for i, offset in enumerate(offset_list):
        if not isinstance(offset, list) or len(offset) != 3:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid offset at index {i}. Expected [x, y, rz]"
            )

        try:
            offset_list[i] = [
                float(offset[0]),
                float(offset[1]),
                float(offset[2])
            ]
        except Exception:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid offset value at index {i}. Offset must be numeric"
            )

    # Decode images
    for i, image in enumerate(images):
        img_bytes = await image.read()

        if img_bytes is None or img_bytes == b'':
            raise HTTPException(
                status_code=400,
                detail=f"Invalid image at index {i}"
            )

        try:
            np_img = np.frombuffer(img_bytes, np.uint8)
            img = cv2.imdecode(np_img, cv2.IMREAD_COLOR)

            if img is None:
                raise HTTPException(
                    status_code=400,
                    detail=f"Cannot decode image at index {i}"
                )

            imgs.append(img)

        except HTTPException:
            raise

        except Exception:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid image input at index {i}"
            )

    # Truyền cả ảnh và offsets vào service
    res = cal_robot_coord_service.update_templates(imgs, offset_list)

    return res


@robot_router.post(path='/test_cal_robot_coord')
async def test_cal_robot_coord(
    image: UploadFile = File(...),
    templates: List[UploadFile] = File(...),
    offsets: str = Form(...)
):
    temp_imgs = []

    # Parse offsets từ string JSON: "[[3,5,6],[2,6,8]]"
    try:
        offset_list = json.loads(offsets)
    except Exception:
        raise HTTPException(
            status_code=400,
            detail="Invalid offsets format. Expected JSON string like [[3,5,6],[2,6,8]]"
        )

    # Check offsets có đúng dạng list không
    if not isinstance(offset_list, list):
        raise HTTPException(
            status_code=400,
            detail="Offsets must be a list"
        )

    # Check số lượng ảnh và offset có khớp không
    if len(templates) != len(offset_list):
        raise HTTPException(
            status_code=400,
            detail=f"Images count and offsets count not match. images={len(templates)}, offsets={len(offset_list)}"
        )

    # Check mỗi offset phải có 3 giá trị: [x, y, rz]
    for i, offset in enumerate(offset_list):
        if not isinstance(offset, list) or len(offset) != 3:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid offset at index {i}. Expected [x, y, rz]"
            )

        try:
            offset_list[i] = [
                float(offset[0]),
                float(offset[1]),
                float(offset[2])
            ]
        except Exception:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid offset value at index {i}. Offset must be numeric"
            )

    # Decode image
    img_bytes = await image.read()

    if img_bytes is None or img_bytes == b'':
        raise HTTPException(
            status_code=400,
            detail="Invalid image"
        )

    try:
        np_img = np.frombuffer(img_bytes, np.uint8)
        img = cv2.imdecode(np_img, cv2.IMREAD_COLOR)

        if img is None:
            raise HTTPException(
                status_code=400,
                detail="Cannot decode image"
                )

    except HTTPException:
        raise

    except Exception:
        raise HTTPException(
            status_code=400,
            detail="Invalid image input"
        )



    # Decode templates
    for i, image in enumerate(templates):
        img_bytes = await image.read()

        if img_bytes is None or img_bytes == b'':
            raise HTTPException(
                status_code=400,
                detail=f"Invalid image at index {i}"
            )

        try:
            np_img = np.frombuffer(img_bytes, np.uint8)
            temp = cv2.imdecode(np_img, cv2.IMREAD_COLOR)

            if temp is None:
                raise HTTPException(
                    status_code=400,
                    detail=f"Cannot decode image at index {i}"
                )

            temp_imgs.append(temp)

        except HTTPException:
            raise

        except Exception:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid image input at index {i}"
            )

    # Truyền cả ảnh và offsets vào service
    res = cal_robot_coord_service.cal_robot_coord_test_phase(img, temp_imgs, offset_list)

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
