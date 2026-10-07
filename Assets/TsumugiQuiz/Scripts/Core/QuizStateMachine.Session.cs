using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> のうち、複数問の進行（セッション）に関する部分（#19、
    /// docs/network.md §6.6 の <c>Result --&gt; Idle</c>（次の問題へ）と <c>Result --&gt; [*]</c>（全問終了））。
    /// 1 問の中の遷移は QuizStateMachine.cs / .Tick.cs、得点は .Score.cs にある。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本クラスは問題データを持たない（正解候補だけを 1 問分保持する）。そのため
    /// 「何問あるか」だけを <see cref="TotalQuestions"/> として受け取り、次問の正解候補は
    /// 呼び出し側（<c>GameSession</c>）が <see cref="AdvanceToNextQuestion"/> に渡す。
    /// 出題順の決定（フィルタ・シャッフル・出題数）は <c>TsumugiQuiz.Network.QuestionSelector</c> の担当で、
    /// <see cref="QuestionIndex"/> は「確定した出題列の中の位置」を指す。
    /// </para>
    /// </remarks>
    public sealed partial class QuizStateMachine
    {
        /// <summary>出題列が未設定（単問モード）であることを表す問題数。</summary>
        public const int NoSession = 0;

        /// <summary>
        /// このセッションの総問題数。<see cref="StartSession"/> を呼ぶまでは <see cref="NoSession"/>
        /// （＝単問モード。<see cref="StartQuestion"/> の問題インデックスに上限を設けない）。
        /// </summary>
        public int TotalQuestions { get; private set; } = NoSession;

        /// <summary>
        /// 全問終了（<see cref="QuizPhase.Finished"/>）時点の得点表。終了前は null。
        /// 終了後は得点が動かないので、最終結果の表示・配信にはこの値を使う。
        /// </summary>
        public ScoreBoard FinalScores { get; private set; }

        /// <summary>
        /// 出題列にまだ出題していない問題が残っているか。単問モード（<see cref="StartSession"/> 未呼び出し）
        /// では常に false。未出題（<see cref="QuestionIndex"/> が -1）の時点では 0 問目を指すので true。
        /// </summary>
        public bool HasNextQuestion => TotalQuestions > NoSession && QuestionIndex + 1 < TotalQuestions;

        /// <summary>次に出題する問題のインデックス。次が無ければ -1。</summary>
        public int NextQuestionIndex => HasNextQuestion ? QuestionIndex + 1 : -1;

        /// <summary>
        /// 複数問のセッションを開始する（Lobby / Finished でのみ受理）。
        /// 得点・ペナルティ・判定結果を初期化し、<see cref="QuizPhase.Lobby"/> に戻す。
        /// 実際の出題は <see cref="StartQuestion"/>（0 問目）から始める。
        /// </summary>
        /// <param name="totalQuestions">出題列の長さ（1 以上）。</param>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        /// <param name="reason">拒否理由。成功時は <see cref="QuizReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool StartSession(int totalQuestions, double serverNow, out QuizReject reason)
        {
            if (Phase != QuizPhase.Lobby && Phase != QuizPhase.Finished)
            {
                // 進行中のセッションを別の出題列で上書きしない（得点が混ざるため）。
                reason = QuizReject.InvalidPhase;
                return false;
            }

            if (totalQuestions < 1)
            {
                reason = QuizReject.InvalidTotalQuestions;
                return false;
            }

            if (!double.IsFinite(serverNow))
            {
                reason = QuizReject.NonFiniteTime;
                return false;
            }

            TotalQuestions = totalQuestions;
            ResetForNewSession(serverNow);
            reason = QuizReject.None;
            return true;
        }

        /// <summary>
        /// 結果の提示を終えて次へ進む（Result → Reading、次が無ければ Result → Finished）。
        /// </summary>
        /// <remarks>
        /// 次の問題があるかは <see cref="HasNextQuestion"/> で先に判定できる。
        /// 呼び出し側は <see cref="NextQuestionIndex"/> の正解候補を用意してから呼ぶこと
        /// （次が無い場合 <paramref name="nextAnswers"/> は使われないので null でよい）。
        /// </remarks>
        /// <param name="nextAnswers">次の問題の正解候補（1 件以上）。次が無ければ null 可。</param>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        /// <param name="reason">拒否理由。成功時は <see cref="QuizReject.None"/>。</param>
        /// <returns>行った遷移。</returns>
        public QuizAdvance AdvanceToNextQuestion(
            IReadOnlyList<string> nextAnswers, double serverNow, out QuizReject reason)
        {
            if (Phase != QuizPhase.Result)
            {
                reason = QuizReject.InvalidPhase;
                return QuizAdvance.Rejected;
            }

            if (!double.IsFinite(serverNow))
            {
                reason = QuizReject.NonFiniteTime;
                return QuizAdvance.Rejected;
            }

            if (!HasNextQuestion)
            {
                return Finish(serverNow, out reason) ? QuizAdvance.SessionFinished : QuizAdvance.Rejected;
            }

            return StartQuestion(NextQuestionIndex, nextAnswers, serverNow, out reason)
                ? QuizAdvance.NextQuestionStarted
                : QuizAdvance.Rejected;
        }

        /// <summary>
        /// 新しいセッションのために、前回の進行（得点・ペナルティ・判定）を捨てて Lobby に戻す。
        /// </summary>
        private void ResetForNewSession(double serverNow)
        {
            Scores = new ScoreBoard(_rules.Score);
            Penalties = PenaltyTracker.Empty;
            FinalScores = null;

            _wrongAnswerers.Clear();
            _answers.Clear();
            _arbiter = null;
            _readingEndServerTime = serverNow;
            _choiceSelections.Clear();
            _lastChoiceResults = EmptyChoiceResults;
            ClearProgress(); // #194
            _isChoiceQuestion = false;
            _correctChoiceIndex = -1;
            _choiceCount = 0;

            QuestionIndex = -1;
            BuzzOpenServerTime = 0.0;
            LockedClientId = NoClientId;
            LastJudgement = QuizJudgement.None;
            LastResolution = null;
            LastAnswerText = string.Empty;
            AnswerAttemptCount = 0;
            ClearLastScoreChange();

            SetPhase(QuizPhase.Lobby, serverNow);
        }
    }
}
