using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> のテストで共有する定数とセットアップ（#12）。
    /// 1 問の進行が長いので、テストは「出題・読み上げ・早押し」と「回答・判定・通し」に分けている。
    /// </summary>
    public abstract class QuizStateMachineTestBase
    {
        protected const double T0 = 1000.0;
        protected const double Window = QuizTimeLimits.DefaultCollectWindowSec;   // 0.15
        protected const ulong Host = 0;
        protected const ulong Client1 = 1;
        protected const ulong Client2 = 2;

        protected static readonly string[] Answers = { "とうきょう", "東京" };

        protected static IRandom Rng() => new DeterministicRandom(12345);

        /// <summary>Reading を抜けて BuzzOpen（T0 = <see cref="T0"/>）にした状態のマシンを返す。</summary>
        protected static QuizStateMachine OpenBuzz(QuizTimeLimits limits = null, QuizRules rules = null)
        {
            var machine = new QuizStateMachine(limits, rules);
            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(T0, Rng()));
            return machine;
        }

        /// <summary>
        /// <see cref="Client1"/> をロック保持者にして Answering まで進めたマシンを返す。
        /// </summary>
        /// <param name="answeringStart">Answering に入ったサーバー時刻。</param>
        /// <param name="rules">得点・再開放・再入力の規則。null なら既定値。</param>
        /// <param name="limits">制限時間。null なら既定値。</param>
        /// <returns>Answering 中のマシン。</returns>
        protected static QuizStateMachine LockAndOpenAnswer(
            out double answeringStart, QuizRules rules = null, QuizTimeLimits limits = null)
        {
            var machine = OpenBuzz(limits, rules);
            machine.AcceptBuzz(Client1, T0 + 0.4, T0 + 0.42, out _);
            machine.Tick(T0 + 0.42 + Window, Rng());

            answeringStart = T0 + 0.7;
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(answeringStart, Rng()));
            Assert.AreEqual(QuizPhase.Answering, machine.Phase);
            Assert.AreEqual(Client1, machine.LockedClientId);
            return machine;
        }
    }
}
