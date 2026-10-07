namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine.Tick"/> が 1 回の呼び出しで行った遷移の種類。
    /// サーバー（<c>GameSession</c>）はこれを見て RPC 送信と <c>NetworkVariable</c> の更新を行う。
    /// </summary>
    public enum QuizEvent
    {
        /// <summary>遷移しなかった。</summary>
        None = 0,

        /// <summary>読み上げが完了し、早押し受付が開いた（Reading → BuzzOpen）。</summary>
        BuzzOpened = 1,

        /// <summary>集計窓が閉じ、勝者が確定した（BuzzOpen → Locked）。</summary>
        BuzzResolved = 2,

        /// <summary>勝者に回答入力を開放した（Locked → Answering）。</summary>
        AnswerOpened = 3,

        /// <summary>回答の制限時間が切れ、誤答として判定へ進んだ（Answering → Judging）。</summary>
        AnswerTimedOut = 4,

        /// <summary>判定が終わり、結果の提示に入った（Judging → Result）。</summary>
        Judged = 5,

        /// <summary>誰も押さないまま早押し受付がタイムアウトした（BuzzOpen → Result）。</summary>
        BuzzTimedOut = 6,

        /// <summary>
        /// 誤答・お手つきのあと、残り時間で早押し受付を再開放した（Judging → BuzzOpen、
        /// <c>buzz.reopenAfterWrongAnswer</c>）。T0 は据え置きで、誤答者は受付対象外。
        /// </summary>
        BuzzReopened = 7,

        /// <summary>
        /// 選択式（<c>choice</c>）の回答受付が開いた（Reading → ChoiceAnswering、<b>仮決め: #17</b>）。
        /// 早押しを介さないため <see cref="BuzzOpened"/> の代わりに発火する。
        /// </summary>
        ChoiceAnsweringOpened = 8,

        /// <summary>
        /// 選択式の回答受付時間（<c>answer.choiceTimeLimitSec</c>）が切れた
        /// （ChoiceAnswering → Judging、<b>仮決め: #17</b>）。誰も選択していなくても発火する。
        /// </summary>
        ChoiceTimedOut = 9,

        /// <summary>
        /// 選択式の一斉判定が終わった（Judging → Result、<b>仮決め: #17</b>）。
        /// 選択した全クライアントの正誤・得点は <see cref="QuizStateMachine.LastChoiceResults"/> を見る。
        /// </summary>
        ChoiceJudged = 10,

        /// <summary>
        /// 早押し受付中に押せる参加者が 1 人も居なくなったため、時間切れを待たずに受付を締めた
        /// （BuzzOpen → Result、#200）。判定は <see cref="QuizJudgement.NoEligibleBuzzers"/>（誰も正解しなかった）で、
        /// 結果の配り方は <see cref="BuzzTimedOut"/> と同じ。押せる人の判定は
        /// <see cref="QuizStateMachine"/> のコンストラクタ引数 <c>hasEligibleBuzzers</c> が行う。
        /// </summary>
        BuzzClosedNoEligibleBuzzers = 11,
    }
}
