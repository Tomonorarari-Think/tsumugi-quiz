using System;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// フェーズごとの締め切り・残り時間を、フェーズの時刻アンカーと制限時間から求める純粋関数
    /// （issue #154、docs/network.md §6.3）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 締め切りの起点と制限時間の組は <see cref="QuizStateMachine"/> のタイムアウト判定
    /// （QuizStateMachine.Tick.cs / QuizStateMachine.Choice.cs）と同じにする。
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see cref="QuizPhase.BuzzOpen"/>: T0（<c>buzzOpenServerTime</c>）＋ <c>buzz.timeLimitSec</c></description></item>
    ///   <item><description><see cref="QuizPhase.Answering"/>: フェーズ開始時刻 ＋ <c>answer.freeTextTimeLimitSec</c></description></item>
    ///   <item><description><see cref="QuizPhase.ChoiceAnswering"/>: 受付開始時刻（<c>buzzOpenServerTime</c> の枠を流用、#17）＋ <c>answer.choiceTimeLimitSec</c></description></item>
    ///   <item><description>それ以外（<see cref="QuizPhase.Reading"/> 等）: 締め切り無し（<c>double.NaN</c>）</description></item>
    /// </list>
    /// <para>
    /// 司会の一時停止（#20）では、サーバーが再開時に時刻アンカーを後ろへずらす（QuizStateMachine.Pause.cs）ので、
    /// ここでは一時停止を特別扱いしない。
    /// </para>
    /// </remarks>
    public static class QuizDeadlines
    {
        /// <summary>
        /// 指定フェーズの締め切り（サーバー時刻軸の秒）を求める。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="phaseStartServerTime">現在のフェーズに入ったサーバー時刻（秒）。</param>
        /// <param name="buzzOpenServerTime">受付開始時刻 T0（サーバー時刻軸の秒）。</param>
        /// <param name="limits">制限時間。</param>
        /// <returns>締め切り。締め切りが無いフェーズ、または起点の時刻が非有限なら <c>double.NaN</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="limits"/> が null のとき。</exception>
        public static double DeadlineServerTime(
            QuizPhase phase, double phaseStartServerTime, double buzzOpenServerTime, QuizTimeLimits limits)
        {
            if (limits == null)
            {
                throw new ArgumentNullException(nameof(limits));
            }

            switch (phase)
            {
                case QuizPhase.BuzzOpen:
                    return AddLimit(
                        IntervalStartServerTime(phase, phaseStartServerTime, buzzOpenServerTime), limits.BuzzTimeLimitSec);
                case QuizPhase.Answering:
                    return AddLimit(
                        IntervalStartServerTime(phase, phaseStartServerTime, buzzOpenServerTime), limits.AnswerTimeLimitSec);
                case QuizPhase.ChoiceAnswering:
                    return AddLimit(
                        IntervalStartServerTime(phase, phaseStartServerTime, buzzOpenServerTime), limits.ChoiceTimeLimitSec);
                default:
                    return double.NaN;
            }
        }

        /// <summary>
        /// 指定フェーズの計測区間の開始時刻（起点）を選ぶ（issue #178）。早押し受付中
        /// （<see cref="QuizPhase.BuzzOpen"/>）と選択式の回答受付中（<see cref="QuizPhase.ChoiceAnswering"/>、
        /// 受付開始時刻 <paramref name="buzzOpenServerTime"/> の枠を流用、#17）は T0、それ以外
        /// （<see cref="QuizPhase.Answering"/> 等）はフェーズ開始時刻を使う。<see cref="DeadlineServerTime"/>
        /// の起点選択と同じ規則で、UI の残り時間バーの開始位置（<c>GameViewPresenter.ProgressFraction</c>）にも使う。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="phaseStartServerTime">現在のフェーズに入ったサーバー時刻（秒）。</param>
        /// <param name="buzzOpenServerTime">受付開始時刻 T0（サーバー時刻軸の秒）。</param>
        public static double IntervalStartServerTime(QuizPhase phase, double phaseStartServerTime, double buzzOpenServerTime)
        {
            switch (phase)
            {
                case QuizPhase.BuzzOpen:
                case QuizPhase.ChoiceAnswering:
                    return buzzOpenServerTime;
                default:
                    return phaseStartServerTime;
            }
        }

        /// <summary>
        /// 起点に制限時間を足す。起点が非有限（NaN / ±∞）なら「締め切り無し」（<c>double.NaN</c>）として扱い、
        /// 表示に「残り ∞ 秒」等を出さない（同期値が壊れていた場合の境界、#154 L4）。
        /// </summary>
        private static double AddLimit(double anchorServerTime, double limitSec)
        {
            return double.IsFinite(anchorServerTime) ? anchorServerTime + limitSec : double.NaN;
        }

        /// <summary>
        /// 指定フェーズの残り秒数を求める。締め切りを過ぎていれば 0、締め切りが無いフェーズ・時刻が非有限なら <c>double.NaN</c>。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="phaseStartServerTime">現在のフェーズに入ったサーバー時刻（秒）。</param>
        /// <param name="buzzOpenServerTime">受付開始時刻 T0（サーバー時刻軸の秒）。</param>
        /// <param name="limits">制限時間。</param>
        /// <param name="currentServerTime">現在のサーバー時刻（秒）。</param>
        /// <returns>残り秒数。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="limits"/> が null のとき。</exception>
        public static double RemainingSeconds(
            QuizPhase phase,
            double phaseStartServerTime,
            double buzzOpenServerTime,
            QuizTimeLimits limits,
            double currentServerTime)
        {
            var deadline = DeadlineServerTime(phase, phaseStartServerTime, buzzOpenServerTime, limits);
            return RemainingSeconds(deadline, currentServerTime);
        }

        /// <summary>
        /// 締め切りからの残り秒数を求める（起点・制限時間を持たず、締め切りを既に求めてある場合の版、issue #178）。
        /// UI 側（<c>GameSession.CurrentDeadlineServerTime</c> など、既に <see cref="DeadlineServerTime"/> で
        /// 求めた値を持っている場合）はこちらを直接使う。
        /// </summary>
        /// <param name="deadlineServerTime">締め切り。締め切りが無いフェーズでは <c>double.NaN</c>。</param>
        /// <param name="currentServerTime">現在のサーバー時刻（秒）。</param>
        /// <returns>残り秒数。締め切りを過ぎていれば 0、締め切りが無い・現在時刻が非有限なら <c>double.NaN</c>。</returns>
        public static double RemainingSeconds(double deadlineServerTime, double currentServerTime)
        {
            if (double.IsNaN(deadlineServerTime) || !double.IsFinite(currentServerTime))
            {
                // 締め切りが無い、または現在時刻が壊れている（NaN / ±∞）場合は「不明」として NaN を返し、
                // 「残り ∞ 秒」を作らない（#154 L4）。
                return double.NaN;
            }

            var remaining = deadlineServerTime - currentServerTime;
            return remaining > 0.0 ? remaining : 0.0;
        }
    }
}
