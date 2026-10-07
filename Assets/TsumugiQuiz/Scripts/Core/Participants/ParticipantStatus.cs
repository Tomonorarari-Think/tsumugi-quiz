namespace TsumugiQuiz.Core.Participants
{
    /// <summary>参加者パネル（#194）の 1 行に出す、現在の問題での状態。</summary>
    public enum ParticipantStatus
    {
        /// <summary>特になし（自由入力: まだ押せる / 選択式: 受付前）。</summary>
        None = 0,

        /// <summary>自由入力で回答権を持って回答中。</summary>
        Answering,

        /// <summary>正解した（結果の確定後）。</summary>
        Correct,

        /// <summary>誤答した（回答権なし）。選択式では一斉判定で不正解。</summary>
        Wrong,

        /// <summary>前の問題のお手つきで、この問題は休み。</summary>
        Suspended,

        /// <summary>選択式で選択を送った（番号・正誤は判定まで分からない）。</summary>
        ChoiceSubmitted,

        /// <summary>選択式で、受付が始まってからまだ選択していない。</summary>
        ChoiceUnanswered,
    }
}
