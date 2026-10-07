namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> のうち、司会専用モードの判定上書き（#20、
    /// docs/tasks/setup-brief.md K18「強制正解」「強制不正解」）をまとめた部分。
    /// </summary>
    /// <remarks>
    /// 新しい判定種別（<c>Overridden</c> 系）は追加しない。既存の
    /// <see cref="QuizJudgement.Correct"/> / <see cref="QuizJudgement.Wrong"/> をそのまま使い、
    /// 「司会由来である」ことはサーバーのログにのみ残す（クライアントへの通知経路は
    /// <c>SubmitAnswer</c> と共通の <c>QuestionResultRpc</c>）。
    /// </remarks>
    public sealed partial class QuizStateMachine
    {
        /// <summary>
        /// 司会が回答者の判定を上書きする（Answering 中、ロック保持者が居るときのみ）。
        /// 得点の反映は通常の回答と同じく <see cref="Tick"/>（Judging からの遷移）で行う。
        /// </summary>
        /// <param name="judgement">
        /// 上書きする判定。<see cref="QuizJudgement.Correct"/> か <see cref="QuizJudgement.Wrong"/> のみ受理する。
        /// </param>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        /// <param name="reason">棄却理由。受理時は <see cref="AnswerReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool ForceJudge(QuizJudgement judgement, double serverNow, out AnswerReject reason)
        {
            if (IsPaused)
            {
                reason = AnswerReject.Paused;
                return false;
            }

            if (Phase != QuizPhase.Answering)
            {
                reason = AnswerReject.NotAnswering;
                return false;
            }

            if (LockedClientId == NoClientId)
            {
                reason = AnswerReject.NotLockedPlayer;
                return false;
            }

            if (judgement != QuizJudgement.Correct && judgement != QuizJudgement.Wrong)
            {
                reason = AnswerReject.InvalidJudgement;
                return false;
            }

            if (!double.IsFinite(serverNow))
            {
                reason = AnswerReject.NonFiniteTime;
                return false;
            }

            // LastAnswerText は書き換えない（司会が判定しただけで、回答者が実際に何か送信したわけではない。
            // 司会由来であることはサーバーのログにのみ残す。呼び出し元の GameSession.ForceJudgeRpc 参照）。
            AnswerAttemptCount++;
            LastJudgement = judgement;
            SetPhase(QuizPhase.Judging, serverNow);
            reason = AnswerReject.None;
            return true;
        }
    }
}
