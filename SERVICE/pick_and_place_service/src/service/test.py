import math

def compute_robot_pose_from_bottom_left(corner_robot, theta_deg, pcb_w, pcb_h):
    theta = math.radians(theta_deg)

    # Vector từ BOTTOM_LEFT về tâm PCB
    dx = -pcb_w / 2
    dy = -pcb_h / 2

    dx_r = dx * math.cos(theta) - dy * math.sin(theta)
    dy_r = dx * math.sin(theta) + dy * math.cos(theta)

    center_x = corner_robot[0] + dx_r
    center_y = corner_robot[1] + dy_r

    robot_theta = theta_deg

    return center_x, center_y, robot_theta

if __name__ == '__main__':
    compute_robot_pose_from_bottom_left((115.548, 965.448),  -5.7650, 84, 98.3)