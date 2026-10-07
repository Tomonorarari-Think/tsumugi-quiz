using System;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.EditMode.Room;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// issue #155: <see cref="HostRolePreference"/> の保存先が <see cref="PlayerPrefs"/> から
    /// <see cref="AppSettingsStore"/>（<c>app-settings.json</c>）へ移行され、旧 <see cref="PlayerPrefs"/> の
    /// 値を初回だけ引き継ぐこと、および issue #155 H-1 再レビューで追加したプロセス内キャッシュ
    /// （<see cref="HostRolePreference.Load"/> が優先して返す値。<see cref="HostRolePreference.Save"/> の
    /// ファイル書き込みが失敗してもこのセッションでは維持される）を確かめる。実ファイル・実レジストリには
    /// 触れず、<see cref="InMemoryRoomFileSystem"/> と <see cref="HostRolePreference.AppSettingsStoreFactory"/> の
    /// 差し替えで隔離する（<see cref="PlayerPrefs"/> の <c>host.role</c> キーのみテスト前後で退避・復元する）。
    /// 各テストの <c>SetUp</c>/<c>TearDown</c> で <see cref="HostRolePreference.ResetCacheForTesting"/> を呼び、
    /// プロセス内キャッシュがテスト間で持ち越されないようにする。
    /// </summary>
    public class HostRolePreferenceTests
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
        public void Load_NoSavedValueAndNoLegacyPlayerPrefs_ReturnsDefaultPlayer()
        {
            var store = new AppSettingsStore(FilePath, new InMemoryRoomFileSystem());
            HostRolePreference.AppSettingsStoreFactory = () => store;

            var role = HostRolePreference.Load();

            Assert.AreEqual(HostRole.Player, role);
        }

        [Test]
        public void Load_AfterAppSettingsStoreSave_ReadsBackSavedRole()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new AppSettingsStore(FilePath, fileSystem);
            store.Save(AppSettings.Default.WithHostRole(HostRole.Moderator));
            HostRolePreference.AppSettingsStoreFactory = () => store;

            var role = HostRolePreference.Load();

            Assert.AreEqual(HostRole.Moderator, role, "app-settings.json に保存済みの host.role を読めるはず。");
        }

        [Test]
        public void Load_LegacyPlayerPrefsPresent_MigratesOnceAndDeletesLegacyKey()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            var store = new AppSettingsStore(FilePath, fileSystem);
            HostRolePreference.AppSettingsStoreFactory = () => store;
            PlayerPrefs.SetString(HostRoles.SettingsKey, HostRoles.ModeratorKey);
            PlayerPrefs.Save();

            var role = HostRolePreference.Load();

            Assert.AreEqual(HostRole.Moderator, role, "旧 PlayerPrefs の値が一度だけ引き継がれるはず。");
            Assert.IsFalse(PlayerPrefs.HasKey(HostRoles.SettingsKey), "移行後は旧キーを削除するはず（以後は参照しない）。");
            Assert.AreEqual(
                HostRole.Moderator,
                store.Load().Settings.HostRole,
                "app-settings.json 側にも書き込まれているはず。");
        }

        /// <summary>issue #155 M-1: 移行の保存自体が失敗した場合、旧キーを削除せず次回に再試行できるようにする。</summary>
        [Test]
        public void Load_LegacyPlayerPrefsPresentButSaveFails_KeepsLegacyKeyForRetry()
        {
            var fileSystem = new InMemoryRoomFileSystem { ThrowOnWrite = true };
            var store = new AppSettingsStore(FilePath, fileSystem);
            HostRolePreference.AppSettingsStoreFactory = () => store;
            PlayerPrefs.SetString(HostRoles.SettingsKey, HostRoles.ModeratorKey);
            PlayerPrefs.Save();

            var role = HostRolePreference.Load();

            Assert.AreEqual(HostRole.Moderator, role, "保存に失敗しても、このセッション内では読み取れた値を使う。");
            Assert.IsTrue(
                PlayerPrefs.HasKey(HostRoles.SettingsKey),
                "app-settings.json への保存が失敗した場合は旧キーを削除せず、次回起動時に再試行できるようにする。");
        }

        /// <summary>issue #155 M-2: 保存に失敗した場合は警告付きの失敗結果を返し、例外を外へ投げない。</summary>
        [Test]
        public void Save_IoExceptionWhileWriting_ReturnsFailureWithWarning()
        {
            var fileSystem = new InMemoryRoomFileSystem { ThrowOnWrite = true };
            HostRolePreference.AppSettingsStoreFactory = () => new AppSettingsStore(FilePath, fileSystem);

            var result = HostRolePreference.Save(HostRole.Moderator);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Warnings.Count > 0);
        }

        /// <summary>issue #155 M-2/M-3: 保存に成功した場合は Load で読み戻せる。</summary>
        [Test]
        public void Save_ThenLoad_RoundTrips()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            HostRolePreference.AppSettingsStoreFactory = () => new AppSettingsStore(FilePath, fileSystem);

            var result = HostRolePreference.Save(HostRole.Moderator);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(HostRole.Moderator, HostRolePreference.Load());
        }

        /// <summary>
        /// issue #155 H-1（再レビュー）: HostSetup View のトグルの値を実際に伝える経路は
        /// <see cref="HostRolePreference.Load"/> だけであり、<see cref="HostRolePreference.Save"/> の
        /// ファイル書き込みが失敗しても、今回のセッションではプロセス内キャッシュ経由でトグルどおりの値を
        /// 返す（app-settings.json への永続化には失敗するが、このプロセスの動作には影響しない）。
        /// </summary>
        [Test]
        public void Save_ThenLoad_ReturnsSavedRoleEvenWhenFileWriteFails()
        {
            var fileSystem = new InMemoryRoomFileSystem { ThrowOnWrite = true };
            HostRolePreference.AppSettingsStoreFactory = () => new AppSettingsStore(FilePath, fileSystem);

            var result = HostRolePreference.Save(HostRole.Moderator);

            Assert.IsFalse(result.Success, "ファイルへの書き込み自体は失敗するはず。");
            Assert.AreEqual(
                HostRole.Moderator,
                HostRolePreference.Load(),
                "書き込みが失敗しても、このプロセス内では Save した値がそのまま返るはず。");
        }

        /// <summary>
        /// issue #155 L-4: <see cref="HostRolePreference.Load"/> より先に <see cref="HostRolePreference.Save"/>
        /// が呼ばれた場合でも、保存に成功したら旧 <see cref="PlayerPrefs"/> キーを削除する。削除しないと、
        /// 次回起動（プロセス内キャッシュが無い状態）の <see cref="HostRolePreference.Load"/> が
        /// 「app-settings.json の値が既定値と一致し、かつ旧キーが残っている」と誤認して移行を再実行し、
        /// 今回明示的に保存した値を旧 <see cref="PlayerPrefs"/> の値で上書きしてしまう。
        /// </summary>
        [Test]
        public void Save_Success_DeletesLegacyKey_SoNextLoadDoesNotMigrate()
        {
            var fileSystem = new InMemoryRoomFileSystem();
            HostRolePreference.AppSettingsStoreFactory = () => new AppSettingsStore(FilePath, fileSystem);
            PlayerPrefs.SetString(HostRoles.SettingsKey, HostRoles.ModeratorKey);
            PlayerPrefs.Save();

            var result = HostRolePreference.Save(HostRole.Player);

            Assert.IsTrue(result.Success);
            Assert.IsFalse(PlayerPrefs.HasKey(HostRoles.SettingsKey), "保存に成功したら旧キーは削除されるはず。");

            // 次回起動を模して、プロセス内キャッシュをリセットしてから Load を呼ぶ。
            HostRolePreference.ResetCacheForTesting();
            var role = HostRolePreference.Load();

            Assert.AreEqual(
                HostRole.Player,
                role,
                "旧キーが削除されているため、明示的に保存した player が旧 PlayerPrefs の値で上書きされてはいけない。");
        }

        /// <summary>issue #155 M-4: AppPaths が未設定（既定ストアの解決に失敗）のときは既定値へフォールバックする。</summary>
        [Test]
        public void Load_AppPathsNotConfigured_ReturnsDefaultPlayer()
        {
            var originalEnv = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, null);
            AppPaths.Reset();
            HostRolePreference.AppSettingsStoreFactory = () => new AppSettingsStore();

            try
            {
                var role = HostRolePreference.Load();

                Assert.AreEqual(HostRole.Player, role);
            }
            finally
            {
                Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, originalEnv);
                AppPaths.Reset();
            }
        }
    }
}
