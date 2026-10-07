using System;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/>（<see cref="QuestionDistributor"/> を含むネットワークプレハブ）の
    /// 探索・スポーン・Despawn を担う（#13、docs/network.md §8.6）。
    /// 接続管理（<see cref="NetworkService"/>）とゲーム進行の器の生成を分けるために切り出している。
    /// </summary>
    /// <remarks>
    /// プレハブ参照はこのクラスにも持たせず、<see cref="NetworkManager"/> に登録済みの
    /// ネットワークプレハブ（実アプリでは <c>Assets/DefaultNetworkPrefabs.asset</c>）から探す。
    /// 登録を唯一の出所にすることで、ホスト・クライアントで別のプレハブを使う事故を防ぐ
    /// （<c>NetworkConfig.ForceSamePrefabs</c> が既定 true）。
    /// </remarks>
    internal sealed class GameSessionSpawner
    {
        private readonly NetworkManager _networkManager;

        /// <summary>
        /// スポーナーを生成する。
        /// </summary>
        /// <param name="networkManager">対象の <see cref="NetworkManager"/>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="networkManager"/> が null のとき。</exception>
        public GameSessionSpawner(NetworkManager networkManager)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        }

        /// <summary>スポーン済みの <see cref="GameSession"/>。未スポーンなら null。</summary>
        public GameSession ActiveGameSession { get; private set; }

        /// <summary>
        /// ホスト（サーバー）側で <see cref="GameSession"/> をスポーンする。冪等。
        /// </summary>
        /// <returns>スポーンした <see cref="GameSession"/>。できなかった場合は null。</returns>
        public GameSession Spawn()
        {
            if (!_networkManager.IsListening || !_networkManager.IsServer)
            {
                Debug.LogWarning("[GameSessionSpawner] GameSession はホスト（サーバー）でのみスポーンできます。");
                return null;
            }

            if (ActiveGameSession != null && ActiveGameSession.IsSpawned)
            {
                return ActiveGameSession;
            }

            var prefab = FindGameSessionPrefab();
            if (prefab == null)
            {
                // ネットワークプレハブ未登録。実アプリでは Assets/DefaultNetworkPrefabs.asset に
                // GameSession プレハブを登録しておく必要がある。
                Debug.LogWarning(
                    "[GameSessionSpawner] GameSession のネットワークプレハブが登録されていないためスポーンしません。");
                return null;
            }

            var spawned = _networkManager.SpawnManager.InstantiateAndSpawn(prefab);
            if (spawned == null)
            {
                Debug.LogWarning("[GameSessionSpawner] GameSession をスポーンできませんでした。");
                return null;
            }

            ActiveGameSession = spawned.GetComponent<GameSession>();
            return ActiveGameSession;
        }

        /// <summary>
        /// スポーン済みの <see cref="GameSession"/> を Despawn する。停止済み・未スポーンなら参照だけを捨てる。
        /// </summary>
        public void Despawn()
        {
            var session = ActiveGameSession;
            ActiveGameSession = null;
            if (session == null)
            {
                return;
            }

            var networkObject = session.NetworkObject;
            if (networkObject == null || !networkObject.IsSpawned)
            {
                return;
            }

            if (_networkManager.IsListening && _networkManager.IsServer)
            {
                networkObject.Despawn();
            }
        }

        /// <summary>
        /// スポーン済みの <see cref="GameSession"/> を探す（#14、docs/network.md §1.2「実装済みの
        /// <c>NetworkVariable</c>」）。ホストは <see cref="Spawn"/> でキャッシュした
        /// <see cref="ActiveGameSession"/> を返す。クライアントは <see cref="Spawn"/> を呼ばない
        /// （ホストのみが呼ぶ API）ため、NGO の同期でスポーンされた <see cref="NetworkObject"/> の
        /// 一覧から探す（テストの <c>GameSessionTestFixture.TryFindSession</c> と同じ手順）。
        /// </summary>
        /// <returns>見つかった <see cref="GameSession"/>。無ければ null。</returns>
        /// <remarks>
        /// レビュー M-9: 停止処理の途中（<c>NetworkManager.Shutdown()</c> 後、内部状態が破棄されつつある
        /// 瞬間）に呼ばれると、NGO 内部のネイティブコレクションが解放済みで
        /// <see cref="ObjectDisposedException"/> が飛ぶことがある。UI 層（<c>GameView</c>）が
        /// 定期的に再探索するため、ここで握りつぶして null を返す（例外を外へ伝播させない）。
        /// </remarks>
        public GameSession FindGameSession()
        {
            if (ActiveGameSession != null && ActiveGameSession.IsSpawned)
            {
                return ActiveGameSession;
            }

            try
            {
                var spawnManager = _networkManager.SpawnManager;
                if (spawnManager == null)
                {
                    return null;
                }

                foreach (var spawnedObject in spawnManager.SpawnedObjectsList)
                {
                    if (spawnedObject == null || spawnedObject.NetworkManager != _networkManager)
                    {
                        continue;
                    }

                    var candidate = spawnedObject.GetComponent<GameSession>();
                    if (candidate != null)
                    {
                        return candidate;
                    }
                }

                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }

        /// <summary>登録済みのネットワークプレハブから <see cref="GameSession"/> を持つものを探す。</summary>
        private NetworkObject FindGameSessionPrefab()
        {
            var prefabs = _networkManager.NetworkConfig?.Prefabs?.Prefabs;
            if (prefabs == null)
            {
                return null;
            }

            for (var i = 0; i < prefabs.Count; i++)
            {
                var candidate = prefabs[i]?.Prefab;
                if (candidate == null || candidate.GetComponent<GameSession>() == null)
                {
                    continue;
                }

                var networkObject = candidate.GetComponent<NetworkObject>();
                if (networkObject != null)
                {
                    return networkObject;
                }
            }

            return null;
        }
    }
}
