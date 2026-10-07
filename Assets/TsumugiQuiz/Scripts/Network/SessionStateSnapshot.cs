using TsumugiQuiz.Core;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 途中参加・再接続したクライアントへ送る進行状態のひとかたまり（#19、
    /// <see cref="GameSession.ResyncClient"/> → <see cref="GameSession.SessionResynced"/>）。
    /// </summary>
    /// <remarks>
    /// 値は受信時に検証済み（フェーズが定義済みの値、問題インデックスが出題列の範囲内）。
    /// 不変な読み取り専用の構造体として扱う。
    /// </remarks>
    public readonly struct SessionStateSnapshot
    {
        /// <summary>
        /// 値を指定して生成する。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="questionIndex">現在の問題インデックス（出題列上の位置）。未出題なら -1。</param>
        /// <param name="totalQuestions">出題列の長さ。セッション未開始なら 0。</param>
        public SessionStateSnapshot(QuizPhase phase, int questionIndex, int totalQuestions)
        {
            Phase = phase;
            QuestionIndex = questionIndex;
            TotalQuestions = totalQuestions;
        }

        /// <summary>現在のフェーズ。</summary>
        public QuizPhase Phase { get; }

        /// <summary>現在の問題インデックス（出題列上の位置）。未出題なら -1。</summary>
        public int QuestionIndex { get; }

        /// <summary>出題列の長さ。セッション未開始なら 0。</summary>
        public int TotalQuestions { get; }

        /// <summary>出題中（問題インデックスが確定している）か。</summary>
        public bool HasQuestion => QuestionIndex >= 0;

        /// <inheritdoc />
        public override string ToString() => $"{Phase} ({QuestionIndex + 1}/{TotalQuestions})";
    }
}
