using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Editor.Tts;
using TsumugiQuiz.Tts;
using UnityEditor;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// ネイティブプラグインの Plugin Inspector 設定（docs/tts-native-api.md §3.2）の検証。
    /// .meta を git 管理しない代わりに <see cref="VoicevoxPluginPostprocessor"/> が設定するので、
    /// 「DLL を配置したら Windows x86_64 のみ有効になっている」ことをここで担保する。
    /// </summary>
    public sealed class VoicevoxPluginImporterTests
    {
        private const string CoreDllAssetPath = VoicevoxPluginPostprocessor.PluginDirectory + "/" + VoicevoxPaths.CoreDllFileName;
        private const string OnnxruntimeDllAssetPath = VoicevoxPluginPostprocessor.PluginDirectory + "/" + VoicevoxPaths.OnnxruntimeDllFileName;

        [TestCase(CoreDllAssetPath)]
        [TestCase(OnnxruntimeDllAssetPath)]
        public void 配置済みDLLはWindowsのx86_64とエディタでのみ有効になる(string assetPath)
        {
            if (!File.Exists(assetPath))
            {
                Assert.Ignore($"{assetPath} が未配置のためスキップします（scripts/setup-external.ps1 を実行してください）。");
            }

            Assert.That(VoicevoxPluginPostprocessor.IsVoicevoxNativePlugin(assetPath), Is.True,
                "対象パスがネイティブプラグインとして認識されること");

            var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            Assert.That(importer, Is.Not.Null, "PluginImporter として取得できること");

            Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False, "Any Platform は無効");
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), Is.True, "Windows x86_64 は有効");
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows), Is.False, "Windows x86 は無効");
            Assert.That(importer.GetCompatibleWithEditor(), Is.True, "Editor は有効");
            Assert.That(importer.isPreloaded, Is.False, "Load on startup は無効");
            Assert.That(VoicevoxPluginPostprocessor.IsConfigured(importer), Is.True);
        }

        [TestCase("Assets/Plugins/voicevox_core/x86_64/voicevox_core.dll", true)]
        [TestCase("Assets/Plugins/voicevox_core/x86_64/voicevox_onnxruntime.dll", true)]
        [TestCase("Assets/Plugins/voicevox_core/x86_64/readme.txt", false)]
        [TestCase("Assets/Plugins/other/x86_64/foo.dll", false)]
        [TestCase("Assets/Plugins/voicevox_core/x86_64", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsVoicevoxNativePlugin_対象パスだけを認識する(string path, bool expected)
        {
            Assert.That(VoicevoxPluginPostprocessor.IsVoicevoxNativePlugin(path), Is.EqualTo(expected));
        }
    }
}
