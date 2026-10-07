using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>ネイティブ・辞書・音声モデルの配置解決（仮決め K24）の検証。</summary>
    public sealed class VoicevoxPathsTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "tsumugi-quiz-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
        }

        [Test]
        public void ResolveNativeDir_エディタではAssetsPluginsの下()
        {
            var dir = VoicevoxPaths.ResolveNativeDir(@"C:\proj\Assets", isEditor: true);

            Assert.That(dir, Is.EqualTo(Path.Combine(@"C:\proj\Assets", "Plugins", "voicevox_core", "x86_64")));
        }

        [Test]
        public void ResolveNativeDir_ビルド後はDataPluginsx86_64()
        {
            var dir = VoicevoxPaths.ResolveNativeDir(@"C:\game\TsumugiQuiz_Data", isEditor: false);

            Assert.That(dir, Is.EqualTo(Path.Combine(@"C:\game\TsumugiQuiz_Data", "Plugins", "x86_64")));
        }

        [Test]
        public void ResolveNativeDir_オーバーライドが最優先()
        {
            var dir = VoicevoxPaths.ResolveNativeDir(@"C:\proj\Assets", isEditor: true, nativeDirOverride: @"D:\external\x86_64");

            Assert.That(dir, Is.EqualTo(@"D:\external\x86_64"));
        }

        [Test]
        public void ResolveNativeDir_dataPathが空なら例外()
        {
            Assert.Throws<ArgumentException>(() => VoicevoxPaths.ResolveNativeDir("", isEditor: true));
        }

        [Test]
        public void ResolveAssetRoots_オーバーライドStreamingAssetsPersistentの順()
        {
            var roots = VoicevoxPaths.ResolveAssetRoots(@"C:\sa", @"C:\pd", @"D:\override");

            Assert.That(roots, Is.EqualTo(new[]
            {
                @"D:\override",
                Path.Combine(@"C:\sa", "voicevox_core"),
                Path.Combine(@"C:\pd", "voicevox_core"),
            }));
        }

        [Test]
        public void ResolveAssetRoots_オーバーライド無指定なら2件()
        {
            var roots = VoicevoxPaths.ResolveAssetRoots(@"C:\sa", @"C:\pd");

            Assert.That(roots.Count, Is.EqualTo(2));
        }

        [Test]
        public void FindDictionaryDir_K24の配置を見つける()
        {
            var dict = CreateDictionary(Path.Combine(_tempRoot, "voicevox_core", "open_jtalk_dic_utf_8-1.11"));

            var found = VoicevoxPaths.FindDictionaryDir(new[] { Path.Combine(_tempRoot, "voicevox_core") });

            Assert.That(found, Is.EqualTo(dict));
        }

        [Test]
        public void FindDictionaryDir_dictサブフォルダ配置も見つける()
        {
            var dict = CreateDictionary(Path.Combine(_tempRoot, "voicevox_core", "dict", "open_jtalk_dic_utf_8-1.11"));

            var found = VoicevoxPaths.FindDictionaryDir(new[] { Path.Combine(_tempRoot, "voicevox_core") });

            Assert.That(found, Is.EqualTo(dict));
        }

        [Test]
        public void FindDictionaryDir_辞書バージョンが変わっても接頭辞で見つける()
        {
            var dict = CreateDictionary(Path.Combine(_tempRoot, "voicevox_core", "open_jtalk_dic_utf_8-1.99"));

            var found = VoicevoxPaths.FindDictionaryDir(new[] { Path.Combine(_tempRoot, "voicevox_core") });

            Assert.That(found, Is.EqualTo(dict));
        }

        [Test]
        public void FindDictionaryDir_sysdicが無ければ辞書とみなさない()
        {
            Directory.CreateDirectory(Path.Combine(_tempRoot, "voicevox_core", "open_jtalk_dic_utf_8-1.11"));

            var found = VoicevoxPaths.FindDictionaryDir(new[] { Path.Combine(_tempRoot, "voicevox_core") });

            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindVoiceModelFiles_modelsvvms配下を再帰的に見つける()
        {
            var vvm = CreateFile(Path.Combine(_tempRoot, "voicevox_core", "models", "vvms", "0.vvm"));

            var found = VoicevoxPaths.FindVoiceModelFiles(new[] { Path.Combine(_tempRoot, "voicevox_core") });

            Assert.That(found.Count, Is.EqualTo(1));
            Assert.That(found[0], Is.EqualTo(Path.GetFullPath(vvm)));
        }

        [Test]
        public void FindVoiceModelFiles_複数ルートでも重複しない()
        {
            var root = Path.Combine(_tempRoot, "voicevox_core");
            CreateFile(Path.Combine(root, "models", "0.vvm"));
            CreateFile(Path.Combine(root, "models", "vvms", "1.vvm"));

            var found = VoicevoxPaths.FindVoiceModelFiles(new[] { root, root });

            Assert.That(found.Count, Is.EqualTo(2));
            Assert.That(found.Select(Path.GetFileName), Is.EquivalentTo(new[] { "0.vvm", "1.vvm" }));
        }

        [Test]
        public void FindVoiceModelFiles_未配置なら0件()
        {
            var found = VoicevoxPaths.FindVoiceModelFiles(new[] { Path.Combine(_tempRoot, "存在しない") });

            Assert.That(found, Is.Empty);
        }

        [Test]
        public void Readiness_不足している段階を順に返す()
        {
            var nativeDir = Path.Combine(_tempRoot, "x86_64");
            var root = Path.Combine(_tempRoot, "voicevox_core");
            Directory.CreateDirectory(root);
            var roots = new[] { root };

            Assert.That(VoicevoxLocation.Create(nativeDir, roots).Readiness, Is.EqualTo(TtsReadiness.MissingNative));

            CreateFile(Path.Combine(nativeDir, "voicevox_core.dll"));
            CreateFile(Path.Combine(nativeDir, "voicevox_onnxruntime.dll"));
            Assert.That(VoicevoxLocation.Create(nativeDir, roots).Readiness, Is.EqualTo(TtsReadiness.MissingDictionary));

            CreateDictionary(Path.Combine(root, "open_jtalk_dic_utf_8-1.11"));
            Assert.That(VoicevoxLocation.Create(nativeDir, roots).Readiness, Is.EqualTo(TtsReadiness.MissingModels));

            CreateFile(Path.Combine(root, "models", "vvms", "0.vvm"));
            var location = VoicevoxLocation.Create(nativeDir, roots);
            Assert.That(location.Readiness, Is.EqualTo(TtsReadiness.Ready));
            Assert.That(location.Describe(), Does.Contain("0.vvm"));
        }

        private static string CreateDictionary(string path)
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "sys.dic"), "dummy");
            return path;
        }

        private static string CreateFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "dummy");
            return path;
        }
    }
}
