using System;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// <see cref="TtsService"/> の状態。
    /// 配置の自己診断結果（<see cref="TtsReadiness"/>）とは別で、「いま読み上げを頼めるか」を表す。
    /// </summary>
    public enum TtsServiceState
    {
        /// <summary>まだ初期化を始めていない。</summary>
        NotInitialized = 0,

        /// <summary>バックグラウンドで初期化中（辞書とモデルの読み込みに数秒かかる）。</summary>
        Initializing = 1,

        /// <summary>合成できる。</summary>
        Ready = 2,

        /// <summary>使えない（配置不足・初期化失敗）。読み上げなしでゲームを続行する（docs/tts.md §9）。</summary>
        NotAvailable = 3,
    }

    /// <summary>
    /// <see cref="TtsService"/> の状態と、使えない場合の理由。生成後は不変。
    ///
    /// <see cref="Reason"/> は<b>UI に出してよい文言</b>だけを入れる（docs/tts.md §9）。
    /// ネイティブの ResultCode や <c>VoicevoxException.Message</c> はログにだけ残し、ここには入れない。
    /// </summary>
    public readonly struct TtsServiceStatus
    {
        private TtsServiceStatus(TtsServiceState state, string reason)
        {
            State = state;
            Reason = reason;
        }

        /// <summary>まだ初期化していない状態。</summary>
        public static TtsServiceStatus NotInitialized { get; } =
            new TtsServiceStatus(TtsServiceState.NotInitialized, null);

        /// <summary>初期化中の状態。</summary>
        public static TtsServiceStatus Initializing { get; } =
            new TtsServiceStatus(TtsServiceState.Initializing, null);

        /// <summary>合成できる状態。</summary>
        public static TtsServiceStatus Ready { get; } = new TtsServiceStatus(TtsServiceState.Ready, null);

        /// <summary>使えない状態を、UI に出してよい理由つきで作る。</summary>
        public static TtsServiceStatus NotAvailable(string reason)
            => new TtsServiceStatus(
                TtsServiceState.NotAvailable,
                string.IsNullOrWhiteSpace(reason) ? "読み上げを利用できません（詳細はログを参照）。" : reason);

        /// <summary>状態。</summary>
        public TtsServiceState State { get; }

        /// <summary>使えない場合の理由（UI 表示可）。それ以外は null。</summary>
        public string Reason { get; }

        /// <summary>合成を頼めるか。</summary>
        public bool IsReady => State == TtsServiceState.Ready;

        /// <summary>ログ用の説明文。</summary>
        public override string ToString()
            => Reason == null ? State.ToString() : $"{State}: {Reason}";
    }
}
