using System.Collections.Generic;

namespace TsumugiQuiz.Core.Participants
{
    /// <summary>参加者パネル（#194）の見出し下に出す要約の種類。</summary>
    public enum ParticipantPanelSummaryKind
    {
        /// <summary>要約を出さない。</summary>
        None = 0,

        /// <summary>自由入力: 「回答権あり n / m 人」。</summary>
        BuzzEligible,

        /// <summary>選択式: 「回答済み n / m 人」。</summary>
        ChoiceAnswered,
    }

    /// <summary>参加者パネル（#194）全体の表示内容。不変。<see cref="ParticipantPanelModel.Build"/> が作る。</summary>
    public sealed class ParticipantPanelState
    {
        /// <summary>値を指定して生成する。</summary>
        /// <param name="rows">行（名簿順）。null は空。</param>
        /// <param name="showScores">得点を表示するか。</param>
        /// <param name="summaryKind">要約の種類。</param>
        /// <param name="summaryCount">要約の分子。</param>
        /// <param name="summaryTotal">要約の分母。</param>
        public ParticipantPanelState(
            IReadOnlyList<ParticipantPanelRow> rows,
            bool showScores,
            ParticipantPanelSummaryKind summaryKind,
            int summaryCount,
            int summaryTotal)
        {
            Rows = rows ?? System.Array.Empty<ParticipantPanelRow>();
            ShowScores = showScores;
            SummaryKind = summaryKind;
            SummaryCount = summaryCount;
            SummaryTotal = summaryTotal;
        }

        /// <summary>行（名簿順 = 参加順で固定。統括判断 #194）。</summary>
        public IReadOnlyList<ParticipantPanelRow> Rows { get; }

        /// <summary>得点を表示するか（<c>display.showScores</c>、司会は常に true）。</summary>
        public bool ShowScores { get; }

        /// <summary>要約の種類。</summary>
        public ParticipantPanelSummaryKind SummaryKind { get; }

        /// <summary>要約の分子（回答権のある人数 / 回答済みの人数）。</summary>
        public int SummaryCount { get; }

        /// <summary>要約の分母。</summary>
        public int SummaryTotal { get; }
    }
}
