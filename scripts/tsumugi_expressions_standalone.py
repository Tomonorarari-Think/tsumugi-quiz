#!/usr/bin/env python3
"""配布 zip に同梱する立ち絵の表情生成ツールの入口（issue #219）。

配布 zip だけを受け取った人（リポジトリなし）が、自分で入手した公式の立ち絵 zip から
9 表情を生成し、アプリのデータルートへ書き出せるようにする。

- 本ファイルは `scripts/package-release.ps1` が `generate_tsumugi_expressions.py`（生成の本体）と一緒に
  zip の `tools/tsumugi-expressions/` へ**そのままコピー**する。PSD の加工・安全装置（制服・私服の検証、
  表情 4 グループへの限定、出力先の検証）は本体の `main` に任せ、ここには書かない（ロジックを二重に持たない）。
- ここで行うのは、リポジトリが無い環境で本体の引数を用意することだけ。
  1. 立ち絵の入手物（zip / 展開したフォルダ / PSD）から PSD を見つける。zip なら PSD だけを一時フォルダに
     取り出し、終わったら消す（原本をアプリのフォルダやデータルートに残さない）。
  2. 出力先を `AppPaths.DataRoot` と同じ規則で決める（下の `resolve_data_root`）。
  3. 出力先がアプリのフォルダ（zip を展開した場所）の中なら拒否する。アプリのフォルダをそのまま人に渡すと
     生成物まで渡してしまう（二次配布、docs/licenses.md §3.1）ため。
  4. データルートの consent.json に、アプリと同じ条件で有効な同意があることを確かめる（#219 H1、
     2026-10-03 ユーザー決定）。無ければ何も書き出さずに終了コード 5（EXIT_CONSENT）で止める。
     `--dry-run` でも同じ。判定は `tsumugi_app_consent.py`（C# の実装との対応はそちらの docstring）。

データルートの規則（`TsumugiQuiz.Core.AppPaths` / `AppPathsBootstrap` と同じ優先順位）:
  1. `--data-root`（アプリの起動引数 `-tq-data-root` に当たる明示指定）
  2. 環境変数 `TSUMUGI_DATA_ROOT`
  3. `Application.persistentDataPath` 相当 = `<LocalLow>\\<companyName>\\<productName>`。
     LocalLow は Unity と同じく SHGetKnownFolderPath(FOLDERID_LocalAppDataLow) で求め、取れなければ
     `%USERPROFILE%\\AppData\\LocalLow`（Unity の Application.persistentDataPath のスクリプトリファレンス）。
     companyName / productName はアプリのフォルダの `<製品名>_Data/app.info`（Unity がビルド時に書き出す。
     1 行目が companyName、2 行目が productName。2026-10-03 のビルドで実測）から読む。

アプリと違う点（#219 L4）: 値が不正なとき、アプリ（`AppPathsBootstrap.TryDisableInvalidEnvironmentRoot` /
`TryConfigureDataRoot`）は `TSUMUGI_DATA_ROOT` や `-tq-data-root` を無視して既定のデータルートへ黙って
フォールバックする（起動を止めないため）。本ツールは、不正な `TSUMUGI_DATA_ROOT` / `--data-root`
（相対パスなど）を受け取ったら書き出さずに止める（どこへ書くかを利用者が誤解したまま進めないため）。

標準ライブラリだけで動く（psd-tools / Pillow は本体が PSD を開くときに初めて読み込む）。
"""

from __future__ import annotations

import argparse
import os
import shutil
import sys
import tempfile
import zipfile
import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Mapping

TOOL_DIR = Path(__file__).resolve().parent
if str(TOOL_DIR) not in sys.path:
    sys.path.insert(0, str(TOOL_DIR))

import generate_tsumugi_expressions as core  # noqa: E402  （同じフォルダの本体。zip でもリポジトリでも隣にある）
import tsumugi_app_consent as consent  # noqa: E402  （アプリの同意の判定、#219 H1）

# 有効な同意が無いときの終了コード。本体の EXIT_OK〜EXIT_SAFETY（0〜4）と重ならない値にする。
EXIT_CONSENT = 5

DATA_ROOT_ENVIRONMENT_VARIABLE = "TSUMUGI_DATA_ROOT"
OUTPUT_SUBDIRECTORY = "tsumugi"
APP_INFO_GLOB = "*_Data/app.info"
# zip 内の配置は tools/tsumugi-expressions/ なので、2 つ上がアプリのフォルダ（TsumugiQuiz.exe のある場所）。
APP_ROOT_LEVELS_UP = 2
# 既定の設定ファイル。zip では隣の expressions.json、リポジトリでは docs/ のサンプル。
CONFIG_CANDIDATES = (
    TOOL_DIR / "expressions.json",
    TOOL_DIR.parent / "docs" / "tsumugi-expressions.sample.json",
)
# 同意の判定に使う規約の本文。zip では package-release がコピーした terms/、リポジトリでは Resources/Terms。
TERMS_DIR_CANDIDATES = (
    TOOL_DIR / "terms",
    TOOL_DIR.parent / "Assets" / "TsumugiQuiz" / "Resources" / "Terms",
)
# 配布 zip 版で、書き出しが終わったときに表示する文言（本体の DEFAULT_COMPLETION_LINES の代わり、#219 L5）。
COMPLETION_LINES = (
    "完了しました。",
    "作った画像は自分のパソコンの中だけで使い、ほかの人には渡さないでください（立ち絵の規約の二次配布の禁止）。",
)
# 引数を省いたときに探す場所（ダウンロード フォルダ）と名前。公式の zip 名は `春日部つむぎ立ち絵_公式_v2.0.zip`。
DOWNLOADED_ZIP_GLOB = "春日部つむぎ立ち絵*.zip"
# zip から取り出す PSD の大きさの上限（公式 v2.0 の PSD は 14,856,318 バイト。壊れた zip・別物の検出用）。
MAX_PSD_BYTES = 256 * 1024 * 1024
ZIP_UTF8_FLAG = 0x800

# Windows の既知フォルダ（KNOWNFOLDERID）。
FOLDERID_LOCAL_APP_DATA_LOW = "{A520A1A4-1780-4FF6-BD18-167343C5AF16}"
FOLDERID_DOWNLOADS = "{374DE290-123F-4565-9164-39C4925E467B}"

_INVALID_NAME_CHARS = set('<>:"/\\|?*')


@dataclass(frozen=True)
class AppIdentity:
    """app.info の companyName / productName（不変）。"""

    company: str
    product: str


@dataclass(frozen=True)
class DataRoot:
    """解決したデータルートと、その出どころ（explicit / environment / app）。"""

    path: Path
    source: str


# ---- データルート ------------------------------------------------------------


def known_folder_path(folder_id: str) -> "Path | None":
    """SHGetKnownFolderPath で既知フォルダのパスを得る（Windows 以外・失敗時は None）。"""
    if sys.platform != "win32":
        return None
    try:
        import ctypes
        from ctypes import wintypes

        class GUID(ctypes.Structure):
            _fields_ = [
                ("Data1", wintypes.DWORD),
                ("Data2", wintypes.WORD),
                ("Data3", wintypes.WORD),
                ("Data4", ctypes.c_ubyte * 8),
            ]

        import uuid

        raw = uuid.UUID(folder_id).bytes_le
        guid = GUID.from_buffer_copy(raw)
        buffer = ctypes.c_wchar_p()
        shell32 = ctypes.windll.shell32
        result = shell32.SHGetKnownFolderPath(ctypes.byref(guid), 0, None, ctypes.byref(buffer))
        try:
            if result != 0 or not buffer.value:
                return None
            return Path(buffer.value)
        finally:
            ctypes.windll.ole32.CoTaskMemFree(buffer)
    except (OSError, AttributeError, ValueError):
        return None


def local_low_directory(
    environ: Mapping[str, str], known_folder: Callable[[str], "Path | None"] = known_folder_path
) -> "Path | None":
    """LocalLow フォルダ。既知フォルダが取れなければ %USERPROFILE%\\AppData\\LocalLow。"""
    path = known_folder(FOLDERID_LOCAL_APP_DATA_LOW)
    if path is not None:
        return path
    profile = environ.get("USERPROFILE", "").strip()
    if not profile:
        return None
    return Path(profile) / "AppData" / "LocalLow"


def _validate_name(value: str, label: str) -> str:
    name = value.strip()
    if not name or name in (".", ".."):
        raise ValueError(f"app.info の{label}が空か不正です: {value!r}")
    if len(name) > 128 or any(ch in _INVALID_NAME_CHARS or ord(ch) < 0x20 for ch in name):
        raise ValueError(f"app.info の{label}にフォルダ名として使えない文字があります: {value!r}")
    return name


def read_app_identity(app_info: Path) -> AppIdentity:
    """app.info（1 行目 companyName、2 行目 productName）を読んで検証する。"""
    try:
        lines = app_info.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeDecodeError) as exc:
        raise ValueError(f"app.info を読めません（{type(exc).__name__}）: {app_info}: {exc}") from None
    if len(lines) < 2:
        raise ValueError(f"app.info の形式が想定と違います（2 行以上を想定）: {app_info}")
    return AppIdentity(_validate_name(lines[0], "会社名"), _validate_name(lines[1], "製品名"))


def find_app_root(tool_dir: Path = TOOL_DIR) -> "Path | None":
    """アプリのフォルダ（`<製品名>_Data/app.info` がある場所）。zip の配置どおりでなければ None。"""
    parents = tool_dir.parents
    if len(parents) < APP_ROOT_LEVELS_UP:
        return None  # ドライブの直下などに置かれた（zip の配置どおりではない、#219 L3）
    candidate = parents[APP_ROOT_LEVELS_UP - 1]
    return candidate if any(candidate.glob(APP_INFO_GLOB)) else None


def find_app_info(app_root: Path) -> Path:
    matches = sorted(app_root.glob(APP_INFO_GLOB))
    if len(matches) != 1:
        raise ValueError(f"アプリのフォルダに app.info が {len(matches)} 件あり、1 つに決まりません: {app_root}")
    return matches[0]


def resolve_data_root(
    explicit: "str | None",
    environ: Mapping[str, str],
    app_root: "Path | None",
    local_low: Callable[[], "Path | None"],
) -> DataRoot:
    """`AppPaths.DataRoot` と同じ優先順位でデータルートを決める。決められなければ ValueError。"""
    if explicit is not None and explicit.strip():
        return DataRoot(_require_absolute(explicit, "--data-root"), "explicit")

    from_environment = environ.get(DATA_ROOT_ENVIRONMENT_VARIABLE, "")
    if from_environment.strip():
        return DataRoot(_require_absolute(from_environment, f"環境変数 {DATA_ROOT_ENVIRONMENT_VARIABLE}"), "environment")

    if app_root is None:
        raise ValueError(
            "アプリのフォルダが見つかりません。このツールは、配布 zip を展開したフォルダの "
            "tools\\tsumugi-expressions\\ に置いたまま実行してください（または --data-root でデータルートを指定してください）。"
        )
    identity = read_app_identity(find_app_info(app_root))
    base = local_low()
    if base is None:
        raise ValueError("LocalLow フォルダを特定できません。--data-root でデータルートを指定してください。")
    return DataRoot(base / identity.company / identity.product, "app")


def _require_absolute(value: str, label: str) -> Path:
    path = Path(value.strip()).expanduser()
    if not path.is_absolute():
        raise ValueError(f"{label} は絶対パスで指定してください: {value!r}")
    return path


def assert_output_outside_app(out_dir: Path, app_root: "Path | None") -> None:
    """出力先がアプリのフォルダの中なら拒否する（フォルダごと人に渡すと二次配布になるため）。"""
    if app_root is None:
        return
    resolved = out_dir.expanduser().resolve()
    if core.is_same_or_under(resolved, app_root.resolve()):
        core.fail(
            core.EXIT_SAFETY,
            f"出力先がアプリのフォルダの中です: {resolved}\n"
            "  アプリのフォルダをそのまま人に渡すと、立ち絵から作った画像も渡すことになります（二次配布の禁止）。\n"
            "  出力先は既定のデータルート（--data-root / --out-dir を省いたときの場所）にしてください。",
        )


# ---- 立ち絵の入手物（zip / フォルダ / PSD） -----------------------------------


def decode_zip_member_name(info: zipfile.ZipInfo) -> str:
    """zip の項目名を復元する。公式の立ち絵 zip は Shift_JIS（CP932）の名前で UTF-8 フラグが無い。"""
    if info.flag_bits & ZIP_UTF8_FLAG:
        return info.filename
    try:
        return info.filename.encode("cp437").decode("cp932")
    except (UnicodeEncodeError, UnicodeDecodeError):
        return info.filename


def _psd_rank(name: str) -> "int | None":
    """PSD の候補順位（小さいほど優先）。PSD でない・v1.1.1 は None。"""
    lowered = name.lower()
    if not lowered.endswith(".psd") or "v1.1.1" in lowered:
        return None
    return 0 if "v2.0" in lowered else 1


def select_psd_member(infos: "list[zipfile.ZipInfo]") -> zipfile.ZipInfo:
    ranked = []
    for info in infos:
        if info.is_dir():
            continue
        rank = _psd_rank(decode_zip_member_name(info))
        if rank is not None:
            ranked.append((rank, decode_zip_member_name(info), info))
    if not ranked:
        raise ValueError("zip の中に立ち絵の PSD（*.psd、v1.1.1 以外）が見つかりません。公式の立ち絵 zip か確かめてください。")
    ranked.sort(key=lambda item: (item[0], item[1]))
    return ranked[0][2]


def select_psd_in_directory(directory: Path) -> Path:
    ranked = sorted(
        (rank, str(path), path)
        for path in directory.rglob("*")
        if path.is_file() and (rank := _psd_rank(path.name)) is not None
    )
    if not ranked:
        raise ValueError(f"フォルダの中に立ち絵の PSD（*.psd、v1.1.1 以外）が見つかりません: {directory}")
    return ranked[0][2]


def extract_psd(zip_path: Path, destination_dir: Path) -> Path:
    """zip から PSD だけを destination_dir に取り出す（名前は固定。zip 内のパスは使わない）。"""
    try:
        with zipfile.ZipFile(zip_path) as archive:
            info = select_psd_member(archive.infolist())
            if info.flag_bits & 0x1:
                raise ValueError("zip が暗号化されています。公式の立ち絵 zip か確かめてください。")
            if info.file_size > MAX_PSD_BYTES:
                raise ValueError(f"zip の中の PSD が大きすぎます（{info.file_size:,} バイト）。公式の立ち絵 zip か確かめてください。")
            target = destination_dir / "tsumugi-source.psd"
            with archive.open(info) as source, target.open("wb") as sink:
                shutil.copyfileobj(source, sink, length=1024 * 1024)
            return target
    except (zipfile.BadZipFile, zlib.error, EOFError, NotImplementedError, RuntimeError) as exc:
        # 壊れた zip・途中までしか無い zip・対応していない圧縮方式・暗号化（#219 L2）。
        raise ValueError(
            f"zip を開けません（壊れているか、ダウンロードが途中で止まった可能性があります）: {zip_path}"
            f"（{type(exc).__name__}: {exc}）"
        ) from None


def find_downloaded_zip(downloads: "Path | None") -> "Path | None":
    """ダウンロード フォルダの公式立ち絵 zip（v2.0 を優先し、同じ順位なら新しいもの）。"""
    if downloads is None or not downloads.is_dir():
        return None
    candidates = [path for path in downloads.glob(DOWNLOADED_ZIP_GLOB) if path.is_file()]
    if not candidates:
        return None
    candidates.sort(key=lambda path: ("v2.0" not in path.name, -path.stat().st_mtime))
    return candidates[0]


def downloads_directory(environ: Mapping[str, str]) -> "Path | None":
    path = known_folder_path(FOLDERID_DOWNLOADS)
    if path is not None:
        return path
    profile = environ.get("USERPROFILE", "").strip()
    return Path(profile) / "Downloads" if profile else None


def resolve_config(explicit: "str | None") -> Path:
    if explicit:
        path = Path(explicit).expanduser()
        if not path.is_file():
            raise ValueError(f"--config のファイルが見つかりません: {path}")
        return path
    for candidate in CONFIG_CANDIDATES:
        if candidate.is_file():
            return candidate
    raise ValueError(f"表情の設定ファイル（expressions.json）が見つかりません: {TOOL_DIR}")


# ---- エントリポイント --------------------------------------------------------


def parse_args(argv: "list[str]") -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="公式の立ち絵 zip から、場面ごとの表情 9 枚をアプリのデータルートに作る（#219）。"
    )
    parser.add_argument(
        "source",
        nargs="?",
        help="立ち絵の zip / 展開したフォルダ / PSD。省くとダウンロード フォルダの「春日部つむぎ立ち絵*.zip」を探す",
    )
    parser.add_argument("--data-root", help="データルート（既定はアプリと同じ規則で決める）")
    parser.add_argument("--out-dir", help="出力先フォルダを直接指定する（既定は <データルート>\\tsumugi）")
    parser.add_argument("--config", help="表情の設定ファイル（既定は隣の expressions.json）")
    parser.add_argument("--crop", default=core.DEFAULT_CROP, help="切り出し範囲（本体の --crop と同じ）")
    parser.add_argument("--max-height", type=int, default=core.DEFAULT_MAX_HEIGHT, help="最大の高さ（本体と同じ）")
    parser.add_argument("--only", action="append", default=[], help="作る表情の key（複数指定可）")
    parser.add_argument("--dry-run", action="store_true", help="検証だけ行い、画像は書き出さない")
    return parser.parse_args(argv)


def resolve_paths(args: argparse.Namespace, app_root: "Path | None", environ: Mapping[str, str]) -> "tuple[Path, Path]":
    """(データルート, 出力先) を決める。データルートは同意の確認にも使うので、--out-dir があっても決める。"""
    try:
        data_root = resolve_data_root(args.data_root, environ, app_root, lambda: local_low_directory(environ))
    except ValueError as exc:
        core.fail(core.EXIT_USAGE, str(exc))
    if data_root.source == "environment":
        core.log(
            f"注意: データルートを環境変数 {DATA_ROOT_ENVIRONMENT_VARIABLE} から決めました: {data_root.path}\n"
            "  アプリをふつうに起動したときの場所と違う場合は、--data-root で指定し直してください。"
        )
    out_dir = Path(args.out_dir).expanduser() if args.out_dir else data_root.path / OUTPUT_SUBDIRECTORY
    return data_root.path, out_dir


def resolve_terms_dir() -> Path:
    for candidate in TERMS_DIR_CANDIDATES:
        if candidate.is_dir():
            return candidate
    raise ValueError(f"規約のファイル（terms フォルダ）が見つかりません: {TOOL_DIR}。zip をもう一度展開し直してください。")


def assert_app_consent(data_root: Path) -> None:
    """アプリで立ち絵の利用規約に同意していなければ、何も書き出さずに止める（#219 H1、NFR-08）。"""
    try:
        status = consent.check_consent(data_root, resolve_terms_dir())
    except (ValueError, OSError) as exc:
        core.fail(core.EXIT_USAGE, f"同意の確認に必要なファイルを読めません: {exc}")
    if not status.accepted:
        core.fail(
            EXIT_CONSENT,
            "先にアプリ（TsumugiQuiz.exe）を起動し、立ち絵の規約を含む利用規約に同意してください。\n"
            f"  {status.reason}\n"
            "  同意したあとで、もう一度このツールを実行してください（同意を撤回した場合や、規約が更新された場合も同じです）。",
        )
    core.log("同意の確認: アプリで利用規約（立ち絵の規約を含む）に同意済みです。")


def _resolve_source(args: argparse.Namespace, environ: Mapping[str, str]) -> Path:
    if args.source:
        source = Path(args.source.strip().strip('"')).expanduser()
        if not source.exists():
            core.fail(core.EXIT_USAGE, f"指定したファイル・フォルダが見つかりません: {source}")
        return source
    found = find_downloaded_zip(downloads_directory(environ))
    if found is None:
        core.fail(
            core.EXIT_USAGE,
            "立ち絵の zip が見つかりません。\n"
            "  公式の配布元から「春日部つむぎ立ち絵_公式_v2.0.zip」を入手し、ダウンロード フォルダに置くか、\n"
            "  zip を make-expressions.bat の上にドラッグ＆ドロップしてください。",
        )
    return found


def main(argv: "list[str]", environ: "Mapping[str, str] | None" = None) -> int:
    environ = os.environ if environ is None else environ
    args = parse_args(argv)
    app_root = find_app_root()

    data_root, out_dir = resolve_paths(args, app_root, environ)
    assert_output_outside_app(out_dir, app_root)
    core.assert_output_dir_allowed(out_dir)
    # 同意の確認は、立ち絵を読む前・何かを書き出す前に行う（--dry-run でも行う。#219 H1）。
    assert_app_consent(data_root)

    try:
        config = resolve_config(args.config)
    except ValueError as exc:
        core.fail(core.EXIT_USAGE, str(exc))

    source = _resolve_source(args, environ)
    if app_root is not None and core.is_same_or_under(source.resolve(), app_root.resolve()):
        core.log("注意: 立ち絵がアプリのフォルダの中にあります。アプリのフォルダを人に渡すときは、先に立ち絵を取り除いてください。")
    core.log(f"立ち絵   : {source}")

    core_args = ["--config", str(config), "--out-dir", str(out_dir), "--crop", args.crop, "--max-height", str(args.max_height)]
    for key in args.only:
        core_args += ["--only", key]
    if args.dry_run:
        core_args.append("--dry-run")

    if source.is_dir() or source.suffix.lower() == ".psd":
        try:
            psd = select_psd_in_directory(source) if source.is_dir() else source
        except ValueError as exc:
            core.fail(core.EXIT_USAGE, str(exc))
        return _run_core(core_args, psd, out_dir)

    # zip: PSD だけを一時フォルダに取り出し、終わったら一時フォルダごと消す（異常終了の SystemExit でも消える）。
    with tempfile.TemporaryDirectory(prefix="tsumugi-expressions-") as work:
        try:
            psd = extract_psd(source, Path(work))
        except (ValueError, OSError) as exc:
            core.fail(core.EXIT_USAGE, str(exc))
        return _run_core(core_args, psd, out_dir)


def _run_core(core_args: "list[str]", psd: Path, out_dir: Path) -> int:
    result = core.main(["--psd", str(psd), *core_args], completion_lines=COMPLETION_LINES)
    if result == core.EXIT_OK and "--dry-run" not in core_args:
        core.log(f"出力先: {out_dir}")
        core.log("アプリを起動中なら、いったん閉じて起動し直すと新しい表情が表示されます。")
    return result


if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass
    sys.exit(main(sys.argv[1:]))
