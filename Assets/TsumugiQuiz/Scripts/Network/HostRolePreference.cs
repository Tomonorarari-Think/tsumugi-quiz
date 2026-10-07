using System;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Room;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <c>host.role</c> の保存先（<see cref="AppSettingsStore"/>、<c>app-settings.json</c>）の読み書き
    /// （docs/room-settings.md §1「ホスト・ルーム」）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// HostSetup View（#5）の「司会専任」トグルの読み書きは
    /// <see cref="TsumugiQuiz.UI.Views.HostSetup.HostSetupPreferences"/> がここへ委譲する（issue #155 M-3。
    /// UI 層 → Network 層の参照は許容されるため、保存先解決・移行ロジックの二重実装を避けるために
    /// 本クラスへ一本化した）。<see cref="NetworkBootstrap"/>（<c>LobbyState.ConfigureRoom</c>）と
    /// <see cref="RoomSettingsSync"/>（ルーム設定の初期値）も本クラスの <see cref="Load"/> を読む（#27）。
    /// #28（Settings View）以降、<c>host.role</c> 以外のルーム設定は
    /// <see cref="TsumugiQuiz.Room.RoomSettingsDraft"/>（<c>app-settings.json</c> の
    /// <c>room.lastApplied</c>）から復元する。<c>host.role</c> だけは「ホストを開始する画面で
    /// 今まさに切り替えた値が勝つ」ようにしたいので、本クラスを
    /// 下書きへ重ねる形で残している（<see cref="RoomSettingsSync"/> の <c>OnNetworkSpawn</c>、
    /// docs/network.md §12.6）。
    /// </para>
    /// <para>
    /// issue #155: 従来は <c>PlayerPrefs</c>（Windows ではレジストリ、companyName/productName キー）に
    /// 暫定保存していたため、同一 PC で複数インスタンス・複数データルート（<c>-tq-data-root</c> /
    /// <c>-IsolateDocuments</c>）を使った場合に値が共有されてしまう不具合があった（実測: docs/tasks/m2-verification.md
    /// 「Run B」）。他のアプリ設定と同じ <see cref="AppSettingsStore"/>（<c>app-settings.json</c>、
    /// <c>TsumugiQuiz.Core.AppPaths.DataRoot</c> 配下）へ保存先を移し、既存の <c>PlayerPrefs</c> の値は
    /// 初回読み込み時に一度だけ引き継ぐ（移行後は <c>PlayerPrefs</c> のキーを削除するため、以後は参照しない）。
    /// </para>
    /// <para>
    /// issue #155 H-1（再レビュー）: HostSetup View のトグルの値は <c>NetworkService.StartHostWhenReady</c> には
    /// 渡らず、実際に <c>LobbyState.ConfigureRoom</c> / <c>RoomSettingsSync</c> へ役割を伝える経路は
    /// <see cref="Load"/>（<c>app-settings.json</c> の再読み込み）だけである。そのため <see cref="Save"/> の
    /// ファイル書き込みが失敗すると、以前の実装では今回のセッションもファイルに残った古い役割で
    /// 開始してしまっていた。本クラスはプロセス内の現在値をキャッシュし、<see cref="Save"/> は
    /// ファイル書き込みの成否に関わらず必ずこの値を更新する。<see cref="Load"/> はこのプロセス内の値が
    /// あればそれを優先して返すため、保存（永続化）に失敗しても今回のセッションはトグルどおりに動作する
    /// （M-1「移行の保存に失敗した場合、このセッションでは読み取れた値を使う」と同じ考え方）。
    /// </para>
    /// </remarks>
    public static class HostRolePreference
    {
        /// <summary>
        /// 使う <see cref="AppSettingsStore"/> の生成方法。既定は実ファイル（<c>app-settings.json</c>）を使う実装。
        /// テストではこのプロパティにテスト用パスを積んだファクトリを差し替えられる（issue #155 L-a:
        /// setter は <c>internal</c> にし、本番コードから差し替えられないようにする）。
        /// </summary>
        public static Func<AppSettingsStore> AppSettingsStoreFactory { get; internal set; } = () => new AppSettingsStore();

        /// <summary>
        /// プロセス内の現在値（issue #155 H-1 再レビュー）。一度でも <see cref="Load"/> / <see cref="Save"/> で
        /// 値が確定したら、以後はファイルを読み直さずこの値を返す。
        /// </summary>
        private static HostRole? _current;

        /// <summary>
        /// 保存されている <c>host.role</c> を読む。プロセス内の現在値（<see cref="Save"/> 済み、または
        /// 初回に読み取り済みの値）があればそれを最優先で返し、ファイルは読み直さない。
        /// 未確定の場合は <c>app-settings.json</c> を読む。未保存・不正な値のときは
        /// <see cref="HostRole.Player"/>（既定値）を返す。<c>app-settings.json</c> にまだ値が無く、
        /// 旧 <c>PlayerPrefs</c>（キー <see cref="HostRoles.SettingsKey"/>）に値が残っている場合は、
        /// この呼び出しで一度だけ <c>app-settings.json</c> へ引き継ぎを試みる（issue #155 M-1: 引き継ぎの
        /// 保存自体が失敗した場合は旧キーを削除せず、次回起動時に再試行する）。
        /// </summary>
        /// <returns>ホストの役割。</returns>
        public static HostRole Load()
        {
            if (_current.HasValue)
            {
                return _current.Value;
            }

            try
            {
                var store = AppSettingsStoreFactory();
                var settings = store.Load().Settings;

                if (settings.HostRole == AppSettings.DefaultHostRole && PlayerPrefs.HasKey(HostRoles.SettingsKey))
                {
                    settings = MigrateFromLegacyPlayerPrefs(store, settings);
                }

                _current = settings.HostRole;
                return _current.Value;
            }
            catch (InvalidOperationException ex)
            {
                // AppPaths 未設定などの一時的な失敗はキャッシュしない（後で解決すれば次回の呼び出しで
                // 再度読み直せるようにする）。
                Debug.LogWarning($"[HostRolePreference] host.role の読み込みに失敗しました: {ex.Message}");
                return AppSettings.DefaultHostRole;
            }
        }

        /// <summary>
        /// <c>host.role</c> だけを保存する（issue #155 M-3。<c>HostSetupPreferences.SaveHostRole</c> が
        /// ここへ委譲する）。issue #155 H-1 再レビュー: ファイルへの書き込みを試みる前に、必ずプロセス内の
        /// 現在値（<see cref="Load"/> が返す値）を <paramref name="role"/> に更新する。これにより、
        /// 直後に <see cref="Load"/> を呼ぶ <c>NetworkBootstrap</c> / <c>RoomSettingsSync</c> は、
        /// ファイル書き込みが失敗した場合でも今回のセッションではトグルどおりの役割を受け取る。
        /// <see cref="AppSettingsStore"/> が例外を投げた場合（<c>AppPaths</c> 未設定）・保存自体が失敗した場合
        /// （ディスク I/O 例外）のいずれも、警告ログを出したうえで失敗結果を返す（issue #155 M-2: 呼び出し元が
        /// UI へ伝えられるようにするため、失敗を握りつぶさない）。
        /// 保存に成功した場合は、旧 <c>PlayerPrefs</c>（キー <see cref="HostRoles.SettingsKey"/>）が
        /// 残っていれば削除する（issue #155 L-4: <see cref="Load"/> より先に本メソッドが呼ばれた場合、
        /// 削除しないと次回起動時の <see cref="Load"/> が「<c>app-settings.json</c> の値が既定値と一致し、
        /// かつ旧キーが残っている」と誤認して移行を再実行し、今回明示的に保存した値を旧 <c>PlayerPrefs</c> の
        /// 値で上書きしてしまう）。
        /// </summary>
        /// <param name="role">保存する役割。</param>
        /// <returns>保存結果（成功可否・警告）。</returns>
        public static AppSettingsSaveResult Save(HostRole role)
        {
            _current = role;

            try
            {
                var store = AppSettingsStoreFactory();
                var current = store.Load().Settings;
                var result = store.Save(current.WithHostRole(role));

                if (result.Success)
                {
                    // L-4: 明示的に保存できたら、旧 PlayerPrefs の値はもう不要（かつ次回起動時に
                    // Load() が誤って移行し直す経路を塞ぐ必要がある）ため削除する。
                    if (PlayerPrefs.HasKey(HostRoles.SettingsKey))
                    {
                        PlayerPrefs.DeleteKey(HostRoles.SettingsKey);
                        PlayerPrefs.Save();
                    }
                }
                else
                {
                    Debug.LogWarning(
                        $"[HostRolePreference] host.role の保存に失敗しました: {string.Join(" / ", result.Warnings)}");
                }

                return result;
            }
            catch (InvalidOperationException ex)
            {
                var message = $"host.role の保存に失敗しました: {ex.Message}";
                Debug.LogWarning($"[HostRolePreference] {message}");
                return AppSettingsSaveResult.Failed(message);
            }
        }

        /// <summary>プロセス内の現在値をリセットする（テスト専用）。</summary>
        internal static void ResetCacheForTesting() => _current = null;

        /// <summary>
        /// 旧 <c>PlayerPrefs</c> の値を <c>app-settings.json</c> へ一度だけ引き継ぐ（issue #155）。
        /// 引き継ぎの保存に失敗した場合（issue #155 M-1）は旧キーを削除しない（次回起動時に再試行できるように
        /// するため）。ただし、このセッション内では読み取れた値を正としてそのまま使う。
        /// </summary>
        /// <returns>引き継ぎ後（保存の成否に関わらず、読み取れた値を反映済み）の設定。</returns>
        private static AppSettings MigrateFromLegacyPlayerPrefs(AppSettingsStore store, AppSettings settings)
        {
            var legacyValue = PlayerPrefs.GetString(HostRoles.SettingsKey, HostRoles.PlayerKey);
            var legacyRole = HostRoles.Parse(legacyValue);

            if (legacyRole != AppSettings.DefaultHostRole)
            {
                var migrated = settings.WithHostRole(legacyRole);
                var saveResult = store.Save(migrated);

                if (!saveResult.Success)
                {
                    Debug.LogWarning(
                        "[HostRolePreference] host.role の移行（app-settings.json への保存）に失敗したため、"
                        + $"旧 PlayerPrefs の値は保持します。次回起動時に再試行します: {string.Join(" / ", saveResult.Warnings)}");
                    return migrated;
                }

                settings = migrated;
            }

            PlayerPrefs.DeleteKey(HostRoles.SettingsKey);
            PlayerPrefs.Save();

            Debug.Log($"[HostRolePreference] PlayerPrefs の host.role（{legacyValue}）を app-settings.json へ移行しました。");

            return settings;
        }
    }
}
