using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.EditMode.Room;
using TsumugiQuiz.UI.Views.Settings;

namespace TsumugiQuiz.Tests.EditMode.UI.Settings
{
    /// <summary>
    /// PR #92 再レビュー H-1: アプリ設定タブの「保存」が、この画面で編集しないキー
    /// （<c>room.lastApplied</c> = <see cref="RoomSettingsDraft"/> が書く編集中のルーム設定）を
    /// 消してしまわないことを確かめる（「UI が持たないキーは保存で失わない」不変条件）。
    /// </summary>
    public class AppSettingsPreservationTests
    {
        private const string FilePath = @"C:\fake\TsumugiQuiz\app-settings.json";

        [TearDown]
        public void RestoreDraftStore()
        {
            RoomSettingsDraft.AppSettingsStoreFactory = null;
            RoomSettingsDraft.ResetCacheForTesting();
        }

        [Test]
        public void SaveAppSettings_AfterDraftSaved_KeepsRoomLastApplied()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new AppSettingsStore(FilePath, fileSystem);

            RoomSettingsDraft.AppSettingsStoreFactory = () => store;
            RoomSettingsDraft.ResetCacheForTesting();

            // ルーム設定タブで「適用」した状態を作る（room.lastApplied が入る）。
            var draft = new RoomSettings(maxPlayers: 11);
            Assert.IsTrue(RoomSettingsDraft.Set(draft, out _), "下書きの保存に成功するはず。");
            Assert.IsNotEmpty(store.Load().Settings.RoomLastApplied, "room.lastApplied が書かれているはず。");

            // アプリ設定タブの「保存」を模す。CollectAppInput は room.lastApplied を持たないため、
            // 何もしないと空文字で上書きされてしまう（H-1）。
            var edited = AppSettingsValidator.Validate(new AppSettingsInput { PlayerName = "つむぎ" }).Settings;
            Assert.IsEmpty(edited.RoomLastApplied, "アプリ設定タブが組み立てる値には room.lastApplied が無い。");

            var preserved = SettingsView.PreserveKeysNotEditedHere(store, edited);
            Assert.IsTrue(store.Save(preserved).Success, "アプリ設定の保存に成功するはず。");

            // 保存後もファイル・下書きの両方が残っている。
            Assert.AreEqual("つむぎ", store.Load().Settings.PlayerName, "アプリ設定の編集内容は反映される。");
            Assert.IsNotEmpty(store.Load().Settings.RoomLastApplied, "room.lastApplied が保存で消えてはいけない。");

            RoomSettingsDraft.ResetCacheForTesting();
            Assert.AreEqual(11, RoomSettingsDraft.Current.MaxPlayers, "下書きの内容が読み直せるはず。");
        }

        [Test]
        public void SaveAppSettings_AfterHostRoleSaved_KeepsHostRole()
        {
            // issue #155: host.role は HostSetup View（HostRolePreference）が書く。
            // アプリ設定タブの CollectAppInput は host.role を持たないため、
            // PreserveKeysNotEditedHere が引き継がないと保存のたびに既定値（player）へ戻ってしまう。
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new AppSettingsStore(FilePath, fileSystem);
            Assert.IsTrue(store.Save(AppSettings.Default.WithHostRole(HostRole.Moderator)).Success);

            var edited = AppSettingsValidator.Validate(new AppSettingsInput { PlayerName = "つむぎ" }).Settings;
            Assert.AreEqual(HostRole.Player, edited.HostRole, "アプリ設定タブが組み立てる値には host.role が無いので既定値になる。");

            var preserved = SettingsView.PreserveKeysNotEditedHere(store, edited);
            Assert.IsTrue(store.Save(preserved).Success, "アプリ設定の保存に成功するはず。");

            Assert.AreEqual("つむぎ", store.Load().Settings.PlayerName, "アプリ設定の編集内容は反映される。");
            Assert.AreEqual(HostRole.Moderator, store.Load().Settings.HostRole, "host.role が保存で既定値に戻ってはいけない。");
        }

        [Test]
        public void PreserveKeysNotEditedHere_WhenNothingSaved_KeepsEditedValue()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new AppSettingsStore(FilePath, fileSystem);

            var edited = AppSettings.Create(playerName: "つむぎ");
            var preserved = SettingsView.PreserveKeysNotEditedHere(store, edited);

            Assert.AreSame(edited, preserved, "引き継ぐものが無ければ元のインスタンスをそのまま返す。");
        }
    }
}
