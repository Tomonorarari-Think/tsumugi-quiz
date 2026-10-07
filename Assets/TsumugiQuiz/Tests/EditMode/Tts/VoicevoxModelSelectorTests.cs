using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// 読み込む .vvm の絞り込み（#22 統括メモ (2)）の検証。
    /// メタ情報の取得をデリゲートで差し替えるので P/Invoke なしで確かめられる。
    /// </summary>
    public sealed class VoicevoxModelSelectorTests
    {
        private const string TsumugiNormal = @"[
          { ""name"": ""四国めたん"", ""styles"": [ { ""name"": ""ノーマル"", ""id"": 2, ""type"": ""talk"" } ], ""version"": ""0.16.4"" },
          { ""name"": ""春日部つむぎ"", ""styles"": [ { ""name"": ""ノーマル"", ""id"": 8, ""type"": ""talk"" } ], ""version"": ""0.16.4"" }
        ]";

        private const string TsumugiSingOnly = @"[
          { ""name"": ""春日部つむぎ"", ""styles"": [ { ""name"": ""ノーマル（ソング）"", ""id"": 3008, ""type"": ""sing"" } ], ""version"": ""0.16.4"" }
        ]";

        private const string TsumugiOtherStyle = @"[
          { ""name"": ""春日部つむぎ"", ""styles"": [ { ""name"": ""あまあま"", ""id"": 9, ""type"": ""talk"" } ], ""version"": ""0.16.4"" }
        ]";

        private const string OtherSpeakerOnly = @"[
          { ""name"": ""ずんだもん"", ""styles"": [ { ""name"": ""ノーマル"", ""id"": 3, ""type"": ""talk"" } ], ""version"": ""0.16.4"" }
        ]";

        private static IReadOnlyList<string> Select(IDictionary<string, string> metasByFile)
            => VoicevoxModelSelector.Select(
                new List<string>(metasByFile.Keys), "春日部つむぎ", "ノーマル", path => metasByFile[path]);

        [Test]
        public void 指定の話者とスタイルを含むvvmだけを選ぶ()
        {
            var selected = Select(new Dictionary<string, string>
            {
                ["0.vvm"] = TsumugiNormal,
                ["s0.vvm"] = TsumugiSingOnly,
                ["1.vvm"] = OtherSpeakerOnly,
            });

            Assert.That(selected, Is.EqualTo(new[] { "0.vvm" }));
        }

        [Test]
        public void 完全一致が無ければ同じ話者のtalkスタイルを含むvvmを選ぶ()
        {
            var selected = Select(new Dictionary<string, string>
            {
                ["1.vvm"] = OtherSpeakerOnly,
                ["2.vvm"] = TsumugiOtherStyle,
            });

            Assert.That(selected, Is.EqualTo(new[] { "2.vvm" }));
        }

        [Test]
        public void 指定の話者が無ければtalkスタイルを含むvvmを選ぶ()
        {
            var selected = Select(new Dictionary<string, string>
            {
                ["s0.vvm"] = TsumugiSingOnly,
                ["1.vvm"] = OtherSpeakerOnly,
            });

            Assert.That(selected, Is.EqualTo(new[] { "1.vvm" }));
        }

        [Test]
        public void どれも判定できなければ全件にフォールバックする()
        {
            var files = new[] { "s0.vvm", "s1.vvm" };

            var selected = VoicevoxModelSelector.Select(
                files, "春日部つむぎ", "ノーマル", _ => TsumugiSingOnly);

            Assert.That(selected, Is.EqualTo(files), "読み上げを試せるようにする");
        }

        [Test]
        public void メタ情報が読めないvvmは候補から外れる()
        {
            var selected = VoicevoxModelSelector.Select(
                new[] { "broken.vvm", "0.vvm" },
                "春日部つむぎ", "ノーマル",
                path => path == "0.vvm" ? TsumugiNormal : throw new TtsSetupException("読めません（テスト）。"));

            Assert.That(selected, Is.EqualTo(new[] { "0.vvm" }));
        }

        [Test]
        public void メタ情報が空のvvmは候補から外れる()
        {
            var selected = VoicevoxModelSelector.Select(
                new[] { "empty.vvm", "0.vvm" },
                "春日部つむぎ", "ノーマル",
                path => path == "0.vvm" ? TsumugiNormal : string.Empty);

            Assert.That(selected, Is.EqualTo(new[] { "0.vvm" }));
        }

        [Test]
        public void 候補が1件以下ならメタ情報を読まない()
        {
            var read = 0;
            var single = new[] { "0.vvm" };

            var selected = VoicevoxModelSelector.Select(single, "春日部つむぎ", "ノーマル", _ =>
            {
                read++;
                return TsumugiNormal;
            });

            Assert.That(selected, Is.EqualTo(single));
            Assert.That(read, Is.EqualTo(0), "1 件なら絞り込む意味がないので開かない");
        }

        [Test]
        public void 引数の検証()
        {
            Assert.Throws<ArgumentNullException>(
                () => VoicevoxModelSelector.Select(null, "春日部つむぎ", "ノーマル", _ => TsumugiNormal));
            Assert.Throws<ArgumentNullException>(
                () => VoicevoxModelSelector.Select(new[] { "a", "b" }, "春日部つむぎ", "ノーマル", null));
            Assert.Throws<ArgumentException>(
                () => VoicevoxModelSelector.Select(new[] { "a", "b" }, " ", "ノーマル", _ => TsumugiNormal));
            Assert.Throws<ArgumentException>(
                () => VoicevoxModelSelector.Select(new[] { "a", "b" }, "春日部つむぎ", " ", _ => TsumugiNormal));
        }

        [Test]
        public void 設定から絞り込み条件を作れる()
        {
            var selection = VoicevoxModelSelection.FromSettings(TtsSettings.Default);

            Assert.That(selection.IsAll, Is.False);
            Assert.That(selection.SpeakerName, Is.EqualTo(TtsSettings.DefaultSpeakerName));
            Assert.That(selection.StyleName, Is.EqualTo(TtsSettings.DefaultStyleName));
            Assert.That(VoicevoxModelSelection.All.IsAll, Is.True);
        }
    }
}
