using UnityEditor;
using UnityEngine;

namespace TsumugiQuiz.Editor.Setup
{
    /// <summary>
    /// バッチモードから一括セットアップ（パッケージ導入 + Boot/Main シーン登録）を行うためのエントリポイント。
    /// `-executeMethod TsumugiQuiz.Editor.Setup.SetupAll.RunAndExit` で呼び出す。
    /// </summary>
    public static class SetupAll
    {
        public static void RunAndExit()
        {
            Debug.Log("[SetupAll] Starting package installation...");
            var packagesOk = PackageInstallerRunAll();

            Debug.Log("[SetupAll] Starting scene bootstrap...");
            var scenesOk = ProjectBootstrapRunAll();

            // Boot シーンへの NetworkManager 配置（Boot / Main シーンが用意できた場合のみ）。
            var networkOk = scenesOk && NetworkSceneSetup.SetupPublic();

            // ロビーの共有状態（#7）のネットワークプレハブ。NetworkManager 配置後に登録する。
            var lobbyPrefabOk = networkOk && LobbyPrefabSetup.SetupPublic();

            Debug.Log("[SetupAll] Starting SE player bootstrap...");
            var sePlayerOk = scenesOk && SePlayerBootstrapRunAll();

            Debug.Log("[SetupAll] Starting TTS service bootstrap...");
            var ttsOk = scenesOk && TtsServiceBootstrapRunAll();

            Debug.Log("[SetupAll] Normalizing Japanese FontAsset...");
            var fontAssetOk = FontAssetSetup.SetupPublic();

            var success = packagesOk && scenesOk && networkOk && lobbyPrefabOk && sePlayerOk && ttsOk && fontAssetOk;
            Debug.Log($"[SetupAll] Done. packagesOk={packagesOk}, scenesOk={scenesOk}, networkOk={networkOk}, " +
                      $"lobbyPrefabOk={lobbyPrefabOk}, sePlayerOk={sePlayerOk}, ttsOk={ttsOk}, fontAssetOk={fontAssetOk}");
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static bool PackageInstallerRunAll()
        {
            // PackageInstaller.InstallAndExit() calls EditorApplication.Exit itself, so we
            // call the internal-equivalent logic via reflection-free direct method instead.
            return PackageInstaller.InstallAllPublic();
        }

        private static bool ProjectBootstrapRunAll()
        {
            return ProjectBootstrap.SetupPublic();
        }

        private static bool SePlayerBootstrapRunAll()
        {
            return SePlayerBootstrap.SetupPublic();
        }

        private static bool TtsServiceBootstrapRunAll()
        {
            return TtsServiceBootstrap.SetupPublic();
        }
    }
}
