using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TsumugiQuiz.Editor.Tts
{
    /// <summary>
    /// Assets/Plugins/voicevox_core/x86_64/*.dll の Plugin Inspector 設定を自動適用する
    /// （docs/tts-native-api.md §3.2、仮決め K24）。
    ///
    /// DLL 本体は git 管理外（External から scripts/setup-external.ps1 でコピー）なので、
    /// .meta も .gitignore の対象になり、プラグイン設定を git で共有できない。
    /// そこで .meta をコミットする代わりに、インポート時に PluginImporter をコードで設定する。
    /// こうすると DLL を配置しただけで誰の環境でも同じ設定になる。
    ///
    /// 本スクリプトより前にインポートされていた DLL にも遡って適用するため、
    /// エディタ起動時（<see cref="ApplyToExistingPlugins"/>）に設定を確認し、違っていれば再インポートする。
    /// </summary>
    public sealed class VoicevoxPluginPostprocessor : AssetPostprocessor
    {
        internal const string PluginDirectory = "Assets/Plugins/voicevox_core/x86_64";
        private const string WindowsOs = "Windows";
        private const string X8664Cpu = "x86_64";

        private void OnPreprocessAsset()
        {
            if (!IsVoicevoxNativePlugin(assetPath)) return;
            if (!(assetImporter is PluginImporter importer)) return;

            Configure(importer);
        }

        /// <summary>対象が voicevox_core のネイティブプラグインか。</summary>
        internal static bool IsVoicevoxNativePlugin(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith(PluginDirectory + "/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Windows x86_64 のスタンドアロンとエディタでのみ有効にする。</summary>
        internal static void Configure(PluginImporter importer)
        {
            if (importer == null) throw new ArgumentNullException(nameof(importer));

            importer.SetCompatibleWithAnyPlatform(false);

            importer.SetCompatibleWithEditor(true);
            importer.SetEditorData("OS", WindowsOs);
            importer.SetEditorData("CPU", X8664Cpu);

            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", X8664Cpu);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);

            // voicevox_onnxruntime.dll は voicevox_core が自分でロードするので、起動時ロードはしない。
            importer.isPreloaded = false;
        }

        /// <summary>
        /// <see cref="Configure"/> の結果になっているか。
        /// 再インポートのループを避けるため、プラットフォームの有効・無効だけを見る。
        /// </summary>
        internal static bool IsConfigured(PluginImporter importer)
        {
            if (importer == null) throw new ArgumentNullException(nameof(importer));

            return !importer.GetCompatibleWithAnyPlatform()
                && importer.GetCompatibleWithEditor()
                && importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64)
                && !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows)
                && !importer.isPreloaded;
        }

        /// <summary>
        /// 既にインポート済みの DLL に設定を遡及適用する。
        /// ドメインロード中に再インポートを始めないよう、エディタの次の更新に遅延させる。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ApplyToExistingPlugins()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (!AssetDatabase.IsValidFolder(PluginDirectory)) return;

                    foreach (var file in Directory.GetFiles(PluginDirectory, "*.dll", SearchOption.TopDirectoryOnly))
                    {
                        var assetPath = file.Replace('\\', '/');
                        if (!(AssetImporter.GetAtPath(assetPath) is PluginImporter importer)) continue;
                        if (IsConfigured(importer)) continue;

                        Configure(importer);
                        importer.SaveAndReimport();
                        Debug.Log($"[voicevox] プラグイン設定を適用しました: {assetPath}");
                    }
                }
                catch (IOException e)
                {
                    Debug.LogWarning($"[voicevox] プラグイン設定の遡及適用に失敗しました: {e.Message}");
                }
            };
        }
    }
}
