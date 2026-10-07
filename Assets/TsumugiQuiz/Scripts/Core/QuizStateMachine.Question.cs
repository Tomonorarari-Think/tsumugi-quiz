using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> のうち、出題（freeText の <c>StartQuestion</c>）と、
    /// freeText / choice の両方から使う共通の出題処理（検証・状態リセット）をまとめた部分（レビュー L9）。
    /// 選択式専用の出題（<c>StartQuestion(int, int, int, double, out QuizReject)</c>）は
    /// <c>QuizStateMachine.Choice.cs</c> にある。
    /// </summary>
    public sealed partial class QuizStateMachine
    {
        /// <summary>
        /// 出題を開始する（Lobby / Result → Reading）。
        /// 読み上げ完了時刻は既定で「出題と同時」＝ TTS 無効時の挙動（docs/network.md §6.3）。
        /// TTS（#23）を使う場合は <see cref="SetReadingCompleted"/> で後から差し替える。
        /// </summary>
        /// <param name="questionIndex">問題インデックス（0 以上）。</param>
        /// <param name="answers">正解候補（1 件以上）。サーバー内にのみ保持する。</param>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        /// <param name="reason">拒否理由。成功時は <see cref="QuizReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool StartQuestion(int questionIndex, IReadOnlyList<string> answers, double serverNow, out QuizReject reason)
        {
            if (!ValidateStartQuestion(questionIndex, serverNow, out reason))
            {
                return false;
            }

            if (!TryCollectAnswers(answers, out var collected))
            {
                reason = QuizReject.NoAnswers;
                return false;
            }

            _answers.Clear();
            _answers.AddRange(collected);
            _isChoiceQuestion = false;
            _correctChoiceIndex = -1;
            _choiceCount = 0;

            CommitQuestionStart(questionIndex, serverNow);
            reason = QuizReject.None;
            return true;
        }

        /// <summary>
        /// 出題（<paramref name="questionIndex"/>）を開始してよいか（Lobby / Result → Reading 共通の検証）。
        /// freeText / choice のどちらの <c>StartQuestion</c> オーバーロードからも呼ぶ（#17）。
        /// </summary>
        private bool ValidateStartQuestion(int questionIndex, double serverNow, out QuizReject reason)
        {
            if (IsPaused)
            {
                // 司会が一時停止中は次の問題へ進めない（#20、司会の「次へ」の棄却）。
                reason = QuizReject.Paused;
                return false;
            }

            if (Phase != QuizPhase.Lobby && Phase != QuizPhase.Result)
            {
                reason = QuizReject.InvalidPhase;
                return false;
            }

            if (questionIndex < 0)
            {
                reason = QuizReject.InvalidQuestionIndex;
                return false;
            }

            if (TotalQuestions > NoSession && questionIndex >= TotalQuestions)
            {
                // 出題列（#19）が確定している場合、その外側は出題できない。
                reason = QuizReject.InvalidQuestionIndex;
                return false;
            }

            if (!double.IsFinite(serverNow))
            {
                reason = QuizReject.NonFiniteTime;
                return false;
            }

            reason = QuizReject.None;
            return true;
        }

        /// <summary>
        /// <see cref="ValidateStartQuestion"/> を通過したあと、共通の状態リセットと
        /// <see cref="QuizPhase.Reading"/> への遷移を行う（#17）。
        /// 出題形式ごとの状態（<c>_answers</c> / 選択式の正解インデックス等）は呼び出し側が先に設定すること。
        /// </summary>
        private void CommitQuestionStart(int questionIndex, double serverNow)
        {
            _arbiter = null;
            _readingEndServerTime = serverNow;
            _wrongAnswerers.Clear();
            _choiceSelections.Clear();
            _lastChoiceResults = EmptyChoiceResults;
            ClearProgress(); // #194

            // 前問のお手つきで積まれた「次問休み」を、この問題の休み集合として確定させる。
            Penalties = Penalties.WithQuestionStarted(questionIndex);

            QuestionIndex = questionIndex;
            BuzzOpenServerTime = 0.0;
            LockedClientId = NoClientId;
            LastJudgement = QuizJudgement.None;
            LastResolution = null;
            LastAnswerText = string.Empty;
            LastScoredClientId = NoClientId;
            LastScoreDelta = 0;
            AnswerAttemptCount = 0;

            SetPhase(QuizPhase.Reading, serverNow);
        }

        private static bool TryCollectAnswers(IReadOnlyList<string> answers, out List<string> collected)
        {
            collected = new List<string>();
            if (answers == null)
            {
                return false;
            }

            for (var i = 0; i < answers.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(answers[i]))
                {
                    collected.Add(answers[i]);
                }
            }

            return collected.Count > 0;
        }
    }
}
