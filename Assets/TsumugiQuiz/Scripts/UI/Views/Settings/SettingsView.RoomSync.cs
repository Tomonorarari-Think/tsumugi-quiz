using System;
using System.Collections.Generic;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// <see cref="SettingsView"/> と <see cref="RoomSettingsSync"/>（#27）の結合点
    /// （issue #28 Phase 2、docs/network.md §12.3）。「適用」からの書き込みはここ 1 か所に閉じている。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>host.role の一本化</b>: <c>host.role</c> の権威は <see cref="RoomSettingsSync"/> に一本化し、
    /// 本 View からは <c>HostRolePreference</c>（PlayerPrefs）にも <c>LobbyState.Role</c> にも
    /// 直接書き込まない。<see cref="RoomSettingsSync"/> 側の <c>ReconcileHostRole</c> →
    /// <c>RoomSettingsApplier</c> がロビーへ反映する（docs/network.md §12.6）。
    /// </para>
    /// <para>
    /// <b>ロック中</b>: <see cref="RoomSettingsSync.IsLocked"/> が true（ゲーム進行中）のときは
    /// 送らずにメッセージだけ出す。ローカルの下書き（<see cref="RoomSettingsDraft"/>）の保存は行う。
    /// <see cref="RoomSettingsSync.Unlock"/> は進行中に呼ぶと拒否される API なので、本 View からは呼ばない
    /// （ロビーへ戻る操作が <c>GameSession.ReturnToLobby</c> 経由で解除する）。
    /// </para>
    /// <para>
    /// <b>tts.enabled と ReadingEnabled</b>: 本 View は <c>tts.enabled</c> を「ルーム設定」として
    /// <see cref="RoomSettingsSync.TrySetSettings"/> に渡すだけで、実行時の読み上げ ON/OFF
    /// （<c>TtsSyncCoordinator.SetReadingEnabled</c>）は直接呼ばない。反映は
    /// <c>RoomSettingsApplier</c> 経由のみ（docs/network.md §12.6、docs/tts.md）。
    /// </para>
    /// </remarks>
    public sealed partial class SettingsView
    {
        /// <summary>ルーム設定の同期先が無い（ホストを開始していない）ときの状態メッセージ。</summary>
        private const string RoomSyncUnavailableMessage =
            "設定を保存しました（まだホストを開始していないため、この PC 内にのみ保存されます）。";

        /// <summary>ロック中（ゲーム進行中）に「適用」したときの状態メッセージ。</summary>
        private const string RoomSyncLockedMessage =
            "ゲーム進行中は変更できません。この PC 内には保存しました（ロビーへ戻ると次回から反映されます）。";

        /// <summary>クライアント（ホストでない）で「適用」したときの状態メッセージ。</summary>
        private const string RoomSyncNotHostMessage =
            "ルーム設定を変更できるのはホストだけです。この PC 内には保存しました。";

        private static Func<RoomSettingsSync> _roomSettingsSyncLocator = FindRoomSettingsSync;

        /// <summary>
        /// スポーン済みの <see cref="RoomSettingsSync"/> の探し方。既定は稼働中の
        /// <see cref="NetworkBootstrap"/> から辿る実装で、Boot を経由していない・ホストもクライアントも
        /// 始まっていない場合は null を返す。PlayMode テストはここを差し替えて実ネットワークに触れずに検証する
        /// （null を代入すると既定へ戻る）。
        /// </summary>
        internal static Func<RoomSettingsSync> RoomSettingsSyncLocator
        {
            get => _roomSettingsSyncLocator;
            set => _roomSettingsSyncLocator = value ?? FindRoomSettingsSync;
        }

        private static RoomSettingsSync FindRoomSettingsSync()
        {
            var service = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;

            // #116: NetworkService 側の破棄判定（IsDisposed。Dispose() 実行中を含む）を先に見る。
            // FindActiveGameSession() 自体は破棄済みでも例外を投げず null を返す（#101）ため、
            // ここで弾かなくても以下の呼び出しは安全だが、先に見ることで「同期先なし」の理由が
            // NetworkService 側であることを明確にする（L-5 のもとになった経路）。
            if (service == null || service.IsDisposed)
            {
                return null;
            }

            // FindActiveGameSession() 自体は #101 以降、破棄済みでも例外を投げず null を返すため
            // try の外に出す（#116 L-7）。try に入れるのは NGO 由来の例外が出うる呼び出しだけに絞る。
            var session = service.FindActiveGameSession();
            if (session == null)
            {
                return null;
            }

            try
            {
                var sync = session.SettingsSync;
                return sync != null && sync.IsSpawned ? sync : null;
            }
            catch (ObjectDisposedException)
            {
                // ここから下の catch は NGO 由来の例外だけを対象にする（L-5）。
                // session.SettingsSync / sync.IsSpawned は NGO の NetworkObject / NetworkBehaviour の
                // プロパティで、上の IsDisposed チェックと呼び出しの間に NetworkManager.Shutdown() の
                // 実処理（フレーム終端）が割り込むと、破棄済みの NetworkObject に触れて例外になりうる。
                return null;
            }
        }

        /// <summary>
        /// 「適用」で確定した <paramref name="settings"/> を <see cref="RoomSettingsSync"/> へ渡す。
        /// </summary>
        /// <param name="settings">検証済みのルーム設定。</param>
        /// <param name="warnings">
        /// 同期側の検証警告（<see cref="RoomSettingsSync.LastValidationWarnings"/>）の追加先。
        /// </param>
        /// <returns>画面に出す状態メッセージ。</returns>
        /// <remarks>
        /// UI 要素に触れないため、<c>OnShow</c> を通さずに単体で呼べる
        /// （PlayMode テストが実スポーンした <see cref="RoomSettingsSync"/> と組み合わせて検証する）。
        /// </remarks>
        internal string PushToRoomSettingsSync(RoomSettings settings, List<string> warnings)
        {
            var sync = RoomSettingsSyncLocator();
            if (sync == null)
            {
                return RoomSyncUnavailableMessage;
            }

            // L-1: 権限（ホストかどうか）を先に判定する。クライアントに対して
            // 「ゲーム進行中は変更できません」と出すと、ロビーへ戻れば変更できるように読めてしまうため。
            if (!sync.IsServer)
            {
                return RoomSyncNotHostMessage;
            }

            if (sync.IsLocked)
            {
                return RoomSyncLockedMessage;
            }

            if (!sync.TrySetSettings(settings))
            {
                // ここに来るのは、判定と実行の間にロック・権限が変わった場合だけ（詳細は警告ログに出る）。
                return "ルーム設定を反映できませんでした。ロビーの状態を確認してください。";
            }

            warnings.AddRange(sync.LastValidationWarnings);
            return "設定を適用しました（参加者全員に反映されます）。";
        }
    }
}
