using System;
using System.IO;
using TsumugiQuiz.Tts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TsumugiQuiz.Editor.Setup
{
    /// <summary>
    /// Boot シーンに読み上げ用の <see cref="TtsService"/> を常駐配置するセットアップスクリプト
    /// （#35 の <c>SePlayerBootstrap</c> と同じ作法）。
    ///
    /// バッチモードから
    /// <c>-executeMethod TsumugiQuiz.Editor.Setup.TtsServiceBootstrap.RunAndExit</c> で実行するか、
    /// メニュー <c>TsumugiQuiz/Setup/Setup TTS Service</c> から実行する。
    /// <see cref="SetupAll"/> からも呼ばれる。
    /// </summary>
    public static class TtsServiceBootstrap
    {
        private const string BootScenePath = "Assets/TsumugiQuiz/Scenes/Boot.unity";
        private const string TtsServiceGameObjectName = "TtsService";

        [MenuItem("TsumugiQuiz/Setup/Setup TTS Service")]
        public static void SetupFromMenu() => Setup();

        /// <summary>バッチモード実行用エントリポイント。</summary>
        public static void RunAndExit()
        {
            var ok = Setup();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary><see cref="SetupAll"/> など他のバッチエントリポイントから呼び出すための公開ラッパー。</summary>
        public static bool SetupPublic() => Setup();

        private static bool Setup()
        {
            try
            {
                return WireBootScene();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TtsServiceBootstrap] セットアップ中に例外が発生しました: {ex}");
                return false;
            }
        }

        private static bool WireBootScene()
        {
            if (!File.Exists(BootScenePath))
            {
                Debug.LogError($"[TtsServiceBootstrap] Boot scene が見つかりません: {BootScenePath}");
                return false;
            }

            var scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);

            var gameObject = FindRootGameObject(scene, TtsServiceGameObjectName) ??
                             new GameObject(TtsServiceGameObjectName);

            // TtsService は DontDestroyOnLoad を自分で行うので、NetworkManager とは別の
            // ルートオブジェクトに乗せる（親を持つと DontDestroyOnLoad が効かない）。
            if (gameObject.transform.parent != null)
            {
                gameObject.transform.SetParent(null, worldPositionStays: false);
            }

            var service = gameObject.GetComponent<TtsService>();
            if (service == null)
            {
                service = gameObject.AddComponent<TtsService>();
            }

            if (!TryConfigure(service))
            {
                return false;
            }

            EditorUtility.SetDirty(gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("[TtsServiceBootstrap] Boot シーンの保存に失敗しました。");
                return false;
            }

            Debug.Log("[TtsServiceBootstrap] Boot シーンに TtsService を配置しました。");
            return true;
        }

        /// <summary>
        /// Inspector 相当（SerializedObject 経由）で設定を書き込む。
        ///
        /// <c>_initializeOnAwake</c> は <b>false</b> にする。voicevox_core の初期化は
        /// ONNX Runtime をプロセス全体にロードする副作用があり、Boot シーンを読み込むだけの
        /// 他のテスト・ツールにまで影響するため、読み上げを使う画面の起動シーケンス（#23）から
        /// <c>TtsService.EnsureInitializedAsync()</c> を明示的に呼ぶ方針にしている（docs/tts.md §6.5）。
        /// </summary>
        private static bool TryConfigure(TtsService service)
        {
            var serialized = new SerializedObject(service);

            var initializeOnAwake = serialized.FindProperty("_initializeOnAwake");
            if (initializeOnAwake == null)
            {
                Debug.LogError("[TtsServiceBootstrap] TtsService の _initializeOnAwake フィールドが見つかりません。");
                return false;
            }

            initializeOnAwake.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static GameObject FindRootGameObject(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
            }

            return null;
        }
    }
}
