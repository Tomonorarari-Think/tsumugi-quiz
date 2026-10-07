using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Participants;
using TsumugiQuiz.Network;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// 選択式の一斉判定（<see cref="GameSession.ChoiceResolved"/>）を、立ち絵の表情に使う判定へ読み替える（issue #212）。
    /// </summary>
    /// <remarks>
    /// 規則（ユーザー決定 2026-10-03、docs/tts.md §8.3.1）:
    /// <list type="bullet">
    ///   <item><description>選んだ人 → 自分の正誤</description></item>
    ///   <item><description>回答できる立場なのに選ばなかったプレイヤー → 時間切れ</description></item>
    ///   <item><description>回答できない立場（司会専任のホスト・その問題が休み）、または自分の ID が分からない →
    ///     全体の結果（誰かが正解なら正解、全員外れなら不正解、誰も選ばなければ時間切れ）</description></item>
    /// </list>
    /// </remarks>
    public static class CharacterChoiceOutcome
    {
        /// <summary>立ち絵の表情に使う判定を返す。</summary>
        /// <param name="entries">選択した全クライアント分の結果（null は「誰も選ばなかった」扱い）。</param>
        /// <param name="localClientId">このクライアントの ID（分からなければ null）。</param>
        /// <param name="isLocalAnswerer">
        /// このクライアントがその問題で選べる立場だったか（<see cref="IsLocalAnswerer"/>）。
        /// </param>
        public static QuizJudgement ToJudgement(
            IReadOnlyList<ChoiceAnswerEntry> entries, ulong? localClientId, bool isLocalAnswerer)
        {
            var anyCorrect = false;
            var anyEntry = false;
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (localClientId.HasValue && entry.ClientId == localClientId.Value)
                    {
                        return entry.IsCorrect ? QuizJudgement.Correct : QuizJudgement.Wrong;
                    }

                    anyEntry = true;
                    anyCorrect |= entry.IsCorrect;
                }
            }

            if (localClientId.HasValue && isLocalAnswerer)
            {
                // 選べたのに選ばなかった。
                return QuizJudgement.TimedOut;
            }

            if (!anyEntry)
            {
                return QuizJudgement.TimedOut;
            }

            return anyCorrect ? QuizJudgement.Correct : QuizJudgement.Wrong;
        }

        /// <summary>
        /// このクライアントが、その選択式の問題で選べる立場か。サーバーが選択を拒否する条件
        /// （司会専任のホスト <c>GameSession.IsModeratorHostSender</c>、休み <c>QuizStateMachine.SubmitChoice</c> の
        /// <c>Penalties.IsSuspended</c>）と同じにする。
        /// </summary>
        /// <param name="isModeratorHost">このクライアントが司会専任のホスト（<c>host.role = "moderator"</c>）か。</param>
        /// <param name="localClientId">このクライアントの ID（分からなければ null = 選べる立場とは判断しない）。</param>
        /// <param name="progress">同期された進行状態（#194。null は行が無い扱い）。</param>
        /// <param name="currentQuestionIndex">いまの問題インデックス。進行状態が別の問題のものなら、その休みは使わない。</param>
        public static bool IsLocalAnswerer(
            bool isModeratorHost, ulong? localClientId, QuestionProgress progress, int currentQuestionIndex)
        {
            if (isModeratorHost || !localClientId.HasValue)
            {
                return false;
            }

            var isSuspendedThisQuestion = progress != null
                && progress.QuestionIndex == currentQuestionIndex
                && progress.TryGet(localClientId.Value, out var row)
                && row.Flags.Has(ParticipantProgressFlags.SuspendedSkipNext);
            return !isSuspendedThisQuestion;
        }
    }
}
