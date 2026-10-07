using System;

namespace TsumugiQuiz.Core.Audio
{
    /// <summary>
    /// ヘッダだけを読んだときの判定結果
    /// （<see cref="WavParser.TryGetDurationSec(ReadOnlySpan{byte}, long, out double, out WavProbeResult)"/>）。
    /// </summary>
    public enum WavProbeResult
    {
        /// <summary>再生時間が求まった。</summary>
        Ok = 0,

        /// <summary>
        /// 渡したバッファが短く、fmt / data チャンクに届かなかった。
        /// <b>ファイルが壊れているとは限らない</b>ので、これを理由にファイルを削除してはいけない。
        /// </summary>
        Incomplete = 1,

        /// <summary>RIFF/WAVE でない、PCM 16bit でないなど、内容として妥当でない。</summary>
        Invalid = 2,
    }

    /// <summary>
    /// 与えられたバッファが短くて解析を続けられないときの例外。
    /// <see cref="WavFormatException"/> を継承しているので、
    /// 区別する必要がない呼び出し側は従来どおり <see cref="WavFormatException"/> で捕捉できる。
    /// </summary>
    public sealed class WavTruncatedException : WavFormatException
    {
        public WavTruncatedException(string message) : base(message)
        {
        }
    }
}
