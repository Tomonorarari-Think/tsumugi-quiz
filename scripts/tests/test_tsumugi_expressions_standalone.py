"""scripts/tsumugi_expressions_standalone.py（配布 zip 同梱の表情生成ツールの入口、#219）の単体テスト。

実行:
    python -m unittest discover -s scripts/tests -p "test_*.py"

立ち絵素材（zip・PSD・PNG）は二次配布禁止（docs/licenses.md §3）のため使わない。PSD の代わりに
中身がダミーの小さなファイルを入れた zip を一時フォルダに作って検証する。標準ライブラリだけで動く。
ユーザーの実際のデータルート（LocalLow）には触れない（LocalLow・環境変数はすべて差し替える）。
"""

from __future__ import annotations

import importlib.util
import io
import json
import sys
import tempfile
import unittest
import zipfile
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest import mock

SCRIPTS_DIR = Path(__file__).resolve().parents[1]


def _load(name: str):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS_DIR / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


standalone = _load("tsumugi_expressions_standalone")
core = standalone.core

OFFICIAL_PSD = "春日部つむぎ立ち絵_公式_v2.0/春日部つむぎ立ち絵_公式_v2.0.psd"
OLD_PNG = "春日部つむぎ立ち絵_公式_v2.0/春日部つむぎ立ち絵_公式_v1.1.1.png"


def _make_app(root: Path, app_info: str = "Tomonorarari-Think\nTsumugiQuiz") -> Path:
    """配布 zip を展開した形（<app>/TsumugiQuiz_Data/app.info と <app>/tools/tsumugi-expressions/）を作る。"""
    data = root / "app" / "TsumugiQuiz_Data"
    data.mkdir(parents=True)
    (data / "app.info").write_text(app_info, encoding="utf-8")
    tool = root / "app" / "tools" / "tsumugi-expressions"
    tool.mkdir(parents=True)
    return tool


class DataRootTests(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.root = Path(self._temp.name)
        self.tool = _make_app(self.root)
        self.app_root = standalone.find_app_root(self.tool)
        self.local_low = self.root / "LocalLow"

    def tearDown(self):
        self._temp.cleanup()

    def _resolve(self, explicit=None, environ=None):
        return standalone.resolve_data_root(explicit, environ or {}, self.app_root, lambda: self.local_low)

    def test_app_root_is_two_levels_above_tool(self):
        self.assertEqual(self.app_root, self.root / "app")

    def test_app_root_is_none_outside_release_layout(self):
        self.assertIsNone(standalone.find_app_root(SCRIPTS_DIR))

    def test_default_follows_persistent_data_path_rule(self):
        result = self._resolve()
        self.assertEqual(result.path, self.local_low / "Tomonorarari-Think" / "TsumugiQuiz")
        self.assertEqual(result.source, "app")

    def test_explicit_wins_over_environment(self):
        explicit = str(self.root / "explicit")
        result = self._resolve(explicit, {"TSUMUGI_DATA_ROOT": str(self.root / "env")})
        self.assertEqual((result.path, result.source), (Path(explicit), "explicit"))

    def test_environment_wins_over_app_info(self):
        result = self._resolve(None, {"TSUMUGI_DATA_ROOT": str(self.root / "env")})
        self.assertEqual((result.path, result.source), (self.root / "env", "environment"))

    def test_relative_paths_are_rejected(self):
        with self.assertRaises(ValueError):
            self._resolve("relative\\path")
        with self.assertRaises(ValueError):
            self._resolve(None, {"TSUMUGI_DATA_ROOT": "relative"})

    def test_missing_app_root_is_explained(self):
        with self.assertRaisesRegex(ValueError, "tools"):
            standalone.resolve_data_root(None, {}, None, lambda: self.local_low)

    def test_missing_local_low_is_explained(self):
        with self.assertRaisesRegex(ValueError, "--data-root"):
            standalone.resolve_data_root(None, {}, self.app_root, lambda: None)

    def test_app_info_is_validated(self):
        cases = ["OnlyOneLine", "\nTsumugiQuiz", "Company\n..", "Com/pany\nTsumugiQuiz", "Company\nTsumugi:Quiz"]
        for text in cases:
            with self.subTest(text=text):
                (self.app_root / "TsumugiQuiz_Data" / "app.info").write_text(text, encoding="utf-8")
                with self.assertRaises(ValueError):
                    self._resolve()

    def test_local_low_falls_back_to_user_profile(self):
        result = standalone.local_low_directory({"USERPROFILE": str(self.root / "user")}, lambda _: None)
        self.assertEqual(result, self.root / "user" / "AppData" / "LocalLow")
        self.assertIsNone(standalone.local_low_directory({}, lambda _: None))

    def test_local_low_prefers_known_folder(self):
        result = standalone.local_low_directory({"USERPROFILE": "C:\\ignored"}, lambda _: self.local_low)
        self.assertEqual(result, self.local_low)

    @unittest.skipUnless(sys.platform == "win32", "Windows の既知フォルダ API")
    def test_known_folder_api_returns_absolute_local_low(self):
        path = standalone.known_folder_path(standalone.FOLDERID_LOCAL_APP_DATA_LOW)
        self.assertIsNotNone(path)
        self.assertTrue(path.is_absolute())
        self.assertEqual(path.name.lower(), "locallow")


class OutputDirTests(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.root = Path(self._temp.name)
        self.app_root = standalone.find_app_root(_make_app(self.root))

    def tearDown(self):
        self._temp.cleanup()

    def test_output_inside_app_folder_is_rejected(self):
        for inside in (self.app_root, self.app_root / "tsumugi", self.app_root / "tools" / "tsumugi-expressions"):
            with self.subTest(inside=inside), redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as caught:
                    standalone.assert_output_outside_app(inside, self.app_root)
                self.assertEqual(caught.exception.code, core.EXIT_SAFETY)

    def test_output_outside_app_folder_is_allowed(self):
        standalone.assert_output_outside_app(self.root / "LocalLow" / "tsumugi", self.app_root)
        standalone.assert_output_outside_app(self.app_root / "tsumugi", None)

    def test_core_does_not_treat_release_tools_folder_as_repository(self):
        # zip 内の配置では本体の SCRIPT_REPOSITORY_ROOT は None（tools/ をリポジトリ扱いしない）。
        tool_copy = self.app_root / "tools" / "tsumugi-expressions" / "generate_tsumugi_expressions.py"
        self.assertIsNone(core._script_repository_root(tool_copy))

    def test_core_still_detects_its_own_repository(self):
        self.assertEqual(core._script_repository_root(SCRIPTS_DIR / "generate_tsumugi_expressions.py"), SCRIPTS_DIR.parent)


class ZipSourceTests(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.root = Path(self._temp.name)

    def tearDown(self):
        self._temp.cleanup()

    def _zip(self, entries: dict) -> Path:
        path = self.root / "source.zip"
        with zipfile.ZipFile(path, "w") as archive:
            for name, data in entries.items():
                archive.writestr(name, data)
        return path

    def test_cp932_names_without_utf8_flag_are_restored(self):
        # 公式の立ち絵 zip は CP932 の名前で UTF-8 フラグが無い。zipfile はそれを CP437 として読む。
        info = zipfile.ZipInfo(OFFICIAL_PSD.encode("cp932").decode("cp437"))
        info.flag_bits = 0
        self.assertEqual(standalone.decode_zip_member_name(info), OFFICIAL_PSD)

    def test_utf8_names_are_kept(self):
        info = zipfile.ZipInfo(OFFICIAL_PSD)
        info.flag_bits = standalone.ZIP_UTF8_FLAG
        self.assertEqual(standalone.decode_zip_member_name(info), OFFICIAL_PSD)

    def test_v2_psd_is_preferred_and_v1_is_ignored(self):
        infos = [zipfile.ZipInfo(name) for name in ("a/old_v1.1.1.psd", "a/other.psd", "a/x_v2.0.psd", "a/readme.txt")]
        self.assertEqual(standalone.select_psd_member(infos).filename, "a/x_v2.0.psd")
        with self.assertRaises(ValueError):
            standalone.select_psd_member([zipfile.ZipInfo("a/old_v1.1.1.psd"), zipfile.ZipInfo("a/readme.txt")])

    def test_extract_psd_writes_only_the_psd_under_a_fixed_name(self):
        source = self._zip({OFFICIAL_PSD: b"8BPS-dummy", OLD_PNG: b"png", "../escape.psd.txt": b"x"})
        work = self.root / "work"
        work.mkdir()
        extracted = standalone.extract_psd(source, work)
        self.assertEqual(extracted, work / "tsumugi-source.psd")
        self.assertEqual(extracted.read_bytes(), b"8BPS-dummy")
        self.assertEqual(sorted(p.name for p in work.iterdir()), ["tsumugi-source.psd"])

    def test_extract_rejects_oversized_and_broken_zip(self):
        source = self._zip({OFFICIAL_PSD: b"x" * 16})
        original = standalone.MAX_PSD_BYTES
        standalone.MAX_PSD_BYTES = 8
        try:
            with self.assertRaisesRegex(ValueError, "大きすぎ"):
                standalone.extract_psd(source, self.root)
        finally:
            standalone.MAX_PSD_BYTES = original
        broken = self.root / "broken.zip"
        broken.write_bytes(b"not a zip")
        with self.assertRaisesRegex(ValueError, "zip を開けません"):
            standalone.extract_psd(broken, self.root)

    def test_psd_in_extracted_folder(self):
        folder = self.root / "extracted" / "春日部つむぎ立ち絵_公式_v2.0"
        folder.mkdir(parents=True)
        (folder / "春日部つむぎ立ち絵_公式_v1.1.1.psd").write_bytes(b"old")
        (folder / "春日部つむぎ立ち絵_公式_v2.0.psd").write_bytes(b"new")
        self.assertEqual(standalone.select_psd_in_directory(self.root / "extracted").read_bytes(), b"new")

    def test_downloaded_zip_prefers_v2(self):
        downloads = self.root / "Downloads"
        downloads.mkdir()
        (downloads / "春日部つむぎ立ち絵_公式_v1.1.1.zip").write_bytes(b"")
        (downloads / "春日部つむぎ立ち絵_公式_v2.0.zip").write_bytes(b"")
        (downloads / "unrelated.zip").write_bytes(b"")
        self.assertEqual(standalone.find_downloaded_zip(downloads).name, "春日部つむぎ立ち絵_公式_v2.0.zip")
        self.assertIsNone(standalone.find_downloaded_zip(self.root / "missing"))


class ZipRobustnessTests(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.root = Path(self._temp.name)

    def tearDown(self):
        self._temp.cleanup()

    def test_traversal_named_psd_is_written_under_fixed_name_only(self):
        # zip の中の PSD の名前が ../x.psd でも、取り出し先は作業フォルダの固定の名前だけ（#219 L7）。
        source = self.root / "evil.zip"
        with zipfile.ZipFile(source, "w") as archive:
            archive.writestr("../x.psd", b"8BPS-evil")
        work = self.root / "work"
        work.mkdir()
        extracted = standalone.extract_psd(source, work)
        self.assertEqual(extracted, work / "tsumugi-source.psd")
        self.assertEqual(extracted.read_bytes(), b"8BPS-evil")
        self.assertFalse((self.root / "x.psd").exists())
        self.assertEqual([p.name for p in work.iterdir()], ["tsumugi-source.psd"])

    def test_corrupted_member_is_reported_as_value_error(self):
        # 中身の圧縮データが壊れている zip（zlib.error など）も利用者向けのメッセージにする（#219 L2）。
        source = self.root / "corrupt.zip"
        with zipfile.ZipFile(source, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr(OFFICIAL_PSD, b"A" * 4096)
        data = bytearray(source.read_bytes())
        start = data.index(b"PK\x03\x04") + 30 + len(OFFICIAL_PSD.encode("utf-8"))
        for offset in range(start, start + 8):
            data[offset] ^= 0xFF
        source.write_bytes(bytes(data))
        work = self.root / "work"
        work.mkdir()
        with self.assertRaisesRegex(ValueError, "zip を開けません"):
            standalone.extract_psd(source, work)

    def test_app_root_at_drive_root_does_not_raise(self):
        # ドライブの直下に置かれた場合（parents が 1 つしか無い）も IndexError にしない（#219 L3）。
        self.assertIsNone(standalone.find_app_root(Path(Path.cwd().anchor) / "tools-only"))


class MainTests(unittest.TestCase):
    """main() を通した確認。生成の本体（core.main）は差し替え、PSD・画像は扱わない（#219 L7 / H1）。"""

    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.root = Path(self._temp.name)
        self.app_root = standalone.find_app_root(_make_app(self.root))
        self.data_root = self.root / "data-root"
        self.source = self.root / "source.zip"
        with zipfile.ZipFile(self.source, "w") as archive:
            archive.writestr(OFFICIAL_PSD, b"8BPS-dummy")
        self.required = standalone.consent.load_required_terms(standalone.resolve_terms_dir())
        self.calls = []
        self._patches = [
            mock.patch.object(standalone, "find_app_root", lambda *a, **k: self.app_root),
            mock.patch.object(standalone.core, "main", self._fake_core_main),
        ]
        for patch in self._patches:
            patch.start()

    def tearDown(self):
        for patch in self._patches:
            patch.stop()
        self._temp.cleanup()

    def _fake_core_main(self, argv, completion_lines=None):
        psd = Path(argv[argv.index("--psd") + 1])
        self.calls.append({"argv": argv, "psd": psd, "psd_existed": psd.is_file(), "lines": completion_lines})
        return core.EXIT_OK

    def _write_consent(self, records):
        self.data_root.mkdir(parents=True, exist_ok=True)
        (self.data_root / "consent.json").write_text(json.dumps(records), encoding="utf-8")

    def _valid_records(self):
        return [
            {"termsId": t.terms_id, "sha256": t.sha256, "acceptedAtUtc": "2026-10-03T00:00:00Z", "appVersion": "1.0"}
            for t in self.required
        ]

    def _main(self, *extra):
        argv = [str(self.source), "--data-root", str(self.data_root), *extra]
        with redirect_stderr(io.StringIO()), redirect_stdout(io.StringIO()):
            try:
                return standalone.main(argv, environ={})
            except SystemExit as exc:
                return exc.code

    def test_valid_consent_runs_core_and_removes_temp_folder(self):
        self._write_consent(self._valid_records())
        self.assertEqual(self._main(), core.EXIT_OK)
        self.assertEqual(len(self.calls), 1)
        call = self.calls[0]
        self.assertTrue(call["psd_existed"])
        self.assertFalse(call["psd"].parent.exists(), "一時フォルダが消えていません")
        self.assertEqual(Path(call["argv"][call["argv"].index("--out-dir") + 1]), self.data_root / "tsumugi")
        self.assertEqual(call["lines"], standalone.COMPLETION_LINES)

    def test_dry_run_is_forwarded(self):
        self._write_consent(self._valid_records())
        self.assertEqual(self._main("--dry-run"), core.EXIT_OK)
        self.assertIn("--dry-run", self.calls[0]["argv"])

    def test_without_consent_nothing_runs(self):
        cases = {
            "no-record": None,
            "revoked": [],
            "version-mismatch": [dict(r, sha256="0" * 64) for r in self._valid_records()],
            "broken-json": '[{"termsId": ',
        }
        for name, content in cases.items():
            for dry_run in (False, True):
                with self.subTest(case=name, dry_run=dry_run):
                    consent_path = self.data_root / "consent.json"
                    if consent_path.exists():
                        consent_path.unlink()
                    if isinstance(content, str):
                        self.data_root.mkdir(parents=True, exist_ok=True)
                        consent_path.write_text(content, encoding="utf-8")
                    elif content is not None:
                        self._write_consent(content)
                    code = self._main("--dry-run") if dry_run else self._main()
                    self.assertEqual(code, standalone.EXIT_CONSENT)
                    self.assertEqual(self.calls, [])
                    self.assertFalse((self.data_root / "tsumugi").exists())

    def test_output_inside_app_folder_is_rejected_before_consent(self):
        code = self._main("--out-dir", str(self.app_root / "tsumugi"))
        self.assertEqual(code, core.EXIT_SAFETY)
        self.assertEqual(self.calls, [])

    def test_exit_codes_do_not_overlap(self):
        self.assertNotIn(
            standalone.EXIT_CONSENT,
            (core.EXIT_OK, core.EXIT_USAGE, core.EXIT_DEPENDENCY, core.EXIT_CONFIG, core.EXIT_SAFETY),
        )


class ConfigTests(unittest.TestCase):
    def test_default_config_in_repository_is_the_sample(self):
        self.assertEqual(standalone.resolve_config(None).name, "tsumugi-expressions.sample.json")

    def test_missing_explicit_config_is_rejected(self):
        with self.assertRaises(ValueError):
            standalone.resolve_config(str(SCRIPTS_DIR / "missing.json"))


if __name__ == "__main__":
    unittest.main()
