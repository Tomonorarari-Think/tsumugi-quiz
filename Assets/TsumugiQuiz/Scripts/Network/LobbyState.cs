using System;
using System.Collections.Generic;
using TsumugiQuiz.Core.Network;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// ロビーの共有状態を持つ <see cref="NetworkBehaviour"/>（docs/network.md §1.2 / §2.3 / §2.4、issue #7）。
    /// サーバー（ホスト）だけが名簿を書き換え、クライアントは <c>NetworkList</c> / <c>NetworkVariable</c> を
    /// 読むだけ（書き込み権限は NGO 既定の <c>Server</c>）。
    ///
    /// 判定ロジックそのものは <see cref="LobbyRoster"/>（<c>TsumugiQuiz.Core</c>、純 C#）に置き、
    /// 本クラスは「NGO の接続イベント・<c>NetworkList</c>」と名簿をつなぐアダプタに徹する。
    /// サーバー側の処理は <c>LobbyState.Server.cs</c>（同じ partial class）にある。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GameSession"/> とは別のプレハブ・別の <see cref="NetworkBehaviour"/> にしてある。
    /// ロビーの名簿はゲーム進行（#12 / #13 / #18）とライフサイクルが異なり、
    /// ホストが立っている間ずっと存在する必要があるため。
    /// </para>
    /// <para>
    /// 生成・破棄は <see cref="NetworkBootstrap"/>（<c>NetworkBootstrap.Lobby.cs</c>）が
    /// <c>NetworkManager.OnServerStarted</c> / <c>OnServerStopped</c> に合わせて行う。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed partial class LobbyState : NetworkBehaviour
    {
        /// <summary>切断中のエントリを削除するまでの保持時間（秒）。TODO(#26): ルーム設定へ。</summary>
        public const double DisconnectedRetentionSec = LobbyRoster.DefaultDisconnectedRetentionSec;

        /// <summary>
        /// 同じ席を別の接続（同じ名前・同じ再接続トークン）が引き継いだため、ホストが旧接続を切るときの理由（#163）。
        /// クライアントの画面にはそのまま表示される（受信側が自前の理由と判定するため、文言の定義は Core に置く。#208）。
        /// </summary>
        public const string SeatTakenOverDisconnectReason = DisconnectReasonMessages.SeatTakenOver;

        /// <summary>保持期間切れのエントリを掃除する間隔（秒）。</summary>
        private const double SweepIntervalSec = 1.0;

        private readonly NetworkList<PlayerEntry> _players = new NetworkList<PlayerEntry>();

        private readonly NetworkVariable<HostRole> _hostRole = new NetworkVariable<HostRole>(HostRole.Player);

        private readonly NetworkVariable<int> _maxPlayers =
            new NetworkVariable<int>(LobbyRoster.DefaultMaxPlayers);

        private readonly NetworkVariable<bool> _allowLateJoin = new NetworkVariable<bool>(false);

        /// <summary>UI へ渡す読み取り用のバッファ（毎フレーム確保しないよう使い回す）。</summary>
        private readonly List<PlayerEntry> _snapshot = new List<PlayerEntry>();

        /// <summary>参加者の名簿（サーバー書き込み・クライアント読み取り専用）。</summary>
        public NetworkList<PlayerEntry> Players => _players;

        /// <summary>ホストの役割（<c>host.role</c>）。</summary>
        public NetworkVariable<HostRole> Role => _hostRole;

        /// <summary>参加人数の上限（<c>room.maxPlayers</c>）。</summary>
        public NetworkVariable<int> MaxPlayers => _maxPlayers;

        /// <summary>ゲーム進行中の途中参加を許可するか（<c>network.allowLateJoin</c>）。</summary>
        public NetworkVariable<bool> AllowLateJoin => _allowLateJoin;

        /// <summary>
        /// 名簿が変化したとき（追加・削除・状態変更のいずれでも発火）。
        ///
        /// サーバーは名簿を書き換えるたびに <c>NetworkList</c> を作り直す（冪等にするため。
        /// <c>LobbyState.Server.cs</c> の <c>SyncRosterToNetworkList</c> 参照）ので、
        /// 1 回の変更で複数回発火し、その途中では名簿が空に見えることがある。
        /// 購読側は 1 フレーム分をまとめてから読み直すこと。
        /// </summary>
        public event Action RosterChanged;

        /// <summary>
        /// 再接続で席（名簿エントリ）のクライアント ID が付け替わったとき（サーバーのみ発火、#84）。
        /// 得点・ペナルティなどの引き継ぎは <see cref="GameSession.TransferSeat"/> が行う
        /// （<c>LobbyState.Server.cs</c> の <c>NotifySeatTransferred</c> が直接呼ぶ）ので、
        /// 本イベントは診断・表示側の追従用。
        /// </summary>
        public event Action<SeatTransfer> SeatTransferred;

        /// <summary>接続中のプレイヤー数（司会専任のホストも含む。画面表示用）。</summary>
        public int ConnectedCount
        {
            get
            {
                var count = 0;
                foreach (var entry in _players)
                {
                    if (entry.IsConnected)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 指定した <see cref="NetworkManager"/> でスポーンされている <see cref="LobbyState"/> を探す。
        /// 1 プロセスに複数の <see cref="NetworkManager"/> が立つ PlayMode テストでも正しく引けるよう、
        /// 静的なインスタンス変数は持たず、都度スポーン済みリストから探す（対象は数個なので十分速い）。
        /// </summary>
        /// <param name="networkManager">対象の <see cref="NetworkManager"/>。</param>
        /// <returns>見つかった <see cref="LobbyState"/>。無ければ null。</returns>
        public static LobbyState Find(NetworkManager networkManager)
        {
            if (networkManager == null || networkManager.SpawnManager == null)
            {
                return null;
            }

            foreach (var spawned in networkManager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned == null || spawned.NetworkManager != networkManager)
                {
                    continue;
                }

                var candidate = spawned.GetComponent<LobbyState>();
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// <see cref="NetworkManager"/> に登録済みのネットワークプレハブから
        /// <see cref="LobbyState"/> を持つものを探す。
        /// </summary>
        /// <param name="networkManager">対象の <see cref="NetworkManager"/>。</param>
        /// <param name="prefab">見つかったプレハブの <see cref="NetworkObject"/>。</param>
        /// <returns>見つかったか。</returns>
        public static bool TryResolvePrefab(NetworkManager networkManager, out NetworkObject prefab)
        {
            prefab = null;

            var prefabs = networkManager?.NetworkConfig?.Prefabs;
            if (prefabs == null)
            {
                return false;
            }

            foreach (var list in prefabs.NetworkPrefabsLists)
            {
                if (list == null)
                {
                    continue;
                }

                foreach (var networkPrefab in list.PrefabList)
                {
                    var gameObject = networkPrefab?.Prefab;
                    if (gameObject == null || gameObject.GetComponent<LobbyState>() == null)
                    {
                        continue;
                    }

                    prefab = gameObject.GetComponent<NetworkObject>();
                    if (prefab != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 現在の名簿のコピーを返す（UI 表示用）。返されるリストは次の呼び出しで再利用されるため、
        /// 呼び出し側で保持しないこと。
        /// </summary>
        /// <remarks>
        /// サーバー（ホスト）では <c>NetworkList</c> ではなく名簿の権威
        /// （<see cref="LobbyRoster"/>）から組み立てる。切断中のエントリの名前は
        /// クライアントへ伏せ字で配っている（レビュー H-2）ため、ホストの画面にだけ実名を出すには
        /// サーバー側の名簿を読む必要がある。
        /// </remarks>
        public IReadOnlyList<PlayerEntry> GetPlayersSnapshot()
        {
            _snapshot.Clear();

            if (IsServer && ServerRoster != null)
            {
                foreach (var player in ServerRoster.Players)
                {
                    _snapshot.Add(PlayerEntry.FromLobbyPlayer(player));
                }

                return _snapshot;
            }

            // NetworkList はスポーン前・破棄後に触ると例外になるため、状態を確認してから読む。
            if (!IsSpawned)
            {
                return _snapshot;
            }

            foreach (var entry in _players)
            {
                _snapshot.Add(entry);
            }

            return _snapshot;
        }

        /// <inheritdoc />
        public override void OnNetworkSpawn()
        {
            _players.OnListChanged += HandleListChanged;

            if (IsServer)
            {
                InitializeServer();
            }
        }

        /// <inheritdoc />
        public override void OnNetworkDespawn()
        {
            _players.OnListChanged -= HandleListChanged;
            ShutdownServer();
        }

        /// <summary>
        /// 破棄時の後始末。<c>OnNetworkDespawn</c> を経ずに <see cref="GameObject"/> ごと
        /// 破棄される経路（シーンのアンロード、テストの後片付けなど）でも
        /// <see cref="NetworkManager"/> のコールバック購読が残らないようにする（レビュー LOW）。
        /// </summary>
        public override void OnDestroy()
        {
            ShutdownServer();
            base.OnDestroy();
        }

        /// <summary>
        /// <c>NetworkList</c> の変更通知をそのまま <see cref="RosterChanged"/> として流す。
        /// どの要素がどう変わったかは渡さない（サーバーが毎回作り直すため個々の差分に意味がない）。
        /// 参加・退出の検出と参加通知 SE（<c>SeKind.Join</c>）は、1 フレーム分をまとめた後の
        /// 差分から UI 層が行う（統括メモ #35: SE を鳴らすのは UI 層の責務）。
        /// </summary>
        private void HandleListChanged(NetworkListEvent<PlayerEntry> changeEvent) => RosterChanged?.Invoke();
    }
}
