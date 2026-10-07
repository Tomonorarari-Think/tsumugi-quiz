using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.UI;

namespace TsumugiQuiz.Tests.EditMode.UI.Character
{
    /// <summary>
    /// <see cref="CharacterStateMachine"/> の遷移・タイマーを検証する（issue #24）。
    /// </summary>
    public class CharacterStateMachineTests
    {
        [Test]
        public void InitialState_IsIdle()
        {
            var machine = new CharacterStateMachine();
            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [Test]
        public void NotifyReadingStarted_FromIdle_TransitionsToReading()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingStarted();
            Assert.That(machine.State, Is.EqualTo(CharacterState.Reading));
        }

        [Test]
        public void NotifyReadingCompleted_FromReading_TransitionsToIdle()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingStarted();
            machine.NotifyReadingCompleted();
            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [Test]
        public void NotifyReadingCompleted_WhenNotReading_IsIgnored()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingCompleted();
            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [TestCase(QuizJudgement.Correct, CharacterState.Correct)]
        [TestCase(QuizJudgement.Wrong, CharacterState.Wrong)]
        [TestCase(QuizJudgement.TimedOut, CharacterState.TimedOut)] // #212: 時間切れは不正解と別の表情
        [TestCase(QuizJudgement.NoEligibleBuzzers, CharacterState.NoEligibleBuzzers)] // #212
        public void NotifyQuestionResolved_MapsJudgementToState(QuizJudgement judgement, CharacterState expected)
        {
            var machine = new CharacterStateMachine();
            machine.NotifyQuestionResolved(judgement);
            Assert.That(machine.State, Is.EqualTo(expected));
        }

        [Test]
        public void NotifyQuestionResolved_None_IsIgnored()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyQuestionResolved(QuizJudgement.None);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [Test]
        public void NotifyQuestionResolved_DuringReading_InterruptsImmediately()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingStarted();
            machine.NotifyQuestionResolved(QuizJudgement.Correct);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Correct));
        }

        [Test]
        public void NotifyReadingCompleted_AfterInterruptedByResult_DoesNotOverrideResult()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingStarted();
            machine.NotifyQuestionResolved(QuizJudgement.Wrong);

            // 読み上げの音声自体は最後まで鳴っており、遅れて ReadingCompleted が届くケース。
            machine.NotifyReadingCompleted();

            Assert.That(machine.State, Is.EqualTo(CharacterState.Wrong));
        }

        [Test]
        public void NotifyReadingStarted_WhileShowingResult_DoesNotInterrupt()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyQuestionResolved(QuizJudgement.Correct);
            machine.NotifyReadingStarted();
            Assert.That(machine.State, Is.EqualTo(CharacterState.Correct));
        }

        [Test]
        public void Tick_BeforeHoldElapsed_StaysInResultState()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyQuestionResolved(QuizJudgement.Correct);

            machine.Tick(CharacterStateMachine.ResultHoldSeconds - 0.01);

            Assert.That(machine.State, Is.EqualTo(CharacterState.Correct));
        }

        [Test]
        public void Tick_AfterHoldElapsed_ReturnsToIdle()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyQuestionResolved(QuizJudgement.Wrong);

            machine.Tick(CharacterStateMachine.ResultHoldSeconds + 0.01);

            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [Test]
        public void Tick_AccumulatesAcrossMultipleCalls()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyQuestionResolved(QuizJudgement.Correct);

            for (var i = 0; i < 10; i++)
            {
                machine.Tick(CharacterStateMachine.ResultHoldSeconds / 10);
            }

            // 浮動小数点の累積誤差で合計がわずかに ResultHoldSeconds を下回ることがあるため、
            // 誤差を吸収する微小な tick を追加してから判定する。
            machine.Tick(0.001);

            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [Test]
        public void Tick_WhileIdleOrReading_IsNoOp()
        {
            var machine = new CharacterStateMachine();
            machine.Tick(1000);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));

            machine.NotifyReadingStarted();
            machine.Tick(1000);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Reading));
        }

        [Test]
        public void Tick_NegativeDelta_Throws()
        {
            var machine = new CharacterStateMachine();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => machine.Tick(-0.01));
        }

        [Test]
        public void Reset_ReturnsToIdleAndClearsHold()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyQuestionResolved(QuizJudgement.Correct);

            machine.Reset();

            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));

            // タイマーが残っていないことを、Tick 後も Idle のままであることで確認する。
            machine.Tick(0.001);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [Test]
        public void StateChanged_FiresOnlyOnActualTransitions()
        {
            var machine = new CharacterStateMachine();
            var observed = new List<CharacterState>();
            machine.StateChanged += observed.Add;

            machine.NotifyReadingStarted();
            machine.NotifyReadingStarted(); // 同じ状態への再通知は無視される
            machine.NotifyReadingCompleted();

            CollectionAssert.AreEqual(new[] { CharacterState.Reading, CharacterState.Idle }, observed);
        }

        [Test]
        public void FullCycle_ReadingThenCorrectThenIdle_MatchesIssueSpecification()
        {
            var machine = new CharacterStateMachine();

            machine.NotifyReadingStarted();
            Assert.That(machine.State, Is.EqualTo(CharacterState.Reading));

            machine.NotifyReadingCompleted();
            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));

            machine.NotifyQuestionResolved(QuizJudgement.Correct);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Correct));

            machine.Tick(CharacterStateMachine.ResultHoldSeconds + 0.01);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        // ---- 場面ごとの表情（issue #212） -------------------------------------------

        [TestCase(true, CharacterState.BuzzSelf)]
        [TestCase(false, CharacterState.BuzzOther)]
        public void NotifyBuzzLocked_SelectsSelfOrOther(bool isLocalWinner, CharacterState expected)
        {
            var machine = new CharacterStateMachine();
            machine.NotifyBuzzLocked(isLocalWinner);
            Assert.That(machine.State, Is.EqualTo(expected));
        }

        [Test]
        public void NotifyBuzzLocked_DuringReading_Interrupts_AndIsHeldUntilResult()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingStarted();
            machine.NotifyBuzzLocked(isLocalWinner: false);

            // 回答権の表情は結果が出るまで保持する（時間では戻らない）。
            machine.Tick(60);
            Assert.That(machine.State, Is.EqualTo(CharacterState.BuzzOther));

            // 読み上げの音声は早押しで止まらない（docs/tts.md §6.8）。遅れて届く完了でも回答権の表情を崩さない。
            machine.NotifyReadingCompleted();
            Assert.That(machine.State, Is.EqualTo(CharacterState.BuzzOther));
        }

        [Test]
        public void NotifyBuzzReopened_TwiceInARow_RestartsHoldFromSecond()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyBuzzReopened();
            machine.Tick(CharacterStateMachine.WrongMomentHoldSeconds - 0.1);

            // 保持時間の終わり際に、もう一度受付が開き直された（例: 別の参加者がすぐに誤答した）。
            machine.NotifyBuzzReopened();

            // 1 回目から数えると保持時間を過ぎているが、2 回目から数え直すので誤答の瞬間のまま。
            machine.Tick(0.2);
            Assert.That(machine.State, Is.EqualTo(CharacterState.WrongMoment));

            machine.Tick(CharacterStateMachine.WrongMomentHoldSeconds - 0.2 + 0.01);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [Test]
        public void NotifyBuzzReopened_ShowsWrongMoment_ThenReturnsToIdleAfterHold()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyBuzzLocked(isLocalWinner: true);

            machine.NotifyBuzzReopened();
            Assert.That(machine.State, Is.EqualTo(CharacterState.WrongMoment));

            machine.Tick(CharacterStateMachine.WrongMomentHoldSeconds - 0.01);
            Assert.That(machine.State, Is.EqualTo(CharacterState.WrongMoment));

            machine.Tick(0.02);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [Test]
        public void HoldElapsed_WhileReadingAudioStillPlaying_ReturnsToReading()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingStarted();
            machine.NotifyBuzzLocked(isLocalWinner: false);
            machine.NotifyBuzzReopened();

            machine.Tick(CharacterStateMachine.WrongMomentHoldSeconds + 0.01);

            Assert.That(machine.State, Is.EqualTo(CharacterState.Reading),
                "音声がまだ鳴っていれば、保持時間のあとは読み上げ中に戻る。");
        }

        [Test]
        public void HoldElapsed_AfterReadingAudioFinished_ReturnsToIdle()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingStarted();
            machine.NotifyQuestionResolved(QuizJudgement.Correct);
            machine.NotifyReadingCompleted(); // 結果の表示中に音声が鳴り終わった

            machine.Tick(CharacterStateMachine.ResultHoldSeconds + 0.01);

            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [Test]
        public void ResultHoldElapsed_WhileReadingAudioStillPlaying_ReturnsToReading()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingStarted();
            machine.NotifyQuestionResolved(QuizJudgement.Wrong);

            machine.Tick(CharacterStateMachine.ResultHoldSeconds + 0.01);

            Assert.That(machine.State, Is.EqualTo(CharacterState.Reading));
        }

        [Test]
        public void NotifyReadingStarted_DuringResultHold_TakesEffectAfterHold()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyQuestionResolved(QuizJudgement.TimedOut);

            // 次の問題の読み上げが、前の問題の結果表示中に始まった。
            machine.NotifyReadingStarted();
            Assert.That(machine.State, Is.EqualTo(CharacterState.TimedOut));

            machine.Tick(CharacterStateMachine.ResultHoldSeconds + 0.01);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Reading));
        }

        [Test]
        public void NotifyBuzzLocked_DuringWrongMomentHold_OverridesIt()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyBuzzLocked(isLocalWinner: false);
            machine.NotifyBuzzReopened();

            machine.NotifyBuzzLocked(isLocalWinner: true);
            Assert.That(machine.State, Is.EqualTo(CharacterState.BuzzSelf));

            // 誤答の瞬間の保持時間が残っていても、回答権の表情は時間で戻らない。
            machine.Tick(CharacterStateMachine.WrongMomentHoldSeconds + 0.01);
            Assert.That(machine.State, Is.EqualTo(CharacterState.BuzzSelf));
        }

        [Test]
        public void NotifyQuestionResolved_AfterBuzz_ShowsResult()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyBuzzLocked(isLocalWinner: true);
            machine.NotifyQuestionResolved(QuizJudgement.Correct);
            Assert.That(machine.State, Is.EqualTo(CharacterState.Correct));
        }

        [TestCase(CharacterState.BuzzSelf)]
        [TestCase(CharacterState.BuzzOther)]
        [TestCase(CharacterState.WrongMoment)]
        public void NotifyQuestionShown_ClearsLeftoverBuzzExpressions(CharacterState leftover)
        {
            var machine = new CharacterStateMachine();
            DriveTo(machine, leftover);

            machine.NotifyQuestionShown();

            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle));
        }

        [Test]
        public void NotifyQuestionShown_WhileReadingAudioPlays_ClearsToReading()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingStarted();
            machine.NotifyBuzzLocked(isLocalWinner: true);

            machine.NotifyQuestionShown();

            Assert.That(machine.State, Is.EqualTo(CharacterState.Reading));
        }

        [Test]
        public void NotifyQuestionShown_DuringResultHold_KeepsResult()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyQuestionResolved(QuizJudgement.Correct);

            machine.NotifyQuestionShown();

            Assert.That(machine.State, Is.EqualTo(CharacterState.Correct), "結果の表示は保持時間まで残す。");
        }

        [Test]
        public void Reset_ClearsReadingAudioFlag()
        {
            var machine = new CharacterStateMachine();
            machine.NotifyReadingStarted();
            machine.Reset();

            machine.NotifyQuestionResolved(QuizJudgement.Correct);
            machine.Tick(CharacterStateMachine.ResultHoldSeconds + 0.01);

            Assert.That(machine.State, Is.EqualTo(CharacterState.Idle), "Reset 後は音声が鳴っていない扱いにする。");
        }

        private static void DriveTo(CharacterStateMachine machine, CharacterState state)
        {
            switch (state)
            {
                case CharacterState.BuzzSelf:
                    machine.NotifyBuzzLocked(isLocalWinner: true);
                    break;
                case CharacterState.BuzzOther:
                    machine.NotifyBuzzLocked(isLocalWinner: false);
                    break;
                case CharacterState.WrongMoment:
                    machine.NotifyBuzzLocked(isLocalWinner: false);
                    machine.NotifyBuzzReopened();
                    break;
                default:
                    Assert.Fail($"テストの前提として未対応の状態です: {state}");
                    break;
            }

            Assert.That(machine.State, Is.EqualTo(state));
        }
    }
}
