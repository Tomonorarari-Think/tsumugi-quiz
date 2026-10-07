using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.EditMode.Room;
using TsumugiQuiz.UI.Views.HostSetup;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.UI.HostSetup
{
    /// <summary>
    /// issue #155 M-3/M-4: <see cref="HostSetupPreferences.LoadHostRole"/> /
    /// <see cref="HostSetupPreferences.SaveHostRole"/> が
    /// <see cref="TsumugiQuiz.Network.HostRolePreference"/> への委譲を通じて正しく動作すること、
    /// および UI 側・Network 側のどちらの窓口を先に呼んでも旧 <see cref="PlayerPrefs"/> からの移行が
    /// 1 回で完結する（順序に依存しない）ことを確かめる。issue #155 H-1 再レビューで追加した
    /// プロセス内キャッシュ（<see cref="HostRolePreference.ResetCacheForTesting"/>）は各テストの
    /// <c>SetUp</c>/<c>TearDown</c> でリセットし、テスト間で値が持ち越されないようにする。
    /// </summary>
    public class HostSetupPreferencesTests
    {
        private const string FilePath = @"C:\fake\TsumugiQuiz\app-settings.json";

        private bool _hadLegacyKey;
        private string _legacyValue;

        [SetUp]
        public void BackupLegacyPlayerPrefs()
        {
            // issue #155 H-1 再レビュー: プロセス内キャッシュが前のテストの値を持ち越さないようにする。
            HostRolePreference.ResetCacheForTesting();

            _hadLegacyKey = PlayerPrefs.HasKey(HostRoles.SettingsKey);
            _legacyValue = PlayerPrefs.GetString(HostRoles.SettingsKey, string.Empty);
            PlayerPrefs.DeleteKey(HostRoles.SettingsKey);
            PlayerPrefs.Save();
        }

        [TearDown]
        public void RestoreLegacyPlayerPrefsAndFactory()
        {
            HostRolePreference.AppSettingsStoreFactory = () => new AppSettingsStore();
            HostRolePreference.ResetCacheForTesting();

            if (_hadLegacyKey)
            {
                PlayerPrefs.SetString(HostRoles.SettingsKey, _legacyValue);
            }
            else
            {
                PlayerPrefs.DeleteKey(HostRoles.SettingsKey);
            }

            PlayerPrefs.Save();
        }

        [Test]
        public void SaveHostRole_ThenLoadHostRole_RoundTrips()
        {
            var store = new AppSettingsStore(FilePath, new InMemoryRoomFileSystem());
            HostRolePreference.AppSettingsStoreFactory = () => store;

            var saveResult = HostSetupPreferences.SaveHostRole(HostRole.Moderator);

            Assert.IsTrue(saveResult.Success);
            Assert.AreEqual(HostRole.Moderator, HostSetupPreferences.LoadHostRole());
        }

        /// <summary>
        /// 旧 PlayerPrefs の値が既定値（<c>"player"</c>）と同じ場合、app-settings.json への書き込みは
        /// 不要なので行わず、旧キーの削除だけ行う。
        /// </summary>
        [Test]
        public void LoadHostRole_LegacyPlayerPrefsMatchesDefault_DeletesLegacyKeyOnly()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new AppSettingsStore(FilePath, fileSystem);
            HostRolePreference.AppSettingsStoreFactory = () => store;
            PlayerPrefs.SetString(HostRoles.SettingsKey, HostRoles.PlayerKey);
            PlayerPrefs.Save();

            var role = HostSetupPreferences.LoadHostRole();

            Assert.AreEqual(HostRole.Player, role);
            Assert.IsFalse(PlayerPrefs.HasKey(HostRoles.SettingsKey), "既定値と同じでも旧キーは削除するはず。");
            Assert.IsFalse(
                fileSystem.FileExists(FilePath),
                "既定値と同じ場合、app-settings.json への書き込みは不要（Save を呼ばない）。");
        }

        /// <summary>
        /// issue #155 M-4: UI 側（<see cref="HostSetupPreferences"/>）が先に読んで移行した場合、
        /// 続けて Network 側（<see cref="TsumugiQuiz.Network.HostRolePreference"/>）を呼んでも
        /// 再移行しない。issue #155 H-1 再レビューでプロセス内キャッシュを持たせたため、2 回目の
        /// 呼び出しはそもそもファイル・PlayerPrefs のどちらにも触れずキャッシュを返す。
        /// </summary>
        [Test]
        public void LoadHostRole_ThenHostRolePreferenceLoad_DoesNotReMigrate()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new AppSettingsStore(FilePath, fileSystem);
            HostRolePreference.AppSettingsStoreFactory = () => store;
            PlayerPrefs.SetString(HostRoles.SettingsKey, HostRoles.ModeratorKey);
            PlayerPrefs.Save();

            // UI 側が先に読み、移行を行う。
            var firstRole = HostSetupPreferences.LoadHostRole();
            Assert.AreEqual(HostRole.Moderator, firstRole);
            Assert.IsFalse(PlayerPrefs.HasKey(HostRoles.SettingsKey), "1 回目の呼び出しで旧キーは削除されるはず。");

            // 旧キーがうっかり残っていた場合でも、プロセス内キャッシュが優先されるため
            // 再移行（＝ PlayerPrefs の再参照）は起きないことを確認する。
            PlayerPrefs.SetString(HostRoles.SettingsKey, HostRoles.PlayerKey);
            PlayerPrefs.Save();

            var secondRole = HostRolePreference.Load();

            Assert.AreEqual(
                HostRole.Moderator,
                secondRole,
                "プロセス内キャッシュがある限り、Network 側の呼び出しは PlayerPrefs を再参照しない。");

            PlayerPrefs.DeleteKey(HostRoles.SettingsKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// issue #155 M-4: 逆順（Network 側が先）でも、UI 側の呼び出しが再移行しないことを確認する。
        /// </summary>
        [Test]
        public void HostRolePreferenceLoad_ThenLoadHostRole_DoesNotReMigrate()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new AppSettingsStore(FilePath, fileSystem);
            HostRolePreference.AppSettingsStoreFactory = () => store;
            PlayerPrefs.SetString(HostRoles.SettingsKey, HostRoles.ModeratorKey);
            PlayerPrefs.Save();

            var firstRole = HostRolePreference.Load();
            Assert.AreEqual(HostRole.Moderator, firstRole);
            Assert.IsFalse(PlayerPrefs.HasKey(HostRoles.SettingsKey));

            var secondRole = HostSetupPreferences.LoadHostRole();

            Assert.AreEqual(HostRole.Moderator, secondRole, "UI 側の 2 回目の呼び出しも同じ値を返すはず。");
        }
    }
}
