using System.Reflection;
using NUnit.Framework;
using TsumugiQuiz.Network;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// PlayMode テストから実プレハブ資産を読み込むヘルパー。
    /// PlayMode テストは Editor 上でのみ実行するため <c>AssetDatabase</c> を使う。
    /// </summary>
    internal static class NetworkTestPrefabs
    {
        /// <summary>ゲーム進行・問題配信のネットワークプレハブ（#13）。</summary>
        public const string GameSessionPrefabPath = "Assets/TsumugiQuiz/Prefabs/GameSession.prefab";

        /// <summary>ロビーの共有状態のネットワークプレハブ（#7）。</summary>
        public const string LobbyStatePrefabPath = "Assets/TsumugiQuiz/Prefabs/LobbyState.prefab";

        /// <summary>
        /// <c>LobbyState</c> プレハブを読み込み、必要なコンポーネントが載っていることを確かめる。
        /// </summary>
        /// <returns>プレハブ（資産なので破棄してはいけない）。</returns>
        public static GameObject LoadLobbyState()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(LobbyStatePrefabPath);
            Assert.IsNotNull(prefab, $"{LobbyStatePrefabPath} が見つかりません。");

            var networkObject = prefab.GetComponent<NetworkObject>();
            Assert.IsNotNull(networkObject, "LobbyState プレハブに NetworkObject が必要。");
            Assert.IsNotNull(prefab.GetComponent<LobbyState>(), "LobbyState プレハブに LobbyState が必要。");
            Assert.AreNotEqual(
                0u,
                ReadPrefabHash(networkObject),
                "LobbyState プレハブの GlobalObjectIdHash が 0 です（TsumugiQuiz/Setup/Setup Lobby Prefab を実行してください）。");
            return prefab;
#else
            Assert.Ignore("LobbyState プレハブの読み込みは Editor でのみ行える。");
            return null;
#endif
        }

        /// <summary><c>NetworkObject.GlobalObjectIdHash</c>（internal）を読む。</summary>
        private static uint ReadPrefabHash(NetworkObject networkObject)
        {
            var field = typeof(NetworkObject).GetField(
                "GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "NetworkObject.GlobalObjectIdHash が見つかりません（NGO のバージョン差異）。");
            return (uint)field.GetValue(networkObject);
        }

        /// <summary>
        /// <c>GameSession</c> プレハブを読み込み、必要なコンポーネントが載っていることを確かめる。
        /// </summary>
        /// <returns>プレハブ（資産なので破棄してはいけない）。</returns>
        public static GameObject LoadGameSession()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GameSessionPrefabPath);
            Assert.IsNotNull(prefab, $"{GameSessionPrefabPath} が見つかりません。");
            Assert.IsNotNull(prefab.GetComponent<NetworkObject>(), "GameSession プレハブに NetworkObject が必要。");
            Assert.IsNotNull(prefab.GetComponent<GameSession>(), "GameSession プレハブに GameSession が必要。");
            Assert.IsNotNull(
                prefab.GetComponent<QuestionDistributor>(), "GameSession プレハブに QuestionDistributor が必要。");
            Assert.IsNotNull(
                prefab.GetComponent<RoomSettingsSync>(), "GameSession プレハブに RoomSettingsSync が必要（#27）。");
            return prefab;
#else
            Assert.Ignore("GameSession プレハブの読み込みは Editor でのみ行える。");
            return null;
#endif
        }

        /// <summary>
        /// <c>GameSession</c> + <c>QuestionDistributor</c> を載せたネットワークプレハブ相当の
        /// GameObject を実行時に作る。コンポーネントの並び順（＝ <c>NetworkBehaviour</c> の
        /// インデックスと tick 購読順）を入れ替えた構成を検証するために使う。
        /// </summary>
        /// <param name="name">GameObject 名。</param>
        /// <param name="prefabHash">固定する <c>GlobalObjectIdHash</c>（実資産と衝突しない非 0 値）。</param>
        /// <param name="distributorFirst">true なら <c>QuestionDistributor</c> を先に付ける。</param>
        /// <returns>作ったプレハブ相当の GameObject（呼び出し側が破棄する）。</returns>
        public static GameObject CreateRuntimeGameSessionPrefab(string name, uint prefabHash, bool distributorFirst)
        {
            var prefab = new GameObject(name);
            var networkObject = prefab.AddComponent<NetworkObject>();

            if (distributorFirst)
            {
                prefab.AddComponent<QuestionDistributor>();
                prefab.AddComponent<GameSession>();
            }
            else
            {
                prefab.AddComponent<GameSession>();
                if (prefab.GetComponent<QuestionDistributor>() == null)
                {
                    prefab.AddComponent<QuestionDistributor>();
                }
            }

            ForcePrefabHash(networkObject, prefabHash);
            return prefab;
        }

        /// <summary>
        /// 実行時に作った <see cref="NetworkObject"/> に固定の <c>GlobalObjectIdHash</c> を与える。
        /// Editor の <c>OnValidate</c> は再生中のシーン上オブジェクトには値を振らないため、
        /// NGO 自身の統合テストヘルパーと同じくここで直接設定する（当該フィールドは internal）。
        /// </summary>
        private static void ForcePrefabHash(NetworkObject networkObject, uint hash)
        {
            var field = typeof(NetworkObject).GetField(
                "GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "NetworkObject.GlobalObjectIdHash が見つかりません（NGO のバージョン差異）。");
            field.SetValue(networkObject, hash);
        }
    }
}
