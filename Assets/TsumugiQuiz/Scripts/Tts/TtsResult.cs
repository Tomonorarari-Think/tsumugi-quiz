using System;
using UnityEngine;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// 読み上げ 1 件の合成結果。生成後は不変。
    ///
    /// <see cref="DurationSec"/> は再生同期（#23、docs/tts.md §6）で
    /// <c>readingEndServerTime</c> を求めるために使う。<b>ホストの値を正とする</b>ので、
    /// クライアントは自分の値ではなくホストから配られた値を使うこと。
    ///
    /// 読み上げが行われない場合（<c>tts.enabled=false</c>、未同意、配置不足、合成失敗）は
    /// <see cref="TtsService.SynthesizeAsync"/> が <c>null</c> を返す。
    /// 呼び出し側は null を「読み上げなしで続行」として扱う（docs/tts.md §9）。
    ///
    /// <b><see cref="Clip"/> の所有権は呼び出し側にある。</b>
    /// <see cref="TtsService"/> は生成した <see cref="AudioClip"/> を保持しないので、
    /// <b>再生が終わったら（遅くとも次の問題の合成を始める前に）必ず
    /// <see cref="ReleaseClip"/> を呼んで解放すること</b>。
    /// <see cref="AudioClip"/> はサンプルをネイティブメモリに持つため、
    /// 解放しないまま問題を進めると使用量が増え続ける（docs/tts.md §6.3）。
    /// </summary>
    public sealed class TtsResult
    {
        private AudioClip _clip;

        public TtsResult(AudioClip clip, double durationSec, bool fromCache, string cacheKey)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (durationSec < 0d) throw new ArgumentOutOfRangeException(nameof(durationSec), durationSec, "0 以上でなければなりません。");

            _clip = clip;
            DurationSec = durationSec;
            FromCache = fromCache;
            CacheKey = cacheKey;
        }

        /// <summary>
        /// 再生する <see cref="AudioClip"/>（メインスレッドで生成済み）。
        /// <see cref="ReleaseClip"/> 済みなら null。
        /// </summary>
        public AudioClip Clip => _clip;

        /// <summary>再生時間（秒）。</summary>
        public double DurationSec { get; }

        /// <summary>ディスクキャッシュに命中したか（診断・計測用）。</summary>
        public bool FromCache { get; }

        /// <summary>キャッシュキー（32 文字の小文字 hex）。ログでキャッシュの効きを追うために持つ。</summary>
        public string CacheKey { get; }

        /// <summary>すでに解放済みか。</summary>
        public bool IsReleased => _clip == null;

        /// <summary>
        /// <see cref="Clip"/> を破棄する。<b>再生が終わったら必ず呼ぶこと</b>。
        ///
        /// 二重呼び出し・破棄済み（シーン遷移で Unity 側が先に消した場合を含む）でも安全で、
        /// 2 回目以降は何もしない。<b>メインスレッドから呼ぶこと</b>
        /// （<c>Object.Destroy</c> がメインスレッド専用のため）。
        /// </summary>
        public void ReleaseClip()
        {
            var clip = _clip;
            _clip = null;

            // Unity のオブジェクトは「破棄済みだが参照は non-null」になりうるので、
            // == null（Unity のオーバーロード）で判定してから Destroy する。
            if (clip == null) return;

            UnityEngine.Object.Destroy(clip);
        }
    }
}
