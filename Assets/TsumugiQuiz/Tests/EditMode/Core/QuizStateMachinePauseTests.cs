using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> の一時停止/再開（司会専用モード、#20、
    /// docs/tasks/setup-brief.md K18）のテスト。
    /// </summary>
    public class QuizStateMachinePauseTests : QuizStateMachineTestBase
    {
        private static QuizTimeLimits ShortLimits =>
            new QuizTimeLimits(buzzTimeLimitSec: 2.0, answerTimeLimitSec: 2.0, collectWindowSec: Window);

        [Test]
        public void Pause_DuringBuzzOpen_StopsTimeoutFromAdvancing()
        {
            var machine = OpenBuzz(ShortLimits);

            Assert.IsTrue(machine.Pause(T0 + 0.5, out var reason));
            Assert.AreEqual(QuizReject.None, reason);
            Assert.IsTrue(machine.IsPaused);

            // 本来なら T0 + 2.0 でタイムアウトするはずだが、一時停止中は大きく時間が進んでも遷移しない。
            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + 1000.0, Rng()));
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase, "一時停止中はタイムアウトしないはず。");
        }

        [Test]
        public void Resume_AfterPause_PreservesRemainingTimeBeforeTimeout()
        {
            var machine = OpenBuzz(ShortLimits);

            // 0.5 秒経過した時点（残り 1.5 秒）で一時停止する。
            Assert.IsTrue(machine.Pause(T0 + 0.5, out _));

            // 長時間止めても消費されない。
            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + 500.0, Rng()));

            // 100 秒後に再開する。
            Assert.IsTrue(machine.Resume(T0 + 500.0, out var resumeReason));
            Assert.AreEqual(QuizReject.None, resumeReason);
            Assert.IsFalse(machine.IsPaused);

            // 再開直後、残り時間（1.5 秒）の手前ではまだタイムアウトしない。
            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + 500.0 + 1.4, Rng()));
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);

            // 残り時間ぶん経過すればタイムアウトする（一時停止で消費された時間は含まれない）。
            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(T0 + 500.0 + 1.5, Rng()));
            Assert.AreEqual(QuizPhase.Result, machine.Phase);
        }

        [Test]
        public void Pause_DuringAnswering_StopsAnswerTimeoutFromAdvancing()
        {
            var machine = LockAndOpenAnswer(out var answeringStart, limits: ShortLimits);

            Assert.IsTrue(machine.Pause(answeringStart + 0.3, out _));

            Assert.AreEqual(QuizEvent.None, machine.Tick(answeringStart + 1000.0, Rng()));
            Assert.AreEqual(QuizPhase.Answering, machine.Phase, "一時停止中は回答の時間切れにならないはず。");

            Assert.IsTrue(machine.Resume(answeringStart + 1000.0, out _));

            // 一時停止直前の残り時間（2.0 - 0.3 = 1.7 秒）の手前では時間切れにならない。
            Assert.AreEqual(QuizEvent.None, machine.Tick(answeringStart + 1000.0 + 1.6, Rng()));
            Assert.AreEqual(QuizPhase.Answering, machine.Phase);

            Assert.AreEqual(QuizEvent.AnswerTimedOut, machine.Tick(answeringStart + 1000.0 + 1.7, Rng()));
        }

        [Test]
        public void Pause_RejectsBuzzAndAnswerWhilePaused()
        {
            var machine = OpenBuzz(ShortLimits);
            Assert.IsTrue(machine.Pause(T0 + 0.1, out _));

            Assert.IsFalse(machine.AcceptBuzz(Client1, T0 + 0.2, T0 + 0.2, out var buzzReason));
            Assert.AreEqual(BuzzReject.Paused, buzzReason);

            var answeringMachine = LockAndOpenAnswer(out var answeringStart, limits: ShortLimits);
            Assert.IsTrue(answeringMachine.Pause(answeringStart + 0.1, out _));
            Assert.IsFalse(answeringMachine.SubmitAnswer(Client1, "とうきょう", answeringStart + 0.2, out var answerReason));
            Assert.AreEqual(AnswerReject.Paused, answerReason);
        }

        [Test]
        public void SubmitChoice_WhilePaused_IsRejected()
        {
            // M-B: 選択式（choice）の一時停止中の棄却も freeText / 早押しと同じ扱いにする。
            var machine = new QuizStateMachine(ShortLimits);
            Assert.IsTrue(machine.StartQuestion(0, correctChoiceIndex: 1, choiceCount: 4, T0, out _));
            Assert.AreEqual(QuizEvent.ChoiceAnsweringOpened, machine.Tick(T0, Rng()));
            Assert.AreEqual(QuizPhase.ChoiceAnswering, machine.Phase);

            Assert.IsTrue(machine.Pause(T0 + 0.1, out _));

            Assert.IsFalse(machine.SubmitChoice(Client1, 1, T0 + 0.2, out var reason));
            Assert.AreEqual(AnswerReject.Paused, reason);
        }

        /// <summary>
        /// LOW（PR #81 最終レビュー）: <see cref="QuizStateMachine.CanPause"/> 単体のテスト。
        /// <see cref="QuizStateMachine.Pause"/> と <c>ModeratorControlsPanel</c> が共有する唯一の判定なので、
        /// フェーズごとの境界（選択式・既に一時停止中・Reading）を直接確認する。
        /// </summary>
        [TestCase(QuizPhase.ChoiceAnswering, false, ExpectedResult = true)]
        [TestCase(QuizPhase.Finished, true, ExpectedResult = true)]
        [TestCase(QuizPhase.Reading, false, ExpectedResult = false)]
        public bool CanPause_ReturnsExpectedResult(QuizPhase phase, bool isPaused) =>
            QuizStateMachine.CanPause(phase, isPaused);

        [Test]
        public void Pause_WhenAlreadyPaused_IsRejected()
        {
            var machine = OpenBuzz(ShortLimits);
            Assert.IsTrue(machine.Pause(T0 + 0.1, out _));

            Assert.IsFalse(machine.Pause(T0 + 0.2, out var reason));
            Assert.AreEqual(QuizReject.InvalidPhase, reason);
        }

        [Test]
        public void Resume_WhenNotPaused_IsRejected()
        {
            var machine = OpenBuzz(ShortLimits);

            Assert.IsFalse(machine.Resume(T0 + 0.2, out var reason));
            Assert.AreEqual(QuizReject.InvalidPhase, reason);
        }

        [Test]
        public void Pause_InLobbyOrFinished_IsRejected()
        {
            var machine = new QuizStateMachine();
            Assert.IsFalse(machine.Pause(T0, out var lobbyReason));
            Assert.AreEqual(QuizReject.InvalidPhase, lobbyReason);

            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));
            machine.Tick(T0, Rng());
            machine.Tick(T0 + QuizTimeLimits.DefaultBuzzTimeLimitSec, Rng());
            Assert.IsTrue(machine.Finish(T0 + 20.0, out _));

            Assert.IsFalse(machine.Pause(T0 + 21.0, out var finishedReason));
            Assert.AreEqual(QuizReject.InvalidPhase, finishedReason);
        }

        [Test]
        public void Pause_WithNonFiniteTime_IsRejected()
        {
            var machine = OpenBuzz(ShortLimits);

            Assert.IsFalse(machine.Pause(double.NaN, out var reason));
            Assert.AreEqual(QuizReject.NonFiniteTime, reason);
            Assert.IsFalse(machine.IsPaused);
        }

        [Test]
        public void Pause_DuringReading_IsRejected()
        {
            // TTS の同期再生は一時停止できないため、Reading 中の一時停止は棄却する（M4）。
            var machine = new QuizStateMachine(ShortLimits);
            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));
            Assert.AreEqual(QuizPhase.Reading, machine.Phase);

            Assert.IsFalse(machine.Pause(T0 + 0.1, out var reason));
            Assert.AreEqual(QuizReject.InvalidPhase, reason);
            Assert.IsFalse(machine.IsPaused);
        }

        [Test]
        public void StartQuestion_WhilePaused_IsRejected()
        {
            // 司会の「次へ」（GameSession.NextQuestion → StartQuestion）は一時停止中は棄却する（H2）。
            var machine = LockAndOpenAnswer(out var answeringStart, limits: ShortLimits);
            machine.SubmitAnswer(Client1, "東京", answeringStart + 0.2, out _);
            machine.Tick(answeringStart + 0.3, Rng());
            Assert.AreEqual(QuizPhase.Result, machine.Phase);

            Assert.IsTrue(machine.Pause(answeringStart + 0.4, out _));

            Assert.IsFalse(machine.StartQuestion(1, new[] { "おおさか" }, answeringStart + 0.5, out var reason));
            Assert.AreEqual(QuizReject.Paused, reason);
            Assert.AreEqual(QuizPhase.Result, machine.Phase, "棄却されたのでフェーズは変わらないはず。");
        }

        [Test]
        public void StartQuestion_AfterResume_UsesFreshPhaseStartTime()
        {
            // 再開後に「次へ」を呼んだとき、新しい問題の PhaseStartServerTime が
            // 一時停止でずらしたアンカーの影響を受けず、実際に渡した serverNow になることを確かめる（H2）。
            var machine = LockAndOpenAnswer(out var answeringStart, limits: ShortLimits);
            machine.SubmitAnswer(Client1, "東京", answeringStart + 0.2, out _);
            machine.Tick(answeringStart + 0.3, Rng());
            Assert.AreEqual(QuizPhase.Result, machine.Phase);

            Assert.IsTrue(machine.Pause(answeringStart + 0.4, out _));
            Assert.IsTrue(machine.Resume(answeringStart + 500.4, out _));

            var next = answeringStart + 500.5;
            Assert.IsTrue(machine.StartQuestion(1, new[] { "おおさか" }, next, out var reason));
            Assert.AreEqual(QuizReject.None, reason);
            Assert.AreEqual(QuizPhase.Reading, machine.Phase);
            Assert.AreEqual(next, machine.PhaseStartServerTime, 1e-9, "一時停止でずらした値が漏れ出ていないはず。");
            Assert.IsFalse(next > answeringStart + 500.4 + 1.0, "念のため未来へ大きくずれていないことも確認する。");
        }

        [Test]
        public void Resume_DuringOpenCollectWindow_ShiftsArbiterDeadline()
        {
            // 集計窓が開いている（1 人目が押した直後）状態で一時停止しても、
            // 再開後は残っていた集計窓ぶんの時間で正しく解決するはず（BuzzArbiter.ShiftDeadline）。
            var machine = OpenBuzz(ShortLimits);

            // 1 人目が押下（dt=0.02、集計窓 0.15 秒が T0 + 0.42 から開始 → 締め切り T0 + 0.57）。
            Assert.IsTrue(machine.AcceptBuzz(Client1, T0 + 0.4, T0 + 0.42, out _));

            // 締め切り前（T0 + 0.5）に一時停止する。
            Assert.IsTrue(machine.Pause(T0 + 0.5, out _));
            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + 1000.0, Rng()), "一時停止中は解決しないはず。");

            Assert.IsTrue(machine.Resume(T0 + 1000.0, out _));
            // 一時停止していた 999.5 秒ぶん締め切りが後ろへずれ、T0 + 0.57 + 999.5 = T0 + 1000.07 になる。

            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + 1000.06, Rng()), "ずらした締め切りの手前ではまだ解決しないはず。");
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);

            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(T0 + 1000.07, Rng()));
            Assert.AreEqual(Client1, machine.LockedClientId);
        }
    }
}
