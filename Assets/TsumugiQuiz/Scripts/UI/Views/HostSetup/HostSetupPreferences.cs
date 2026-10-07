using System;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using UnityEngine;

namespace TsumugiQuiz.UI.Views.HostSetup
{
    /// <summary>
    /// HostSetup 画面の入力値のうち、<c>host.role</c> と <c>network.port</c>
    /// （いずれも <see cref="AppSettingsStore"/>、issue #155 で <c>host.role</c> も移行済み）の読み書きを行う。
    /// プレイヤー名は <see cref="PlayerNamePreferences"/>（<see cref="JoinView"/> と共通）に分離した
    /// （PR #92 レビュー H2）。
    /// </summary>
    /// <remarks>
    /// <c>host.role</c> の権威は <c>TsumugiQuiz.Network.RoomSettingsSync</c>（#27）に一本化してある
    /// （#28 Phase 2、docs/network.md §12.6）。本クラスは
    /// 「ホストを開始する前」に HostSetup View の「司会専任」トグルが残す値の置き場所で、
    /// ホスト開始時に <c>RoomSettingsSync</c> / <c>LobbyState</c> が初期値として読む。
    /// <c>host.role</c> の実際の保存先解決・旧 <see cref="PlayerPrefs"/> からの移行ロジックは
    /// <see cref="TsumugiQuiz.Network.HostRolePreference"/> に一本化してあり（issue #155 M-3、
    /// UI 層 → Network 層の参照は許容されるため二重実装を避けた）、本クラスの
    /// <see cref="LoadHostRole"/> / <see cref="SaveHostRole"/> はそこへ委譲するだけ。
    /// <see cref="TsumugiQuiz.UI.Views.Settings.SettingsView"/> はここへ書き込まない。
    /// </remarks>
    internal static class HostSetupPreferences
    {
        /// <summary>移行元の旧 <see cref="PlayerPrefs"/> キー（<c>network.port</c> の暫定保存先、#28 H2 で移行）。</summary>
        internal const string LegacyPortPrefsKey = "HostSetup.Port";

        /// <summary>
        /// 使う <see cref="AppSettingsStore"/> の生成方法（<c>network.port</c> 用）。既定は実ファイル
        /// （<c>app-settings.json</c>）を使う実装。PlayMode テストではこのプロパティにテスト用パスを
        /// 積んだファクトリを差し替えられる。<c>host.role</c> は
        /// <see cref="TsumugiQuiz.Network.HostRolePreference.AppSettingsStoreFactory"/> を使う（issue #155 M-3）。
        /// </summary>
        internal static Func<AppSettingsStore> AppSettingsStoreFactory { get; set; } = () => new AppSettingsStore();

        /// <summary>
        /// 保存済みの <c>host.role</c>。無い・未知の値なら <see cref="HostRole.Player"/>。
        /// 実体は <see cref="TsumugiQuiz.Network.HostRolePreference.Load"/> に委譲する（issue #155 M-3。
        /// 旧 <see cref="PlayerPrefs"/> からの一度きりの移行もそちらが行う）。
        /// </summary>
        public static HostRole LoadHostRole() => HostRolePreference.Load();

        /// <summary>
        /// <c>host.role</c> だけを保存する（PR #92 レビュー H2: 従来の 3 引数版 <c>Save</c> を分割。
        /// issue #155 で保存先を <see cref="PlayerPrefs"/> から <see cref="AppSettingsStore"/> へ移行し、
        /// 実体は <see cref="TsumugiQuiz.Network.HostRolePreference.Save"/> に委譲する（M-3））。
        /// </summary>
        /// <param name="role">保存する役割。</param>
        /// <returns>
        /// 保存結果（issue #155 M-2: 呼び出し元（HostSetup View）が失敗を検知してユーザーへ伝えられるようにする）。
        /// </returns>
        public static AppSettingsSaveResult SaveHostRole(HostRole role) => HostRolePreference.Save(role);

        /// <summary>
        /// 保存済みのポート番号（<c>network.port</c>）。旧 <see cref="PlayerPrefs"/>（<see cref="LegacyPortPrefsKey"/>）
        /// からの一度きりの移行もここで行う。<see cref="AppSettingsStore"/> が例外を投げた場合は
        /// <see cref="NetworkConstants.DefaultPort"/> にフォールバックする（M4）。
        /// </summary>
        public static int LoadPort()
        {
            try
            {
                var store = AppSettingsStoreFactory();
                var settings = store.Load().Settings;

                if (settings.NetworkPort == AppSettings.DefaultNetworkPort && PlayerPrefs.HasKey(LegacyPortPrefsKey))
                {
                    var legacyPort = PlayerPrefs.GetInt(LegacyPortPrefsKey, AppSettings.DefaultNetworkPort);

                    // 0（OS 自動選択）は AppSettings.NetworkPort の範囲（1024〜65535）で表現できないため、
                    // 「今後の既定値」としては移行しない（当時の1回限りの選択として扱う）。
                    if (legacyPort != 0 && legacyPort != AppSettings.DefaultNetworkPort)
                    {
                        settings = settings.WithNetworkPort(legacyPort);
                        store.Save(settings);
                    }

                    PlayerPrefs.DeleteKey(LegacyPortPrefsKey);
                    PlayerPrefs.Save();
                }

                return settings.NetworkPort;
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning($"[HostSetupPreferences] network.port の読み込みに失敗しました: {ex.Message}");
                return NetworkConstants.DefaultPort;
            }
        }

        /// <summary>
        /// ポート番号を保存する。<c>0</c>（OS 自動選択）は「次回以降の既定値」としては保存しない
        /// （<see cref="AppSettings.NetworkPort"/> の範囲外のため、呼び出し側でスキップすること）。
        /// </summary>
        public static void SavePort(int port)
        {
            try
            {
                var store = AppSettingsStoreFactory();
                var current = store.Load().Settings;
                store.Save(current.WithNetworkPort(ClampPort(port)));
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning($"[HostSetupPreferences] network.port の保存に失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// ポート番号を ushort として使える範囲（0〜65535）に丸める。
        /// 範囲外の値（保存されたファイルの改変・過去バージョンの不正値等）は既定ポートに戻す。
        /// </summary>
        public static int ClampPort(int rawPort)
            => rawPort >= 0 && rawPort <= ushort.MaxValue ? rawPort : NetworkConstants.DefaultPort;
    }
}
