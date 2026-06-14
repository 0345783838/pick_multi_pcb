from fastapi import FastAPI
from src.controller.service_controller import robot_router


app = FastAPI()
app.include_router(robot_router, tags=['ROBOT CONTROLLER'], prefix="/ai")

