#!/usr/bin/env python3
"""サンプル問題用の画像（猫のイラスト）生成スクリプト（issue #220）。

サンプル問題 q3「この画像に写っている動物は？」（正解: ねこ）に添える画像を、
Pillow の図形描画だけで描く。第三者の素材・生成 AI の画像は使っていない（本プロジェクトのために作成したもの）。
描画は 2 倍サイズで行い、LANCZOS で縮小してなめらかにする。乱数は使わないため、
同じ Pillow のバージョンなら毎回同じ画像になる（同梱の画像は Pillow 12.3.0 で生成）。

出力: Assets/TsumugiQuiz/Resources/Questions/sample-image.bytes（実体は PNG。.bytes 拡張子の TextAsset として同梱する）。

使い方:
    python scripts/gen-sample-image.py                  # 既定の場所へ書き出す
    python scripts/gen-sample-image.py --out cat.png    # 任意の場所へ書き出す
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("Pillow が必要です: pip install Pillow", file=sys.stderr)
    sys.exit(1)

WIDTH, HEIGHT = 800, 600  # 表示エリアは最大 480px の高さ（#193）。余裕をもたせた大きさ
SCALE = 2
REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUT = REPO_ROOT / "Assets" / "TsumugiQuiz" / "Resources" / "Questions" / "sample-image.bytes"

BG = (255, 240, 214)
FLOOR = (246, 214, 160)
FUR = (240, 158, 66)
FUR_DARK = (204, 112, 30)
BELLY = (255, 236, 208)
INK = (60, 40, 30)
PINK = (244, 143, 160)
EYE = (120, 190, 80)


def draw_cat() -> Image.Image:
    s = SCALE
    img = Image.new("RGB", (WIDTH * s, HEIGHT * s), BG)
    d = ImageDraw.Draw(img)

    def e(cx, cy, rx, ry, fill, outline=None, width=0):
        d.ellipse([(cx - rx) * s, (cy - ry) * s, (cx + rx) * s, (cy + ry) * s],
                  fill=fill, outline=outline, width=width * s)

    def poly(pts, fill):
        d.polygon([(x * s, y * s) for x, y in pts], fill=fill)

    def line(pts, fill, w):
        d.line([(x * s, y * s) for x, y in pts], fill=fill, width=w * s, joint="curve")

    # 床
    d.rectangle([0, 470 * s, WIDTH * s, HEIGHT * s], fill=FLOOR)
    e(400, 540, 230, 30, (230, 196, 140))

    # しっぽ（体の後ろ）
    line([(560, 470), (680, 440), (710, 340), (660, 280)], FUR, 40)
    e(660, 282, 20, 20, FUR_DARK)

    # 体
    e(400, 430, 170, 130, FUR)
    e(400, 470, 100, 90, BELLY)
    # 前足
    e(335, 535, 45, 28, BELLY, INK, 4)
    e(465, 535, 45, 28, BELLY, INK, 4)
    # 体のしま
    for x in (270, 300, 500, 530):
        line([(x, 350 + abs(x - 400) // 6), (x + (12 if x < 400 else -12), 395 + abs(x - 400) // 6)], FUR_DARK, 14)

    # 耳
    poly([(262, 190), (290, 70), (375, 140)], FUR)
    poly([(538, 190), (510, 70), (425, 140)], FUR)
    poly([(285, 160), (298, 106), (338, 140)], PINK)
    poly([(515, 160), (502, 106), (462, 140)], PINK)

    # 頭
    e(400, 235, 150, 120, FUR)
    # 額のしま
    line([(400, 120), (400, 160)], FUR_DARK, 14)
    line([(358, 128), (366, 164)], FUR_DARK, 12)
    line([(442, 128), (434, 164)], FUR_DARK, 12)
    # ほっぺ
    e(400, 285, 90, 62, BELLY)

    # 目
    for cx in (345, 455):
        e(cx, 225, 30, 34, (255, 255, 255), INK, 4)
        e(cx, 228, 20, 26, EYE)
        e(cx, 228, 9, 20, INK)
        e(cx - 7, 216, 6, 6, (255, 255, 255))

    # 鼻と口
    poly([(385, 262), (415, 262), (400, 280)], PINK)
    line([(400, 280), (400, 296)], INK, 4)
    line([(400, 296), (380, 310), (362, 302)], INK, 4)
    line([(400, 296), (420, 310), (438, 302)], INK, 4)

    # ひげ
    for sign in (-1, 1):
        for dy, ey in ((268, 250), (290, 292), (312, 334)):
            line([(400 + sign * 62, dy), (400 + sign * 160, ey)], INK, 3)

    return img.resize((WIDTH, HEIGHT), Image.LANCZOS)


def main() -> int:
    parser = argparse.ArgumentParser(description="サンプル問題用の猫のイラストを生成する")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="出力先（PNG 形式で書き出す）")
    args = parser.parse_args()

    args.out.parent.mkdir(parents=True, exist_ok=True)
    # format を明示する（拡張子が .bytes でも PNG で保存するため）
    draw_cat().save(args.out, format="PNG", optimize=True)
    print(f"wrote {args.out} ({args.out.stat().st_size} bytes, {WIDTH}x{HEIGHT})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
