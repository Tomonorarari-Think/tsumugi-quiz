using System.IO;
using TsumugiQuiz.Network;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TsumugiQuiz.Editor.Setup
{
    /// <summary>
    /// Boot シーンに常駐用の <c>NetworkManager</c>（+ <c>UnityTransport</c> + <see cref="NetworkBootstrap"/>）を
    /// 配置するセットアップスクリプト（docs/architecture.md §2、docs/network.md §2.1）。
    ///
    /// シーンは YAML なので手で書くと GUID を間違えやすい。再現可能な形で残すためにスクリプト化し、
    /// バッチモードから
    /// <c>-executeMethod TsumugiQuiz.Editor.Setup.NetworkSceneSetup.SetupAndExit</c> で実行する。
    /// すでに配置済みの場合は既存のオブジェクトの設定だけを整える（冪等）。
    /// </summary>
    public static class NetworkSceneSetup
    {
        private const string BootScenePath = "Assets/TsumugiQuiz/Scenes/Boot.unity";
        private const string NetworkManagerObjectName = "NetworkManager";

        /// <summary>メニューからの実行。</summary>
        [MenuItem("TsumugiQuiz/Setup/Setup Network Manager In Boot Scene")]
        public static void SetupFromMenu() => Setup();

        /// <summary>バッチモードからの実行（終了コードを返して Unity を終了する）。</summary>
        public static void SetupAndExit() => EditorApplication.Exit(Setup() ? 0 : 1);

        /// <summary>他のバッチエントリポイントから呼ぶための公開ラッパー。</summary>
        public static bool SetupPublic() => Setup();

        private static bool Setup()
        {
            if (!File.Exists(BootScenePath))
            {
                Debug.LogWarning($"[NetworkSceneSetup] Boot scene not found at {BootScenePath}. Run ProjectBootstrap first.");
                return false;
            }

            var scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);

            var networkManagerObject = FindRootObject(scene, NetworkManagerObjectName);
            if (networkManagerObject == null)
            {
                networkManagerObject = new GameObject(NetworkManagerObjectName);
                Debug.Log($"[NetworkSceneSetup] Created {NetworkManagerObjectName} GameObject in Boot scene.");
            }

            // NetworkManager は DontDestroyOnLoad をルートオブジェクトに対して行うため、
            // 親を持たせない（NetworkManagerCheckForParent）。
            networkManagerObject.transform.SetParent(null);

            var transport = GetOrAddComponent<UnityTransport>(networkManagerObject);

            // forceOverrideCommandLineArgs: true。UnityTransport 自身が持つ -port / -ip の解析を
            // 無効にして、シーンに保存される値を「アプリが決めた既定値」に固定する
            // （引数はアプリ側の NetworkRuntimeOptions が解析して NetworkService に渡す）。
            transport.SetConnectionData(
                true,
                NetworkConstants.AnyAddress,
                NetworkConstants.DefaultPort,
                NetworkConstants.AnyAddress);
            transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            var networkManager = GetOrAddComponent<NetworkManager>(networkManagerObject);
            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.RunInBackground = true;

            GetOrAddComponent<NetworkBootstrap>(networkManagerObject);

            EditorUtility.SetDirty(networkManagerObject);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, BootScenePath))
            {
                Debug.LogWarning($"[NetworkSceneSetup] Failed to save Boot scene at {BootScenePath}");
                return false;
            }

            AssetDatabase.Refresh();
            Debug.Log("[NetworkSceneSetup] Boot scene now contains NetworkManager + UnityTransport + NetworkBootstrap.");
            return true;
        }

        private static GameObject FindRootObject(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            return null;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
