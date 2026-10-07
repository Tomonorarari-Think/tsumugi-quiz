namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// 読み上げ（TTS）が利用できない理由（docs/tts.md §9 の失敗ケース一覧に対応）。
    ///
    /// <see cref="TtsService"/> 自身が判定できるのは配置・初期化に関する理由
    /// （<see cref="MissingCoreDll"/>〜<see cref="InitializationFailed"/>）だけである。
    /// <see cref="ConsentNotGiven"/>・<see cref="UserSuppressed"/> は UI 層（同意ゲート・ルーム設定）が
    /// 追加で判定する理由で、<c>TsumugiQuiz.UI.TtsStatusPanel</c> がここに合流させる。
    ///
    /// ユーザー向けの文言・対処案内は <see cref="TtsStatusMessages"/> にまとめてある。
    /// ネイティブの ResultCode（数値）はここにも <see cref="TtsStatusMessages"/> にも含めない
    /// （docs/tts.md §9: 「ユーザー向けの文言に ResultCode の数値をそのまま出さない」）。
    /// </summary>
    public enum TtsUnavailableReason
    {
        /// <summary><c>voicevox_core.dll</c> が見つからない。</summary>
        MissingCoreDll = 0,

        /// <summary><c>voicevox_onnxruntime.dll</c> が見つからない。</summary>
        MissingOnnxRuntime = 1,

        /// <summary>Open JTalk 辞書（<c>open_jtalk_dic_utf_8-1.11</c> 等）が見つからない。</summary>
        MissingDictionary = 2,

        /// <summary>音声モデル（<c>.vvm</c>）が 1 つも見つからない。</summary>
        MissingModel = 3,

        /// <summary>ONNX Runtime のバージョンが voicevox_core の対応範囲外（min 未満など）。</summary>
        OnnxRuntimeVersionMismatch = 4,

        /// <summary>
        /// 上記のいずれにも当てはまらない初期化失敗（スタイル解決失敗、ネイティブ側の予期しない失敗など）。
        /// 理由を判定できない場合のフォールバック値でもある。
        /// </summary>
        InitializationFailed = 5,

        /// <summary>利用規約に同意していない、または撤回済み（requirements.md FR-74・FR-75）。</summary>
        ConsentNotGiven = 6,

        /// <summary>
        /// ユーザー操作（ルーム設定・将来の設定画面）で読み上げそのものが無効化されている
        /// （<see cref="TtsService.ReadingEnabled"/> が false）。配置不足による無効化とは別概念。
        /// このセッション限りの状態で、永続化は #28 で行う（docs/tts.md §9.1）。
        /// </summary>
        UserSuppressed = 7,
    }
}
