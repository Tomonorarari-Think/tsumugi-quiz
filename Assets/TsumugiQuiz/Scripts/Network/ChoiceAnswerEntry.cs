namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession.ChoiceResolved"/> が配る、選択式（<b>仮決め: #17</b>）1 クライアント分の
    /// 選択・正誤・得点。<c>TsumugiQuiz.Core.ChoiceAnswerResult</c>（サーバー内部の権威データ）から、
    /// RPC で受け取った値だけを持つ表示用に写したもの。
    /// </summary>
    public readonly struct ChoiceAnswerEntry
    {
        /// <summary>
        /// 値を指定して生成する。
        /// </summary>
        /// <param name="clientId">選択したクライアント ID。</param>
        /// <param name="choiceIndex">選択した元 <c>choices</c> インデックス。</param>
        /// <param name="isCorrect">正解だったか。</param>
        /// <param name="scoreDelta">この問題での得点の増減。</param>
        /// <param name="totalScore">増減後の累計得点。</param>
        public ChoiceAnswerEntry(ulong clientId, int choiceIndex, bool isCorrect, int scoreDelta, int totalScore)
        {
            ClientId = clientId;
            ChoiceIndex = choiceIndex;
            IsCorrect = isCorrect;
            ScoreDelta = scoreDelta;
            TotalScore = totalScore;
        }

        /// <summary>選択したクライアント ID。</summary>
        public ulong ClientId { get; }

        /// <summary>選択した元 <c>choices</c> インデックス。</summary>
        public int ChoiceIndex { get; }

        /// <summary>正解だったか。</summary>
        public bool IsCorrect { get; }

        /// <summary>この問題での得点の増減。</summary>
        public int ScoreDelta { get; }

        /// <summary>増減後の累計得点。</summary>
        public int TotalScore { get; }
    }
}
