namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> のうち、司会専用モードの一時停止/再開（#20、
    /// docs/tasks/setup-brief.md K18）をまとめた部分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 一時停止中は <see cref="QuizStateMachine.Tick"/> が時間経過による遷移を一切行わない
    /// （<c>QuizStateMachine.Tick.cs</c> の <see cref="Tick"/> 冒頭のガード）。押下・回答の受理も
    /// 拒否する（<see cref="AcceptBuzz"/> / <see cref="SubmitAnswer"/>）。
    /// </para>
    /// <para>
    /// 再開時は、フェーズ開始時刻・受付開始 T0・読み上げ完了時刻・早押し集計窓の締め切りを
    /// 一時停止していた秒数だけ後ろへずらす（「<c>PhaseStartServerTime</c> をずらす方式」、
    /// docs/tasks/setup-brief.md K18 の実装ノート）。これにより「経過時間」の計算
    /// （<c>serverNow - PhaseStartServerTime</c> 等）が一時停止前後で連続し、残り時間が保存される。
    /// </para>
    /// </remarks>
    public sealed partial class QuizStateMachine
    {
        private double _pauseStartServerTime;

        /// <summary>司会が一時停止中か。</summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// 指定したフェーズ・状態で一時停止（または「一時停止」ボタンの活性化）を受理してよいか。
        /// <see cref="Pause"/> 自身の判定と、UI 側のボタン活性化判定
        /// （<c>ModeratorControlsPanel.Refresh</c>）の両方がここを参照する唯一の定義（統括判断 M-C）。
        /// Unity API に依存しない純ロジックなので UI 層（<c>TsumugiQuiz.UI</c>）からも直接呼べる。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="isPaused">
        /// 既に一時停止中か。true の場合は「再開」として常に受理してよい（<see cref="Resume"/> はフェーズを問わない）。
        /// </param>
        /// <returns>
        /// 一時停止（<paramref name="isPaused"/> が false のとき）または再開（true のとき）を
        /// 受理してよいなら true。Lobby / Finished / Reading 中の一時停止だけを拒否する
        /// （Reading は TTS の同期再生を途中で止められないため、docs/network.md の司会操作節）。
        /// </returns>
        public static bool CanPause(QuizPhase phase, bool isPaused)
        {
            if (isPaused)
            {
                return true;
            }

            return phase != QuizPhase.Lobby && phase != QuizPhase.Finished && phase != QuizPhase.Reading;
        }

        /// <summary>
        /// 進行を一時停止する（司会専用モードの「一時停止」、#20）。
        /// 受理できるフェーズは <see cref="CanPause"/> を参照（BuzzOpen / Locked / Answering /
        /// ChoiceAnswering / Judging / Result）。既に一時停止中は拒否する。
        /// </summary>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        /// <param name="reason">拒否理由。成功時は <see cref="QuizReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool Pause(double serverNow, out QuizReject reason)
        {
            if (IsPaused)
            {
                reason = QuizReject.InvalidPhase;
                return false;
            }

            if (!CanPause(Phase, isPaused: false))
            {
                reason = QuizReject.InvalidPhase;
                return false;
            }

            if (!double.IsFinite(serverNow))
            {
                reason = QuizReject.NonFiniteTime;
                return false;
            }

            IsPaused = true;
            _pauseStartServerTime = serverNow;
            reason = QuizReject.None;
            return true;
        }

        /// <summary>
        /// 一時停止を解除する（司会専用モードの「再開」、#20）。
        /// 一時停止していた秒数だけ各種の時刻アンカーを後ろへずらし、残り時間を保存する。
        /// </summary>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        /// <param name="reason">拒否理由。成功時は <see cref="QuizReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool Resume(double serverNow, out QuizReject reason)
        {
            if (!IsPaused)
            {
                reason = QuizReject.InvalidPhase;
                return false;
            }

            if (!double.IsFinite(serverNow))
            {
                reason = QuizReject.NonFiniteTime;
                return false;
            }

            // 稀に serverNow が一時停止開始時刻より前になる（時刻の丸め等）ことがあっても、
            // 負の経過として扱ってアンカーを巻き戻さない（安全側に倒す）。
            var pausedDurationSec = serverNow - _pauseStartServerTime;
            if (pausedDurationSec > 0.0)
            {
                PhaseStartServerTime += pausedDurationSec;

                if (BuzzOpenServerTime > 0.0)
                {
                    BuzzOpenServerTime += pausedDurationSec;
                }

                _readingEndServerTime += pausedDurationSec;
                _arbiter?.ShiftDeadline(pausedDurationSec);
            }

            IsPaused = false;
            reason = QuizReject.None;
            return true;
        }
    }
}
