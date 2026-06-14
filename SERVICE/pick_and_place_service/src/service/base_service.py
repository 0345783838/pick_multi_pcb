# Import the Model Achitecture here!!!
import cv2
import decouple

from src.engine.calibration.calibration_2d import Calibration2D
from src.engine.image_matcher.image_matcher import ImageMatcher
from src.tools.caliper_advanced_new import AdvancedMultiEdgeCaliper

# Load config here!!!
config = decouple.config
Csv = decouple.Csv


# Check if there is any config


# Get the config from the config file

CALIB_FILE_PATH = config('CALIB_FILE_PATH')

OFFSET_X = config('OFFSET_X', cast=float)
OFFSET_Y = config('OFFSET_Y', cast=float)

IMAGE_MATCHING_DLL = config('IMAGE_MATCHING_DLL')
IMAGE_MATCHING_MAX_COUNT = config('IMAGE_MATCHING_MAX_COUNT', cast=int)
IMAGE_MATCHING_SCORE_THRESHOLD = config('IMAGE_MATCHING_SCORE_THRESHOLD', cast=float)
IMAGE_MATCHING_IOU_THRESHOLD = config('IMAGE_MATCHING_IOU_THRESHOLD', cast=float)
IMAGE_MATCHING_ANGLE = config('IMAGE_MATCHING_ANGLE', cast=int)
IMAGE_MATCHING_MIN_AREA = config('IMAGE_MATCHING_MIN_AREA', cast=int)
IMAGE_MATCHING_SCALE = config('IMAGE_MATCHING_SCALE', cast=lambda v: [float(s.strip()) for s in v.split(',')])


calib_file_path = CALIB_FILE_PATH
calib = Calibration2D(calib_file_path)

offset_x = OFFSET_X
offset_y = OFFSET_Y

image_matcher = ImageMatcher(IMAGE_MATCHING_DLL,
                             IMAGE_MATCHING_MAX_COUNT,
                             IMAGE_MATCHING_SCORE_THRESHOLD,
                             IMAGE_MATCHING_IOU_THRESHOLD,
                             IMAGE_MATCHING_ANGLE,
                             IMAGE_MATCHING_MIN_AREA,
                             IMAGE_MATCHING_SCALE)


class BaseService:
    def __init__(self):
        if not hasattr(self, '_initialized'):
            self._initialized = True
            self.calib = calib
            self.calib_file_path = calib_file_path
            self.image_matcher = image_matcher
            self.offset_x = offset_x
            self.offset_y = offset_y
        else:
            pass
