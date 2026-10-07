using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Room;
using TsumugiQuiz.UI.Views.Settings;

namespace TsumugiQuiz.Tests.EditMode.UI.Settings
{
    /// <summary>
    /// <see cref="TtsSettingsProviderFactory"/>（PR #103 再レビュー N1）を検証する。
    /// <see cref="TsumugiQuiz.UI.Views.Settings.SettingsView"/>・<see cref="TsumugiQuiz.UI.Views.Game.GameView"/>・
    /// <see cref="TsumugiQuiz.UI.Views.QuestionEditor.QuestionEditorView"/>（読み上げプレビュー、issue #32）の
    /// 3か所が本ファクトリ経由で保存済みアプリ設定（<c>tts.*</c>）を <c>TtsService</c> へ渡すため、
    /// ここでの正しさが3か所すべての正しさの前提になる。
    /// </summary>
    public class TtsSettingsProviderFactoryTests
    {
        private string _originalEnvironmentValue;

        [SetUp]
        public void SetUp()
        {
            // AppPathsTests と同じ作法（#71）: 環境変数を退避してから空にし、AppPaths の状態をリセットする。
            _originalEnvironmentValue = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, null);
            AppPaths.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            AppPaths.Reset();
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, _originalEnvironmentValue);
        }

        [Test]
        public void BuildOrNull_AppPathsConfigured_ReturnsProviderMatchingSavedSettings()
        {
            var explicitRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsSettingsProviderFactoryTests_" + Guid.NewGuid().ToString("N"));
            AppPaths.Configure(explicitRoot);

            try
            {
                var appSettings = AppSettings.Create(
                    ttsSpeakerName: "テスト話者",
                    ttsStyleName: "テストスタイル",
                    ttsCacheMaxBytes: 12345L,
                    ttsCacheMaxEntries: 42,
                    ttsAssetPathOverride: "C:/tmp/voicevox");
                var saveResult = new AppSettingsStore().Save(appSettings);
                Assert.IsTrue(saveResult.Success, string.Join(" / ", saveResult.Warnings));

                var provider = TtsSettingsProviderFactory.BuildOrNull();

                Assert.IsNotNull(provider, "AppPaths 設定済みなら provider を返すこと。");
                var loaded = provider.Load();
                Assert.AreEqual("テスト話者", loaded.SpeakerName);
                Assert.AreEqual("テストスタイル", loaded.StyleName);
                Assert.AreEqual(12345L, loaded.CacheMaxBytes);
                Assert.AreEqual(42, loaded.CacheMaxEntries);
                Assert.AreEqual("C:/tmp/voicevox", loaded.AssetPathOverride);
            }
            finally
            {
                if (Directory.Exists(explicitRoot))
                {
                    Directory.Delete(explicitRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// issue #138: 返す provider は「起動時スナップショット」ではなく<b>都度読み</b>であること。
        /// アプリ起動時（<c>ConfigureTtsConsentGate</c>）に登録した provider をロビーの先行初期化が読むため、
        /// ここが固定値だと「設定画面で変更 → 再起動せずホスト」で古い値が使われる。
        /// </summary>
        [Test]
        public void BuildOrNull_ReturnedProviderRereadsSavedSettingsOnEachLoad()
        {
            var explicitRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsSettingsProviderFactoryTests_" + Guid.NewGuid().ToString("N"));
            AppPaths.Configure(explicitRoot);

            try
            {
                Assert.IsTrue(
                    new AppSettingsStore().Save(AppSettings.Create(ttsSpeakerName: "保存前の話者")).Success);

                var provider = TtsSettingsProviderFactory.BuildOrNull();
                Assert.IsNotNull(provider);
                Assert.AreEqual("保存前の話者", provider.Load().SpeakerName, "まずは保存済みの値を読むこと。");

                // 設定画面での保存に相当（provider は作り直さない）。
                Assert.IsTrue(
                    new AppSettingsStore().Save(AppSettings.Create(ttsSpeakerName: "保存後の話者")).Success);

                Assert.AreEqual(
                    "保存後の話者", provider.Load().SpeakerName,
                    "同じ provider インスタンスでも、Load のたびに保存済みの最新値を読むこと（#138）。");
            }
            finally
            {
                if (Directory.Exists(explicitRoot))
                {
                    Directory.Delete(explicitRoot, recursive: true);
                }
            }
        }

        [Test]
        public void BuildOrNull_AppPathsNotConfigured_ReturnsNull()
        {
            // AppPaths.Reset() 済み（SetUp）。AppSettingsStore.GetDefaultFilePath が
            // InvalidOperationException を投げるので、TtsService 既定にフォールバックするため null を返す。
            var provider = TtsSettingsProviderFactory.BuildOrNull();

            Assert.IsNull(provider, "AppPaths 未設定のときは null（TtsService 既定の設定にフォールバック）を返すこと。");
        }
    }
}
