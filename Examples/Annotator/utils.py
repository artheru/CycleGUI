import numpy as np
import os
import open3d as o3d


def get_rodrigues_matrix(axis, angle):
    axis = np.array(axis)
    identity = np.eye(3)
    s1 = np.array(
        [
            [np.zeros([]), -axis[2], axis[1]],
            [axis[2], np.zeros([]), -axis[0]],
            [-axis[1], axis[0], np.zeros([])],
        ]
    )
    s2 = np.matmul(axis[:, None], axis[None])
    cos_angle = np.cos(angle)
    sin_angle = np.sin(angle)

    rodrigues_matrix = cos_angle * identity + sin_angle * s1 + (1 - cos_angle) * s2
    return rodrigues_matrix


def apply_transformation(vertices, position, rotation, rotation_order="XYZ", offset_first=False):

    # process position first
    if offset_first:
        vertices = vertices + np.array(position)

    # process rotation
    rot_mat = {}

    rot_mat["X"] = get_rodrigues_matrix([1, 0, 0], rotation[0])
    rot_mat["Y"] = get_rodrigues_matrix([0, 1, 0], rotation[1])
    rot_mat["Z"] = get_rodrigues_matrix([0, 0, 1], rotation[2])

    for s in rotation_order:
        vertices = np.matmul(vertices, rot_mat[s].T)

    # process position second
    if not offset_first:
        vertices = vertices + np.array(position)

    return vertices


def adjust_position_from_rotation(position, rotation, rotation_order="XYZ"):

    position_new = np.array(position)[None, :]
    position_new = apply_transformation(
        position_new, [0, 0, 0], rotation, rotation_order
    )

    return list(position_new[0])


def list_add(list1, list2):
    list_res = []
    for i in range(len(list1)):
        list_res.append(list1[i] + list2[i])
    return list_res


# ---------------------------------------------
# C# Quaternion <-> Python rotation (apply_transformation)
# 说明：
# - Python 侧 apply_transformation(vertices, position, rotation) 使用的是按顺序 XYZ 的欧拉角（弧度），
#   并根据 rotation_order 依次应用罗德里格斯旋转矩阵。
# - 约定轴向映射：C# 中的 (X, Y, Z) 轴 对应 Python 的 (Z, X, Y) 轴。
#   因此若在 C# 拿到欧拉角 (ex, ey, ez)（单位：弧度），传给 Python 应该为 [ey, ez, ex]。
# - 若在 C# 以 System.Numerics.Quaternion(qw, qx, qy, qz) 表示旋转，可先将四元数转为 C# 欧拉角 (ex, ey, ez)，
#   再按上述轴映射重排为 Python rotation。

def _quaternion_to_euler_xyz(w, x, y, z):
    """将单位四元数转换为 XYZ 顺序的欧拉角（弧度）。
    返回 (ex, ey, ez)，对应绕 X、Y、Z 轴的内禀旋转。
    参考公式来源：通用四元数->欧拉角转换。
    """
    # 归一化（防止数值误差）
    norm = np.sqrt(w*w + x*x + y*y + z*z)
    if norm == 0:
        w, x, y, z = 1.0, 0.0, 0.0, 0.0
    else:
        w, x, y, z = w / norm, x / norm, y / norm, z / norm

    # 绕 X（roll）
    sinr_cosp = 2.0 * (w * x + y * z)
    cosr_cosp = 1.0 - 2.0 * (x * x + y * y)
    ex = np.arctan2(sinr_cosp, cosr_cosp)

    # 绕 Y（pitch）
    sinp = 2.0 * (w * y - z * x)
    if abs(sinp) >= 1.0:
        ey = np.pi / 2.0 * np.sign(sinp)  # 防止超出范围
    else:
        ey = np.arcsin(sinp)

    # 绕 Z（yaw）
    siny_cosp = 2.0 * (w * z + x * y)
    cosy_cosp = 1.0 - 2.0 * (y * y + z * z)
    ez = np.arctan2(siny_cosp, cosy_cosp)

    return float(ex), float(ey), float(ez)


def csharp_quaternion_to_python_rotation(qw, qx, qy, qz, rotation_order="XYZ"):
    """将 C# 的 System.Numerics.Quaternion(qw,qx,qy,qz) 转为 Python apply_transformation 的 rotation（弧度列表）。

    轴映射：C# (X,Y,Z) -> Python (Z,X,Y)
    步骤：
      1) 四元数 -> C# 欧拉角 (ex, ey, ez) in XYZ（弧度）
      2) Python rotation = [ey, ez, ex]（按轴映射重排）
      3) 可选：若 rotation_order 非 "XYZ"，请在外部按需要调整顺序
    """
    ex, ey, ez = _quaternion_to_euler_xyz(qw, qx, qy, qz)
    py_rx = ey  # C# Y -> Python X
    py_ry = ez  # C# Z -> Python Y
    py_rz = ex  # C# X -> Python Z
    if rotation_order == "XYZ":
        return [py_rx, py_ry, py_rz]
    # 若使用其他顺序，可在此扩展；默认返回 XYZ 顺序
    return [py_rx, py_ry, py_rz]