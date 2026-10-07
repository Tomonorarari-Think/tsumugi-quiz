using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Room;
using TsumugiQuiz.UI.Views.Settings;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// PR #92 再レビュー H-2 / M-3: Settings View が保存した下書き（<see cref="RoomSettingsDraft"/>）が
    /// ホスト開始時のルーム設定になること、および Settings View の「適用」
    /// （<c>SettingsView.PushToRoomSettingsSync</c>）が <c>RoomSettingsSync</c> を正しく操作することを、
    /// 実スポーンしたホスト + クライアントで確かめる。
    /// </summary>
    /// <remarks>
    /// 下書き（<c>room.lastApplied</c>）と <c>host.role</c>（issue #155 で app-settings.json へ移行済み）の
    /// 隔離は、基底クラス <see cref="GameSessionTestFixture"/> の <c>SetUp</c> が
    /// <c>TsumugiQuiz.Tests.Shared.Room.RoomSettingsDraftScope</c> を通じて行う。
    /// </remarks>
    public sealed class RoomSettingsDraftSpawnTests : GameSessionTestFixture
    {
        [TearDown]
        public void ResetRoomSettingsSyncLocator()
        {
            SettingsView.RoomSettingsSyncLocator = null;
        }

        /// <summary>
        /// H-2: 下書きを保存してからホストを開始すると、<c>host.role</c> 以外は下書きの内容が採用される。
        /// <c>host.role</c> だけは HostSetup View の直前のトグル（issue #155 で app-settings.json へ移行済み。
        /// 本テストでは未設定＝既定値の <see cref="HostRole.Player"/>）が勝つ。
        /// </summary>
        [UnityTest]
        public IEnumerator OnNetworkSpawn_AdoptsSavedDraft_ExceptHostRole()
        {
            var draft = new RoomSettings(
                hostRole: HostRole.Moderator,
                maxPlayers: 11,
                ttsEnabled: false,
                ttsSpeed: 1.5,
                ttsReadyTimeoutMs: 4000);

            Assert.IsTrue(RoomSettingsDraft.Set(draft, out _), "下書きの保存に成功するはず。");

            yield return ConnectHostAndClient();

            var current = HostSession.SettingsSync.Current;
            Assert.AreEqual(11, current.MaxPlayers, "room.maxPlayers が下書きから復元されるはず。");
            Assert.IsFalse(current.TtsEnabled, "tts.enabled が下書きから復元されるはず。");
            Assert.AreEqual(1.5, current.TtsSpeed, 0.0001, "tts.speed が下書きから復元されるはず。");
            Assert.AreEqual(4000, current.TtsReadyTimeoutMs, "tts.readyTimeoutMs が下書きから復元されるはず。");
            Assert.AreEqual(
                HostRole.Player,
                current.HostRole,
                "host.role は HostSetup View の直前のトグル（app-settings.json、既定値 player）が勝つはず。");

            yield return WaitUntil(
                () => ClientSession.SettingsSync.Current.MaxPlayers == 11,
                () => "下書き由来のルーム設定がクライアントへ同期されませんでした。");
        }

        /// <summary>
        /// M-3: ホストで Settings View の「適用」を実行すると、クライアントまで同期され、
        /// <c>LastValidationWarnings</c> が呼び出し側の警告一覧へ渡る。
        /// </summary>
        [UnityTest]
        public IEnumerator PushToRoomSettingsSync_OnHost_SyncsAndReportsValidationWarnings()
        {
            yield return ConnectHostAndClient();

            SettingsView.RoomSettingsSyncLocator = () => HostSession.SettingsSync;

            // buzz.timeLimitSec は RoomSettings のコンストラクタでは検証されず、
            // RoomSettingsValidator が 60 秒へクランプする（= LastValidationWarnings に載る）。
            var timeLimits = new QuizTimeLimits(
                buzzTimeLimitSec: 999.0,
                answerTimeLimitSec: 8.0,
                choiceTimeLimitSec: 12.0,
                collectWindowSec: 0.2);
            var session = new SessionSettings(timeLimits: timeLimits);
            var settings = new RoomSettings(maxPlayers: 9, session: session);

            var view = new SettingsView();
            var warnings = new List<string>();
            var status = view.PushToRoomSettingsSync(settings, warnings);

            StringAssert.Contains("参加者全員に反映されます", status, "ホスト・未ロックなら反映成功のメッセージ。");
            CollectionAssert.IsNotEmpty(warnings, "クランプされた項目が LastValidationWarnings 経由で渡るはず。");
            CollectionAssert.AreEqual(HostSession.SettingsSync.LastValidationWarnings, warnings);

            yield return WaitUntil(
                () => ClientSession.SettingsSync.Current.MaxPlayers == 9,
                () => "「適用」した設定がクライアントへ同期されませんでした。");
        }

        /// <summary>M-3: ロック中（ゲーム開始後）は反映せず、その旨を返す。</summary>
        [UnityTest]
        public IEnumerator PushToRoomSettingsSync_WhenLocked_ReportsLockedMessage()
        {
            yield return ConnectHostAndClient();

            Assert.IsTrue(HostSession.SettingsSync.LockForGameStart(), "ロックできるはず。");
            SettingsView.RoomSettingsSyncLocator = () => HostSession.SettingsSync;

            var view = new SettingsView();
            var warnings = new List<string>();
            var status = view.PushToRoomSettingsSync(new RoomSettings(maxPlayers: 7), warnings);

            StringAssert.Contains("ゲーム進行中は変更できません", status);
            Assert.AreEqual(
                RoomSettings.DefaultMaxPlayers,
                HostSession.SettingsSync.Current.MaxPlayers,
                "ロック中は反映されないはず。");
        }

        /// <summary>M-3: クライアントからは反映せず、ホスト限定である旨を返す（ロック判定より先に権限判定、L-1）。</summary>
        [UnityTest]
        public IEnumerator PushToRoomSettingsSync_OnClient_ReportsHostOnlyMessage()
        {
            yield return ConnectHostAndClient();

            SettingsView.RoomSettingsSyncLocator = () => ClientSession.SettingsSync;

            var view = new SettingsView();
            var warnings = new List<string>();
            var status = view.PushToRoomSettingsSync(new RoomSettings(maxPlayers: 7), warnings);

            StringAssert.Contains("ホストだけです", status);
            Assert.AreEqual(
                RoomSettings.DefaultMaxPlayers,
                HostSession.SettingsSync.Current.MaxPlayers,
                "クライアントからは反映されないはず。");
        }
    }
}
