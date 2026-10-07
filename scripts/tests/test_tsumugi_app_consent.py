"""scripts/tsumugi_app_consent.py（アプリの同意の記録の判定、#219 H1）の単体テスト。

実行:
    python -m unittest discover -s scripts/tests -p "test_*.py"

scripts/tests/fixtures/ の入力は C# の EditMode テスト（ExpressionToolCompatibilityTests）と共有している。
同じ入力に対して、Python の移植とアプリ（C#）が同じ判定・同じハッシュになることを両方から確かめる。
標準ライブラリだけで動く。ユーザーの実際のデータルートには触れない（一時フォルダだけを使う）。
"""

from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS_DIR = Path(__file__).resolve().parents[1]
FIXTURES_DIR = Path(__file__).resolve().parent / "fixtures"
REPO_TERMS_DIR = SCRIPTS_DIR.parent / "Assets" / "TsumugiQuiz" / "Resources" / "Terms"


def _load(name: str):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS_DIR / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


consent = _load("tsumugi_app_consent")


def write_consent(data_root: Path, records) -> Path:
    data_root.mkdir(parents=True, exist_ok=True)
    path = data_root / consent.CONSENT_FILE_NAME
    path.write_text(json.dumps(records, ensure_ascii=False), encoding="utf-8")
    return path


def records_for(required, **overrides):
    return [
        {
            "termsId": terms.terms_id,
            "sha256": overrides.get(terms.terms_id, terms.sha256),
            "acceptedAtUtc": "2026-10-03T09:12:34.5678901Z",
            "appVersion": "1.0",
        }
        for terms in required
    ]


class TermsHashTests(unittest.TestCase):
    def test_shared_vectors(self):
        vectors = json.loads((FIXTURES_DIR / "terms-hash-vectors.json").read_text(encoding="utf-8"))
        self.assertGreater(len(vectors), 0)
        for vector in vectors:
            with self.subTest(name=vector["name"]):
                self.assertEqual(consent.compute_terms_body_hash(vector["text"]), vector["sha256"])

    def test_sha256_of_abc_matches_csharp_test(self):
        # TermsHasherTests.ComputeSha256Hex_KnownInput_ReturnsExpectedHash と同じ値。
        self.assertEqual(
            consent.compute_sha256_hex("abc"), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        )

    def test_dotnet_trim_differs_from_python_strip(self):
        self.assertEqual(consent.dotnet_trim("\x1cbody\x1c"), "\x1cbody\x1c")
        self.assertEqual(consent.dotnet_trim("　\u0085body  "), "body")

    def test_required_terms_from_repository(self):
        required = consent.load_required_terms(REPO_TERMS_DIR)
        self.assertEqual(
            sorted(t.terms_id for t in required),
            ["tsumugi-illustration-terms", "tsumugi-voice-credit", "voicevox-models-terms", "voicevox-onnxruntime-terms"],
        )
        self.assertTrue(all(len(t.sha256) == 64 for t in required))

    def test_missing_terms_dir_is_an_error(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError):
                consent.load_required_terms(Path(temp))


class SharedConsentCaseTests(unittest.TestCase):
    def test_same_verdict_as_app(self):
        directory = FIXTURES_DIR / "consent"
        manifest = json.loads((directory / "cases.json").read_text(encoding="utf-8"))
        required = tuple(consent.TermsDefinition(t["termsId"], t["sha256"]) for t in manifest["requiredTerms"])
        self.assertGreater(len(manifest["cases"]), 0)
        for case in manifest["cases"]:
            with self.subTest(name=case["name"]):
                path = directory / case["file"] if case["file"] else directory / "does-not-exist" / "consent.json"
                try:
                    verdict = consent.has_accepted_all(consent.load_consent_records(path), required)
                except consent.ConsentFileError:
                    verdict = False
                self.assertEqual(verdict, case["accepted"])


class CheckConsentTests(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.data_root = Path(self._temp.name) / "data-root"
        self.required = consent.load_required_terms(REPO_TERMS_DIR)

    def tearDown(self):
        self._temp.cleanup()

    def _check(self):
        return consent.check_consent(self.data_root, REPO_TERMS_DIR)

    def test_valid_consent(self):
        write_consent(self.data_root, records_for(self.required))
        self.assertEqual(self._check(), consent.ConsentStatus(True, ""))

    def test_no_consent_file(self):
        status = self._check()
        self.assertFalse(status.accepted)
        self.assertIn("ありません", status.reason)

    def test_revoked(self):
        # 撤回（ConsentStore.Revoke）は空の配列を保存する。
        write_consent(self.data_root, [])
        status = self._check()
        self.assertFalse(status.accepted)
        self.assertIn("撤回", status.reason)

    def test_terms_version_mismatch(self):
        write_consent(self.data_root, records_for(self.required, **{"tsumugi-illustration-terms": "0" * 64}))
        status = self._check()
        self.assertFalse(status.accepted)
        self.assertIn("tsumugi-illustration-terms", status.reason)

    def test_illustration_terms_alone_is_not_enough(self):
        only = [t for t in self.required if t.terms_id == "tsumugi-illustration-terms"]
        write_consent(self.data_root, records_for(only))
        self.assertFalse(self._check().accepted)

    def test_broken_json(self):
        self.data_root.mkdir(parents=True)
        (self.data_root / consent.CONSENT_FILE_NAME).write_text('[{"termsId": ', encoding="utf-8")
        status = self._check()
        self.assertFalse(status.accepted)
        self.assertIn("壊れて", status.reason)

    def test_invalid_utf8_is_broken(self):
        self.data_root.mkdir(parents=True)
        (self.data_root / consent.CONSENT_FILE_NAME).write_bytes(b"[\xff\xfe\xfd]")
        self.assertFalse(self._check().accepted)

    def test_impossible_date_is_broken(self):
        records = records_for(self.required)
        records[0]["acceptedAtUtc"] = "2026-02-30T00:00:00Z"
        write_consent(self.data_root, records)
        self.assertFalse(self._check().accepted)


if __name__ == "__main__":
    unittest.main()
