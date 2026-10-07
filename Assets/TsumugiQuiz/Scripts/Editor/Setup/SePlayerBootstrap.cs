using System.Collections.Generic;
using System.IO;
using TsumugiQuiz.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TsumugiQuiz.Editor.Setup
{
    /// <summary>
    /// Boot シーンに SE 再生用の <see cref="SePlayer"/> を常駐配置する一時的なセットアップスクリプト。
    /// AudioClip の参照は Inspector 相当（SerializedObject 経由）でしか埋め込めないため、
    /// UiToolkitBootstrap と同様にバッチモードから
    /// `-executeMethod TsumugiQuiz.Editor.Setup.SePlayerBootstrap.RunAndExit` で実行する。
    /// scripts/gen-se.py で wav を再生成した場合や、SE の追加/削除を行った場合は再実行すること。
    /// </summary>
    public static class SePlayerBootstrap
    {
        private const string BootScenePath = "Assets/TsumugiQuiz/Scenes/Boot.unity";
        private const string SePlayerGameObjectName = "SePlayer";
        private const float DefaultVolume = 0.8f;

        [MenuItem("TsumugiQuiz/Setup/Setup SE Player")]
        public static void SetupFromMenu() => Setup();

        /// <summary>バッチモード実行用エントリポイント。</summary>
        public static void RunAndExit()
        {
            var ok = Setup();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>SetupAll など他のバッチエントリポイントから呼び出すための公開ラッパー。</summary>
        public static bool SetupPublic() => Setup();

        private static bool Setup()
        {
            try
            {
                if (!ConfigureAudioImportSettings())
                {
                    return false;
                }

                return WireBootScene();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SePlayerBootstrap] セットアップ中に例外が発生しました: {ex}");
                return false;
            }
        }

        /// <summary>
        /// SE の wav は無圧縮 PCM・常時プリロードで扱う（M1）。効果音は短く再生頻度が高いため、
        /// 圧縮（既定は Vorbis）による再生開始レイテンシや音質劣化を避け、都度ディスクから
        /// ストリーミングするのではなく事前にメモリへ展開しておく。
        /// </summary>
        private static bool ConfigureAudioImportSettings()
        {
            foreach (var kind in SeAssetPaths.AllKinds)
            {
                var path = SeAssetPaths.GetAssetPath(kind);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                {
                    Debug.LogError($"[SePlayerBootstrap] '{kind}' の AudioImporter を取得できません: {path}。" +
                        "scripts/gen-se.py を実行して wav を生成してください。");
                    return false;
                }

                var settings = importer.defaultSampleSettings;
                var changed = false;

                if (settings.compressionFormat != AudioCompressionFormat.PCM)
                {
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    changed = true;
                }

                // Unity 6 では preloadAudioData は AudioImporter の直接プロパティではなく、
                // AudioImporterSampleSettings（defaultSampleSettings）側のフィールドに移動している。
                if (!settings.preloadAudioData)
                {
                    settings.preloadAudioData = true;
                    changed = true;
                }

                if (changed)
                {
                    importer.defaultSampleSettings = settings;
                    EditorUtility.SetDirty(importer);
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    Debug.Log($"[SePlayerBootstrap] '{kind}' の AudioImporter 設定を更新しました（PCM / preloadAudioData=true）: {path}");
                }
            }

            return true;
        }

        private static bool WireBootScene()
        {
            if (!File.Exists(BootScenePath))
            {
                Debug.LogError($"[SePlayerBootstrap] Boot scene が見つかりません: {BootScenePath}");
                return false;
            }

            var scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);

            var sePlayerGameObject = FindRootGameObject(scene, SePlayerGameObjectName);
            if (sePlayerGameObject == null)
            {
                sePlayerGameObject = new GameObject(SePlayerGameObjectName);
            }

            var audioSource = sePlayerGameObject.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = sePlayerGameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f; // SE は 2D 再生（3D 位置に依存しない）
            audioSource.loop = false;

            // Boot シーン単独（Main へ遷移する前や、Boot シーンだけを読み込むテスト等）で
            // AudioListener が1つも存在しないケースのフォールバックとして同居させる。
            // 実際に有効化されるかどうかは SePlayer.Awake が実行時に判定する
            // （他に有効な AudioListener があれば無効化し、重複警告を避ける。M2）。
            if (sePlayerGameObject.GetComponent<AudioListener>() == null)
            {
                sePlayerGameObject.AddComponent<AudioListener>();
            }

            var sePlayer = sePlayerGameObject.GetComponent<SePlayer>();
            if (sePlayer == null)
            {
                sePlayer = sePlayerGameObject.AddComponent<SePlayer>();
            }

            if (!TryPopulateClips(sePlayer))
            {
                return false;
            }

            EditorUtility.SetDirty(sePlayerGameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            var saved = EditorSceneManager.SaveScene(scene);
            if (!saved)
            {
                Debug.LogError("[SePlayerBootstrap] Boot シーンの保存に失敗しました。");
                return false;
            }

            Debug.Log("[SePlayerBootstrap] Boot シーンに SePlayer を配置しました。");
            return true;
        }

        private static bool TryPopulateClips(SePlayer sePlayer)
        {
            var serializedPlayer = new SerializedObject(sePlayer);

            var volumeProp = serializedPlayer.FindProperty("_volume");
            if (volumeProp == null)
            {
                Debug.LogError("[SePlayerBootstrap] SePlayer の _volume フィールドが見つかりません。");
                return false;
            }

            // 既に（Inspector 等で）音量が設定済みの場合は上書きしない。float フィールドの
            // 既定値である 0 のとき（＝新規追加直後で未設定）のみ、デフォルト音量を適用する（M5）。
            if (volumeProp.floatValue <= 0f)
            {
                volumeProp.floatValue = DefaultVolume;
            }

            var clipsProp = serializedPlayer.FindProperty("_clips");
            if (clipsProp == null)
            {
                Debug.LogError("[SePlayerBootstrap] SePlayer の _clips フィールドが見つかりません。");
                return false;
            }

            var kinds = new List<SeKind>(SeAssetPaths.AllKinds);
            clipsProp.arraySize = kinds.Count;
            for (var i = 0; i < kinds.Count; i++)
            {
                var kind = kinds[i];
                var path = SeAssetPaths.GetAssetPath(kind);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    Debug.LogError($"[SePlayerBootstrap] '{kind}' の AudioClip を読み込めません: {path}。" +
                        "scripts/gen-se.py を実行して wav を生成してください。");
                    return false;
                }

                var element = clipsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_kind").enumValueIndex = (int)kind;
                element.FindPropertyRelative("_clip").objectReferenceValue = clip;
            }

            serializedPlayer.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static GameObject FindRootGameObject(Scene scene, string name)
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
    }
}
