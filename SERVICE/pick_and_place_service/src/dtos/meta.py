from pydantic import BaseModel


class DataResponse(BaseModel):
    Result: bool = False  # True/False
    Score: float = None
    ResImg: str = None  # base64 result encoded image
    ImageX: float = None
    ImageY: float = None
    ImageAngle: float = None
    RobotX: float = None
    RobotY: float = None
    RobotAngle: float = None
    Message: str = None


class Calib2DResponse(BaseModel):
    Result: bool = False  # True/False
    RobotX: float = None
    RobotY: float = None
    Message: str = None

class ErrorCode:
    PASS = ("PASS", "Khay đĩa đạt chất lượng")
    ABNORMAL = ("ERROR_001", "Khay đĩa có bất thường!")
    ERR_NUM_DISK = ("ERROR_002", "Số lượng khe đĩa trong khay bất thường!")
    ERR_NUM_UV_DISK = ("ERROR_003", "Phát sinh mixing đĩa!")
