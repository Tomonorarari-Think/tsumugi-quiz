using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// クライアントの残り時間表示（<see cref="GameSession.CurrentDeadlineServerTime"/>）が、
    /// 既定値ではなくホストのルーム設定の制限時間で計算されることを実接続で確かめる（issue #154）。
    /// </summary>
    public sealed class GameSessionDeadlineSyncTests : GameSessionTestFixture
    {
        /// <summary>既定値（早押し 10 / 自由入力 15 / 選択式 20 秒）と全部違う値。</summary>
        private const double BuzzLimitSec = 37.0;
        private const double AnswerLimitSec = 23.0;
        private const double ChoiceLimitSec = 7.0;

        /// <summary>サーバー時刻の同期ずれ・フレーム待ちを見込んだ、開始直後の残り時間の許容幅（秒）。</summary>
        private const double StartTolerance = 3.0;

        private static QuizTimeLimits CustomLimits => new QuizTimeLimits(
            buzzTimeLimitSec: BuzzLimitSec,
            answerTimeLimitSec: AnswerLimitSec,
            choiceTimeLimitSec: ChoiceLimitSec,
            collectWindowSec: QuizTimeLimits.DefaultCollectWindowSec);

        [UnityTest]
        public IEnumerator LobbyRoomSettings_NonDefaultLimits_ClientDeadlineUsesConfiguredValues()
        {
            // 通常の操作経路: ロビーでルーム設定を変更 → ホストがゲーム開始（StartSession）。
            // ホストの Configure には制限時間を渡さない（= 既定値のまま）ので、ルーム設定だけが効く。
            yield return ConnectHostAndClient();

            var session = new SessionSettings(
                questions: null, timeLimits: CustomLimits, scoring: null, resultAutoAdvanceSec: 1.0, allowLateJoin: false);
            Assert.IsTrue(HostSession.SettingsSync.TrySetSettings(RoomSettings.Default.WithSession(session)));
            Assert.IsTrue(HostSession.StartSession(), "ロビーのルーム設定でセッションを開始できるはず。");

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {ClientSession.Phase.Value}）。");

            var t0 = ClientSession.BuzzOpenServerTime.Value;
            Assert.AreEqual(
                t0 + BuzzLimitSec, ClientSession.CurrentDeadlineServerTime, 1e-9,
                "クライアントの早押し締め切りはルーム設定の buzz.timeLimitSec で計算されるはず（既定 10 秒ではない）。");
            Assert.AreEqual(
                HostSession.CurrentDeadlineServerTime, ClientSession.CurrentDeadlineServerTime, 1e-9,
                "クライアントとホストの締め切りは一致するはず。");

            var remaining = ClientSession.CurrentDeadlineServerTime - ClientManager.ServerTime.Time;
            Assert.That(
                remaining,
                Is.InRange(BuzzLimitSec - StartTolerance, BuzzLimitSec + StartTolerance),
                "クライアントの残り時間は設定値（37 秒）付近から始まるはず。");

            // --- 押して回答フェーズへ ---
            Assert.IsTrue(ClientSession.RequestBuzz(), "受付中なので押下を送れるはず。");
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.Answering,
                () => $"クライアントが Answering を受け取れませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.AreEqual(
                ClientSession.PhaseStartServerTime.Value + AnswerLimitSec, ClientSession.CurrentDeadlineServerTime, 1e-9,
                "回答の締め切りはルーム設定の answer.freeTextTimeLimitSec で計算されるはず（既定 15 秒ではない）。");
            Assert.AreEqual(
                HostSession.CurrentDeadlineServerTime, ClientSession.CurrentDeadlineServerTime, 1e-9,
                "回答フェーズでもクライアントとホストの締め切りは一致するはず。");
        }

        [UnityTest]
        public IEnumerator ConfiguredChoiceLimit_ClientDeadlineUsesConfiguredValue()
        {
            // 単問モード（Configure で制限時間を渡す経路）の選択式。
            var questionSource = new TestQuestionSource(
                TestQuestionSource.Choice("q-choice-deadline", "日本の首都は？", 1, "大阪", "東京", "京都"));
            yield return ConnectHostAndClient(questionSource, limits: CustomLimits);

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.ChoiceAnswering,
                () => $"選択式の回答受付が開きませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.AreEqual(
                ClientSession.BuzzOpenServerTime.Value + ChoiceLimitSec, ClientSession.CurrentDeadlineServerTime, 1e-9,
                "選択式の締め切りは answer.choiceTimeLimitSec で計算されるはず（既定 20 秒ではない）。");
            Assert.AreEqual(
                HostSession.CurrentDeadlineServerTime, ClientSession.CurrentDeadlineServerTime, 1e-9,
                "クライアントとホストの締め切りは一致するはず。");
        }
        [UnityTest]
        public IEnumerator StartSession_WhileLockedWithDifferentExplicitSettings_WarnsAndKeepsCommittedSettings()
        {
            // #154 L1: 全問終了後にロビーへ戻らず（ロック中のまま）、明示した設定で StartSession を呼ぶと、
            // RoomSettingsSync.Current は更新されない（docs/room-settings.md §4）。開始は拒否せず、警告だけ出す。
            yield return ConnectHostAndClient();

            Assert.IsTrue(HostSession.StartSession(FastSession(buzzTimeLimitSec: 1.0)), "1 回目を開始できるはず。");
            yield return WaitUntil(
                () => HostSession.ServerPhase == QuizPhase.Finished,
                () => $"1 回目のセッションが終了しませんでした（フェーズ {HostSession.ServerPhase}）。");
            Assert.IsTrue(HostSession.SettingsSync.IsLocked, "ロビーへ戻っていないのでロック中のはず。");

            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"^\[GameSession\] ルーム設定がロック中のため.*buzz\.timeLimitSec"));
            Assert.IsTrue(
                HostSession.StartSession(FastSession(buzzTimeLimitSec: 5.0)),
                "ロック中でも開始は拒否しない（警告のみ）。");

            Assert.AreEqual(
                1.0, HostSession.SettingsSync.Current.TimeLimits.BuzzTimeLimitSec,
                "ロック中は確定済みのルーム設定が残る（これが警告の理由）。");
        }

        private static SessionSettings FastSession(double buzzTimeLimitSec) => new SessionSettings(
            questions: null,
            timeLimits: new QuizTimeLimits(
                buzzTimeLimitSec: buzzTimeLimitSec,
                answerTimeLimitSec: 5.0,
                choiceTimeLimitSec: 5.0,
                collectWindowSec: QuizTimeLimits.DefaultCollectWindowSec),
            scoring: null,
            resultAutoAdvanceSec: 0.5,
            allowLateJoin: false);
    }
}
