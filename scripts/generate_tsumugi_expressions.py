#!/usr/bin/env python3
"""春日部つむぎ公式立ち絵 v2.0 の PSD から表情差分 PNG を書き出す（issue #86）。

権利上の前提（docs/licenses.md §3 / External/README.md §5）:

- 立ち絵素材（zip・PSD・PNG 原本）も、本スクリプトが生成した表情差分 PNG も、
  **リポジトリ・配布物には一切含めない**。生成先は既定で `AppPaths.DataRoot`
  （`Application.persistentDataPath` 相当）配下の `tsumugi/`、つまりユーザーのローカル環境だけ。
  「二次配布、自作発言」は readme.txt / 公式サイト規約 7 項で明確に禁止されている。
- 規約 5 項「加筆、加工できます。ただし、春日部つむぎと分からない・春日部つむぎではない
  キャラクターへの改変は禁止します。」に従い、本スクリプトが行う加工は
  「PSD にもとから入っている表情レイヤーの表示切り替え」「バストアップ範囲への切り出し（#191）」
  「等比縮小」だけに限定する。レイヤーの追加・描き足し・色変更は行わない。
- readme.txt「服を脱がせた状態での利用は厳禁です。」に触れないよう、
  服のグループ（`制服`）が表示されていること・`私服` グループが非表示であることを
  書き出し前に検証し、満たさない場合は 1 枚も書き出さずに失敗させる。

**設定ファイルでは無効化できない安全装置（PR #135 レビュー H1・H2）**

設定ファイル（`--config`）は外部入力であり、そこに書かれた `safety` 設定だけに頼ると、
`safety` を省いた JSON や `"layers": {"!体部分": [...]}` のような設定で服装レイヤーを
落とした合成が書き出せてしまう。そのため次の 3 つはコード側に固定し、設定では緩められない。

1. `ALLOWED_EXPRESSION_GROUPS` — 表示を切り替えてよいグループのホワイトリスト。
   `layers` のキーがこれ以外なら `EXIT_SAFETY`（`!体部分` / `制服` / `私服` は指定できない）。
2. `BUILTIN_FORBIDDEN_GROUPS` / `BUILTIN_REQUIRED_VISIBLE_GROUPS` — 組み込みの安全セット。
   設定ファイルの `safety` は「追加」だけでき、削除はできない（union する）。
   該当グループが PSD に見つからない場合は「検証不能」として `EXIT_SAFETY`。
3. `assert_output_dir_allowed` — 出力先がリポジトリ内（`External/` 以外）や `Assets/` 配下なら
   `EXIT_SAFETY`。PowerShell ラッパーを経由せず本スクリプトを直接実行しても効く。
   リポジトリの判定は**出力先から上へ辿って** `ProjectSettings/ProjectSettings.asset` または
   `.git` を持つ祖先を探す方式なので、worktree から本体ツリーの `Assets/` を指定しても捕まる
   （PR #135 再レビュー M1）。

使い方（PowerShell ラッパー `scripts/generate-tsumugi-expressions.ps1` 経由を推奨）:

    python scripts/generate_tsumugi_expressions.py \
        --psd  External/tsumugi/extracted/<展開先>/春日部つむぎ立ち絵_公式_v2.0.psd \
        --config docs/tsumugi-expressions.sample.json \
        --out-dir "%USERPROFILE%/AppData/LocalLow/Tomonorarari-Think/TsumugiQuiz/tsumugi" \
        --crop bustup --max-height 1280

`--crop` は切り出し範囲（#191）。既定の `bustup` は頭から腰の上まで（PSD キャンバスに対する比率
`0.22,0.01,0.90,0.45`）。`full` で従来の全身（切り出しなし）、`左,上,右,下` の比率 4 つで任意の範囲を指定できる。
処理の順序は「合成 → 切り出し → 等比縮小」（原寸で切り出してから 1 回だけ縮小する）。

`--out-dir` にはリポジトリの外（データルート）か `External/` 配下しか指定できない。
`Assets/` 配下やリポジトリ内のその他の場所を指定すると、1 枚も書き出さずに終了する
（生成物は二次配布禁止の素材の加工物であり、git 管理下に置けないため）。
「リポジトリ内」の判定は出力先の祖先を辿って行うので、本スクリプトが置かれているツリーとは
別のツリー（本体ツリー・他の worktree・無関係な別リポジトリ）を指定した場合も拒否する。

配布 zip への同梱（#219）: 本ファイルは `scripts/package-release.ps1` が配布 zip の
`tools/tsumugi-expressions/` に**そのままコピー**する（リポジトリ版と zip 版でロジックを二重に持たない）。
zip 版の入口は `scripts/tsumugi_expressions_standalone.py`（立ち絵 zip からの PSD の取り出し・
データルートの解決）で、本ファイルの `main` を呼ぶ。上の安全装置は zip 版でもそのまま効く。

依存（psd-tools は MIT、Pillow は MIT-CMU。docs/licenses.md §15 に記載。
指定は `scripts/tsumugi-expressions-requirements.txt` の 1 か所にまとめてある）:
  - psd-tools >= 1.19.0
  - Pillow    >= 10.0
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from dataclasses import dataclass
from pathlib import Path

EXIT_OK = 0
EXIT_USAGE = 1
EXIT_DEPENDENCY = 2
EXIT_CONFIG = 3
EXIT_SAFETY = 4

# 設定ファイル内で 1 つの表情に許す最大レイヤー数（設定ミスの検出用。実際の差分は 1〜3 枚）。
MAX_LAYERS_PER_GROUP = 8

# ---- 設定ファイルでは無効化できない安全装置（PR #135 レビュー H1） ----------
#
# 表情差分として表示を切り替えてよいグループ。これ以外（`!体部分` / `制服` / `私服` など、
# 服装・体に関わるグループ）を `layers` に書くことはできない。
# readme.txt「服を脱がせた状態での利用は厳禁です。着せ替え差分作成時のみご利用ください。」
ALLOWED_EXPRESSION_GROUPS = ("!口", "!目", "!眉", "!アクセサリー")

# 常に非表示でなければならないグループ（設定ファイルで削除できない。追加は可）。
BUILTIN_FORBIDDEN_GROUPS = ("私服",)

# 常に表示されていなければならないグループ（設定ファイルで削除できない。追加は可）。
BUILTIN_REQUIRED_VISIBLE_GROUPS = ("!体部分", "制服")

# リポジトリ（Unity プロジェクト）のルートを見分ける目印（PR #135 再レビュー M1）。
# スクリプト自身のツリーだけを基準にすると、worktree から本体ツリーの Assets/ を指定したときに
# 素通りしてしまう。出力先から上へ辿って「別のリポジトリの中」も検出できるようにする。
# `.git` は worktree ではファイル、通常のクローンではディレクトリなので exists() で見る。
REPOSITORY_MARKERS = (
    Path("ProjectSettings") / "ProjectSettings.asset",
    Path(".git"),
)


def _script_repository_root(script_path: Path) -> "Path | None":
    """本スクリプト自身が置かれているリポジトリのルート（scripts/ の 1 つ上）。リポジトリの外なら None。

    #219: 本スクリプトは配布 zip の `tools/tsumugi-expressions/` にも同じファイルを同梱する。そこでは
    1 つ上（`tools/`）はリポジトリではないので、基準にしない（None）。リポジトリかどうかは、目印
    （REPOSITORY_MARKERS）か Unity プロジェクトの `Assets/` があるかで見る（目印が消えていても
    `Assets/` があれば従来どおり基準にする）。
    """
    candidate = script_path.resolve().parents[1]
    try:
        if any((candidate / marker).exists() for marker in REPOSITORY_MARKERS) or (candidate / "Assets").is_dir():
            return candidate
    except OSError:
        return None
    return None


# 本スクリプト自身が置かれているツリーのルート。配布 zip に同梱したときは None（#219）。
SCRIPT_REPOSITORY_ROOT = _script_repository_root(Path(__file__))

# ---- 切り出し範囲・出力サイズ（#191 / #190） --------------------------------
#
# 切り出し範囲は PSD キャンバスに対する比率（左, 上, 右, 下。0.0〜1.0）で持つ。座標（px）ではなく比率に
# したのは、同じ構図で解像度だけ違う版の PSD を渡されても同じ範囲を指せるようにするため。
#
# 既定のバストアップ（`bustup`）の根拠（春日部つむぎ立ち絵_公式_v2.0.psd、2037x4084 の実測、2026-09-30）:
#   - 絵のある範囲（アルファの外接矩形）は x 147〜1805 / y 123〜3991。頭頂（アホ毛）が y 123、
#     右のサイドテールの端が x 1805、ジャケットの裾（スカートの上端）が y 1840 前後。
#   - 表情差分に使うグループ（!口 / !目 / !眉 / !アクセサリー）の全レイヤーの外接矩形は
#     x 735〜1475 / y 327〜837（未使用のレイヤーも含む）。既定の 4 表情の差分は x 1064〜1475 / y 427〜807。
#   - 比率 0.22,0.01,0.90,0.45 は px で (448, 41)〜(1833, 1838)、1385x1797（縦横比 0.771）。頭頂の上に
#     約 80px の余白を残し、腰の上（ジャケットの裾）で切る。左は袖の途中で切り、シュシュ（左手）は入れない。
#     表情レイヤーはすべて範囲内に収まる（書き出し前にも expression_bbox で検証し、はみ出せば警告する）。
# 表示側の枠（theme-views-game.uss の .character-image-frame）はこの縦横比を既定にしている。
# 範囲を変えた場合も、アプリは読み込んだ画像の縦横比に枠を合わせる（CharacterView）。


@dataclass(frozen=True)
class CropBox:
    """切り出し範囲（PSD キャンバスに対する比率、不変）。"""

    left: float
    top: float
    right: float
    bottom: float

    def to_pixels(self, width: int, height: int) -> tuple[int, int, int, int]:
        """キャンバスの大きさから px の矩形 (left, top, right, bottom) を求める（右・下は含まない）。"""
        if width <= 0 or height <= 0:
            raise ValueError(f"画像の大きさが不正です: {width}x{height}")
        box = (
            round(self.left * width),
            round(self.top * height),
            round(self.right * width),
            round(self.bottom * height),
        )
        if box[2] - box[0] < 1 or box[3] - box[1] < 1:
            raise ValueError(f"切り出し範囲が 1px 未満になります（{width}x{height} に対して {box}）。")
        return box


CROP_BUSTUP = CropBox(0.22, 0.01, 0.90, 0.45)
CROP_FULL = CropBox(0.0, 0.0, 1.0, 1.0)
CROP_PRESETS = {"bustup": CROP_BUSTUP, "full": CROP_FULL}
DEFAULT_CROP = "bustup"

# 出力 PNG の最大高さ（px）の既定値（#190 / #191）。表示側の枠の最大の大きさから決めた
# （数値は docs/architecture.md §10.2「立ち絵の枠（#191）」の実測）:
#   - 枠の幅は右列（26%、最小 280px・最大 420px）いっぱい。バストアップ（縦横比 0.771）の枠の
#     内側（画像の表示）の高さは論理 px で 16:9 が 510px、21:9 が 536px（右列 420px、最大）、900x750 が 403px。
#   - 物理 px はパネルの倍率 lerp(実幅/1600, 実高/900, 0.5) を掛けた値。16:9 の 510px は
#     1920x1080（倍率 1.2）で約 612px、2560x1440（1.6）で約 816px、3840x2160（4K、2.4）で約 1224px。
#     21:9 の 536px は 2560x1080（1.4）で約 750px、3440x1440（1.875）で約 1004px。
#   - 1280 は「一般的な 1920x1080 の表示の約 2 倍」かつ「4K 16:9 でも拡大せずに表示できる」値
#     （5120x2160 の 21:9（2.8、約 1500px）だけは約 1.17 倍に拡大される）。
#     縮小表示は mipmap + Trilinear（CharacterImageLoader、#190）で補間するので荒れない。
DEFAULT_MAX_HEIGHT = 1280


def parse_crop(text: str) -> CropBox:
    """`--crop` の値（プリセット名か `左,上,右,下` の比率）を検証して CropBox にする。

    不正な値は ValueError（呼び出し側で終了コード EXIT_USAGE に読み替える）。
    """
    if not isinstance(text, str) or not text.strip():
        raise ValueError("--crop が空です。bustup / full / 左,上,右,下（0.0〜1.0 の比率）のいずれかを指定してください。")

    key = text.strip().lower()
    if key in CROP_PRESETS:
        return CROP_PRESETS[key]

    parts = [part.strip() for part in text.split(",")]
    if len(parts) != 4:
        raise ValueError(
            f"--crop の形式が不正です: {text!r}。"
            "bustup / full / 左,上,右,下（比率 4 つ、例: 0.22,0.01,0.90,0.45）で指定してください。"
        )

    values: list[float] = []
    for name, part in zip(("左", "上", "右", "下"), parts):
        try:
            value = float(part)
        except ValueError:
            raise ValueError(f"--crop の{name}が数値ではありません: {part!r}") from None
        if math.isnan(value) or math.isinf(value):
            raise ValueError(f"--crop の{name}が有限の数ではありません: {part!r}")
        if not 0.0 <= value <= 1.0:
            raise ValueError(f"--crop の{name}は 0.0〜1.0 の比率で指定してください（実際: {value}）。")
        values.append(value)

    left, top, right, bottom = values
    if left >= right:
        raise ValueError(f"--crop の左（{left}）は右（{right}）より小さくしてください。")
    if top >= bottom:
        raise ValueError(f"--crop の上（{top}）は下（{bottom}）より小さくしてください。")
    return CropBox(left, top, right, bottom)


def log(message: str) -> None:
    print(message, flush=True)


def fail(exit_code: int, message: str) -> None:
    print(f"エラー: {message}", file=sys.stderr, flush=True)
    sys.exit(exit_code)


# ---- 設定ファイルの読み込み・検証 -------------------------------------------


@dataclass(frozen=True)
class Expression:
    """1 つの表情差分の定義（不変）。"""

    key: str
    file_name: str
    description: str
    # グループ名 -> そのグループで表示するレイヤー名（ここに無い兄弟レイヤーは非表示にする）
    layers: dict[str, tuple[str, ...]]


@dataclass(frozen=True)
class Config:
    expected_width: int | None
    expected_height: int | None
    forbidden_groups: tuple[str, ...]
    required_visible_groups: tuple[str, ...]
    expressions: tuple[Expression, ...]


def _require_str(value: object, where: str) -> str:
    if not isinstance(value, str) or not value.strip():
        fail(EXIT_CONFIG, f"{where} は空でない文字列である必要があります（実際: {value!r}）。")
    return value  # type: ignore[return-value]


def _require_str_list(value: object, where: str) -> tuple[str, ...]:
    if not isinstance(value, list):
        fail(EXIT_CONFIG, f"{where} は文字列の配列である必要があります（実際: {type(value).__name__}）。")
    return tuple(_require_str(item, f"{where}[{i}]") for i, item in enumerate(value))


def _merge_unique(builtin: tuple[str, ...], extra: tuple[str, ...]) -> tuple[str, ...]:
    """組み込みの安全セットと設定ファイルの追加分を、順序を保ったまま結合する。"""
    return tuple(dict.fromkeys(builtin + extra))


def load_config(path: Path) -> Config:
    """設定 JSON を読み、境界検証をしてから不変オブジェクトに変換する。"""
    if not path.is_file():
        fail(EXIT_CONFIG, f"設定ファイルが見つかりません: {path}")

    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError) as exc:
        fail(EXIT_CONFIG, f"設定ファイルを読めません（{type(exc).__name__}、UTF-8 で保存してください）: {path}: {exc}")
    except json.JSONDecodeError as exc:
        fail(EXIT_CONFIG, f"設定ファイルの JSON が壊れています: {path}: {exc}")

    if not isinstance(raw, dict):
        fail(EXIT_CONFIG, f"設定ファイルのトップレベルはオブジェクトである必要があります: {path}")

    psd_section = raw.get("psd") or {}
    if not isinstance(psd_section, dict):
        fail(EXIT_CONFIG, "psd はオブジェクトである必要があります。")

    def _optional_int(key: str) -> int | None:
        value = psd_section.get(key)
        if value is None:
            return None
        if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
            fail(EXIT_CONFIG, f"psd.{key} は正の整数である必要があります（実際: {value!r}）。")
        return value

    safety = raw.get("safety") or {}
    if not isinstance(safety, dict):
        fail(EXIT_CONFIG, "safety はオブジェクトである必要があります。")

    # レビュー H1: 組み込みの安全セットと union する。設定ファイルからは「追加」だけでき、
    # 組み込み分を削ることはできない（`safety` セクションを丸ごと省いても同じ検証が走る）。
    forbidden = _merge_unique(
        BUILTIN_FORBIDDEN_GROUPS,
        _require_str_list(safety.get("forbiddenGroups", []), "safety.forbiddenGroups"),
    )
    required_visible = _merge_unique(
        BUILTIN_REQUIRED_VISIBLE_GROUPS,
        _require_str_list(safety.get("requiredVisibleGroups", []), "safety.requiredVisibleGroups"),
    )

    raw_expressions = raw.get("expressions")
    if not isinstance(raw_expressions, list) or not raw_expressions:
        fail(EXIT_CONFIG, "expressions は 1 件以上の配列である必要があります。")

    expressions: list[Expression] = []
    seen_keys: set[str] = set()
    seen_files: set[str] = set()
    for index, item in enumerate(raw_expressions):
        where = f"expressions[{index}]"
        if not isinstance(item, dict):
            fail(EXIT_CONFIG, f"{where} はオブジェクトである必要があります。")

        key = _require_str(item.get("key"), f"{where}.key")
        file_name = _require_str(item.get("fileName"), f"{where}.fileName")
        description = item.get("description") or key
        if not isinstance(description, str):
            fail(EXIT_CONFIG, f"{where}.description は文字列である必要があります。")

        if key in seen_keys:
            fail(EXIT_CONFIG, f"{where}.key が重複しています: {key}")
        seen_keys.add(key)

        # 出力先ディレクトリの外へ書かせない（パストラバーサル対策。設定ファイルは外部入力）。
        if file_name != Path(file_name).name or file_name in (".", ".."):
            fail(EXIT_CONFIG, f"{where}.fileName にはディレクトリを含められません: {file_name!r}")
        if not file_name.lower().endswith(".png"):
            fail(EXIT_CONFIG, f"{where}.fileName は .png である必要があります: {file_name!r}")
        if file_name in seen_files:
            fail(EXIT_CONFIG, f"{where}.fileName が重複しています: {file_name}")
        seen_files.add(file_name)

        raw_layers = item.get("layers")
        if not isinstance(raw_layers, dict) or not raw_layers:
            fail(EXIT_CONFIG, f"{where}.layers は 1 件以上のオブジェクトである必要があります。")

        layers: dict[str, tuple[str, ...]] = {}
        for group_name, layer_names in raw_layers.items():
            group_name = _require_str(group_name, f"{where}.layers のキー")

            # レビュー H1: ホワイトリスト方式。設定ファイルから服装・体のグループには触れない。
            if group_name not in ALLOWED_EXPRESSION_GROUPS:
                fail(
                    EXIT_SAFETY,
                    f"{where}.layers に指定できないグループ '{group_name}' が含まれています。\n"
                    f"  指定できるのは表情のグループだけです: {list(ALLOWED_EXPRESSION_GROUPS)}\n"
                    "  服装・体のグループ（!体部分 / 制服 / 私服 など）の表示切り替えは、"
                    "readme.txt の禁止事項（服を脱がせた状態での利用は厳禁）に触れるため許可していません"
                    "（docs/licenses.md §3.1）。",
                )

            names = _require_str_list(layer_names, f"{where}.layers[{group_name}]")
            if len(names) > MAX_LAYERS_PER_GROUP:
                fail(
                    EXIT_CONFIG,
                    f"{where}.layers[{group_name}] のレイヤー数が多すぎます"
                    f"（{len(names)} > {MAX_LAYERS_PER_GROUP}）。設定ミスの可能性があります。",
                )
            if len(set(names)) != len(names):
                fail(EXIT_CONFIG, f"{where}.layers[{group_name}] にレイヤー名の重複があります。")
            # 二重の防御（上のホワイトリストで既に弾かれるはずだが、
            # ALLOWED_EXPRESSION_GROUPS を将来広げたときに禁止グループが通らないようにする）。
            if group_name in forbidden:
                fail(
                    EXIT_SAFETY,
                    f"{where}.layers で禁止グループ '{group_name}' を指定しています。"
                    "docs/licenses.md §3.1 を参照してください。",
                )
            layers[group_name] = names

        expressions.append(
            Expression(key=key, file_name=file_name, description=description, layers=layers)
        )

    return Config(
        expected_width=_optional_int("expectedWidth"),
        expected_height=_optional_int("expectedHeight"),
        forbidden_groups=forbidden,
        required_visible_groups=required_visible,
        expressions=tuple(expressions),
    )


# ---- PSD の走査 --------------------------------------------------------------


def iter_all(node) -> "list":
    """PSD のレイヤーツリーを深さ優先で列挙する（グループも含む）。"""
    result = []
    for layer in node:
        result.append(layer)
        if layer.is_group():
            result.extend(iter_all(layer))
    return result


def find_unique(psd, name: str, *, want_group: bool, missing_exit_code: int = EXIT_CONFIG):
    """名前でレイヤー/グループを 1 件だけ引く。0 件・複数件は設定ミスとして失敗させる。"""
    matches = [
        layer
        for layer in iter_all(psd)
        if layer.name == name and layer.is_group() == want_group
    ]
    kind = "グループ" if want_group else "レイヤー"
    if not matches:
        available = sorted({layer.name for layer in iter_all(psd) if layer.is_group() == want_group})
        fail(
            missing_exit_code,
            f"{kind} '{name}' が PSD に見つかりません。\n"
            f"  PSD 内の{kind}名: {available}\n"
            "  （全角スペース U+3000 と半角スペースの違いに注意してください）",
        )
    if len(matches) > 1:
        fail(missing_exit_code, f"{kind} '{name}' が PSD に {len(matches)} 件あり一意に決まりません。")
    return matches[0]


def assert_safety(psd, config: Config) -> None:
    """服を脱がせた状態などにならないことを、書き出し直前に PSD の実状態で検証する。

    レビュー H1: 安全確認の対象グループが PSD に見つからない場合は「検証不能」として
    `EXIT_SAFETY` で中止する（想定と違う構成の PSD を黙って書き出さない）。
    """
    for name in config.forbidden_groups:
        group = find_unique(psd, name, want_group=True, missing_exit_code=EXIT_SAFETY)
        if group.visible:
            fail(
                EXIT_SAFETY,
                f"禁止グループ '{name}' が表示状態になっています。"
                "着せ替え差分は本スクリプトの対象外です（docs/licenses.md §3.1、readme.txt 利用のルール）。",
            )

    for name in config.required_visible_groups:
        group = find_unique(psd, name, want_group=True, missing_exit_code=EXIT_SAFETY)
        if not group.visible:
            fail(
                EXIT_SAFETY,
                f"表示が必須のグループ '{name}' が非表示になっています。"
                "服を脱がせた状態での利用は厳禁のため中止します（readme.txt 禁止事項）。",
            )


def is_same_or_under(path: Path, parent: Path) -> bool:
    """`path` が `parent` と同じか、その配下か（PurePath の比較は Windows では大文字小文字を区別しない）。"""
    return path == parent or parent in path.parents


def find_repository_root(path: Path) -> "Path | None":
    """`path` 自身から上へ辿り、最初に見つかったリポジトリ（Unity プロジェクト）のルートを返す。

    PR #135 再レビュー M1: リポジトリの場所に依存しない一般解。出力先がどのツリーの中にあっても
    （本体ツリー・別の worktree・無関係な別リポジトリでも）検出できる。見つからなければ None。
    """
    for candidate in (path, *path.parents):
        for marker in REPOSITORY_MARKERS:
            try:
                if (candidate / marker).exists():
                    return candidate
            except OSError:
                # 権限が無い・長すぎる等で判定できない階層は「目印なし」として次へ進む。
                continue
    return None


def assert_output_dir_allowed(out_dir: Path) -> None:
    """出力先が git 管理下にならないことを検証する（レビュー H2、再レビュー M1）。

    生成物は二次配布禁止の素材（docs/licenses.md §3）の加工物なので、リポジトリ内
    （`External/` 配下を除く）や `Assets/` 配下には置けない。PowerShell ラッパー
    （`scripts/generate-tsumugi-expressions.ps1`）にも同じ検証があるが、本スクリプトを
    直接実行された場合にも効くよう、実際に書き込む側であるここでも必ず検証する。

    判定の基準にするリポジトリルートは 2 つ。
    1. 出力先から上へ辿って見つかったリポジトリ（`find_repository_root`）。
       worktree から本体ツリーの `Assets/` を指定した場合もこれで捕まる。
    2. 本スクリプト自身のツリー（`SCRIPT_REPOSITORY_ROOT`）。目印が消えている等で
       1 が失敗しても、最低限これだけは守る（配布 zip に同梱したときは None なので使わない、#219）。
    """
    try:
        resolved = Path(out_dir).expanduser().resolve()
    except (OSError, RuntimeError, ValueError) as exc:
        fail(EXIT_USAGE, f"--out-dir を絶対パスとして解決できません（{type(exc).__name__}）: {out_dir}: {exc}")

    roots: list[Path] = []
    detected = find_repository_root(resolved)
    if detected is not None:
        roots.append(detected)
    if SCRIPT_REPOSITORY_ROOT is not None and SCRIPT_REPOSITORY_ROOT not in roots:
        roots.append(SCRIPT_REPOSITORY_ROOT)

    for root in roots:
        _assert_not_inside_repository(resolved, root)


def _assert_not_inside_repository(resolved: Path, repository_root: Path) -> None:
    if is_same_or_under(resolved, repository_root / "Assets"):
        fail(
            EXIT_SAFETY,
            f"--out-dir に Assets/ 配下は指定できません: {resolved}\n"
            f"  検出したリポジトリ: {repository_root}\n"
            "  Assets/ 配下はビルド成果物に同梱されるため、立ち絵の加工物を置けません"
            "（docs/licenses.md §3.1、External/README.md §5.4）。",
        )

    if is_same_or_under(resolved, repository_root) and not is_same_or_under(
        resolved, repository_root / "External"
    ):
        fail(
            EXIT_SAFETY,
            f"--out-dir がリポジトリ内（External/ 以外）です: {resolved}\n"
            f"  検出したリポジトリ: {repository_root}\n"
            "  立ち絵の加工物は二次配布禁止（docs/licenses.md §3.1）のため git 管理下に置けません。\n"
            "  データルート（Application.persistentDataPath 相当）か External/ 配下を指定してください。\n"
            "  ホームやデータルート自体が git 管理下の場合は、--out-dir で管理外のパスを指定してください。",
        )


def expression_bbox(psd) -> "tuple[int, int, int, int] | None":
    """いま表示されている表情レイヤー（ALLOWED_EXPRESSION_GROUPS の子）の外接矩形（キャンバス座標）。

    表示中のレイヤーが無い（または大きさが 0）なら None。切り出し範囲から表情がはみ出していないかの
    検証に使う（#191。範囲を狭く指定したときに、差分が画面外に出て気づかないのを防ぐ）。
    """
    boxes = []
    for layer in iter_all(psd):
        if not layer.is_group() or layer.name not in ALLOWED_EXPRESSION_GROUPS or not layer.visible:
            continue
        for child in layer:
            if not child.visible:
                continue
            left, top, right, bottom = child.bbox
            if right > left and bottom > top:
                boxes.append((left, top, right, bottom))
    if not boxes:
        return None
    return (
        min(box[0] for box in boxes),
        min(box[1] for box in boxes),
        max(box[2] for box in boxes),
        max(box[3] for box in boxes),
    )


def box_contains(outer: tuple[int, int, int, int], inner: tuple[int, int, int, int]) -> bool:
    """`inner` が `outer` の中に収まっているか（どちらも (left, top, right, bottom)、右・下は含まない）。"""
    return outer[0] <= inner[0] and outer[1] <= inner[1] and inner[2] <= outer[2] and inner[3] <= outer[3]


def warn_if_expression_outside_crop(psd, expression: Expression, crop_pixels: tuple[int, int, int, int]) -> None:
    """表情レイヤーが切り出し範囲からはみ出していれば警告する（範囲はユーザーの選択なので書き出しは止めない）。"""
    bbox = expression_bbox(psd)
    if bbox is not None and not box_contains(crop_pixels, bbox):
        log(
            f"警告: 表情 '{expression.key}' の表情レイヤー {bbox} が切り出し範囲 {crop_pixels} からはみ出しています。"
            "差分の一部が見えなくなります（--crop を見直してください）。"
        )


def apply_expression(psd, expression: Expression) -> None:
    """設定に従ってレイヤーの表示/非表示を切り替える（PSD ファイル自体は書き換えない）。"""
    for group_name, visible_names in expression.layers.items():
        group = find_unique(psd, group_name, want_group=True)
        group.visible = True

        children = {child.name: child for child in group}
        for name in visible_names:
            if name not in children:
                fail(
                    EXIT_CONFIG,
                    f"グループ '{group_name}' に レイヤー '{name}' がありません。\n"
                    f"  選べるレイヤー名: {sorted(children)}\n"
                    "  （全角スペース U+3000 と半角スペースの違いに注意してください）",
                )

        for child in group:
            child.visible = child.name in visible_names


# ---- 書き出し ----------------------------------------------------------------


def crop_and_resize(image, crop: CropBox, max_height: int):
    """合成済みの画像を切り出し（#191）、`max_height` を超えていれば等比縮小した新しい画像を返す。

    順序は「切り出し → 縮小」。原寸で切り出してから 1 回だけ縮小するので、先に縮小してから切り出すより
    画質が良く、`max_height` がそのまま出力の高さになる（表示サイズから決めた値と対応させやすい）。
    切り出し範囲の px は画像の大きさだけで決まるので 4 枚とも同じになり、表情を切り替えても
    立ち絵の位置はずれない。縮小は等比のみで、縦横比は切り出し範囲のまま変えない。
    """
    from PIL import Image

    if max_height < 0:
        raise ValueError(f"max_height は 0 以上である必要があります（実際: {max_height}）。")

    result = image.crop(crop.to_pixels(image.width, image.height))
    if 0 < max_height < result.height:
        ratio = max_height / result.height
        new_size = (max(1, round(result.width * ratio)), max_height)
        result = result.resize(new_size, Image.LANCZOS)
    return result


def composite_to_png(psd, out_path: Path, max_height: int, crop: CropBox = CROP_BUSTUP) -> int:
    """現在の表示状態で合成し、切り出し・縮小して PNG として保存する。戻り値は書き出したバイト数。"""
    image = psd.composite(viewport=psd.viewbox, force=True, ignore_preview=True)
    if image.mode != "RGBA":
        image = image.convert("RGBA")

    image = crop_and_resize(image, crop, max_height)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(out_path, format="PNG", optimize=True)
    return out_path.stat().st_size


# ---- エントリポイント --------------------------------------------------------


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="春日部つむぎ公式立ち絵 v2.0 の PSD から表情差分 PNG を書き出す（issue #86）。"
    )
    parser.add_argument("--psd", required=True, help="立ち絵 PSD のパス")
    parser.add_argument("--config", required=True, help="レイヤー対応表 JSON のパス")
    parser.add_argument("--out-dir", required=True, help="PNG の出力先ディレクトリ")
    parser.add_argument(
        "--max-height",
        type=int,
        default=DEFAULT_MAX_HEIGHT,
        # 既定値の根拠は DEFAULT_MAX_HEIGHT のコメント（#190 / #191、表示サイズから決めた）。
        help=f"出力 PNG の最大高さ（切り出し後に等比縮小。0 で切り出したままの原寸。既定 {DEFAULT_MAX_HEIGHT}）",
    )
    parser.add_argument(
        "--crop",
        default=DEFAULT_CROP,
        help=(
            "切り出し範囲（#191）。bustup = 頭から腰の上まで（既定、比率 0.22,0.01,0.90,0.45）/ "
            "full = 切り出さない（全身）/ 左,上,右,下 = PSD キャンバスに対する比率（0.0〜1.0）"
        ),
    )
    parser.add_argument("--only", action="append", default=[], help="書き出す表情の key（複数指定可）")
    parser.add_argument("--dry-run", action="store_true", help="検証だけ行い PNG を書き出さない")
    return parser.parse_args(argv)


# 書き出しが終わったときに表示する文言。リポジトリ（開発者）向けの既定と、配布 zip のツール向け
# （tsumugi_expressions_standalone.py が渡す、#219 L5）とで、処理は同じまま文言だけを切り替える。
DEFAULT_COMPLETION_LINES = (
    "完了しました。生成した PNG は git 管理外のユーザーデータです。",
    "二次配布禁止（docs/licenses.md §3）のため、リポジトリ・配布物に含めないでください。",
)


def main(argv: list[str], completion_lines: "tuple[str, ...]" = DEFAULT_COMPLETION_LINES) -> int:
    args = parse_args(argv)

    if args.max_height < 0:
        fail(EXIT_USAGE, f"--max-height は 0 以上である必要があります（実際: {args.max_height}）。")

    try:
        crop = parse_crop(args.crop)
    except ValueError as exc:
        fail(EXIT_USAGE, str(exc))

    # レビュー H2: 出力先の検証は何よりも先に行う（依存の導入状況・PSD の有無に関わらず、
    # 置いてはいけない場所が指定されていたら 1 枚も書き出さずに止める）。
    out_dir = Path(args.out_dir)
    assert_output_dir_allowed(out_dir)

    try:
        from psd_tools import PSDImage  # noqa: F401
        import PIL  # noqa: F401
    except ImportError as exc:
        fail(
            EXIT_DEPENDENCY,
            "必要な Python パッケージがありません（psd-tools は MIT、Pillow は MIT-CMU）。"
            f"`python -m pip install psd-tools Pillow` を実行してください: {exc}",
        )

    from psd_tools import PSDImage

    psd_path = Path(args.psd)
    if not psd_path.is_file():
        fail(EXIT_USAGE, f"PSD が見つかりません: {psd_path}")

    config = load_config(Path(args.config))

    log(f"PSD    : {psd_path}")
    log(f"設定   : {args.config}")
    log(f"出力先 : {out_dir}")
    log(f"切り出し: {args.crop}（比率 {crop.left},{crop.top},{crop.right},{crop.bottom}）")

    try:
        psd = PSDImage.open(psd_path)
    except Exception as exc:  # psd-tools は独自例外を多数投げるため広く捕捉して読み替える
        fail(EXIT_USAGE, f"PSD を開けません（{type(exc).__name__}）: {psd_path}: {exc}")

    log(f"PSD サイズ: {psd.width}x{psd.height}")
    if (
        config.expected_width
        and config.expected_height
        and (psd.width, psd.height) != (config.expected_width, config.expected_height)
    ):
        log(
            "警告: 設定ファイルが想定する PSD サイズ "
            f"({config.expected_width}x{config.expected_height}) と異なります。"
            "レイヤー構成が違う版の可能性があります。"
        )

    assert_safety(psd, config)
    log("安全確認: 制服グループ表示 / 私服グループ非表示 を確認しました。")

    try:
        crop_pixels = crop.to_pixels(psd.width, psd.height)
    except ValueError as exc:
        fail(EXIT_USAGE, str(exc))
    log(f"切り出し範囲: {crop_pixels}（{crop_pixels[2] - crop_pixels[0]}x{crop_pixels[3] - crop_pixels[1]} px）")

    targets = config.expressions
    if args.only:
        wanted = set(args.only)
        unknown = wanted - {e.key for e in targets}
        if unknown:
            fail(EXIT_USAGE, f"--only に未知の key があります: {sorted(unknown)}")
        targets = tuple(e for e in targets if e.key in wanted)

    # 先に全表情のレイヤー名を検証してから書き出す（途中で失敗して中途半端な出力を残さない）。
    for expression in targets:
        apply_expression(psd, expression)
        warn_if_expression_outside_crop(psd, expression, crop_pixels)
    assert_safety(psd, config)
    log(f"レイヤー名の検証: {len(targets)} 件すべて OK。")

    if args.dry_run:
        for expression in targets:
            log(f"[DryRun] {expression.key}: {out_dir / expression.file_name}（{expression.description}）")
        return EXIT_OK

    for expression in targets:
        apply_expression(psd, expression)
        assert_safety(psd, config)
        out_path = out_dir / expression.file_name
        size = composite_to_png(psd, out_path, args.max_height, crop)
        log(f"書き出し: {out_path}（{expression.description}、{size:,} バイト）")

    log("")
    for line in completion_lines:
        log(line)
    return EXIT_OK


if __name__ == "__main__":
    # Windows の既定コンソール（cp932）でも日本語ログが落ちないようにする。
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass
    sys.exit(main(sys.argv[1:]))
