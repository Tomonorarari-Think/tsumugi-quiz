using System;

namespace TsumugiQuiz.Tts.Native
{
    /// <summary>
    /// voicevox_core のネイティブ API が Ok 以外の結果コードを返したときに投げる例外。
    /// 上位（TtsService）は必ずここで捕捉し、読み上げを無効化してゲームは続行する（docs/tts.md §9）。
    ///
    /// <b><see cref="Exception.Message"/> は UI にそのまま表示しないこと。</b>
    /// 「どの API が失敗したか + ResultCode の数値 + ネイティブ側の原文」を含むログ向けの文言である。
    /// UI には固定の案内文を出し、この例外の内容はログにだけ残す。
    /// </summary>
    public sealed class VoicevoxException : Exception
    {
        /// <summary>ネイティブの結果コード（数値）。ユーザー向け文言には出さず、ログにのみ残す。</summary>
        public int ResultCode { get; }

        /// <summary>voicevox_error_result_to_message が返したネイティブ側のメッセージ。</summary>
        public string NativeMessage { get; }

        internal VoicevoxException(VoicevoxResultCode code, string nativeMessage, string message)
            : base(message)
        {
            ResultCode = (int)code;
            NativeMessage = nativeMessage;
        }
    }
}
