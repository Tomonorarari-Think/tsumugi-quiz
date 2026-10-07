using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// ルーム設定の同期・ロック（<see cref="RoomSettingsSync"/>、issue #27）を
    /// ホスト + クライアントの実接続で確かめる（docs/room-settings.md §4、docs/network.md §12）。
    /// </summary>
    public sealed class RoomSettingsSyncTests : GameSessionTestFixture
    {
        private const string LockedWarning =
            "[RoomSettingsSync] ゲーム開始で確定済みのため、ルーム設定は変更できません"
            + "（docs/room-settings.md §4）。ロビーへ戻ってから変更してください。";

        private const string ClientWriteWarning =
            "[RoomSettingsSync] ルーム設定はホスト（サーバー）でのみ変更できます。";

        /// <summary>進行中の <c>Unlock()</c> が出す警告（フェーズ名は伏せて前方一致で見る）。</summary>
        private static readonly Regex UnlockWhileRunningWarning =
            new Regex(@"^\[RoomSettingsSync\] 進行中はルーム設定のロックを解除できません（フェーズ: ");

        /// <summary>
        /// 範囲外の制限時間を <c>Configure</c> に渡したときに、確定時のクランプで
        /// 「サーバーが使う値」と「クライアントが読む値」がずれることを知らせる警告。
        /// </summary>
        private static readonly Regex ClampedOnCommitWarning =
            new Regex(
                @"^\[GameSession\] 進行設定がルーム設定の範囲外だったためクランプされました。"
                + @".*buzz\.timeLimitSec: サーバー 999 / ルーム設定 60");

        private static RoomSettings BuildCustomSettings()
        {
            var timeLimits = new QuizTimeLimits(
                buzzTimeLimitSec: 6.0,
                answerTimeLimitSec: 8.0,
                choiceTimeLimitSec: 12.0,
                collectWindowSec: 0.2);

            var scoring = new ScoringSettings(
                correctPoints: 20,
                incorrectPoints: -3,
                penaltyType: PenaltyKind.MinusPoints,
                penaltyMinusPoints: -7);

            var session = new SessionSettings(
                questions: null,
                timeLimits: timeLimits,
                scoring: scoring,
                resultAutoAdvanceSec: 3.0,
                allowLateJoin: true);

            return new RoomSettings(
                hostRole: HostRole.Player,
                maxPlayers: 11,
                session: session,
                shuffleChoiceDisplay: false,
                allowDuringReading: false,
                ttsEnabled: false,
                ttsSpeed: 1.5,
                ttsReadyTimeoutMs: 4000,
                ttsLeadTimeSec: 0.8);
        }

        /// <summary>
        /// 1 問を「誰も押さずに時間切れ → 自動進行で全問終了」まで短時間で流すためのルーム設定。
        /// </summary>
        private static RoomSettings BuildFastSettings(
            double buzzTimeLimitSec, int correctPoints, int maxPlayers)
        {
            var timeLimits = new QuizTimeLimits(
                buzzTimeLimitSec: buzzTimeLimitSec,
                answerTimeLimitSec: 5.0,
                choiceTimeLimitSec: 5.0,
                collectWindowSec: 0.15);

            var scoring = new ScoringSettings(correctPoints: correctPoints, incorrectPoints: 0);

            var session = new SessionSettings(
                questions: null,
                timeLimits: timeLimits,
                scoring: scoring,
                resultAutoAdvanceSec: 1.0,
                allowLateJoin: false);

            return new RoomSettings(maxPlayers: maxPlayers, session: session);
        }

        [UnityTest]
        public IEnumerator Spawn_BeforeGameStart_BothPeersSeeDefaultsAndAreUnlocked()
        {
            yield return ConnectHostAndClient();

            Assert.IsNotNull(HostSession.SettingsSync, "GameSession プレハブに RoomSettingsSync が載っているはず。");
            Assert.IsNotNull(ClientSession.SettingsSync, "クライアント側にも RoomSettingsSync が届くはず。");

            yield return WaitUntil(
                () => ClientSession.SettingsSync.Current != null,
                () => "クライアントがルーム設定を受け取れませんでした。");

            Assert.IsFalse(HostSession.SettingsSync.IsLocked, "ゲーム開始前はロックされていないはず。");
            Assert.IsFalse(ClientSession.SettingsSync.IsLocked);
            Assert.AreEqual(RoomSettings.DefaultMaxPlayers, ClientSession.SettingsSync.Current.MaxPlayers);
            Assert.IsEmpty(ClientSession.SettingsSync.LastValidationWarnings, "既定値の再検証で警告は出ないはず。");
        }

        [UnityTest]
        public IEnumerator TrySetSettings_OnHostBeforeStart_SyncsToClientAsReadOnly()
        {
            yield return ConnectHostAndClient();

            var custom = BuildCustomSettings();
            Assert.IsTrue(HostSession.SettingsSync.TrySetSettings(custom), "ロック前ならホストは設定を変更できるはず。");

            yield return WaitUntil(
                () => ClientSession.SettingsSync.Current.MaxPlayers == custom.MaxPlayers,
                () => "ルーム設定がクライアントへ同期されませんでした。");

            var synced = ClientSession.SettingsSync.Current;
            Assert.AreEqual(custom.TimeLimits.BuzzTimeLimitSec, synced.TimeLimits.BuzzTimeLimitSec);
            Assert.AreEqual(custom.TimeLimits.AnswerTimeLimitSec, synced.TimeLimits.AnswerTimeLimitSec);
            Assert.AreEqual(custom.TimeLimits.ChoiceTimeLimitSec, synced.TimeLimits.ChoiceTimeLimitSec);
            Assert.AreEqual(custom.Scoring.CorrectPoints, synced.Scoring.CorrectPoints);
            Assert.AreEqual(custom.Scoring.PenaltyType, synced.Scoring.PenaltyType);
            Assert.AreEqual(custom.ShuffleChoiceDisplay, synced.ShuffleChoiceDisplay);
            Assert.AreEqual(custom.AllowDuringReading, synced.AllowDuringReading);
            Assert.AreEqual(custom.ResultAutoAdvanceSec, synced.ResultAutoAdvanceSec);
            Assert.AreEqual(custom.AllowLateJoin, synced.AllowLateJoin);
            Assert.AreEqual(custom.TtsEnabled, synced.TtsEnabled);
            Assert.AreEqual(custom.TtsSpeed, synced.TtsSpeed);
            Assert.AreEqual(custom.TtsReadyTimeoutMs, synced.TtsReadyTimeoutMs);
            Assert.AreEqual(custom.TtsLeadTimeSec, synced.TtsLeadTimeSec);
        }

        [UnityTest]
        public IEnumerator TrySetSettings_OnClient_RejectedWithWarning()
        {
            yield return ConnectHostAndClient();

            LogAssert.Expect(UnityEngine.LogType.Warning, ClientWriteWarning);
            Assert.IsFalse(
                ClientSession.SettingsSync.TrySetSettings(BuildCustomSettings()),
                "クライアントからはルーム設定を書き込めないはず。");
        }

        [UnityTest]
        public IEnumerator StartQuestion_LocksSettings_AndRejectsLaterChanges()
        {
            yield return ConnectHostAndClient();

            Assert.IsTrue(HostSession.SettingsSync.TrySetSettings(BuildCustomSettings()));
            Assert.IsTrue(HostSession.StartQuestion(), "1 問目の出題を開始できるはず。");

            Assert.IsTrue(HostSession.SettingsSync.IsLocked, "ゲーム開始でロックされるはず。");

            yield return WaitUntil(
                () => ClientSession.SettingsSync.IsLocked,
                () => "ロック状態がクライアントへ同期されませんでした。");

            LogAssert.Expect(UnityEngine.LogType.Warning, LockedWarning);
            Assert.IsFalse(
                HostSession.SettingsSync.TrySetSettings(RoomSettings.Default),
                "ゲーム開始後はホストでも変更できないはず。");

            // 拒否された変更が同期されていないこと。
            yield return WaitFrames(5);
            Assert.AreEqual(11, ClientSession.SettingsSync.Current.MaxPlayers);
            Assert.AreEqual(11, HostSession.SettingsSync.Current.MaxPlayers);
        }

        [UnityTest]
        public IEnumerator StartQuestion_PublishesConfiguredTimeLimitsToClient()
        {
            // Configure で渡した制限時間（単問モード）が、開始時に確定値としてクライアントへ届くこと。
            var limits = new QuizTimeLimits(
                buzzTimeLimitSec: 5.0, answerTimeLimitSec: 7.0, choiceTimeLimitSec: 9.0, collectWindowSec: 0.3);
            var scoring = new ScoringSettings(correctPoints: 30, incorrectPoints: -1);

            yield return ConnectHostAndClient(scoring, limits);

            Assert.IsTrue(HostSession.StartQuestion(), "1 問目の出題を開始できるはず。");

            yield return WaitUntil(
                () => ClientSession.SettingsSync.Current.TimeLimits.BuzzTimeLimitSec == limits.BuzzTimeLimitSec,
                () => "Configure で渡した制限時間がクライアントへ同期されませんでした（"
                    + ClientSession.SettingsSync.Current.TimeLimits.BuzzTimeLimitSec + " 秒）。");

            var synced = ClientSession.SettingsSync.Current;
            Assert.AreEqual(limits.AnswerTimeLimitSec, synced.TimeLimits.AnswerTimeLimitSec);
            Assert.AreEqual(limits.ChoiceTimeLimitSec, synced.TimeLimits.ChoiceTimeLimitSec);
            Assert.AreEqual(scoring.CorrectPoints, synced.Scoring.CorrectPoints);
            Assert.AreEqual(scoring.IncorrectPoints, synced.Scoring.IncorrectPoints);

            // クライアントは「フェーズ開始時刻 + 制限時間」で残り時間を出せる（#28 の UI 配線の前提）。
            Assert.AreEqual(
                limits.BuzzTimeLimitSec,
                ClientSession.SettingsSync.Current.TimeLimits.BuzzTimeLimitSec);
        }

        [UnityTest]
        public IEnumerator Unlock_WhileSessionInProgress_RejectedWithWarning()
        {
            // 進行中にロックを解除できると、出題・回答の最中にルールを書き換えられてしまう。
            // ロビーへ戻る経路（ReturnToLobby → フェーズが Lobby）以外では拒否すること。
            yield return ConnectHostAndClient();

            Assert.IsTrue(HostSession.StartQuestion(), "1 問目の出題を開始できるはず。");
            Assert.IsTrue(HostSession.SettingsSync.IsLocked, "ゲーム開始でロックされるはず。");
            Assert.AreNotEqual(
                QuizPhase.Lobby, HostSession.ServerPhase, "出題開始後は進行中フェーズのはず。");

            LogAssert.Expect(UnityEngine.LogType.Warning, UnlockWhileRunningWarning);
            Assert.IsFalse(HostSession.SettingsSync.Unlock(), "進行中はロックを解除できないはず。");

            Assert.IsTrue(HostSession.SettingsSync.IsLocked, "拒否されたのでロックは外れていないはず。");

            LogAssert.Expect(UnityEngine.LogType.Warning, LockedWarning);
            Assert.IsFalse(
                HostSession.SettingsSync.TrySetSettings(BuildCustomSettings()),
                "ロックが外れていないので設定変更も拒否されるはず。");

            yield return WaitUntil(
                () => ClientSession.SettingsSync.IsLocked,
                () => "クライアント側のロック状態が同期されませんでした（拒否後もロックされたままのはず）。");
        }

        [UnityTest]
        public IEnumerator StartQuestion_WithOutOfRangeLimits_WarnsAboutClampMismatch()
        {
            // Configure に範囲外の制限時間を直接渡すと、確定時にルーム設定側だけがクランプされ、
            // サーバーの進行（60 秒ではなく 999 秒）とクライアントの表示がずれる。
            // 挙動は変えず、ずれを警告ログで知らせること（PR #91 レビュー M-1）。
            var limits = new QuizTimeLimits(
                buzzTimeLimitSec: 999.0, answerTimeLimitSec: 15.0, choiceTimeLimitSec: 20.0, collectWindowSec: 0.15);

            yield return ConnectHostAndClient(limits: limits);

            LogAssert.Expect(UnityEngine.LogType.Warning, ClampedOnCommitWarning);
            Assert.IsTrue(HostSession.StartQuestion(), "1 問目の出題を開始できるはず。");

            yield return WaitUntil(
                () => ClientSession.SettingsSync.Current.TimeLimits.BuzzTimeLimitSec
                    == QuizTimeLimits.MaxBuzzOrAnswerTimeLimitSec,
                () => "クランプ後の制限時間がクライアントへ同期されませんでした（"
                    + ClientSession.SettingsSync.Current.TimeLimits.BuzzTimeLimitSec + " 秒）。");

            Assert.AreEqual(
                QuizTimeLimits.MaxBuzzOrAnswerTimeLimitSec,
                HostSession.SettingsSync.Current.TimeLimits.BuzzTimeLimitSec,
                "ホスト側のルーム設定も上限へクランプされるはず。");
        }

        [UnityTest]
        public IEnumerator StartSession_AfterReturnToLobbyAndChange_UsesNewSettingsOnBothPeers()
        {
            // 回帰（PR #91 レビュー H1）: 2 回目の StartSession() が前回の進行設定を優先してしまうと、
            // ロビーで変更した設定が無視され、確定時に同期値まで巻き戻る。
            // 実際の操作経路（結果画面の「ロビーへ戻る」= GameSession.ReturnToLobby、#20）で確かめる。
            yield return ConnectHostAndClient();

            var first = BuildFastSettings(buzzTimeLimitSec: 2.0, correctPoints: 20, maxPlayers: 11);
            Assert.IsTrue(HostSession.SettingsSync.TrySetSettings(first));
            Assert.IsTrue(HostSession.StartSession(), "ロビーのルーム設定でセッションを開始できるはず。");

            Assert.AreEqual(20, HostSession.ActiveSessionSettings.Scoring.CorrectPoints);
            Assert.AreEqual(2.0, HostSession.ActiveSessionSettings.TimeLimits.BuzzTimeLimitSec);

            yield return WaitUntil(
                () => ClientSession.SettingsSync.Current.MaxPlayers == 11
                    && ClientSession.SettingsSync.Current.Scoring.CorrectPoints == 20,
                () => "1 回目の設定がクライアントへ同期されませんでした。");

            // 誰も押さずに時間切れ → 自動進行で全問終了。
            yield return WaitUntil(
                () => HostSession.ServerPhase == QuizPhase.Finished,
                () => $"セッションが終了しませんでした（フェーズ {HostSession.ServerPhase}）。");

            // 結果画面の「ロビーへ戻る」→ 設定を変更 → 再開始。
            Assert.IsTrue(HostSession.ReturnToLobby(), "全問終了後はロビーへ戻せるはず。");
            Assert.IsFalse(
                HostSession.SettingsSync.IsLocked, "ロビーへ戻ったらルーム設定のロックが外れるはず。");
            Assert.IsNull(
                HostSession.ActiveSessionSettings,
                "ロビーへ戻ったら前回の進行設定を捨てるはず（次の開始でロビーの設定を使うため）。");

            yield return WaitUntil(
                () => !ClientSession.SettingsSync.IsLocked,
                () => "ロック解除がクライアントへ同期されませんでした。");

            var second = BuildFastSettings(buzzTimeLimitSec: 4.0, correctPoints: 50, maxPlayers: 8);
            Assert.IsTrue(HostSession.SettingsSync.TrySetSettings(second));
            Assert.IsTrue(HostSession.StartSession(), "2 回目のセッションを開始できるはず。");

            Assert.AreEqual(
                50, HostSession.ActiveSessionSettings.Scoring.CorrectPoints,
                "2 回目はロビーで変更した得点設定を使うはず。");
            Assert.AreEqual(
                4.0, HostSession.ActiveSessionSettings.TimeLimits.BuzzTimeLimitSec,
                "2 回目はロビーで変更した制限時間を使うはず。");

            yield return WaitUntil(
                () => ClientSession.SettingsSync.Current.MaxPlayers == 8
                    && ClientSession.SettingsSync.Current.Scoring.CorrectPoints == 50
                    && ClientSession.SettingsSync.Current.TimeLimits.BuzzTimeLimitSec == 4.0,
                () => "2 回目の設定がクライアントへ同期されませんでした（"
                    + ClientSession.SettingsSync.Current.MaxPlayers + " 人 / "
                    + ClientSession.SettingsSync.Current.Scoring.CorrectPoints + " 点 / "
                    + ClientSession.SettingsSync.Current.TimeLimits.BuzzTimeLimitSec + " 秒）。");

            Assert.IsTrue(HostSession.SettingsSync.IsLocked, "再開始でふたたびロックされるはず。");
        }

        [UnityTest]
        public IEnumerator AdoptPayload_OutOfRangeFromHost_ClampsAndWarnsOnClient()
        {
            // 受信ペイロードの再検証経路（範囲外・未知の列挙値）。
            // ホストのアプリが壊れている / バージョンが違う場合を、NetworkVariable への直接書き込みで再現する。
            yield return ConnectHostAndClient();

            var payload = RoomSettingsPayload.FromRoomSettings(RoomSettings.Default);
            payload.MaxPlayers = 999;
            payload.HostRole = 200;
            payload.TtsSpeed = 9.5;
            HostSession.SettingsSync.Settings.Value = payload;

            yield return WaitUntil(
                () => ClientSession.SettingsSync.LastValidationWarnings.Count > 0,
                () => "クライアントが再検証の警告を出しませんでした。");

            var warnings = ClientSession.SettingsSync.LastValidationWarnings;
            Assert.IsTrue(
                warnings.Count >= 3,
                "未知の host.role・範囲外の room.maxPlayers・tts.speed の 3 件が出るはず: "
                + string.Join(" / ", warnings));

            var synced = ClientSession.SettingsSync.Current;
            Assert.AreEqual(RoomSettings.MaxMaxPlayers, synced.MaxPlayers, "room.maxPlayers は上限へクランプ。");
            Assert.AreEqual(RoomSettings.DefaultHostRole, synced.HostRole, "未知の host.role は既定値へ。");
            Assert.AreEqual(RoomSettings.MaxTtsSpeed, synced.TtsSpeed, "tts.speed は上限へクランプ。");
        }
    }
}
