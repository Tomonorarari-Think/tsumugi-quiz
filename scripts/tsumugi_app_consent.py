"""アプリの利用規約への同意の記録（consent.json）を、アプリと同じ条件で確かめる（issue #219 H1）。

配布 zip の表情生成ツールは、アプリの同意フロー（requirements.md FR-71〜FR-76、NFR-08）を通った人だけが
使えるようにする（2026-10-03 ユーザー決定、案 (a)）。立ち絵の表示（CharacterView）が使う判定
`ConsentGate.HasUserConsented()` と同じ条件を、ここで Python に移した。標準ライブラリだけで動く。

C# の実装との対応（行番号は 2026-10-03 の develop 895ec8d 時点）:

- 判定の入口: `Assets/TsumugiQuiz/Scripts/UI/Consent/ConsentGate.cs:54-67` `HasUserConsented`
  （立ち絵は `UI/Character/CharacterView.cs:287` → `UI/Views/Settings/TtsConsentCheckFactory.cs:64` 経由で同じ関数）
  → `has_accepted_all(load_consent_records(...), load_required_terms(...))`
- 全規約の一致: `Scripts/Core/Consent/ConsentStore.cs:31-67` `HasAcceptedAll` / `IsAccepted`
  → `has_accepted_all`。必要な規約の**すべて**について、termsId と sha256 が両方一致する記録が要る。
  撤回は `ConsentStore.cs:103-105` `Revoke` が空の配列を保存すること（= 記録が 0 件）なので、
  撤回済みは「記録が無い」と同じ扱いになる。
- 必要な規約: `Scripts/UI/Consent/TermsCatalog.cs:57-85` `Entries`（4 件）と `:154-165` `ComputeRequiredTerms`
  → `load_required_terms`。アプリはビルドに入れた `Assets/TsumugiQuiz/Resources/Terms/*.txt` の本文を
  ハッシュにする。zip 版は同じファイルを package-release が `terms/` にコピーしたものを読む
  （`Entries` とファイルの組が一致することは EditMode テスト `TermsCatalogTests` で確かめる）。
- 規約の版（ハッシュ）: `Scripts/Core/Consent/TermsHasher.cs:22-89` → `compute_terms_body_hash`。
  改行を LF にそろえ、`---` だけの最初の行より後ろを本文とし（無い・最後の行なら全文）、
  .NET の `String.Trim()`（`char.IsWhiteSpace` の文字）で前後を落とし、先頭の BOM を除いて
  UTF-8 の SHA-256 を小文字 16 進にする。Python の `str.strip()` は空白の定義が違う
  （U+001C〜U+001F を含む）ので使わない。
- 記録の読み込み: `Scripts/UI/Consent/JsonConsentStorage.cs:53-91` `Load` と `:150-169` `TryToRecord`
  → `load_consent_records`。ファイルが無ければ記録 0 件。JSON が壊れている・形が違う（配列でない、
  要素が null など）ときは、アプリは記録 0 件（= 未同意）にするので、ここでも未同意として止める。
  termsId / sha256 / appVersion のどれかが空の要素は読み飛ばす（アプリと同じ）。未知のキーは無視する。
  キー名の大文字小文字は区別しない（Newtonsoft.Json の既定のプロパティの照合と同じ）。

アプリより厳しく「止める」側に倒している点（どれも同意を偽って通すことにはならない）:

- JSON の文法は標準の JSON だけを受け付ける（Newtonsoft が許すコメント・単一引用符などは壊れている扱い）。
- `acceptedAtUtc` は ISO 8601 の文字列だけを受け付ける（アプリが書くのは `2026-10-03T09:12:34.5678901Z` の形）。
  キーが無いときはアプリと同じく問題にしない。null・数値・読めない文字列は、アプリと同じく壊れている扱い。
- 文字コードは UTF-8（BOM の有無を問わない）と、BOM 付きの UTF-16 / UTF-32。不正な UTF-8 は壊れている扱い
  （.NET は置換文字にして読み進める）。
"""

from __future__ import annotations

import codecs
import hashlib
import json
import re
from datetime import datetime
from dataclasses import dataclass
from pathlib import Path

CONSENT_FILE_NAME = "consent.json"
TERMS_FILE_GLOB = "*.txt"
HEADER_BODY_SEPARATOR_LINE = "---"
BYTE_ORDER_MARK = "﻿"
# consent.json の大きさの上限（アプリが書くのは 4 件で 1KB 程度。壊れたファイル・別物の検出用）。
MAX_CONSENT_FILE_BYTES = 1024 * 1024

# .NET の char.IsWhiteSpace が空白とみなす文字（String.Trim() が落とす文字）。
# 根拠: https://learn.microsoft.com/dotnet/api/system.char.iswhitespace の Remarks（2026-10-03 確認）。
DOTNET_WHITESPACE = frozenset(
    "\u0009\u000a\u000b\u000c\u000d \u0085  "
    "           "
    "    　"
)

_ISO_8601 = re.compile(
    r"^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?(Z|[+-]\d{2}:\d{2})?$"
)


@dataclass(frozen=True)
class TermsDefinition:
    """必要な規約 1 件（規約 ID と本文のハッシュ、不変）。C# の TermsDefinition と同じ。"""

    terms_id: str
    sha256: str


@dataclass(frozen=True)
class ConsentRecord:
    """consent.json の 1 件（判定に使う項目だけ、不変）。"""

    terms_id: str
    sha256: str
    app_version: str


@dataclass(frozen=True)
class ConsentStatus:
    """同意の確認結果。accepted が False のときは reason に利用者向けの理由を入れる。"""

    accepted: bool
    reason: str


class ConsentFileError(ValueError):
    """consent.json が壊れている・形が違う（アプリは記録 0 件として扱う）。"""


# ---- 規約のハッシュ（TermsHasher.cs） ------------------------------------------


def normalize_line_endings(text: str) -> str:
    return text.replace("\r\n", "\n").replace("\r", "\n")


def dotnet_trim(text: str) -> str:
    """.NET の String.Trim() と同じく、char.IsWhiteSpace の文字だけを前後から落とす。"""
    start, end = 0, len(text)
    while start < end and text[start] in DOTNET_WHITESPACE:
        start += 1
    while end > start and text[end - 1] in DOTNET_WHITESPACE:
        end -= 1
    return text[start:end]


def compute_sha256_hex(content: str) -> str:
    """TermsHasher.ComputeSha256Hex: 改行をそろえ、先頭の BOM を除いた UTF-8 の SHA-256（小文字 16 進）。"""
    normalized = normalize_line_endings(content).lstrip(BYTE_ORDER_MARK)
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def extract_terms_body(full_text: str) -> str:
    """TermsHasher.ExtractBody: 最初の `---` だけの行より後ろ（無い・最後の行なら全文）を Trim する。"""
    normalized = normalize_line_endings(full_text)
    lines = normalized.split("\n")
    try:
        separator_index = lines.index(HEADER_BODY_SEPARATOR_LINE)
    except ValueError:
        separator_index = -1
    if separator_index < 0 or separator_index == len(lines) - 1:
        return dotnet_trim(normalized)
    return dotnet_trim("\n".join(lines[separator_index + 1:]))


def compute_terms_body_hash(full_text: str) -> str:
    """TermsHasher.ComputeSha256HexForTermsBody。"""
    return compute_sha256_hex(extract_terms_body(full_text))


def load_required_terms(terms_dir: Path) -> "tuple[TermsDefinition, ...]":
    """`terms_dir` の `<規約 ID>.txt` から、必要な規約の一覧を作る（TermsCatalog.ComputeRequiredTerms）。"""
    files = sorted(path for path in terms_dir.glob(TERMS_FILE_GLOB) if path.is_file())
    if not files:
        raise ValueError(f"規約のファイル（*.txt）が見つかりません: {terms_dir}")
    # アプリの TextAsset と同じく UTF-8 として読む（先頭の BOM は compute_sha256_hex でも除く）。
    return tuple(
        TermsDefinition(path.stem, compute_terms_body_hash(path.read_text(encoding="utf-8-sig"))) for path in files
    )


# ---- 同意の記録（JsonConsentStorage.cs） --------------------------------------


def _decode_text(raw: bytes) -> str:
    """File.ReadAllText と同じく BOM で文字コードを見分ける（BOM が無ければ UTF-8）。"""
    for bom, encoding in (
        (codecs.BOM_UTF32_LE, "utf-32-le"),
        (codecs.BOM_UTF32_BE, "utf-32-be"),
        (codecs.BOM_UTF8, "utf-8"),
        (codecs.BOM_UTF16_LE, "utf-16-le"),
        (codecs.BOM_UTF16_BE, "utf-16-be"),
    ):
        if raw.startswith(bom):
            return raw[len(bom):].decode(encoding)
    return raw.decode("utf-8")


def _field(item: dict, name: str):
    """キーを取り出す（完全一致を優先し、無ければ大文字小文字を無視して探す。Newtonsoft の既定と同じ）。"""
    if name in item:
        return item[name]
    lowered = name.lower()
    for key, value in item.items():
        if isinstance(key, str) and key.lower() == lowered:
            return value
    return None


def _as_string(value, where: str) -> "str | None":
    """文字列の項目を読む。null・無しは None。数値・真偽値はアプリ（Newtonsoft）と同じく文字列にする。"""
    if value is None:
        return None
    if isinstance(value, bool):
        return "True" if value else "False"
    if isinstance(value, (str, int, float)):
        return str(value)
    raise ConsentFileError(f"{where} が文字列ではありません（{type(value).__name__}）。")


def _validate_accepted_at(item: dict, where: str) -> None:
    key_present = any(isinstance(key, str) and key.lower() == "acceptedatutc" for key in item)
    if not key_present:
        return
    value = _field(item, "acceptedAtUtc")
    match = _ISO_8601.match(value) if isinstance(value, str) else None
    if match is None:
        raise ConsentFileError(f"{where}.acceptedAtUtc が日時として読めません: {value!r}")
    year, month, day, hour, minute, second = (int(part) for part in match.groups()[:6])
    offset = match.group(8)
    try:
        datetime(year, month, day, hour, minute, second)  # 2 月 30 日などの存在しない日時を弾く
    except ValueError:
        raise ConsentFileError(f"{where}.acceptedAtUtc が存在しない日時です: {value!r}") from None
    if offset not in (None, "Z") and (int(offset[1:3]) > 14 or int(offset[4:6]) > 59):
        raise ConsentFileError(f"{where}.acceptedAtUtc の時差が不正です: {value!r}")


def parse_consent_records(text: str) -> "tuple[ConsentRecord, ...]":
    """consent.json の中身を読む。壊れている・形が違うときは ConsentFileError。"""
    try:
        data = json.loads(text)
    except json.JSONDecodeError as exc:
        raise ConsentFileError(f"JSON として読めません: {exc}") from None
    if data is None:
        return ()
    if not isinstance(data, list):
        raise ConsentFileError("同意の記録の一覧（配列）ではありません。")

    records = []
    for index, item in enumerate(data):
        where = f"[{index}]"
        if not isinstance(item, dict):
            raise ConsentFileError(f"{where} が同意の記録（オブジェクト）ではありません。")
        terms_id = _as_string(_field(item, "termsId"), f"{where}.termsId")
        sha256 = _as_string(_field(item, "sha256"), f"{where}.sha256")
        app_version = _as_string(_field(item, "appVersion"), f"{where}.appVersion")
        _validate_accepted_at(item, where)
        if not terms_id or not sha256 or not app_version:
            continue  # アプリ（TryToRecord）と同じく、欠けた記録は読み飛ばす
        records.append(ConsentRecord(terms_id, sha256, app_version))
    return tuple(records)


def load_consent_records(path: Path) -> "tuple[ConsentRecord, ...]":
    """consent.json を読む。無ければ記録 0 件。壊れている・形が違うときは ConsentFileError。"""
    if not path.exists():
        return ()
    try:
        if path.stat().st_size > MAX_CONSENT_FILE_BYTES:
            raise ConsentFileError(f"ファイルが大きすぎます（{path.stat().st_size:,} バイト）。")
        text = _decode_text(path.read_bytes())
    except UnicodeDecodeError as exc:
        raise ConsentFileError(f"文字コードが読めません: {exc}") from None
    except OSError as exc:
        raise ConsentFileError(f"読めません（{type(exc).__name__}）: {exc}") from None
    return parse_consent_records(text)


def has_accepted_all(records: "tuple[ConsentRecord, ...]", required: "tuple[TermsDefinition, ...]") -> bool:
    """ConsentStore.HasAcceptedAll: 必要な規約のすべてに、termsId と sha256 が一致する記録があるか。"""
    if not required:
        raise ValueError("必要な規約が 0 件です。")
    return all(
        any(record.terms_id == terms.terms_id and record.sha256 == terms.sha256 for record in records)
        for terms in required
    )


def check_consent(data_root: Path, terms_dir: Path) -> ConsentStatus:
    """データルートの consent.json に、アプリと同じ条件で有効な同意があるかを確かめる。"""
    path = data_root / CONSENT_FILE_NAME
    required = load_required_terms(terms_dir)
    try:
        records = load_consent_records(path)
    except ConsentFileError as exc:
        return ConsentStatus(False, f"同意の記録が壊れています（{path}）: {exc}")
    if not records:
        return ConsentStatus(False, f"同意の記録がありません（未同意か、同意を撤回しています）: {path}")
    if not has_accepted_all(records, required):
        missing = [t.terms_id for t in required if not any(
            r.terms_id == t.terms_id and r.sha256 == t.sha256 for r in records)]
        return ConsentStatus(
            False,
            f"同意の記録が今の規約と合いません（規約が更新された可能性があります）: {', '.join(missing)}",
        )
    return ConsentStatus(True, "")
