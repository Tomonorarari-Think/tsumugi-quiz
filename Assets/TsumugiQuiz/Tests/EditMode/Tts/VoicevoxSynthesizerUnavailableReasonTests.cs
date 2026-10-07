using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// <see cref="VoicevoxSynthesizer.Create(VoicevoxLocation)"/> が配置不足を検出したときに、
    /// <see cref="TtsSetupException.Reason"/> が正しい <see cref="TtsUnavailableReason"/> になることの検証（#25）。
    ///
    /// いずれのケースも、配置チェック（<see cref="VoicevoxLocation.Readiness"/>）の時点で例外を投げるため、
    /// ネイティブ DLL には一切触れない（ダミーファイルを置くだけで検証できる）。
    /// </summary>
    public sealed class VoicevoxSynthesizerUnavailableReasonTests
    {
        private string _tempRoot;
        private string _nativeDir;
        private string _assetRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "tsumugi-quiz-tts-reason-tests", Guid.NewGuid().ToString("N"));
            _nativeDir = Path.Combine(_tempRoot, "native");
            _assetRoot = Path.Combine(_tempRoot, "assets");
            Directory.CreateDirectory(_nativeDir);
            Directory.CreateDirectory(_assetRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
        }

        [Test]
        public void ネイティブDLLが両方無いとMissingCoreDll()
        {
            var location = VoicevoxLocation.Create(_nativeDir, new[] { _assetRoot });

            var ex = Assert.Throws<TtsSetupException>(() => VoicevoxSynthesizer.Create(location));

            Assert.That(ex.Reason, Is.EqualTo(TtsUnavailableReason.MissingCoreDll));
        }

        [Test]
        public void CoreDllだけあってOnnxRuntimeが無いとMissingOnnxRuntime()
        {
            WriteDummyFile(Path.Combine(_nativeDir, VoicevoxPaths.CoreDllFileName));
            var location = VoicevoxLocation.Create(_nativeDir, new[] { _assetRoot });

            var ex = Assert.Throws<TtsSetupException>(() => VoicevoxSynthesizer.Create(location));

            Assert.That(ex.Reason, Is.EqualTo(TtsUnavailableReason.MissingOnnxRuntime));
        }

        [Test]
        public void 辞書が無いとMissingDictionary()
        {
            WriteBothNativeDlls();
            var location = VoicevoxLocation.Create(_nativeDir, new[] { _assetRoot });

            var ex = Assert.Throws<TtsSetupException>(() => VoicevoxSynthesizer.Create(location));

            Assert.That(ex.Reason, Is.EqualTo(TtsUnavailableReason.MissingDictionary));
        }

        [Test]
        public void 音声モデルが無いとMissingModel()
        {
            WriteBothNativeDlls();
            WriteValidDictionary();
            var location = VoicevoxLocation.Create(_nativeDir, new[] { _assetRoot });

            var ex = Assert.Throws<TtsSetupException>(() => VoicevoxSynthesizer.Create(location));

            Assert.That(ex.Reason, Is.EqualTo(TtsUnavailableReason.MissingModel));
        }

        private void WriteBothNativeDlls()
        {
            WriteDummyFile(Path.Combine(_nativeDir, VoicevoxPaths.CoreDllFileName));
            WriteDummyFile(Path.Combine(_nativeDir, VoicevoxPaths.OnnxruntimeDllFileName));
        }

        private void WriteValidDictionary()
        {
            var dictDir = Path.Combine(_assetRoot, VoicevoxPaths.DictionaryDirName);
            Directory.CreateDirectory(dictDir);
            WriteDummyFile(Path.Combine(dictDir, VoicevoxPaths.DictionaryMarkerFileName));
        }

        private static void WriteDummyFile(string path) => File.WriteAllBytes(path, new byte[] { 0 });
    }
}
