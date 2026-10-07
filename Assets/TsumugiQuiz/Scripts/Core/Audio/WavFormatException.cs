using System;

namespace TsumugiQuiz.Core.Audio
{
    /// <summary>
    /// WAV のヘッダ・チャンク構造が想定と異なるときの例外（docs/tts.md §6.3）。
    /// キャッシュから読んだファイルが壊れている場合にも出るので、
    /// 呼び出し側は握りつぶさずログに残し、合成にフォールバックすること。
    ///
    /// バッファ不足（先頭だけを読んだ場合）は派生クラスの <see cref="WavTruncatedException"/> で表す。
    /// </summary>
    public class WavFormatException : Exception
    {
        public WavFormatException(string message) : base(message)
        {
        }

        public WavFormatException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
