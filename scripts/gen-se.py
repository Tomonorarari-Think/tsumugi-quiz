#!/usr/bin/env python3
"""SE（効果音）生成スクリプト（docs/tasks/setup-brief.md K17）。

Assets/TsumugiQuiz/Audio/SE/ に、早押し・正解・不正解・タイムアップ・開始・参加
の 6 種類の wav（44.1kHz 16bit mono）を生成する。

標準的な波形合成（サイン波・矩形波・ノイズ）と ADSR エンベロープを組み合わせて音を作る。
乱数（ノイズ生成のみで使用）はシードを固定しているため、同じ環境で再実行しても
バイト単位で同一の wav が出力される（決定的）。

使い方:
    python scripts/gen-se.py            # 6種類の wav を生成して Audio/SE/ に書き出す
    python scripts/gen-se.py --check    # 生成済み wav を検証する
                                         # （存在・チャンネル数・サンプル幅・サンプルレート・
                                         #   長さ・ピークに加え、一時ディレクトリへの再生成結果との
                                         #   バイト完全一致を毎回確認する）
"""
from __future__ import annotations

import argparse
import sys
import tempfile
import wave
from pathlib import Path
from typing import Callable, Dict, List

try:
    import numpy as np
except ImportError:
    print(
        "numpy が見つかりません。次のコマンドでインストールしてから再実行してください:\n"
        "    pip install numpy",
        file=sys.stderr,
    )
    sys.exit(1)


SAMPLE_RATE = 44100
SEED = 20260913  # 決定的な出力にするための固定シード（値そのものに意味はない）
PEAK_DBFS = -3.0
PEAK_AMPLITUDE = 10 ** (PEAK_DBFS / 20.0)
MIN_DURATION = 0.2
MAX_DURATION = 1.0
# 16bit PCM の正規化に使うフルスケール値。書き出し（to_pcm16）と検証（check_all）の
# 両方でこの値に統一する（符号付き16bitの最大値は32767、最小値は-32768だが、
# 対称なスケールとして 32767 を使う）。
PCM_FULL_SCALE = 32767.0
# --check で許容するピークの範囲（-3dBFS 程度、書き出し時の丸め誤差を考慮）
CHECK_PEAK_DBFS_MIN = -6.0
CHECK_PEAK_DBFS_MAX = -1.0

OUTPUT_DIR = Path(__file__).resolve().parent.parent / "Assets" / "TsumugiQuiz" / "Audio" / "SE"


# ---------------------------------------------------------------------------
# 波形合成の基本要素
# ---------------------------------------------------------------------------

def _time_axis(duration: float) -> "np.ndarray":
    n = int(round(duration * SAMPLE_RATE))
    return np.arange(n) / SAMPLE_RATE


def sine_wave(freq: float, duration: float, phase: float = 0.0) -> "np.ndarray":
    """サイン波。"""
    t = _time_axis(duration)
    return np.sin(2 * np.pi * freq * t + phase)


def square_wave(freq: float, duration: float, duty: float = 0.5) -> "np.ndarray":
    """矩形波（duty は 0〜1 のデューティ比）。"""
    t = _time_axis(duration)
    phase = np.mod(freq * t, 1.0)
    return np.where(phase < duty, 1.0, -1.0)


def noise(duration: float, rng: "np.random.RandomState") -> "np.ndarray":
    """ホワイトノイズ。呼び出し側でシード固定済みの RandomState を渡すことで決定的にする。"""
    n = int(round(duration * SAMPLE_RATE))
    return rng.uniform(-1.0, 1.0, size=n)


def linear_sweep(freq_start: float, freq_end: float, duration: float) -> "np.ndarray":
    """周波数が線形に変化するサイン波スイープ（タイムアップ演出用）。"""
    n = int(round(duration * SAMPLE_RATE))
    t = np.arange(n) / SAMPLE_RATE
    freq = np.linspace(freq_start, freq_end, n)
    # 瞬時周波数を積分して位相を作る（単純に freq*t だと周波数変化が反映されないため）
    phase = 2 * np.pi * np.cumsum(freq) / SAMPLE_RATE
    return np.sin(phase)


def adsr_envelope(
    n: int,
    attack: float,
    decay: float,
    sustain_level: float,
    release: float,
) -> "np.ndarray":
    """ADSR エンベロープ（attack/decay/release は秒、sustain はレベルのみ）。

    attack -> decay -> sustain（残り時間分だけ一定レベルを維持） -> release の順に
    サンプル数 n のエンベロープを構築する。

    attack + decay + release がサンプル数 n を超える場合、sustain 区間が確保できず
    意図しない音になる（release が途中で打ち切られる等）ため、呼び出し側の設定ミスを
    早期に検出できるよう ValueError を送出する。
    """
    a = max(int(round(attack * SAMPLE_RATE)), 0)
    d = max(int(round(decay * SAMPLE_RATE)), 0)
    r = max(int(round(release * SAMPLE_RATE)), 0)

    if a + d + r > n:
        raise ValueError(
            f"ADSR の attack+decay+release（{a + d + r}サンプル）が"
            f"全体の長さ（{n}サンプル）を超えています。sustain区間を確保できません。"
        )

    s = max(n - a - d - r, 0)

    env = np.zeros(n)
    idx = 0
    if a > 0:
        env[idx:idx + a] = np.linspace(0.0, 1.0, a, endpoint=False)
        idx += a
    if d > 0:
        env[idx:idx + d] = np.linspace(1.0, sustain_level, d, endpoint=False)
        idx += d
    if s > 0:
        env[idx:idx + s] = sustain_level
        idx += s
    if r > 0:
        remaining = n - idx
        if remaining > 0:
            env[idx:idx + remaining] = np.linspace(sustain_level, 0.0, remaining, endpoint=True)
    return env[:n]


def apply_envelope(signal: "np.ndarray", envelope: "np.ndarray") -> "np.ndarray":
    n = min(len(signal), len(envelope))
    return signal[:n] * envelope[:n]


def concat_with_gap(parts: List["np.ndarray"], gap_seconds: float) -> "np.ndarray":
    """複数の音を無音区間（gap_seconds）を挟んで連結する。"""
    gap = np.zeros(int(round(gap_seconds * SAMPLE_RATE)))
    pieces: List["np.ndarray"] = []
    for i, part in enumerate(parts):
        pieces.append(part)
        if i != len(parts) - 1:
            pieces.append(gap)
    return np.concatenate(pieces)


def normalize_peak(signal: "np.ndarray", target_peak: float = PEAK_AMPLITUDE) -> "np.ndarray":
    """ピークが target_peak（既定 -3dBFS 相当）になるようスケーリングする。"""
    peak = float(np.max(np.abs(signal))) if len(signal) else 0.0
    if peak < 1e-9:
        return signal
    return signal * (target_peak / peak)


def to_pcm16(signal: "np.ndarray") -> "np.ndarray":
    """float(-1.0〜1.0) の信号を 16bit PCM に変換する。単純な切り捨て（キャスト）ではなく
    np.round で最近接丸めを行い、量子化誤差の偏り（常に0方向に丸められるバイアス）を避ける。"""
    clipped = np.clip(signal, -1.0, 1.0)
    return np.round(clipped * PCM_FULL_SCALE).astype("<i2")


def write_wav(path: Path, signal: "np.ndarray") -> None:
    pcm = to_pcm16(signal)
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(SAMPLE_RATE)
        wf.writeframes(pcm.tobytes())


# ---------------------------------------------------------------------------
# 各 SE の定義
# ---------------------------------------------------------------------------

def make_buzz(rng: "np.random.RandomState") -> "np.ndarray":
    """早押し: 短い高音ピッ。"""
    duration = 0.22
    tone = sine_wave(1500.0, duration)
    env = adsr_envelope(len(tone), attack=0.004, decay=0.02, sustain_level=0.8, release=0.08)
    return normalize_peak(apply_envelope(tone, env))


def make_correct(rng: "np.random.RandomState") -> "np.ndarray":
    """正解: ピンポン2音（上昇。完全5度）。"""
    note1 = sine_wave(880.0, 0.15)  # A5
    env1 = adsr_envelope(len(note1), attack=0.005, decay=0.02, sustain_level=0.7, release=0.08)
    note2 = sine_wave(1318.51, 0.22)  # E6（A5の完全5度上）
    env2 = adsr_envelope(len(note2), attack=0.005, decay=0.03, sustain_level=0.7, release=0.15)
    parts = [apply_envelope(note1, env1), apply_envelope(note2, env2)]
    return normalize_peak(concat_with_gap(parts, gap_seconds=0.03))


def make_wrong(rng: "np.random.RandomState") -> "np.ndarray":
    """不正解: ブッブー低音2回（矩形波＋わずかなノイズで濁らせる）。"""

    def buzz_tone(duration: float) -> "np.ndarray":
        base = square_wave(180.0, duration)
        dirt = noise(duration, rng) * 0.15
        return base * 0.85 + dirt

    tone1 = buzz_tone(0.14)
    env1 = adsr_envelope(len(tone1), attack=0.004, decay=0.02, sustain_level=0.8, release=0.05)
    tone2 = buzz_tone(0.14)
    env2 = adsr_envelope(len(tone2), attack=0.004, decay=0.02, sustain_level=0.8, release=0.08)
    parts = [apply_envelope(tone1, env1), apply_envelope(tone2, env2)]
    return normalize_peak(concat_with_gap(parts, gap_seconds=0.06))


def make_timeup(rng: "np.random.RandomState") -> "np.ndarray":
    """タイムアップ: 下降スイープ＋余韻。"""
    duration = 0.5
    sweep = linear_sweep(900.0, 220.0, duration)
    env = adsr_envelope(len(sweep), attack=0.01, decay=0.05, sustain_level=0.6, release=0.3)
    return normalize_peak(apply_envelope(sweep, env))


def make_start(rng: "np.random.RandomState") -> "np.ndarray":
    """開始ジングル: 上昇3音（ド・ミ・ソ）。"""
    freqs = [523.25, 659.25, 783.99]  # C5, E5, G5
    parts = []
    for freq in freqs:
        note = sine_wave(freq, 0.14)
        env = adsr_envelope(len(note), attack=0.004, decay=0.02, sustain_level=0.75, release=0.06)
        parts.append(apply_envelope(note, env))
    return normalize_peak(concat_with_gap(parts, gap_seconds=0.02))


def make_join(rng: "np.random.RandomState") -> "np.ndarray":
    """参加通知: 柔らかい1音（倍音を薄く足してベルのように）。"""
    duration = 0.35
    fundamental = sine_wave(660.0, duration)  # E5
    overtone = sine_wave(1320.0, duration) * 0.25
    tone = fundamental + overtone
    env = adsr_envelope(len(tone), attack=0.03, decay=0.05, sustain_level=0.6, release=0.2)
    return normalize_peak(apply_envelope(tone, env))


SE_DEFINITIONS: Dict[str, Callable[["np.random.RandomState"], "np.ndarray"]] = {
    "buzz.wav": make_buzz,
    "correct.wav": make_correct,
    "wrong.wav": make_wrong,
    "timeup.wav": make_timeup,
    "start.wav": make_start,
    "join.wav": make_join,
}


# ---------------------------------------------------------------------------
# 生成・検証
# ---------------------------------------------------------------------------

def generate_all() -> None:
    for filename, factory in SE_DEFINITIONS.items():
        # ファイルごとに同じシードから RandomState を作り直すことで、
        # 生成順序に関わらず各ファイルが決定的になるようにする。
        rng = np.random.RandomState(SEED)
        signal = factory(rng)
        duration = len(signal) / SAMPLE_RATE
        if not (MIN_DURATION - 1e-6 <= duration <= MAX_DURATION + 1e-6):
            raise ValueError(f"{filename} の長さが規定外です（0.2〜1.0秒）: {duration:.3f}s")

        path = OUTPUT_DIR / filename
        write_wav(path, signal)
        print(f"generated: {path} ({duration:.3f}s)")


def _read_wav_bytes(path: Path) -> bytes:
    with wave.open(str(path), "rb") as wf:
        return wf.readframes(wf.getnframes())


def _regenerate_bytes(filename: str) -> bytes:
    """SE_DEFINITIONS のファクトリを使い、コミット済み wav と同じ手順で
    メモリ上に再生成した wav バイト列を返す（一時ディレクトリ経由）。"""
    factory = SE_DEFINITIONS[filename]
    rng = np.random.RandomState(SEED)
    signal = factory(rng)

    with tempfile.TemporaryDirectory(prefix="gen-se-check-") as tmp_dir:
        tmp_path = Path(tmp_dir) / filename
        write_wav(tmp_path, signal)
        return _read_wav_bytes(tmp_path)


def check_all() -> bool:
    ok = True
    for filename in SE_DEFINITIONS:
        path = OUTPUT_DIR / filename
        if not path.exists():
            print(f"NG: {path} が存在しません", file=sys.stderr)
            ok = False
            continue

        with wave.open(str(path), "rb") as wf:
            n_channels = wf.getnchannels()
            sample_width = wf.getsampwidth()
            framerate = wf.getframerate()
            n_frames = wf.getnframes()
            frames = wf.readframes(n_frames)

        duration = n_frames / framerate if framerate else 0.0
        samples = np.frombuffer(frames, dtype="<i2").astype(np.float64) / PCM_FULL_SCALE
        peak = float(np.max(np.abs(samples))) if len(samples) else 0.0
        peak_dbfs = 20 * np.log10(peak) if peak > 0 else float("-inf")

        errors = []
        if n_channels != 1:
            errors.append(f"チャンネル数が 1 ではありません: {n_channels}")
        if sample_width != 2:
            errors.append(f"サンプル幅が 16bit ではありません: {sample_width * 8}bit")
        if framerate != SAMPLE_RATE:
            errors.append(f"サンプルレートが {SAMPLE_RATE}Hz ではありません: {framerate}Hz")
        if not (MIN_DURATION - 1e-6 <= duration <= MAX_DURATION + 1e-6):
            errors.append(f"長さが 0.2〜1.0 秒の範囲外です: {duration:.3f}s")
        if not (CHECK_PEAK_DBFS_MIN <= peak_dbfs <= CHECK_PEAK_DBFS_MAX):
            errors.append(f"ピークが -3dBFS 程度ではありません: {peak_dbfs:.2f}dBFS")

        # gen-se.py は決定的（シード固定）なので、同じ手順で再生成したバイト列は
        # コミット済み wav と完全一致するはず。不一致は「スクリプトを変更したのに
        # wav を再生成/再コミットし忘れた」ことを検出できる（常に実行する）。
        regenerated = _regenerate_bytes(filename)
        if regenerated != frames:
            errors.append(
                "再生成したバイト列がコミット済み wav と一致しません。"
                "scripts/gen-se.py を実行して再生成し、コミットし直してください。"
            )

        if errors:
            ok = False
            print(f"NG: {path}", file=sys.stderr)
            for e in errors:
                print(f"  - {e}", file=sys.stderr)
        else:
            print(f"OK: {path} ({duration:.3f}s, peak={peak_dbfs:.2f}dBFS, bytes-match=True)")

    return ok


def main(argv: List[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="生成済み wav の検証のみ行う（存在・チャンネル数・サンプル幅・サンプルレート・長さ・ピーク）",
    )
    args = parser.parse_args(argv)

    if args.check:
        return 0 if check_all() else 1

    generate_all()
    return 0


if __name__ == "__main__":
    sys.exit(main())
