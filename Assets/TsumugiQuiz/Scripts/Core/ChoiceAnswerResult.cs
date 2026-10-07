namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 選択式の一斉判定（<see cref="QuizPhase.ChoiceAnswering"/> の時間切れ）で確定した、
    /// 1 クライアント分の選択・正誤・得点（<b>仮決め: #17</b>）。
    /// 選択しなかったクライアントは <see cref="QuizStateMachine.LastChoiceResults"/> に含まれない。
    /// </summary>
    public readonly struct ChoiceAnswerResult
    {
        /// <summary>
        /// 値を指定して生成する。
        /// </summary>
        /// <param name="clientId">選択したクライアント ID。</param>
        /// <param name="choiceIndex">選択した元 <c>choices</c> インデックス。</param>
        /// <param name="judgement">正誤（<see cref="QuizJudgement.Correct"/> / <see cref="QuizJudgement.Wrong"/>）。</param>
        /// <param name="scoreDelta">この問題での得点の増減。</param>
        /// <param name="totalScore">増減後の累計得点。</param>
        public ChoiceAnswerResult(ulong clientId, int choiceIndex, QuizJudgement judgement, int scoreDelta, int totalScore)
        {
            ClientId = clientId;
            ChoiceIndex = choiceIndex;
            Judgement = judgement;
            ScoreDelta = scoreDelta;
            TotalScore = totalScore;
        }

        /// <summary>選択したクライアント ID。</summary>
        public ulong ClientId { get; }

        /// <summary>選択した元 <c>choices</c> インデックス。</summary>
        public int ChoiceIndex { get; }

        /// <summary>正誤。</summary>
        public QuizJudgement Judgement { get; }

        /// <summary>この問題での得点の増減。</summary>
        public int ScoreDelta { get; }

        /// <summary>増減後の累計得点。</summary>
        public int TotalScore { get; }
    }
}
