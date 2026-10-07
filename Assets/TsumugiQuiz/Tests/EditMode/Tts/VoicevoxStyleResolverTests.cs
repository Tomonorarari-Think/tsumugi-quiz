using NUnit.Framework;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>メタ JSON からのスタイル解決とフォールバック順（docs/tts.md §4.1）の検証。</summary>
    public sealed class VoicevoxStyleResolverTests
    {
        // 実際の 0.vvm のメタ情報に合わせた形（値は検証用。コードにスタイル ID を書かないこと）
        private const string MetasJson = @"[
          {
            ""name"": ""四国めたん"",
            ""speaker_uuid"": ""7ffcb7ce-00ec-4bdc-82cd-45a8889e43ff"",
            ""styles"": [ { ""name"": ""ノーマル"", ""id"": 2, ""type"": ""talk"" } ],
            ""version"": ""0.17.0""
          },
          {
            ""name"": ""春日部つむぎ"",
            ""speaker_uuid"": ""35b2c544-660e-401e-b503-0e14c635303a"",
            ""styles"": [
              { ""name"": ""ノーマル（ソング）"", ""id"": 3008, ""type"": ""sing"" },
              { ""name"": ""ノーマル"", ""id"": 8, ""type"": ""talk"" }
            ],
            ""version"": ""0.17.0""
          }
        ]";

        [Test]
        public void Resolve_話者名とスタイル名で解決する()
        {
            var resolution = VoicevoxStyleResolver.Resolve(MetasJson);

            Assert.That(resolution.StyleId, Is.EqualTo(8u));
            Assert.That(resolution.SpeakerName, Is.EqualTo("春日部つむぎ"));
            Assert.That(resolution.StyleName, Is.EqualTo("ノーマル"));
            Assert.That(resolution.Match, Is.EqualTo(VoicevoxStyleMatch.Exact));
        }

        [Test]
        public void Resolve_歌唱スタイルは読み上げに選ばれない()
        {
            var resolution = VoicevoxStyleResolver.Resolve(MetasJson, "春日部つむぎ", "ノーマル（ソング）");

            Assert.That(resolution.Match, Is.EqualTo(VoicevoxStyleMatch.SpeakerFallback));
            Assert.That(resolution.StyleId, Is.EqualTo(8u), "talk スタイルにフォールバックすること");
        }

        [Test]
        public void Resolve_スタイル名が変わったら同じ話者の最初のtalkスタイルにフォールバックする()
        {
            var resolution = VoicevoxStyleResolver.Resolve(MetasJson, "春日部つむぎ", "存在しないスタイル");

            Assert.That(resolution.Match, Is.EqualTo(VoicevoxStyleMatch.SpeakerFallback));
            Assert.That(resolution.SpeakerName, Is.EqualTo("春日部つむぎ"));
            Assert.That(resolution.StyleId, Is.EqualTo(8u));
        }

        [Test]
        public void Resolve_話者が居なければ最初のtalkスタイルにフォールバックする()
        {
            var resolution = VoicevoxStyleResolver.Resolve(MetasJson, "居ない話者", "ノーマル");

            Assert.That(resolution.Match, Is.EqualTo(VoicevoxStyleMatch.AnySpeakerFallback));
            Assert.That(resolution.SpeakerName, Is.EqualTo("四国めたん"));
            Assert.That(resolution.StyleId, Is.EqualTo(2u));
        }

        [Test]
        public void Resolve_talkスタイルが1つも無ければ例外()
        {
            const string singOnly = @"[{ ""name"": ""春日部つむぎ"", ""speaker_uuid"": ""x"",
                ""styles"": [ { ""name"": ""ノーマル（ソング）"", ""id"": 3008, ""type"": ""sing"" } ] }]";

            var e = Assert.Throws<TtsSetupException>(() => VoicevoxStyleResolver.Resolve(singOnly));

            Assert.That(e.Message, Does.Contain("春日部つむぎ"));
        }

        [Test]
        public void Resolve_typeが無いスタイルはtalk扱い()
        {
            const string noType = @"[{ ""name"": ""春日部つむぎ"", ""speaker_uuid"": ""x"",
                ""styles"": [ { ""name"": ""ノーマル"", ""id"": 8 } ] }]";

            var resolution = VoicevoxStyleResolver.Resolve(noType);

            Assert.That(resolution.StyleId, Is.EqualTo(8u));
            Assert.That(resolution.Match, Is.EqualTo(VoicevoxStyleMatch.Exact));
        }

        [Test]
        public void ParseMetas_スネークケースのキーを読める()
        {
            var metas = VoicevoxStyleResolver.ParseMetas(MetasJson);

            Assert.That(metas.Count, Is.EqualTo(2));
            Assert.That(metas[1].SpeakerUuid, Is.EqualTo("35b2c544-660e-401e-b503-0e14c635303a"));
            Assert.That(metas[1].Version, Is.EqualTo("0.17.0"));
        }

        [Test]
        public void ParseMetas_壊れたJSONは握りつぶさずTtsSetupException()
        {
            Assert.Throws<TtsSetupException>(() => VoicevoxStyleResolver.ParseMetas("{ これはJSONではない"));
        }

        [Test]
        public void ParseMetas_空文字列は例外()
        {
            Assert.Throws<TtsSetupException>(() => VoicevoxStyleResolver.ParseMetas("  "));
        }

        [Test]
        public void Resolve_空配列は読み込まれている話者が無い旨の例外()
        {
            var e = Assert.Throws<TtsSetupException>(() => VoicevoxStyleResolver.Resolve("[]"));

            Assert.That(e.Message, Does.Contain("(なし)"));
        }
    }
}
