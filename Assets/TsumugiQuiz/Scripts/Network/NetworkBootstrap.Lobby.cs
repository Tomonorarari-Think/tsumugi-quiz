using TsumugiQuiz.Room;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="NetworkBootstrap"/> のうち、ロビーの共有状態（<see cref="LobbyState"/>）の
    /// 生成・破棄を行う部分クラス（issue #7）。
    ///
    /// ホストを開始した瞬間（<c>NetworkManager.OnServerStarted</c>）に <see cref="LobbyState"/> を
    /// 動的スポーンし、停止時（<c>OnServerStopped</c>）に参照を落とす。View（<c>LobbyView</c>）は
    /// <see cref="Lobby"/> を読むだけで、生成・破棄には関与しない（HostConnectivityService と同じ方針。#5 C-1）。
    /// </summary>
    /// <remarks>
    /// プレハブは Boot シーンの <c>NetworkManager</c> が参照するネットワークプレハブ一覧
    /// （<c>Assets/DefaultNetworkPrefabs.asset</c>）から、<see cref="LobbyState"/> を持つものを探して使う
    /// （<see cref="LobbyState.TryResolvePrefab"/>）。<c>NetworkConfig.ForceSamePrefabs</c> が既定 true なので、
    /// ホストとクライアントが同じ一覧を持っていれば接続時の設定ハッシュも一致する。
    /// </remarks>
    public sealed partial class NetworkBootstrap
    {
        private LobbyState _lobby;

        /// <summary>
        /// 同じ GameObject の <see cref="NetworkManager"/>（<c>[RequireComponent]</c> により必ず存在する）。
        /// 毎フレーム参照されうるので <c>GetComponent</c> の結果をキャッシュする（レビュー LOW）。
        /// </summary>
        private NetworkManager _networkManager;

        private NetworkManager NetworkManagerComponent
            => _networkManager != null ? _networkManager : (_networkManager = GetComponent<NetworkManager>());

        /// <summary>
        /// 稼働中の <see cref="LobbyState"/>。ホストなら自分がスポーンしたもの、
        /// クライアントならサーバーから届いたものを返す。まだ無ければ null。
        /// </summary>
        public LobbyState Lobby
        {
            get
            {
                if (_lobby != null && _lobby.IsSpawned)
                {
                    return _lobby;
                }

                _lobby = LobbyState.Find(NetworkManagerComponent);
                return _lobby;
            }
        }

        /// <summary>
        /// <c>NetworkManager.OnServerStarted</c> のハンドラ。ロビーの共有状態をスポーンし、
        /// 接続承認ハンドラとホスト自身のプレイヤー名を渡してから、ルーム設定を 1 度だけ流し込む。
        /// </summary>
        private void HandleServerStarted()
        {
            var networkManager = NetworkManagerComponent;
            if (networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            if (LobbyState.Find(networkManager) != null)
            {
                // 既に立っている（多重呼び出し）。
                return;
            }

            if (!LobbyState.TryResolvePrefab(networkManager, out var prefab))
            {
                Debug.LogError(
                    "[NetworkBootstrap] LobbyState を持つネットワークプレハブが見つかりません。" +
                    "TsumugiQuiz/Setup/Setup Lobby Prefab を実行して Assets/DefaultNetworkPrefabs.asset に登録してください。");
                return;
            }

            var spawned = networkManager.SpawnManager.InstantiateAndSpawn(prefab);
            if (spawned == null)
            {
                Debug.LogError("[NetworkBootstrap] LobbyState のスポーンに失敗しました。");
                return;
            }

            spawned.gameObject.name = "LobbyState";

            _lobby = spawned.GetComponent<LobbyState>();
            if (_lobby == null)
            {
                Debug.LogError("[NetworkBootstrap] スポーンしたプレハブに LobbyState がありません。");
                return;
            }

            _lobby.AttachServer(_service?.ApprovalHandler, _service?.LocalPlayerName);

            // レビュー M-8: ルーム設定の初期値の流し込みはここで 1 度だけ行う
            // （View は Show のたびに作り直されるので、View からやると毎回上書きしてしまう）。
            // #27: この直後（NetworkService.StartHost の中）に GameSession がスポーンされ、
            // その RoomSettingsSync が確定したルーム設定をここへ上書きする
            // （RoomSettingsApplier.Apply → LobbyState.ConfigureRoom）。
            // LobbyState は GameSession より先に立つため、ここでは同じ暫定値
            // （host.role は HostRolePreference が保持するプロセス内の現在値。issue #155 で
            // PlayerPrefs から app-settings.json へ移行し、さらに H-1 再レビューで Save 時に
            // 即時反映するプロセス内キャッシュを持たせたため、ファイル保存の成否に関わらず
            // 直前にトグルした役割が使われる。それ以外は docs/room-settings.md の既定値）を入れておく。
            _lobby.ConfigureRoom(
                HostRolePreference.Load(),
                RoomSettings.DefaultMaxPlayers,
                allowLateJoin: SessionSettings.DefaultAllowLateJoin);

            // M-3（issue #8 レビュー）: 実際にバインドされたポートは、UI 経由のホスト開始時に
            // HostSetupView.OnHostStartCompleted が「[HostSetupView] ホストを開始しました
            // activePort=...」でログする（この OnServerStarted ハンドラは StartHost() 呼び出しの
            // 内部から同期的に発火するため、呼び出し元へ ActivePort が反映される前にここへ来てしまい、
            // 実測で 0 と誤表示した。また NetworkService.StartHost 自体にログを足すと、UI を経由しない
            // 各種テストフィクスチャの厳密な LogAssert.Expect 順序を崩すことも実測で判明した）。
            Debug.Log("[NetworkBootstrap] LobbyState をスポーンしました。");
        }

        /// <summary>
        /// <c>NetworkManager.OnServerStopped</c> / <c>OnClientStopped</c> のハンドラ。
        /// NGO 側が <see cref="NetworkObject"/> を破棄するので、ここでは参照を落とすだけ。
        /// </summary>
        private void HandleNetworkStopped(bool _) => _lobby = null;
    }
}
