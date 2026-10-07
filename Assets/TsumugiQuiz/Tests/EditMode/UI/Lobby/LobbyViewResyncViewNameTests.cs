using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Lobby;

namespace TsumugiQuiz.Tests.EditMode.UI.Lobby
{
    /// <summary>
    /// 合流時点のフェーズから移る先の View を決める規則（<c>LobbyView.ResolveResyncedViewName</c>、#117）のテスト。
    /// </summary>
    /// <remarks>
    /// 結果表示中（<see cref="QuizPhase.Result"/>）に合流したら Game View（結果表示）、
    /// 全問終了後（<see cref="QuizPhase.Finished"/>）に合流したら Result View。
    /// 後者を Game View にすると、<c>GameView.HandlePhaseChanged</c> は
    /// 「<see cref="QuizPhase.Finished"/> へ<b>変わった</b>とき」しか Result View へ送らないため
    /// Game View で止まる（docs/network.md §12.7）。
    /// </remarks>
    public class LobbyViewResyncViewNameTests
    {
        /// <summary>Result View へ移るフェーズ（これ以外はすべて Game View）。</summary>
        private static readonly HashSet<QuizPhase> ResultViewPhases = new HashSet<QuizPhase>
        {
            QuizPhase.Finished,
        };

        [Test]
        public void ResolveResyncedViewName_FinishedGoesToResultView()
        {
            Assert.AreEqual(ViewNames.Result, LobbyView.ResolveResyncedViewName(QuizPhase.Finished));
        }

        [Test]
        public void ResolveResyncedViewName_ResultGoesToGameView()
        {
            Assert.AreEqual(ViewNames.Game, LobbyView.ResolveResyncedViewName(QuizPhase.Result));
        }

        [Test]
        public void QuizPhase_HasExpectedNumberOfValues()
        {
            // PR #123 レビュー L-2: フェーズが増えたら、遷移先の期待表（ResultViewPhases）を
            // 見直したかをこのアサートで必ず一度立ち止まって確認する。
            Assert.AreEqual(
                9,
                Enum.GetValues(typeof(QuizPhase)).Length,
                "QuizPhase の値が増減しています。合流時の遷移先（ResultViewPhases）を見直してから件数を更新すること。");
        }

        [Test]
        public void ResolveResyncedViewName_CoversEveryPhase()
        {
            // QuizPhase に値を足したら、この期待表も見直すこと（QuizPhasesTests と同じ作法）。
            foreach (QuizPhase phase in Enum.GetValues(typeof(QuizPhase)))
            {
                var expected = ResultViewPhases.Contains(phase) ? ViewNames.Result : ViewNames.Game;
                Assert.AreEqual(
                    expected,
                    LobbyView.ResolveResyncedViewName(phase),
                    $"{phase} で合流したときの遷移先が期待と違います。");
            }
        }
    }
}
