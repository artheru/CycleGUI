#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
OBJ -> GLB 单文件转换脚本

依赖:
  - trimesh (pip install trimesh)

功能:
  - 将指定路径的 .obj 转换为指定路径的 .glb
  - 可选缩放、合并场景、关闭trimesh预处理

用法:
  python convert_obj_to_glb.py input.obj output.glb
  python convert_obj_to_glb.py input.obj output.glb --scale 0.01 --merge-scene
"""

import os
import sys
import argparse
from typing import List


def ensure_trimesh_installed():
    try:
        import trimesh  # noqa: F401
    except Exception as exc:  # pragma: no cover
        print("错误: 需要安装 trimesh。请先运行: pip install trimesh", file=sys.stderr)
        raise


def _ensure_parent_dir(path: str) -> None:
    parent = os.path.dirname(path) or '.'
    os.makedirs(parent, exist_ok=True)


def _validate_paths(input_obj: str, output_glb: str) -> None:
    if not os.path.exists(input_obj):
        raise FileNotFoundError(f"输入文件不存在: {input_obj}")
    if not input_obj.lower().endswith('.obj'):
        raise ValueError("输入文件必须为 .obj")
    if not output_glb.lower().endswith('.glb'):
        raise ValueError("输出文件必须以 .glb 结尾")


def convert_one(
    input_path: str,
    output_path: str,
    scale: float = 1.0,
    merge_scene: bool = False,
    process: bool = True,
) -> bool:
    import trimesh

    try:
        loaded = trimesh.load(input_path, force='scene', process=process)
    except Exception as exc:
        print(f"[失败] 读取失败: {input_path} -> {exc}")
        return False

    try:
        # 统一处理为Scene, 便于后续导出
        if isinstance(loaded, trimesh.Scene):
            scene = loaded
        else:
            scene = trimesh.Scene(loaded)

        # 缩放: 对场景应用整体变换
        if scale != 1.0:
            import numpy as np
            S = np.eye(4)
            S[0, 0] = S[1, 1] = S[2, 2] = scale
            scene.apply_transform(S)

        # 是否合并所有几何为单一网格
        if merge_scene:
            try:
                mesh = trimesh.util.concatenate([g for g in scene.dump().geometry.values()])
                export_target = mesh
            except Exception:
                # 回退: 若失败则直接导出Scene
                export_target = scene
        else:
            export_target = scene

        # 确保输出目录存在
        os.makedirs(os.path.dirname(output_path) or '.', exist_ok=True)

        # 导出为GLB (嵌入纹理/材质)
        export_target.export(output_path, file_type='glb')
        print(f"[成功] {input_path} -> {output_path}")
        return True
    except Exception as exc:
        print(f"[失败] 导出失败: {input_path} -> {exc}")
        return False


def parse_args():
    p = argparse.ArgumentParser(description="将指定 .obj 转为指定 .glb")
    p.add_argument('input_obj', help='输入 .obj 文件路径')
    p.add_argument('output_glb', help='输出 .glb 文件路径')
    p.add_argument('--scale', type=float, default=1.0, help='缩放比例 (默认1.0)')
    p.add_argument('--merge-scene', action='store_true', help='将所有网格合并为单一网格后导出')
    p.add_argument('--no-process', action='store_true', help='禁用trimesh预处理')
    return p.parse_args()


def main() -> int:
    ensure_trimesh_installed()
    args = parse_args()

    try:
        _validate_paths(args.input_obj, args.output_glb)
    except Exception as exc:
        print(f"参数错误: {exc}")
        return 1

    try:
        _ensure_parent_dir(args.output_glb)
        ok = convert_one(
            input_path=args.input_obj,
            output_path=args.output_glb,
            scale=args.scale,
            merge_scene=args.merge_scene,
            process=not args.no_process,
        )
        return 0 if ok else 2
    except Exception as exc:
        print(f"执行失败: {exc}")
        return 2


if __name__ == '__main__':
    raise SystemExit(main())


