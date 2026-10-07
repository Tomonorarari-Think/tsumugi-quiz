using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Reveal;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.PlayMode.Tts;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// 問題文の文字送り表示（issue #144）のうち、View の復元・再開放・次の問題・View を閉じたときの
    /// PlayMode テスト（PR #166 レビュー H-1 / M-3 / L-5）。
    /// </summary>
    public class GameViewQuestionRevealRestoreTests : GameViewQuestionRevealFixture
    {
        /// <summary>2 問目（1 問目と見分けられる長さの問題文）。</summary>
        private const string SecondQuestionText = "春日部つむぎの出身地として設定されている県はどこ？";

        /// <summary>
        /// H-1: Game View が購読する前に出題（<see cref="GameSession.QuestionShown"/>）と再生開始時刻
        /// （<c>PlayAtRpc</c>）が届いていても、復元時に全文を出さず、覚えている再生開始時刻から読み上げの位置へ追いつく。
        /// </summary>
        /// <remarks>
        /// 実機の 2 プロセス確認で再現した #95 の競合（ロビー → Game の切り替えと問題配信が同じフレームに重なる）と
        /// 同じ状況を、「出題が始まって再生開始時刻が届いてから Game View を開く」という決定的な順序で作る。
        /// ホストはフェイクの読み上げ（8 秒）、クライアントは読み上げなし（B 案）。
        /// </remarks>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator OpenedAfterQuestionShown_RestoresRevealAndCatchesUpWithReading()
        {
            const double readingSec = 8.0;
            var context = new RevealContext();
            yield return SetUpClientGameView(
                context, FreeTextSource(), QuestionRevealSchedule.MaxMsPerChar, ttsEnabled: true, showGameView: false);

            HostSession.GetComponent<TtsSyncCoordinator>().SetPlayback(new FixedDurationReadingPlayback(readingSec));
            var clientCoordinator = context.ClientSession.GetComponent<TtsSyncCoordinator>();
            clientCoordinator.SetPlayback(null);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => clientCoordinator.LastReadingQuestionIndex == 0,
                DefaultTimeoutSeconds,
                "クライアントに再生開始時刻（PlayAtRpc）が届きませんでした。");

            // ここで初めて Game View を開く（QuestionShown / ReadingScheduled はどちらも取りこぼした状態）。
            yield return ShowGameView(context);

            // 復元は表示直後（TryAcquireSession）か、その後の Tick（TryRestoreMissedQuestion）で行われる。
            yield return WaitUntil(
                () => context.GameView.CurrentReveal.TotalCount > 0,
                DefaultTimeoutSeconds,
                "取りこぼした問題が復元されませんでした。");

            var reveal = context.GameView.CurrentReveal;
            Assert.AreEqual(QuestionRevealMode.Synced, reveal.Mode, $"取りこぼした再生開始時刻に追いつくはず（{reveal}）。");
            Assert.AreEqual(readingSec / LongQuestionLength, reveal.SecondsPerChar, 1e-9, "ホストの読み上げ時間を按分する。");
            Assert.Less(
                context.QuestionTextLabel.text.Length, LongQuestionText.Length,
                "読み上げの途中で開いたので、全文はまだ出ていないはず（受付前に全文が読めてしまわない）。");
            StringAssert.StartsWith(context.QuestionTextLabel.text, LongQuestionText);
        }

        /// <summary>
        /// M-2: 再同期（Resync）で届いた問題でも、この PC が既に再生開始時刻を受け取っていれば、
        /// 全文ではなくその時間軸で途中から送る。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ResyncWithKnownReading_ContinuesOnReadingTimeline()
        {
            const double readingSec = 8.0;
            var context = new RevealContext();
            yield return SetUpClientGameView(
                context, FreeTextSource(), QuestionRevealSchedule.MaxMsPerChar, ttsEnabled: true);

            HostSession.GetComponent<TtsSyncCoordinator>().SetPlayback(new FixedDurationReadingPlayback(readingSec));
            var clientCoordinator = context.ClientSession.GetComponent<TtsSyncCoordinator>();
            clientCoordinator.SetPlayback(null);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => clientCoordinator.LastReadingQuestionIndex == 0
                      && context.GameView.CurrentReveal.Mode == QuestionRevealMode.Synced,
                DefaultTimeoutSeconds,
                "前提: 通常の出題で読み上げに同期しませんでした。");

            var resyncShown = false;
            System.Action<int, QuestionDto, QuestionShownSource> onShown =
                (_, _, source) => resyncShown |= source == QuestionShownSource.Resync;
            context.ClientSession.QuestionShown += onShown;
            try
            {
                Assert.IsTrue(
                    HostSession.ResyncClient(context.ClientSession.NetworkManager.LocalClientId), "再同期を送れるはず。");
                yield return WaitUntil(() => resyncShown, DefaultTimeoutSeconds, "再同期の提示が届きませんでした。");
            }
            finally
            {
                context.ClientSession.QuestionShown -= onShown;
            }

            var reveal = context.GameView.CurrentReveal;
            Assert.AreEqual(QuestionRevealMode.Synced, reveal.Mode, $"既知の再生開始時刻があれば全文にしない（{reveal}）。");
            Assert.AreEqual(readingSec / LongQuestionLength, reveal.SecondsPerChar, 1e-9);
            Assert.Less(context.QuestionTextLabel.text.Length, LongQuestionText.Length, "読み上げの途中なので全文ではない。");
        }

        /// <summary>
        /// 再レビュー 2 回目: 読み上げの時間軸を知らない PC（再接続で coordinator が新しい、提示と再生開始時刻を
        /// 取りこぼした等）でも、再同期の応答（<c>SessionStateRpc</c>）に載った時間軸で読み上げに同期する。
        /// </summary>
        /// <remarks>
        /// 「時間軸を知らない」状態は、通常配信の提示の合図を止めるテスト用 seam
        /// （<c>SuppressDistributedQuestionShownForTests</c>）で作る。提示を受けていないクライアントは
        /// <c>PlayAtRpc</c> を「現在の問題と一致しない」として捨てるので、再接続直後と同じく記録が無い。
        /// </remarks>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ResyncWithoutKnownReading_UsesReadingTimelineFromResyncResponse()
        {
            const double readingSec = 8.0;
            var context = new RevealContext();
            yield return SetUpClientGameView(
                context, FreeTextSource(), QuestionRevealSchedule.MaxMsPerChar, ttsEnabled: true);

            var hostCoordinator = HostSession.GetComponent<TtsSyncCoordinator>();
            hostCoordinator.SetPlayback(new FixedDurationReadingPlayback(readingSec));
            var clientCoordinator = context.ClientSession.GetComponent<TtsSyncCoordinator>();
            clientCoordinator.SetPlayback(null);

            context.ClientSession.SuppressDistributedQuestionShownForTests = true;
            try
            {
                Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
                yield return WaitUntil(
                    () => hostCoordinator.LastReadingQuestionIndex == 0
                          && context.ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                    DefaultTimeoutSeconds,
                    "ホストの読み上げ・受付開始に進みませんでした。");
                yield return WaitRealtime(0.3);
                Assert.AreNotEqual(
                    0, clientCoordinator.LastReadingQuestionIndex, "前提: クライアントは再生開始時刻を知らない。");

                var resyncShown = false;
                System.Action<int, QuestionDto, QuestionShownSource> onShown =
                    (_, _, source) => resyncShown |= source == QuestionShownSource.Resync;
                context.ClientSession.QuestionShown += onShown;
                try
                {
                    Assert.IsTrue(
                        HostSession.ResyncClient(context.ClientSession.NetworkManager.LocalClientId), "再同期を送れるはず。");
                    yield return WaitUntil(() => resyncShown, DefaultTimeoutSeconds, "再同期の提示が届きませんでした。");
                }
                finally
                {
                    context.ClientSession.QuestionShown -= onShown;
                }
            }
            finally
            {
                context.ClientSession.SuppressDistributedQuestionShownForTests = false;
            }

            Assert.AreEqual(0, clientCoordinator.LastReadingQuestionIndex, "再同期の応答で時間軸を受け取るはず。");
            Assert.AreEqual(
                hostCoordinator.LastPlayAtServerTime, clientCoordinator.LastPlayAtServerTime, 1e-9,
                "ホストと同じ再生開始時刻。");
            var reveal = context.GameView.CurrentReveal;
            Assert.AreEqual(QuestionRevealMode.Synced, reveal.Mode, $"読み上げの時間軸に同期するはず（{reveal}）。");
            Assert.AreEqual(
                readingSec / LongQuestionLength, reveal.SecondsPerChar, 1e-9, "1 文字あたりの秒数は読み上げ由来。");
            Assert.Less(context.QuestionTextLabel.text.Length, LongQuestionText.Length, "読み上げの途中なので全文ではない。");
        }

        /// <summary>
        /// NH-1: 同じ問題番号がもう一度出題されたとき（2 回目のゲームの同じ番号と同じ状況）、通常の配信では
        /// 前回の再生開始時刻を「既知」として使わず、新しい再生開始時刻を待つ（受付前に全文を出さない）。
        /// </summary>
        /// <remarks>
        /// 1 回目の出題と決着のあと、結果表示（Result）から同じ問題 0 を出し直す。coordinator の記録
        /// （<c>LastReadingQuestionIndex</c> = 0）は出題の合図で捨てられ、UI も通常の配信では記録を使わない
        /// （二重の防御。記録の破棄と 2 回目のゲームの読み上げは Network 側の <c>TtsSyncSecondGameTests</c> が確かめる）。
        /// 2 回目はホストの合成が終わらない（<see cref="NeverReadyReadingPlayback"/>）ので、再生開始時刻は
        /// Ready 待ちの上限（既定 3 秒）まで届かない。その間は読み上げ待ちのまま 1 文字も出ないことを確かめる。
        /// </remarks>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SameQuestionIndexAgain_WaitsForNewReadingInsteadOfStaleOne()
        {
            var context = new RevealContext();
            yield return SetUpClientGameView(
                context, FreeTextSource(), QuestionRevealSchedule.MaxMsPerChar, ttsEnabled: true);

            var hostCoordinator = HostSession.GetComponent<TtsSyncCoordinator>();
            hostCoordinator.SetPlayback(new FixedDurationReadingPlayback(1.0));
            var clientCoordinator = context.ClientSession.GetComponent<TtsSyncCoordinator>();
            clientCoordinator.SetPlayback(null);

            var shownCount = 0;
            System.Action<int, QuestionDto, QuestionShownSource> onShown = (_, _, _) => shownCount++;
            context.ClientSession.QuestionShown += onShown;
            try
            {
                // --- 1 回目: 読み上げに同期 → 正解 → 結果表示 ---
                Assert.IsTrue(HostSession.StartQuestion(0), "1 回目の出題を開始できるはず。");
                yield return WaitUntil(
                    () => clientCoordinator.LastReadingQuestionIndex == 0
                          && context.ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                    DefaultTimeoutSeconds,
                    "1 回目の読み上げ・受付開始に進みませんでした。");
                Assert.IsTrue(context.ClientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
                yield return WaitUntil(
                    () => context.ClientSession.Phase.Value == QuizPhase.Answering,
                    DefaultTimeoutSeconds,
                    "回答フェーズに進みませんでした。");
                Assert.IsTrue(context.ClientSession.RequestAnswer(CorrectAnswer), "勝者は回答を送れるはず。");
                yield return WaitUntil(
                    () => HostSession.Phase.Value == QuizPhase.Result && context.QuestionTextLabel.text == LongQuestionText,
                    DefaultTimeoutSeconds,
                    "1 回目の判定後に全文が表示されませんでした。");

                // --- 2 回目: 同じ問題 0。ホストの合成が終わらないので再生開始時刻はしばらく届かない ---
                hostCoordinator.SetPlayback(new NeverReadyReadingPlayback());
                Assert.AreEqual(0, clientCoordinator.LastReadingQuestionIndex, "前提: 前回の記録が残っている。");
                var shownBefore = shownCount;
                Assert.IsTrue(HostSession.StartQuestion(0), "同じ問題をもう一度出題できるはず。");
                yield return WaitUntil(
                    () => shownCount > shownBefore, DefaultTimeoutSeconds, "2 回目の出題が届きませんでした。");

                Assert.AreEqual(
                    QuestionRevealMode.AwaitingReading, context.GameView.CurrentReveal.Mode,
                    $"前回の再生開始時刻を使わず、新しい再生開始時刻を待つはず（{context.GameView.CurrentReveal}）。");
                Assert.AreEqual(string.Empty, context.QuestionTextLabel.text, "再生開始時刻が届くまで 1 文字も出さない。");

                yield return WaitRealtime(1.0);
                Assert.AreEqual(QuestionRevealMode.AwaitingReading, context.GameView.CurrentReveal.Mode);
                Assert.AreEqual(string.Empty, context.QuestionTextLabel.text, "待っている間も全文にならない。");
            }
            finally
            {
                context.ClientSession.QuestionShown -= onShown;
            }
        }

        /// <summary>
        /// L-5 / M-3: 誤答後に受付を再開放すると文字送りが再開する。読み上げの無い部屋（固定速度）では、
        /// 止めていた時間ぶん後ろへずらすので、再開の直後に全文にはならず、止めた位置から続く。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator WrongAnswerReopen_ResumesFromFrozenPosition()
        {
            var context = new RevealContext();
            yield return SetUpClientGameView(context, FreeTextSource(), QuestionRevealSchedule.MaxMsPerChar);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => context.ClientSession.Phase.Value == QuizPhase.BuzzOpen && context.QuestionTextLabel.text.Length >= 2,
                DefaultTimeoutSeconds,
                "受付開始・文字送りの開始を確認できませんでした。");

            Assert.IsTrue(context.ClientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => context.ClientSession.Phase.Value == QuizPhase.Answering && context.GameView.CurrentReveal.IsFrozen,
                DefaultTimeoutSeconds,
                "回答フェーズ・文字送りの停止に進みませんでした。");
            var frozenLength = context.QuestionTextLabel.text.Length;

            // 止めている時間を十分に取る（ずらさなければ、この間に 4 文字以上進んでしまう）。
            yield return WaitRealtime(2.0);
            Assert.IsTrue(context.ClientSession.RequestAnswer("まちがい"), "勝者は回答を送れるはず。");

            yield return WaitUntil(
                () => context.ClientSession.Phase.Value == QuizPhase.BuzzOpen && !context.GameView.CurrentReveal.IsFrozen,
                DefaultTimeoutSeconds,
                () => $"誤答後に受付が再開放されず、文字送りも再開しませんでした（{context.ClientSession.Phase.Value} / {context.GameView.CurrentReveal}）。");

            var resumedLength = context.QuestionTextLabel.text.Length;
            Assert.AreEqual(QuestionRevealMode.FixedSpeed, context.GameView.CurrentReveal.Mode);
            Assert.LessOrEqual(resumedLength, frozenLength + 1, "再開の直後は止めた位置から（止めていた間の分は飛ばさない）。");
            Assert.Less(resumedLength, LongQuestionText.Length, "再開の直後に全文にならない（M-3）。");

            yield return WaitUntil(
                () => context.QuestionTextLabel.text.Length > resumedLength,
                DefaultTimeoutSeconds,
                "再開後に文字送りが進みませんでした。");
        }

        /// <summary>
        /// L-5: 文字送りの途中で次の問題へ進むと新しい問題文で始め直し、Game View を閉じると描画の更新が止まる。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator NextQuestionRestartsReveal_AndOnHideStopsTick()
        {
            var context = new RevealContext();
            var source = new TestQuestionSource(
                TestQuestionSource.FreeText("q-reveal-1", LongQuestionText, CorrectAnswer),
                TestQuestionSource.FreeText("q-reveal-2", SecondQuestionText, "とうほく"));
            yield return SetUpClientGameView(context, source, QuestionRevealSchedule.MaxMsPerChar);

            // --- 1 問目: 途中で早押し → 正解 → 全文 ---
            Assert.IsTrue(HostSession.StartQuestion(0), "1 問目を出題できるはず。");
            yield return WaitUntil(
                () => context.ClientSession.Phase.Value == QuizPhase.BuzzOpen && context.QuestionTextLabel.text.Length >= 1,
                DefaultTimeoutSeconds,
                "1 問目の文字送りが始まりませんでした。");
            Assert.IsTrue(context.ClientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => context.ClientSession.Phase.Value == QuizPhase.Answering,
                DefaultTimeoutSeconds,
                "回答フェーズに進みませんでした。");
            Assert.IsTrue(context.ClientSession.RequestAnswer(CorrectAnswer), "勝者は回答を送れるはず。");
            yield return WaitUntil(
                () => HostSession.Phase.Value == QuizPhase.Result && context.QuestionTextLabel.text == LongQuestionText,
                DefaultTimeoutSeconds,
                "1 問目の判定後に全文が表示されませんでした。");

            // --- 2 問目: 新しい問題文の先頭から始め直す ---
            Assert.IsTrue(HostSession.StartQuestion(1), "2 問目を出題できるはず。");
            yield return WaitUntil(
                () =>
                {
                    var text = context.QuestionTextLabel.text;
                    return text.Length >= 1 && SecondQuestionText.StartsWith(text);
                },
                DefaultTimeoutSeconds,
                () => $"2 問目の文字送りが始まりませんでした（実際: '{context.QuestionTextLabel.text}'）。");

            Assert.Less(context.QuestionTextLabel.text.Length, SecondQuestionText.Length, "2 問目は途中から（全文ではない）。");
            Assert.AreEqual(QuestionRevealMode.FixedSpeed, context.GameView.CurrentReveal.Mode);
            Assert.IsFalse(context.GameView.CurrentReveal.IsFrozen, "前問の停止状態を持ち越さない。");
            Assert.IsTrue(context.GameView.IsRevealTickScheduled, "文字送り中は描画の更新が動いているはず。");

            // --- 文字送りの途中で Game View を閉じる ---
            var gameView = context.GameView;
            var router = Object.FindAnyObjectByType<ViewRouter>();
            router.ShowView(ViewNames.Title);

            Assert.IsFalse(gameView.IsRevealTickScheduled, "OnHide で文字送りの描画更新が止まるはず。");

            // 止めたあとも例外・エラーログが出ないこと（LogAssert が未処理のエラーを検出する）。
            yield return WaitRealtime(0.5);
        }
    }
}
