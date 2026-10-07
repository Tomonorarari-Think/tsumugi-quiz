"""scripts/generate_tsumugi_expressions.py の切り出し（#191）・縮小の単体テスト。

実行:
    python -m unittest discover -s scripts/tests -p "test_*.py"

立ち絵素材（PSD・PNG）は二次配布禁止（docs/licenses.md §3）のため、テストでは一切使わない。
Pillow で作った小さな合成画像と、psd-tools のレイヤーを真似た偽オブジェクトだけで検証する。
Pillow が無い環境では Pillow を使うテストだけをスキップする（切り出し範囲の検証は標準ライブラリだけで動く）。
"""

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[1] / "generate_tsumugi_expressions.py"


def _load_module():
    spec = importlib.util.spec_from_file_location("generate_tsumugi_expressions", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


gen = _load_module()

try:
    from PIL import Image

    HAS_PILLOW = True
except ImportError:  # pragma: no cover - 環境依存
    HAS_PILLOW = False


class ParseCropTests(unittest.TestCase):
    def test_default_is_bustup_preset(self):
        self.assertEqual(gen.DEFAULT_CROP, "bustup")
        self.assertEqual(gen.parse_crop(gen.DEFAULT_CROP), gen.CROP_BUSTUP)

    def test_presets_are_case_insensitive_and_trimmed(self):
        self.assertEqual(gen.parse_crop(" FULL "), gen.CROP_FULL)
        self.assertEqual(gen.parse_crop("BustUp"), gen.CROP_BUSTUP)

    def test_full_preset_covers_whole_canvas(self):
        self.assertEqual(gen.CROP_FULL.to_pixels(2037, 4084), (0, 0, 2037, 4084))

    def test_ratio_list(self):
        self.assertEqual(gen.parse_crop("0.1, 0.2 ,0.9,1"), gen.CropBox(0.1, 0.2, 0.9, 1.0))

    def test_rejects_invalid_values(self):
        invalid = [
            "",
            "   ",
            "half",
            "0.1,0.2,0.3",
            "0.1,0.2,0.3,0.4,0.5",
            "a,0,1,1",
            "nan,0,1,1",
            "0,inf,1,1",
            "-0.1,0,1,1",
            "0,0,1.01,1",
            "0.5,0,0.5,1",  # 左 == 右
            "0.6,0,0.5,1",  # 左 > 右
            "0,0.5,1,0.5",  # 上 == 下
            "0,0.7,1,0.2",  # 上 > 下
        ]
        for text in invalid:
            with self.subTest(text=text):
                with self.assertRaises(ValueError):
                    gen.parse_crop(text)


class CropBoxTests(unittest.TestCase):
    def test_bustup_pixels_on_reference_psd_size(self):
        # 春日部つむぎ立ち絵_公式_v2.0.psd の実寸（2037x4084）に対する既定の範囲（コメントの根拠と一致させる）。
        box = gen.CROP_BUSTUP.to_pixels(2037, 4084)
        self.assertEqual(box, (448, 41, 1833, 1838))
        width, height = box[2] - box[0], box[3] - box[1]
        self.assertEqual((width, height), (1385, 1797))
        self.assertAlmostEqual(width / height, 0.771, places=3)

    def test_bustup_contains_expression_layers_of_reference_psd(self):
        # 表情グループ（!口 / !目 / !眉 / !アクセサリー）の全レイヤーの外接矩形（実測値、数値のみ）。
        expression_layers = (735, 327, 1475, 837)
        self.assertTrue(gen.box_contains(gen.CROP_BUSTUP.to_pixels(2037, 4084), expression_layers))

    def test_too_small_box_is_rejected(self):
        with self.assertRaises(ValueError):
            gen.CropBox(0.5, 0.5, 0.5001, 0.6).to_pixels(10, 10)

    def test_invalid_canvas_is_rejected(self):
        with self.assertRaises(ValueError):
            gen.CROP_FULL.to_pixels(0, 10)


@unittest.skipUnless(HAS_PILLOW, "Pillow が無いため画像処理のテストを省略")
class CropAndResizeTests(unittest.TestCase):
    @staticmethod
    def _quadrant_image(width=200, height=400):
        """上半分が赤・下半分が青の RGBA 画像（左上に 1px の緑の目印）。"""
        image = Image.new("RGBA", (width, height), (0, 0, 255, 255))
        image.paste((255, 0, 0, 255), (0, 0, width, height // 2))
        image.putpixel((0, 0), (0, 255, 0, 255))
        return image

    def test_crop_then_resize_keeps_aspect_and_height(self):
        source = self._quadrant_image()
        result = gen.crop_and_resize(source, gen.CropBox(0.0, 0.0, 1.0, 0.5), max_height=50)
        # 切り出し（200x200）→ 高さ 50 に等比縮小。
        self.assertEqual(result.size, (50, 50))
        self.assertEqual(result.mode, "RGBA")
        # 切り出した上半分（赤）だけが残る。
        self.assertEqual(result.getpixel((25, 40))[:3], (255, 0, 0))

    def test_does_not_upscale(self):
        source = self._quadrant_image()
        result = gen.crop_and_resize(source, gen.CropBox(0.25, 0.5, 0.75, 1.0), max_height=1000)
        self.assertEqual(result.size, (100, 200))
        self.assertEqual(result.getpixel((50, 100))[:3], (0, 0, 255))

    def test_zero_max_height_keeps_cropped_size(self):
        source = self._quadrant_image()
        result = gen.crop_and_resize(source, gen.CropBox(0.0, 0.0, 0.5, 0.5), max_height=0)
        self.assertEqual(result.size, (100, 200))
        self.assertEqual(result.getpixel((0, 0))[:3], (0, 255, 0), "左上の目印が残る（切り出し位置が正しい）")

    def test_full_crop_is_resize_only(self):
        source = self._quadrant_image()
        result = gen.crop_and_resize(source, gen.CROP_FULL, max_height=100)
        self.assertEqual(result.size, (50, 100))

    def test_source_is_not_mutated(self):
        source = self._quadrant_image()
        before = source.tobytes()
        gen.crop_and_resize(source, gen.CROP_BUSTUP, max_height=10)
        self.assertEqual(source.size, (200, 400))
        self.assertEqual(source.tobytes(), before)

    def test_negative_max_height_is_rejected(self):
        with self.assertRaises(ValueError):
            gen.crop_and_resize(self._quadrant_image(), gen.CROP_FULL, max_height=-1)


class _FakeLayer:
    """psd-tools のレイヤーのうち expression_bbox が使う属性だけを持つ偽物。"""

    def __init__(self, name, bbox=(0, 0, 0, 0), visible=True, children=None):
        self.name = name
        self.bbox = bbox
        self.visible = visible
        self._children = children

    def is_group(self):
        return self._children is not None

    def __iter__(self):
        return iter(self._children or [])


class ExpressionBboxTests(unittest.TestCase):
    def test_union_of_visible_expression_layers_only(self):
        psd = [
            _FakeLayer("!体部分", children=[_FakeLayer("体", (0, 0, 1000, 1000))]),
            _FakeLayer(
                "!口",
                children=[_FakeLayer("*あ", (10, 20, 30, 40)), _FakeLayer("*い", (0, 0, 500, 500), visible=False)],
            ),
            _FakeLayer("!アクセサリー", children=[_FakeLayer("汗", (50, 5, 60, 25)), _FakeLayer("空", (0, 0, 0, 0))]),
            _FakeLayer("!眉", visible=False, children=[_FakeLayer("*普通", (0, 0, 900, 900))]),
        ]
        self.assertEqual(gen.expression_bbox(psd), (10, 5, 60, 40))

    def test_none_when_no_visible_expression_layer(self):
        psd = [_FakeLayer("!口", children=[_FakeLayer("*あ", (10, 20, 30, 40), visible=False)])]
        self.assertIsNone(gen.expression_bbox(psd))

    def test_box_contains(self):
        self.assertTrue(gen.box_contains((0, 0, 10, 10), (0, 0, 10, 10)))
        self.assertFalse(gen.box_contains((0, 0, 10, 10), (0, 0, 11, 10)))
        self.assertFalse(gen.box_contains((5, 0, 10, 10), (4, 0, 6, 1)))


class ArgumentDefaultsTests(unittest.TestCase):
    def test_defaults(self):
        args = gen.parse_args(["--psd", "a.psd", "--config", "c.json", "--out-dir", "out"])
        self.assertEqual(args.crop, "bustup")
        self.assertEqual(args.max_height, gen.DEFAULT_MAX_HEIGHT)
        self.assertEqual(gen.DEFAULT_MAX_HEIGHT, 1280)


class SampleConfigTests(unittest.TestCase):
    """docs/tsumugi-expressions.sample.json（既定の設定、#212 で 9 表情）が検証を通ること。

    アプリが読むファイル名との一致は EditMode テスト（CharacterImagePathsTests）で確認している。
    """

    SAMPLE_PATH = Path(__file__).resolve().parents[2] / "docs" / "tsumugi-expressions.sample.json"

    def test_sample_config_loads_with_scene_expressions(self):
        config = gen.load_config(self.SAMPLE_PATH)
        keys = [expression.key for expression in config.expressions]
        self.assertEqual(
            keys,
            ["idle", "reading", "buzz_self", "buzz_other", "wrong_moment",
             "correct", "wrong", "timeout", "no_eligible"],
        )

    def test_sample_config_touches_only_expression_groups(self):
        config = gen.load_config(self.SAMPLE_PATH)
        for expression in config.expressions:
            self.assertLessEqual(set(expression.layers), set(gen.ALLOWED_EXPRESSION_GROUPS), expression.key)
            # 既定表示のホクロはどの表情でも維持する（docs/licenses.md §3.1）。
            self.assertIn("ホクロ", expression.layers["!アクセサリー"], expression.key)

    def test_sample_config_keeps_builtin_safety(self):
        config = gen.load_config(self.SAMPLE_PATH)
        self.assertIn("私服", config.forbidden_groups)
        self.assertIn("制服", config.required_visible_groups)
        self.assertIn("!体部分", config.required_visible_groups)


if __name__ == "__main__":
    unittest.main()
