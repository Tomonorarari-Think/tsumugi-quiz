using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.Shared.Room;
using UnityEngine;

namespace TsumugiQuiz.Tests.PlayMode.UI.HostSetup
{
    /// <summary>
    /// M-9: HostSetup 画面が書き込む状態（player.name / network.port / host.role の
    /// <c>app-settings.json</c>、#28 H2 で移行した旧 <see cref="PlayerPrefs"/> キーの残骸）を
    /// テスト前後で退避・復元するスコープ。キー名は
    /// <c>Assets/TsumugiQuiz/Scripts/UI/Views/HostSetup/HostSetupPreferences.cs</c>（internal のため
    /// テストからは直接参照できない）と同じ文字列をここに複製して使う。
    /// </summary>
    /// <remarks>
    /// issue #155: <c>host.role</c> は <see cref="PlayerPrefs"/> から <c>app-settings.json</c> へ移行した。
    /// 旧 <see cref="PlayerPrefs"/> の値の退避・復元と、一度きりの移行が本テストへ混入しないための
    /// クリアは <see cref="RoomSettingsDraftScope"/> がまとめて行う。
    /// <c>HostSetupPreferences.AppSettingsStoreFactory</c> は <c>network.port</c> にのみ使う（既定＝実行マシンの
    /// 実ファイルのまま）。<c>host.role</c> の保存先は <c>HostSetupPreferences.LoadHostRole/SaveHostRole</c> が
    /// 委譲する <c>TsumugiQuiz.Network.HostRolePreference</c>（issue #155 M-3）に一本化されているため、
    /// <see cref="RoomSettingsDraftScope"/> が差し替える <c>HostRolePreference.AppSettingsStoreFactory</c> を
    /// ここで既定（実ファイル）へ戻す。こうしないと、HostSetup View の「司会専任」トグルの保存
    /// （<c>HostRolePreference</c> 経由）と <c>RoomSettingsSync</c> の読み込み（同じく <c>HostRolePreference</c>）が
    /// 別々のファイルを見てしまい、シーンテストで「トグルを切り替えてホストを開始する」フローが成立しない
    /// （<see cref="MainSceneTestHelpers.AppSettingsFileScope"/> が退避・復元する実ファイルに統一する）。
    /// さらに issue #155 L-5: 実行機の実ファイルに以前の対話的な確認作業で残った <c>host.role</c>
    /// （<c>moderator</c> 等）が混入しないよう、<see cref="Backup"/> 直後に既定値（<c>player</c>）へ明示的に
    /// 書き戻す（<see cref="Restore"/> で元のバイト列に戻るため、実行機の状態は破壊しない）。
    /// </remarks>
    internal sealed class HostSetupPreferencesScope
    {
        // 移行元の旧 PlayerPrefs キー（#28 H2）。移行後は削除されるが、テストの前後で復元しておく。
        private static readonly string LegacyPlayerNameKey = TsumugiQuiz.UI.Views.JoinView.PlayerNamePrefsKey;
        private const string LegacyPortKey = "HostSetup.Port";

        private readonly bool _hadPlayerName;
        private readonly string _playerName;
        private readonly bool _hadPort;
        private readonly int _port;

        private readonly MainSceneTestHelpers.AppSettingsFileScope _appSettingsScope;

        /// <summary>
        /// ホスト開始時に <c>RoomSettingsSync</c> が読む下書き（<c>room.lastApplied</c>）と、
        /// 旧 <c>PlayerPrefs</c> の <c>host.role</c> の隔離（PR #92 再レビュー M-1、issue #155）。
        /// 実行マシンの <c>app-settings.json</c> に「適用」済みの設定があっても、
        /// シーンテストは常に既定のルーム設定で始まるようにする。
        /// </summary>
        private readonly RoomSettingsDraftScope _roomSettingsDraftScope;

        private HostSetupPreferencesScope(
            bool hadPlayerName, string playerName,
            bool hadPort, int port,
            MainSceneTestHelpers.AppSettingsFileScope appSettingsScope,
            RoomSettingsDraftScope roomSettingsDraftScope)
        {
            _hadPlayerName = hadPlayerName;
            _playerName = playerName;
            _hadPort = hadPort;
            _port = port;
            _appSettingsScope = appSettingsScope;
            _roomSettingsDraftScope = roomSettingsDraftScope;
        }

        public static HostSetupPreferencesScope Backup()
        {
            var appSettingsScope = MainSceneTestHelpers.AppSettingsFileScope.Backup();
            var roomSettingsDraftScope = RoomSettingsDraftScope.Redirect();

            // issue #155: RoomSettingsDraftScope.Redirect() は HostRolePreference.AppSettingsStoreFactory を
            // 一時ファイルへ差し替える（RoomSettingsSync 単体のテスト向け）。本スコープを使うシーンテストは
            // HostSetup View の「司会専任」トグル（HostSetupPreferences → HostRolePreference に委譲）を実際に
            // 切り替えてホストを開始するフローを検証するため、HostRolePreference 側も既定（実ファイル）へ
            // 戻し、両者が同じ保存先を見るようにする。
            HostRolePreference.AppSettingsStoreFactory = () => new AppSettingsStore();

            // L-5: 実行機の実ファイルに以前の対話的な確認作業で残った host.role（moderator 等）が
            // 混入しないよう、既定値へ明示的に書き戻す（バイト列は appSettingsScope が退避済みなので
            // Restore() で元に戻る）。
            HostRolePreference.Save(HostRole.Player);

            return new HostSetupPreferencesScope(
                PlayerPrefs.HasKey(LegacyPlayerNameKey), PlayerPrefs.GetString(LegacyPlayerNameKey, string.Empty),
                PlayerPrefs.HasKey(LegacyPortKey), PlayerPrefs.GetInt(LegacyPortKey, 0),
                appSettingsScope,
                roomSettingsDraftScope);
        }

        public void Restore()
        {
            RestoreString(LegacyPlayerNameKey, _hadPlayerName, _playerName);
            RestoreInt(LegacyPortKey, _hadPort, _port);
            PlayerPrefs.Save();

            _appSettingsScope.Restore();
            _roomSettingsDraftScope.Restore();
        }

        private static void RestoreString(string key, bool hadValue, string value)
        {
            if (hadValue)
            {
                PlayerPrefs.SetString(key, value);
            }
            else
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        private static void RestoreInt(string key, bool hadValue, int value)
        {
            if (hadValue)
            {
                PlayerPrefs.SetInt(key, value);
            }
            else
            {
                PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
