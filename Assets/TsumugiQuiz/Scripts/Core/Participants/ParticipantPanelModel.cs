using System.Collections.Generic;

namespace TsumugiQuiz.Core.Participants
{
    /// <summary>
    /// 参加者パネル（#194）の表示内容を、名簿・得点・現在の問題の進行状態から組み立てる純関数。
    /// Unity API に依存しないので EditMode でテストする。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>並びは名簿順（参加順）で固定する（統括判断 #194。途中で行が並び替わると印を追いにくいため）</description></item>
    /// <item><description>司会専任のホストは参加者ではないので行に出さない（LobbyView と同じ）</description></item>
    /// <item><description>進行状態の問題インデックスが現在の問題と違う（同期の途中）ときは、進行状態を無いものとして扱う</description></item>
    /// </list>
    /// </remarks>
    public static class ParticipantPanelModel
    {
        /// <summary>表示内容を組み立てる。</summary>
        /// <param name="input">入力。null なら空の表示内容。</param>
        /// <returns>表示内容。</returns>
        public static ParticipantPanelState Build(ParticipantPanelInput input)
        {
            if (input == null)
            {
                return new ParticipantPanelState(null, false, ParticipantPanelSummaryKind.None, 0, 0);
            }

            var progress = ResolveProgress(input);
            var answererCount = CountAnswerers(progress);
            var rows = new List<ParticipantPanelRow>();

            foreach (var entry in input.Roster)
            {
                if (entry.IsModeratorHost)
                {
                    continue;
                }

                progress.TryGet(entry.ClientId, out var state);
                var status = ResolveStatus(input, entry.ClientId, state.Flags);
                var buzzRank = input.IsChoiceQuestion ? ParticipantProgress.NoRank : state.BuzzRank;

                rows.Add(new ParticipantPanelRow(
                    entry.ClientId,
                    entry.DisplayName,
                    input.LocalClientId.HasValue && input.LocalClientId.Value == entry.ClientId,
                    entry.IsConnected,
                    status,
                    buzzRank,
                    buzzRank != ParticipantProgress.NoRank && state.Flags.Has(ParticipantProgressFlags.TiedWithWinner),
                    answererCount >= 2 ? state.AnswerOrder : ParticipantProgress.NoRank,
                    input.GetScore(entry.ClientId)));
            }

            var summaryKind = ResolveSummaryKind(input);
            CountSummary(rows, summaryKind, out var count, out var total);
            return new ParticipantPanelState(rows, input.ShowScores, summaryKind, count, total);
        }

        /// <summary>現在の問題の進行状態だけを採用する（問題インデックスが食い違えば空）。</summary>
        private static QuestionProgress ResolveProgress(ParticipantPanelInput input)
        {
            var progress = input.Progress;
            return input.QuestionIndex >= 0 && progress.QuestionIndex == input.QuestionIndex
                ? progress
                : QuestionProgress.Empty;
        }

        private static int CountAnswerers(QuestionProgress progress)
        {
            var count = 0;
            foreach (var entry in progress.Entries)
            {
                if (entry.AnswerOrder != ParticipantProgress.NoRank)
                {
                    count++;
                }
            }

            return count;
        }

        private static ParticipantStatus ResolveStatus(
            ParticipantPanelInput input, ulong clientId, ParticipantProgressFlags flags)
        {
            if (flags.Has(ParticipantProgressFlags.SuspendedSkipNext))
            {
                return ParticipantStatus.Suspended;
            }

            if (flags.Has(ParticipantProgressFlags.Correct))
            {
                return ParticipantStatus.Correct;
            }

            if (flags.Has(ParticipantProgressFlags.WrongAnswered))
            {
                return ParticipantStatus.Wrong;
            }

            if (input.IsChoiceQuestion)
            {
                if (flags.Has(ParticipantProgressFlags.ChoiceSubmitted))
                {
                    return ParticipantStatus.ChoiceSubmitted;
                }

                return IsChoiceOpenedOrAfter(input.Phase) ? ParticipantStatus.ChoiceUnanswered : ParticipantStatus.None;
            }

            return clientId == input.LockedClientId && IsAnsweringPhase(input.Phase)
                ? ParticipantStatus.Answering
                : ParticipantStatus.None;
        }

        private static ParticipantPanelSummaryKind ResolveSummaryKind(ParticipantPanelInput input)
        {
            if (input.QuestionIndex < 0)
            {
                return ParticipantPanelSummaryKind.None;
            }

            if (input.IsChoiceQuestion)
            {
                return IsChoiceOpenedOrAfter(input.Phase)
                    ? ParticipantPanelSummaryKind.ChoiceAnswered
                    : ParticipantPanelSummaryKind.None;
            }

            switch (input.Phase)
            {
                case QuizPhase.Reading:
                case QuizPhase.BuzzOpen:
                case QuizPhase.Locked:
                case QuizPhase.Answering:
                case QuizPhase.Judging:
                    return ParticipantPanelSummaryKind.BuzzEligible;
                default:
                    return ParticipantPanelSummaryKind.None;
            }
        }

        /// <summary>
        /// 要約の人数を数える（統括判断 #194、PR #201 再レビュー M-A）。
        /// 自由入力は「接続中の参加者のうち、誤答・休み・正解でない人（回答中を含む） / 接続中の参加者（休みの人を含む）」。
        /// 選択式は「選択を送った人（判定後は正誤が付いた人も含む） / 接続中で休みでない参加者」
        /// （休みの人はそもそも選択できないので母数に入れない）。
        /// </summary>
        private static void CountSummary(
            List<ParticipantPanelRow> rows, ParticipantPanelSummaryKind kind, out int count, out int total)
        {
            count = 0;
            total = 0;
            foreach (var row in rows)
            {
                if (kind == ParticipantPanelSummaryKind.BuzzEligible && row.IsConnected)
                {
                    total++;
                    if (row.Status == ParticipantStatus.None || row.Status == ParticipantStatus.Answering)
                    {
                        count++;
                    }
                }
                else if (kind == ParticipantPanelSummaryKind.ChoiceAnswered
                         && row.IsConnected
                         && row.Status != ParticipantStatus.Suspended)
                {
                    total++;
                    if (row.Status == ParticipantStatus.ChoiceSubmitted
                        || row.Status == ParticipantStatus.Correct
                        || row.Status == ParticipantStatus.Wrong)
                    {
                        count++;
                    }
                }
            }
        }

        private static bool IsAnsweringPhase(QuizPhase phase) =>
            phase == QuizPhase.Locked || phase == QuizPhase.Answering || phase == QuizPhase.Judging;

        private static bool IsChoiceOpenedOrAfter(QuizPhase phase) =>
            phase == QuizPhase.ChoiceAnswering
            || phase == QuizPhase.Judging
            || phase == QuizPhase.Result
            || phase == QuizPhase.Finished;
    }
}
