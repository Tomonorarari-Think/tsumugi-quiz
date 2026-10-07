using System;
using System.IO;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using UnityEngine;

namespace TsumugiQuiz.Tests.Shared.Room
{
    /// <summary>
    /// <see cref="RoomSettingsDraft"/>（<c>app-settings.json</c> の <c>room.lastApplied</c>）と
    /// <c>host.role</c>（<see cref="TsumugiQuiz.Network.HostRolePreference"/>、issue #155 で
    /// <c>PlayerPrefs</c> から移行済み）を、テスト用の一時パスへ隔離するスコープ（PR #92 再レビュー M-1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RoomSettingsSync.OnNetworkSpawn</c> は「前回 Settings View で適用した下書き」を
    /// ホスト開始時の初期値に使う（docs/network.md §12.6）。そのため、ホストを起動する PlayMode テストは
    /// 何もしないと**実行マシンの <c>app-settings.json</c>**（Editor から直接実行した場合は実ユーザーの
    /// ファイル）を読んでしまい、「設定を一度でも適用した環境」では既定値を前提にしたアサートが落ちる。
    /// </para>
    /// <para>
    /// 本スコープは <c>RoomSettingsDraft</c> と <c>HostRolePreference</c> の両方の保存先を
    /// 同じ実行ごとの一時フォルダの <c>app-settings.json</c> へ差し替え、プロセス内キャッシュも破棄する。
    /// あわせて、旧 <c>PlayerPrefs</c>（<see cref="HostRoles.SettingsKey"/>）に実行機の値が残っていると
    /// <c>HostRolePreference.Load</c> が一度きりの移行をテスト実行中に行ってしまう
    /// （issue #155 の実測ケースそのもの）ため、テスト中は退避してクリアし、<see cref="Restore"/> で元へ戻す。
    /// <see cref="Restore"/>（= <see cref="Dispose"/>）で既定の保存先へ戻し、一時フォルダを削除する。
    /// </para>
    /// </remarks>
    public sealed class RoomSettingsDraftScope : IDisposable
    {
        private readonly string _tempRoot;
        private readonly bool _hadLegacyHostRole;
        private readonly string _legacyHostRole;
        private bool _restored;

        private RoomSettingsDraftScope(string tempRoot, bool hadLegacyHostRole, string legacyHostRole)
        {
            _tempRoot = tempRoot;
            _hadLegacyHostRole = hadLegacyHostRole;
            _legacyHostRole = legacyHostRole;
        }

        /// <summary>
        /// 下書き・<c>host.role</c> の保存先を一時パスへ差し替える（既存の下書きが無い状態から始まる）。
        /// </summary>
        public static RoomSettingsDraftScope Redirect()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "tsumugi-draft-scope-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            var appSettingsFile = Path.Combine(tempRoot, "app-settings.json");
            RoomSettingsDraft.AppSettingsStoreFactory = () => new AppSettingsStore(appSettingsFile);
            RoomSettingsDraft.ResetCacheForTesting();
            HostRolePreference.AppSettingsStoreFactory = () => new AppSettingsStore(appSettingsFile);
            HostRolePreference.ResetCacheForTesting();

            var hadLegacyHostRole = PlayerPrefs.HasKey(HostRoles.SettingsKey);
            var legacyHostRole = PlayerPrefs.GetString(HostRoles.SettingsKey, string.Empty);
            PlayerPrefs.DeleteKey(HostRoles.SettingsKey);
            PlayerPrefs.Save();

            return new RoomSettingsDraftScope(tempRoot, hadLegacyHostRole, legacyHostRole);
        }

        /// <summary>既定の保存先へ戻し、一時フォルダを削除する（多重呼び出しは無害）。</summary>
        public void Restore()
        {
            if (_restored)
            {
                return;
            }

            _restored = true;

            RoomSettingsDraft.AppSettingsStoreFactory = null;
            RoomSettingsDraft.ResetCacheForTesting();
            HostRolePreference.AppSettingsStoreFactory = () => new AppSettingsStore();
            HostRolePreference.ResetCacheForTesting();

            if (_hadLegacyHostRole)
            {
                PlayerPrefs.SetString(HostRoles.SettingsKey, _legacyHostRole);
            }
            else
            {
                PlayerPrefs.DeleteKey(HostRoles.SettingsKey);
            }

            PlayerPrefs.Save();

            if (!Directory.Exists(_tempRoot))
            {
                return;
            }

            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // 後始末に失敗しても、テスト結果には影響しないので無視する（OS の一時フォルダ）。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <inheritdoc />
        public void Dispose() => Restore();
    }
}
