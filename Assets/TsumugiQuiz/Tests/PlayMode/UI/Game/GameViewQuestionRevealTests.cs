using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Reveal;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.PlayMode.Tts;
using TsumugiQuiz.Tests.Shared.Room;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// 問題文の文字送り表示（issue #144）の PlayMode テスト。
    /// <see cref="GameViewSceneTests"/> と同じく、1 プロセス内に立てた生の <see cref="NetworkManager"/> をホストにし、
    /// Boot → Main を読み込んだ側をクライアントとして Game View を駆動する。
    /// </summary>
    /// <remarks>
    /// 読み上げ（voicevox_core）はテスト環境に依存するため、ルーム設定で <c>tts.enabled = false</c> にして
    /// 固定速度（<c>question.revealMsPerChar</c>）の経路を決定的に確かめる。読み上げとの同期は
    /// 再生開始時刻の通知（<see cref="GameView.HandleReadingScheduled"/>）を直接与えて確かめる
    /// （按分の計算そのものは EditMode の <c>QuestionRevealScheduleTests</c>）。
    /// </remarks>
    public class GameViewQuestionRevealTests : GameViewQuestionRevealFixture
    {
        /// <summary>自由入力は、時間とともに先頭から 1 文字ずつ増えて最後に全文になる（仕様 1）。</summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator FreeText_RevealsProgressively_ThenShowsFullText()
        {
            var context = new RevealContext();
            yield return SetUpClientGameView(context, FreeTextSource(), msPerChar: 120);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            var observedLengths = new List<int>();
            var lastLength = 0;
            yield return WaitUntil(
                () =>
                {
                    var text = context.QuestionTextLabel.text;
                    StringAssert.StartsWith(text, LongQuestionText, "表示は常に問題文の先頭部分であるはず。");
                    Assert.GreaterOrEqual(text.Length, lastLength, "表示文字数は減らないはず。");
                    lastLength = text.Length;
                    if (text.Length > 0 && text.Length < LongQuestionText.Length && !observedLengths.Contains(text.Length))
                    {
                        observedLengths.Add(text.Length);
                    }

                    return text == LongQuestionText;
                },
                DefaultTimeoutSeconds,
                () => $"全文まで表示されませんでした（実際: '{context.QuestionTextLabel.text}'）。");

            Assert.GreaterOrEqual(
                observedLengths.Count, 3,
                $"途中の段階（一部だけ表示）が複数回観測されるはず（観測: {string.Join(",", observedLengths)}）。");
            Assert.AreEqual(QuestionRevealMode.FixedSpeed, context.GameView.CurrentReveal.Mode, "読み上げ OFF なので固定速度。");

            var sizer = context.PanelRoot.Q<Label>("question-text-sizer");
            Assert.IsNotNull(sizer, "高さを先に確保する question-text-sizer があるはず。");
            Assert.AreEqual(LongQuestionText, sizer.text, "sizer は最初から全文を持つ。");
        }

        /// <summary>早押しで文字送りが止まり、判定後に全文になる（仕様 2）。</summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Buzz_StopsReveal_AndJudgementShowsFullText()
        {
            var context = new RevealContext();
            yield return SetUpClientGameView(context, FreeTextSource(), msPerChar: QuestionRevealSchedule.MaxMsPerChar);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => context.ClientSession.Phase.Value == QuizPhase.BuzzOpen && context.QuestionTextLabel.text.Length >= 2,
                DefaultTimeoutSeconds,
                () => $"受付開始・文字送りの開始を確認できませんでした（{context.ClientSession.Phase.Value} / '{context.QuestionTextLabel.text}'）。");

            Assert.IsTrue(context.ClientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => context.ClientSession.Phase.Value == QuizPhase.Answering && context.GameView.CurrentReveal.IsFrozen,
                DefaultTimeoutSeconds,
                () => $"回答フェーズ・文字送りの停止に進みませんでした（{context.ClientSession.Phase.Value} / {context.GameView.CurrentReveal}）。");

            var frozenText = context.QuestionTextLabel.text;
            Assert.Less(frozenText.Length, LongQuestionText.Length, "早押しした時点では全文はまだ出ていないはず。");

            // 1 文字 500ms で送る設定なので、止まっていなければ 1.2 秒で 2 文字以上増える。
            yield return WaitRealtime(1.2);
            Assert.AreEqual(frozenText, context.QuestionTextLabel.text, "早押し後は文字送りが止まるはず。");

            Assert.IsTrue(context.ClientSession.RequestAnswer(CorrectAnswer), "勝者は回答を送れるはず。");
            yield return WaitUntil(
                () => context.QuestionTextLabel.text == LongQuestionText,
                DefaultTimeoutSeconds,
                () => $"判定後に全文が表示されませんでした（実際: '{context.QuestionTextLabel.text}'）。");
            Assert.AreEqual(QuestionRevealMode.Full, context.GameView.CurrentReveal.Mode);
        }

        /// <summary>選択式は文字送りせず一括表示（仕様 3）。</summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Choice_ShowsFullTextAtOnce()
        {
            var context = new RevealContext();
            var source = new TestQuestionSource(
                TestQuestionSource.Choice("q-reveal-choice", LongQuestionText, 0, "ぱん", "ごはん", "めん"));
            yield return SetUpClientGameView(context, source, msPerChar: QuestionRevealSchedule.MaxMsPerChar);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => !string.IsNullOrEmpty(context.QuestionTextLabel.text),
                DefaultTimeoutSeconds,
                "選択式の問題文が表示されませんでした。");

            Assert.AreEqual(LongQuestionText, context.QuestionTextLabel.text, "最初に表示された時点で全文のはず。");
            Assert.AreEqual(QuestionRevealMode.Full, context.GameView.CurrentReveal.Mode);
        }

        /// <summary>
        /// 再同期（Resync）の時点で受付中なら、既知の再生開始時刻が無くても全文にせず、受付開始から固定速度で送る
        /// （仕様 4、PR #166 再レビュー M-2）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Resync_DuringBuzzOpen_ContinuesAtFixedSpeedFromBuzzOpen()
        {
            var context = new RevealContext();
            yield return SetUpClientGameView(context, FreeTextSource(), msPerChar: QuestionRevealSchedule.MaxMsPerChar);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => context.ClientSession.Phase.Value == QuizPhase.BuzzOpen && context.QuestionTextLabel.text.Length >= 1,
                DefaultTimeoutSeconds,
                "受付開始・文字送りの開始を確認できませんでした。");

            yield return ResyncAndWait(context);

            var reveal = context.GameView.CurrentReveal;
            Assert.AreEqual(QuestionRevealMode.FixedSpeed, reveal.Mode, $"受付中の再同期は固定速度で続ける（{reveal}）。");
            Assert.Less(context.QuestionTextLabel.text.Length, LongQuestionText.Length, "全文にはしない。");
        }

        /// <summary>
        /// 再同期（Resync）の時点で誰かが回答中で、既知の再生開始時刻も無ければ全文表示（仕様 4）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Resync_WhileAnswering_ShowsFullText()
        {
            var context = new RevealContext();
            yield return SetUpClientGameView(context, FreeTextSource(), msPerChar: QuestionRevealSchedule.MaxMsPerChar);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => context.ClientSession.Phase.Value == QuizPhase.BuzzOpen && context.QuestionTextLabel.text.Length >= 1,
                DefaultTimeoutSeconds,
                "受付開始・文字送りの開始を確認できませんでした。");
            Assert.IsTrue(context.ClientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => context.ClientSession.Phase.Value == QuizPhase.Answering,
                DefaultTimeoutSeconds,
                "回答フェーズに進みませんでした。");

            yield return ResyncAndWait(context);

            Assert.AreEqual(LongQuestionText, context.QuestionTextLabel.text, "回答中の再同期は全文。");
            Assert.AreEqual(QuestionRevealMode.Full, context.GameView.CurrentReveal.Mode);
        }

        /// <summary>ホストから再同期を送り、クライアントが再同期の提示を処理し終えるまで待つ。</summary>
        private IEnumerator ResyncAndWait(RevealContext context)
        {
            var resyncShown = false;
            Action<int, QuestionDto, QuestionShownSource> onShown =
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

        /// <summary>
        /// 読み上げの再生開始時刻が届くと、読み上げ時間を文字数で按分して同期する（仕様 1）。
        /// 固定速度（1 文字 500ms = 全文まで約 13.5 秒）より速い 1 秒の読み上げに追従して全文になる。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ReadingScheduled_SyncsRevealToReadingDuration()
        {
            var context = new RevealContext();
            yield return SetUpClientGameView(context, FreeTextSource(), msPerChar: QuestionRevealSchedule.MaxMsPerChar);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => context.QuestionTextLabel.text.Length >= 1,
                DefaultTimeoutSeconds,
                "文字送りが始まりませんでした。");

            var startedAt = Time.realtimeSinceStartupAsDouble;
            var networkNow = context.ClientSession.NetworkManager.LocalTime.Time;
            context.GameView.HandleReadingScheduled(0, playAtServerTime: networkNow, durationSec: 1.0);
            Assert.AreEqual(QuestionRevealMode.Synced, context.GameView.CurrentReveal.Mode);

            // 別の問題インデックス宛ての通知は無視する（古い配信・取り違えの防止）。
            context.GameView.HandleReadingScheduled(5, networkNow, 100.0);
            Assert.AreEqual(1.0 / LongQuestionLength, context.GameView.CurrentReveal.SecondsPerChar, 1e-9);

            yield return WaitUntil(
                () => context.QuestionTextLabel.text == LongQuestionText,
                DefaultTimeoutSeconds,
                () => $"読み上げ時間に合わせて全文まで進みませんでした（実際: '{context.QuestionTextLabel.text}'）。");

            var elapsed = Time.realtimeSinceStartupAsDouble - startedAt;
            Assert.Less(elapsed, 5.0, $"固定速度（約 13.5 秒）ではなく読み上げ（1 秒）に合わせて進むはず（実測 {elapsed:F2} 秒）。");
        }

        /// <summary>
        /// 統括判断 B 案（issue #144）: この PC が読み上げない（同意撤回・読み上げ未準備）場合でも、
        /// ホストが読み上げていれば再生開始時刻（<c>PlayAtRpc</c>）を待ち、ホストの読み上げの時間軸
        /// （読み上げ時間の按分）に合わせて表示する。出題と同時に固定速度で送り始めない。
        /// </summary>
        /// <remarks>
        /// ホストの読み上げは長さ 1 秒を返すフェイク（<see cref="FixedDurationReadingPlayback"/>）で、
        /// クライアントは <c>SetPlayback(null)</c>（読み上げなし＝長さ 0 で即 Ready を返す）にして、
        /// 実際の Ready 待ち → <c>PlayAtRpc</c> → <c>ReadingScheduled</c> の経路を通す。
        /// </remarks>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator NonReadingClient_FollowsHostReadingTimeline()
        {
            var context = new RevealContext();
            yield return SetUpClientGameView(
                context, FreeTextSource(), msPerChar: QuestionRevealSchedule.MaxMsPerChar, ttsEnabled: true);

            var hostPlayback = new FixedDurationReadingPlayback(HostReadingDurationSec);
            HostSession.GetComponent<TtsSyncCoordinator>().SetPlayback(hostPlayback);
            var clientCoordinator = context.ClientSession.GetComponent<TtsSyncCoordinator>();
            clientCoordinator.SetPlayback(null);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            // 再生開始時刻が届くまでは 1 文字も出さない（受付開始より前に読めてしまわない）。
            var shownBeforeReading = false;
            var sawAwaiting = false;
            yield return WaitUntil(
                () =>
                {
                    var reveal = context.GameView.CurrentReveal;
                    if (clientCoordinator.LastReadingQuestionIndex != 0)
                    {
                        sawAwaiting |= reveal.Mode == QuestionRevealMode.AwaitingReading;
                        shownBeforeReading |= context.QuestionTextLabel.text.Length > 0;
                        return false;
                    }

                    return true;
                },
                DefaultTimeoutSeconds,
                "クライアントに再生開始時刻（PlayAtRpc）が届きませんでした。");

            Assert.IsTrue(sawAwaiting, "読み上げのある部屋では、出題後まず再生開始時刻を待つはず。");
            Assert.IsFalse(shownBeforeReading, "再生開始時刻が届く前に問題文を出してはいけない。");
            Assert.AreEqual(1, hostPlayback.PrepareCallCount, "前提: ホストは読み上げを用意した。");
            Assert.AreEqual(HostReadingDurationSec, clientCoordinator.LastDurationSec, 1e-9, "前提: ホストの読み上げ時間が配られた。");
            Assert.AreEqual(QuestionRevealMode.Synced, context.GameView.CurrentReveal.Mode, "読み上げない PC でもホストの時間軸に同期する。");
            Assert.AreEqual(
                HostReadingDurationSec / LongQuestionLength, context.GameView.CurrentReveal.SecondsPerChar, 1e-9,
                "ホストの読み上げ時間を文字数で按分する。");

            var startedAt = Time.realtimeSinceStartupAsDouble;
            yield return WaitUntil(
                () => context.QuestionTextLabel.text == LongQuestionText,
                DefaultTimeoutSeconds,
                () => $"読み上げに合わせて全文まで進みませんでした（実際: '{context.QuestionTextLabel.text}'）。");
            var elapsed = Time.realtimeSinceStartupAsDouble - startedAt;
            Assert.Less(elapsed, 5.0, $"固定速度（約 13.5 秒）ではなくホストの読み上げ（1 秒）に合わせて進むはず（実測 {elapsed:F2} 秒）。");
        }
    }
}
