using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="QuizPhases"/>（フェーズのまとまりの判定）の検証（PR #104 レビュー L-1、#109）。
    /// </summary>
    /// <remarks>
    /// <see cref="QuizPhase"/> のすべての値を <c>Enum.GetValues</c> で列挙して照合するので、
    /// フェーズを追加したまま本テストの期待表を更新しなければ失敗する（追加時の見落としを防ぐ）。
    /// </remarks>
    public class QuizPhasesTests
    {
        /// <summary>問題の進行中として扱うフェーズ（これ以外はすべて false）。</summary>
        private static readonly HashSet<QuizPhase> QuestionInProgressPhases = new HashSet<QuizPhase>
        {
            QuizPhase.Reading,
            QuizPhase.BuzzOpen,
            QuizPhase.Locked,
            QuizPhase.Answering,
            QuizPhase.Judging,
            QuizPhase.ChoiceAnswering,
        };

        /// <summary>ゲームが進行中として扱うフェーズ（Result を含む。Lobby / Finished 以外）。</summary>
        private static readonly HashSet<QuizPhase> GameInProgressPhases = new HashSet<QuizPhase>
        {
            QuizPhase.Reading,
            QuizPhase.BuzzOpen,
            QuizPhase.Locked,
            QuizPhase.Answering,
            QuizPhase.Judging,
            QuizPhase.ChoiceAnswering,
            QuizPhase.Result,
        };

        /// <summary>再同期が必要なフェーズ（Lobby 以外のすべて）。</summary>
        private static readonly HashSet<QuizPhase> NeedsResyncPhases = new HashSet<QuizPhase>
        {
            QuizPhase.Reading,
            QuizPhase.BuzzOpen,
            QuizPhase.Locked,
            QuizPhase.Answering,
            QuizPhase.Judging,
            QuizPhase.ChoiceAnswering,
            QuizPhase.Result,
            QuizPhase.Finished,
        };

        [Test]
        public void IsQuestionInProgress_CoversEveryPhase()
        {
            foreach (QuizPhase phase in Enum.GetValues(typeof(QuizPhase)))
            {
                Assert.AreEqual(
                    QuestionInProgressPhases.Contains(phase),
                    QuizPhases.IsQuestionInProgress(phase),
                    $"{phase} の進行中判定が期待と違います。QuizPhase に値を足したら期待表も更新すること。");
            }
        }

        [Test]
        public void IsGameInProgress_CoversEveryPhase()
        {
            // 期待値は実装と同じ式で書かず、フェーズ名を明示した集合で持つ（PR #114 レビュー L-1）。
            foreach (QuizPhase phase in Enum.GetValues(typeof(QuizPhase)))
            {
                Assert.AreEqual(
                    GameInProgressPhases.Contains(phase),
                    QuizPhases.IsGameInProgress(phase),
                    $"{phase} の進行中判定が期待と違います。QuizPhase に値を足したら期待表も更新すること。");
            }
        }

        [Test]
        public void IsGameInProgress_IncludesResult()
        {
            // 結果表示中も「進行中」（docs/network.md §2.3 の 5。途中参加の可否判定に使う）。
            Assert.IsTrue(QuizPhases.IsGameInProgress(QuizPhase.Result));
        }

        [Test]
        public void NeedsResync_CoversEveryPhase()
        {
            foreach (QuizPhase phase in Enum.GetValues(typeof(QuizPhase)))
            {
                Assert.AreEqual(
                    NeedsResyncPhases.Contains(phase),
                    QuizPhases.NeedsResync(phase),
                    $"{phase} の再同期要否が期待と違います。QuizPhase に値を足したら期待表も更新すること。");
            }
        }

        [Test]
        public void ExpectationTables_CoverEveryDefinedPhase()
        {
            // 期待表の更新漏れ自体を検出する（フェーズを足したら 3 つの表すべてを見直させる）。
            var phases = Enum.GetValues(typeof(QuizPhase));
            foreach (QuizPhase phase in phases)
            {
                Assert.IsTrue(
                    QuestionInProgressPhases.Contains(phase)
                    || phase == QuizPhase.Lobby || phase == QuizPhase.Result || phase == QuizPhase.Finished,
                    $"{phase} が IsQuestionInProgress の期待表にありません。");
            }

            Assert.AreEqual(
                phases.Length - 1, NeedsResyncPhases.Count, "再同期の期待表は Lobby 以外のすべてを挙げるはず。");
            Assert.AreEqual(
                phases.Length - 2, GameInProgressPhases.Count, "進行中の期待表は Lobby / Finished 以外を挙げるはず。");
        }

        [Test]
        public void NeedsResync_IsFalseInLobby()
        {
            // まだ 1 問も出していないので、スポーン時の NetworkVariable 同期だけで足りる。
            Assert.IsFalse(QuizPhases.NeedsResync(QuizPhase.Lobby));
        }

        [Test]
        public void IsDefined_IsTrueForEveryDeclaredPhase()
        {
            // #117: 「Lobby 以上 Finished 以下」の範囲判定にすると ChoiceAnswering（= 8）が漏れる。
            foreach (QuizPhase phase in Enum.GetValues(typeof(QuizPhase)))
            {
                Assert.IsTrue(QuizPhases.IsDefined(phase), $"{phase} は定義済みの値として扱うはず。");
            }
        }

        [Test]
        public void IsDefined_IsTrueForChoiceAnsweringOutsideTheLobbyToFinishedRange()
        {
            // 回帰の要点をフェーズ名で固定する（選択式の出題中に合流したクライアントの再同期が
            // まるごと捨てられていた原因）。
            Assert.Greater((int)QuizPhase.ChoiceAnswering, (int)QuizPhase.Finished, "値の並びの前提。");
            Assert.IsTrue(QuizPhases.IsDefined(QuizPhase.ChoiceAnswering));
        }

        [Test]
        public void IsDefined_IsFalseForUnknownValue()
        {
            Assert.IsFalse(QuizPhases.IsDefined((QuizPhase)999));
            Assert.IsFalse(QuizPhases.IsDefined((QuizPhase)(-1)));
        }

        [Test]
        public void UndefinedPhaseValue_IsTreatedAsInProgress()
        {
            // 除外リスト形の意図の固定: 未知の値（将来追加されるフェーズ）は「進行中」側に入る。
            var unknown = (QuizPhase)999;

            Assert.IsTrue(QuizPhases.IsQuestionInProgress(unknown));
            Assert.IsTrue(QuizPhases.IsGameInProgress(unknown));
            Assert.IsTrue(QuizPhases.NeedsResync(unknown));
        }
    }
}
