using System;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// TTS の初期化・設定に関する失敗（配置不足、メタ情報の解析失敗、スタイル解決失敗など）。
    /// ネイティブ API の結果コードを伴う失敗は <see cref="Native.VoicevoxException"/> を使う。
    /// </summary>
    public sealed class TtsSetupException : Exception
    {
        public TtsSetupException(string message) : base(message)
        {
        }

        public TtsSetupException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// 理由（<see cref="TtsUnavailableReason"/>）を分類できる場合のコンストラクタ。
        /// <see cref="TtsService"/> はこの値を <see cref="TtsService.UnavailableReason"/> にそのまま反映する。
        /// </summary>
        public TtsSetupException(string message, TtsUnavailableReason reason) : base(message)
        {
            Reason = reason;
        }

        /// <inheritdoc cref="TtsSetupException(string, TtsUnavailableReason)"/>
        public TtsSetupException(string message, TtsUnavailableReason reason, Exception innerException)
            : base(message, innerException)
        {
            Reason = reason;
        }

        /// <summary>
        /// <see cref="TtsUnavailableReason.OnnxRuntimeVersionMismatch"/> 用。ONNX Runtime の対応バージョン範囲
        /// （<c>voicevox_get_onnxruntime_lib_min_required_minor_version</c> /
        /// <c>..._max_supported_minor_version</c>）を取得できたときだけ使う。
        /// <see cref="TtsService"/> はこれを「対応バージョン: 1.{min}〜1.{max}」という
        /// 数値のみの案内文（<see cref="TtsStatusMessages"/> の <c>Detail</c>）に整形する（docs/tts.md §9.1）。
        /// </summary>
        public TtsSetupException(string message, TtsUnavailableReason reason, int onnxMinMinor, int onnxMaxMinor)
            : base(message)
        {
            Reason = reason;
            OnnxMinMinor = onnxMinMinor;
            OnnxMaxMinor = onnxMaxMinor;
        }

        /// <summary>
        /// 分類できた理由。呼び出し元が分類せずに投げた場合は null
        /// （<see cref="TtsService"/> 側で <see cref="TtsUnavailableReason.InitializationFailed"/> にフォールバックする）。
        /// </summary>
        public TtsUnavailableReason? Reason { get; }

        /// <summary>ONNX Runtime が要求する最小マイナーバージョン（例: 1.17 なら 17）。取得できた場合のみ。</summary>
        public int? OnnxMinMinor { get; }

        /// <summary>ONNX Runtime が対応する最大マイナーバージョン。取得できた場合のみ。</summary>
        public int? OnnxMaxMinor { get; }
    }
}
