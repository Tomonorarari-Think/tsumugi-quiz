using System.IO;
using System.Reflection;
using TsumugiQuiz.Network;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace TsumugiQuiz.Editor.Setup
{
    /// <summary>
    /// ロビーの共有状態（<see cref="LobbyState"/>）のネットワークプレハブを作り、
    /// <c>Assets/DefaultNetworkPrefabs.asset</c>（Boot シーンの <c>NetworkManager</c> が参照している
    /// ネットワークプレハブ一覧）へ登録するセットアップスクリプト（issue #7）。
    ///
    /// プレハブの <c>GlobalObjectIdHash</c> は Editor が自動で振るため、YAML を手書きせずに
    /// スクリプトから生成する。バッチモードからは
    /// <c>-executeMethod TsumugiQuiz.Editor.Setup.LobbyPrefabSetup.RunAndExit</c> で実行する。
    /// すでに作成・登録済みなら何もしない（冪等）。
    /// </summary>
    public static class LobbyPrefabSetup
    {
        private const string PrefabFolder = "Assets/TsumugiQuiz/Prefabs";
        private const string PrefabPath = PrefabFolder + "/LobbyState.prefab";
        private const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

        [MenuItem("TsumugiQuiz/Setup/Setup Lobby Prefab")]
        public static void SetupFromMenu() => Setup();

        /// <summary>バッチモード実行用エントリポイント。</summary>
        public static void RunAndExit() => EditorApplication.Exit(Setup() ? 0 : 1);

        /// <summary>SetupAll など他のバッチエントリポイントから呼ぶための公開ラッパー。</summary>
        public static bool SetupPublic() => Setup();

        private static bool Setup()
        {
            try
            {
                var prefab = CreateOrLoadPrefab();
                if (prefab == null)
                {
                    return false;
                }

                if (!EnsureGlobalObjectIdHash(prefab))
                {
                    return false;
                }

                var registered = RegisterInNetworkPrefabsList(prefab);

                // NetworkObject.OnValidate が振り直した GlobalObjectIdHash を必ずディスクへ書き出す
                // （登録が既に済んでいて RegisterInNetworkPrefabsList が何も保存しない場合に備える）。
                AssetDatabase.SaveAssets();
                return registered;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LobbyPrefabSetup] セットアップ中に例外が発生しました: {ex}");
                return false;
            }
        }

        private static GameObject CreateOrLoadPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                // コンポーネントが欠けていたら（手作業での編集など）補う。
                var changed = false;
                if (existing.GetComponent<NetworkObject>() == null)
                {
                    existing.AddComponent<NetworkObject>();
                    changed = true;
                }

                if (existing.GetComponent<LobbyState>() == null)
                {
                    existing.AddComponent<LobbyState>();
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SavePrefabAsset(existing);
                    Debug.Log($"[LobbyPrefabSetup] 既存のプレハブに不足コンポーネントを追加しました: {PrefabPath}");
                }
                else
                {
                    Debug.Log($"[LobbyPrefabSetup] 既存のプレハブを使用します: {PrefabPath}");
                }

                return existing;
            }

            if (!Directory.Exists(PrefabFolder))
            {
                Directory.CreateDirectory(PrefabFolder);
                AssetDatabase.Refresh();
            }

            var temporary = new GameObject("LobbyState");
            try
            {
                temporary.AddComponent<NetworkObject>();
                temporary.AddComponent<LobbyState>();

                var created = PrefabUtility.SaveAsPrefabAsset(temporary, PrefabPath, out var success);
                if (!success || created == null)
                {
                    Debug.LogError($"[LobbyPrefabSetup] プレハブの保存に失敗しました: {PrefabPath}");
                    return null;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[LobbyPrefabSetup] プレハブを作成しました: {PrefabPath}");

                // ImportAsset のあとは新しいインスタンスを読み直す（GlobalObjectIdHash 確定後の参照を使う）。
                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(temporary);
            }
        }

        /// <summary>
        /// プレハブの <c>GlobalObjectIdHash</c>（NGO がネットワークプレハブを識別するキー）が
        /// 0 のまま保存されていないことを保証する。
        ///
        /// この値は <c>NetworkObject.OnValidate</c>（internal）が Editor 上で振るが、
        /// バッチモードでプレハブを新規作成した直後は「メモリ上では振られたが未保存」になりうる。
        /// 0 のまま実行すると NGO が「GlobalObjectIdHash value of 0」エラーを出してスポーンに失敗するため、
        /// ここで明示的に振り直してから保存する。
        /// </summary>
        private static bool EnsureGlobalObjectIdHash(GameObject prefab)
        {
            var networkObject = prefab.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError($"[LobbyPrefabSetup] プレハブに NetworkObject がありません: {PrefabPath}");
                return false;
            }

            var field = typeof(NetworkObject).GetField(
                "GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                Debug.LogError("[LobbyPrefabSetup] NetworkObject.GlobalObjectIdHash が見つかりません（NGO のバージョン差異）。");
                return false;
            }

            if ((uint)field.GetValue(networkObject) == 0u)
            {
                var onValidate = typeof(NetworkObject).GetMethod(
                    "OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
                onValidate?.Invoke(networkObject, null);
            }

            EditorUtility.SetDirty(networkObject);
            AssetDatabase.SaveAssets();

            var hash = (uint)field.GetValue(networkObject);
            if (hash == 0u)
            {
                Debug.LogError(
                    $"[LobbyPrefabSetup] GlobalObjectIdHash を確定できませんでした: {PrefabPath}。" +
                    "Unity Editor でプレハブを開いて保存し直してください。");
                return false;
            }

            Debug.Log($"[LobbyPrefabSetup] GlobalObjectIdHash={hash}: {PrefabPath}");
            return true;
        }

        private static bool RegisterInNetworkPrefabsList(GameObject prefab)
        {
            var prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            if (prefabsList == null)
            {
                Debug.LogError($"[LobbyPrefabSetup] ネットワークプレハブ一覧が見つかりません: {NetworkPrefabsListPath}");
                return false;
            }

            if (prefabsList.Contains(prefab))
            {
                Debug.Log($"[LobbyPrefabSetup] すでに登録済みです: {PrefabPath}");
                return true;
            }

            prefabsList.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(prefabsList);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LobbyPrefabSetup] {NetworkPrefabsListPath} に登録しました: {PrefabPath}");
            return true;
        }
    }
}
