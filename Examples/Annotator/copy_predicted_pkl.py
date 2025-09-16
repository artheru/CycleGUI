import os
import shutil
import argparse
from glob import glob

def copy_and_rename(src_root, objects_root):
    if not os.path.isdir(src_root):
        raise FileNotFoundError(f"源目录不存在: {src_root}")
    if not os.path.isdir(objects_root):
        raise FileNotFoundError(f"Objects 目录不存在: {objects_root}")

    pattern = os.path.join(src_root, "*_conceptualization.pkl")
    src_files = glob(pattern)
    if not src_files:
        print(f"[WARN] 未找到文件: {pattern}")
        return

    copied = 0
    for src in src_files:
        base = os.path.basename(src)
        if not base.endswith("_conceptualization.pkl"):
            continue
        category = base[:-len("_conceptualization.pkl")]  # 去掉后缀得到 XXX
        if not category:
            print(f"[WARN] 无法解析类别名: {base}")
            continue

        dst_dir = os.path.join(objects_root, category, "predicted_pkl")
        os.makedirs(dst_dir, exist_ok=True)
        dst = os.path.join(dst_dir, "predicted_conceptualization.pkl")

        try:
            shutil.copy2(src, dst)
            print(f"[OK] {base} -> {dst}")
            copied += 1
        except Exception as e:
            print(f"[ERROR] 复制失败 {src} -> {dst}: {e}")

    print(f"[DONE] 共处理 {len(src_files)} 个，成功拷贝 {copied} 个。")

def main():
    parser = argparse.ArgumentParser(description="拷贝并重命名 *_conceptualization.pkl")
    parser.add_argument("--src", required=False, default="ConceptFactory Conceptualization Results",
                        help="源目录（包含 *_conceptualization.pkl）")
    parser.add_argument("--objects", required=False, default="Objects",
                        help="Objects 根目录")
    args = parser.parse_args()

    src_root = os.path.abspath(args.src)
    objects_root = os.path.abspath(args.objects)
    copy_and_rename(src_root, objects_root)

if __name__ == "__main__":
    main()