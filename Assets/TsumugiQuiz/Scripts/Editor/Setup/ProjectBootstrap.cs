using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TsumugiQuiz.Editor.Setup
{
    /// <summary>
    /// Boot / Main シーンの用意と EditorBuildSettings への登録（Boot → Main の順）を行う
    /// 一時的なセットアップスクリプト。バッチモードから
    /// `-executeMethod TsumugiQuiz.Editor.Setup.ProjectBootstrap.SetupAndExit` で実行する。
    /// </summary>
    public static class ProjectBootstrap
    {
        private const string BootScenePath = "Assets/TsumugiQuiz/Scenes/Boot.unity";
        private const string MainScenePath = "Assets/TsumugiQuiz/Scenes/Main.unity";

        [MenuItem("TsumugiQuiz/Setup/Setup Boot And Main Scenes")]
        public static void SetupFromMenu()
        {
            Setup();
        }

        public static void SetupAndExit()
        {
            var ok = Setup();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>SetupAll など他のバッチエントリポイントから呼び出すための公開ラッパー。</summary>
        public static bool SetupPublic() => Setup();

        private static bool Setup()
        {
            if (!File.Exists(MainScenePath))
            {
                Debug.LogError($"[ProjectBootstrap] Main scene not found at {MainScenePath}. Move SampleScene.unity there first.");
                return false;
            }

            if (!File.Exists(BootScenePath))
            {
                var bootScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var saved = EditorSceneManager.SaveScene(bootScene, BootScenePath);
                if (!saved)
                {
                    Debug.LogError($"[ProjectBootstrap] Failed to save Boot scene at {BootScenePath}");
                    return false;
                }

                Debug.Log($"[ProjectBootstrap] Created empty Boot scene at {BootScenePath}");
            }
            else
            {
                Debug.Log($"[ProjectBootstrap] Boot scene already exists at {BootScenePath}");
            }

            AssetDatabase.Refresh();

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(MainScenePath, true),
            };

            Debug.Log("[ProjectBootstrap] EditorBuildSettings updated: Boot -> Main");
            return true;
        }
    }
}
