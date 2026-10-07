using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace TsumugiQuiz.Editor.Setup
{
    /// <summary>
    /// セットアップ時に必要なパッケージ（Netcode for GameObjects / Transport / Newtonsoft Json）を
    /// Package Manager 経由で解決・追加するための一時的な導入スクリプト。
    /// バッチモードから `-executeMethod TsumugiQuiz.Editor.Setup.PackageInstaller.InstallAndExit` で実行する。
    /// </summary>
    public static class PackageInstaller
    {
        private static readonly string[] RequiredPackages =
        {
            "com.unity.netcode.gameobjects",
            "com.unity.transport",
            "com.unity.nuget.newtonsoft-json",
        };

        [MenuItem("TsumugiQuiz/Setup/Install Required Packages")]
        public static void InstallFromMenu()
        {
            InstallAll();
        }

        /// <summary>
        /// バッチモード実行用エントリポイント。全パッケージ解決後にエディタを終了する。
        /// </summary>
        public static void InstallAndExit()
        {
            var ok = InstallAll();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>SetupAll など他のバッチエントリポイントから呼び出すための公開ラッパー。</summary>
        public static bool InstallAllPublic() => InstallAll();

        private static bool InstallAll()
        {
            var success = true;
            foreach (var packageId in RequiredPackages)
            {
                if (!AddAndWait(packageId))
                {
                    success = false;
                }
            }

            return success;
        }

        private static bool AddAndWait(string packageId)
        {
            Debug.Log($"[PackageInstaller] Adding package: {packageId}");
            AddRequest request = Client.Add(packageId);

            while (!request.IsCompleted)
            {
                Thread.Sleep(200);
            }

            if (request.Status == StatusCode.Success)
            {
                Debug.Log($"[PackageInstaller] Resolved {request.Result.packageId} (version {request.Result.version})");
                return true;
            }

            if (request.Status >= StatusCode.Failure)
            {
                Debug.LogError($"[PackageInstaller] Failed to add {packageId}: {request.Error?.message}");
                return false;
            }

            return true;
        }
    }
}
