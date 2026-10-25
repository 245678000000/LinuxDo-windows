#!/usr/bin/env python3
# 生成 Windows 应用图标 AppIcon.ico（16–256 px，PNG 压缩帧）
# 按原版图标（黑 / 白 / 金 三色带圆形徽标）矢量重绘。Windows 图标不加 macOS 的圆角方形底板，
# 只保留圆形徽标和细描边，小尺寸单独栅格化以保持清晰。只依赖 Python 标准库。
# 用法：python3 tools/make_icon.py [输出路径.ico]

import math
import struct
import sys
import zlib

# 颜色取样自原版 linuxdo_256.ico，与 macOS 版 make_icon.swift 一致
BLACK = (0x25, 0x25, 0x28)
LIGHT = (0xF3, 0xF3, 0xF3)
GOLD = (0xFF, 0xBC, 0x00)
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SAMPLES = 4


def band(y_ratio):
    # 黑带占上 27%，金带从 72.2% 开始
    if y_ratio < 0.27:
        return BLACK
    if y_ratio >= 0.722:
        return GOLD
    return LIGHT


def render(size):
    margin = max(0.5, size * 0.04)
    radius = size / 2 - margin
    center = size / 2
    stroke = max(0.75, size / 96)
    rows = []
    for py in range(size):
        row = bytearray([0])  # PNG filter: none
        for px in range(size):
            r = g = b = a = 0.0
            for sy in range(SAMPLES):
                for sx in range(SAMPLES):
                    x = px + (sx + 0.5) / SAMPLES
                    y = py + (sy + 0.5) / SAMPLES
                    d = math.hypot(x - center, y - center)
                    if d > radius:
                        continue
                    color = band((y - (center - radius)) / (2 * radius))
                    # 细描边，让徽标在浅色任务栏上边缘清晰
                    if d > radius - stroke:
                        color = tuple(c * 0.82 for c in color)
                    r += color[0]; g += color[1]; b += color[2]; a += 1
            n = SAMPLES * SAMPLES
            if a:
                row += bytes([round(r / a), round(g / a), round(b / a), round(255 * a / n)])
            else:
                row += bytes(4)
        rows.append(bytes(row))
    return png(size, b"".join(rows))


def png(size, raw):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def main():
    output = sys.argv[1] if len(sys.argv) > 1 else "AppIcon.ico"
    frames = [render(s) for s in SIZES]
    offset = 6 + 16 * len(frames)
    directory = b""
    for size, data in zip(SIZES, frames):
        edge = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", edge, edge, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(output, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(frames)) + directory + b"".join(frames))
    print(f"图标已生成：{output}")


if __name__ == "__main__":
    main()
