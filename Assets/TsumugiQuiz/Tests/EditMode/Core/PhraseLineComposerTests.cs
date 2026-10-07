using System;
using NUnit.Framework;
using TsumugiQuiz.Core.TextLayout;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="PhraseLineComposer"/>（区切りの位置で改行を入れる、issue #199）を検証する。
    /// 幅は「1 文字 10px」の仮の測り方で与える（実際の字幅は PlayMode の DynamicTextWrappingSceneTests で確かめる）。
    /// </summary>
    public class PhraseLineComposerTests
    {
        private const float CharWidth = 10f;

        private static float Measure(string line) => line.Length * CharWidth;

        [Test]
        public void Compose_TextThatFits_IsUnchanged()
        {
            const string text = "接続中…";
            Assert.That(PhraseLineComposer.Compose(text, Measure, 100f), Is.EqualTo(text));
        }

        [Test]
        public void Compose_BreaksOnlyBetweenSegments()
        {
            // 区切り: 接続が(3)|タイムアウトしました。(11)|参加コードや(6)|接続先を(4)|確認してください。(9)
            var composed = PhraseLineComposer.Compose(
                "接続がタイムアウトしました。参加コードや接続先を確認してください。", Measure, 20 * CharWidth);

            Assert.That(composed, Is.EqualTo("接続がタイムアウトしました。\n参加コードや接続先を確認してください。"));
        }

        [Test]
        public void Compose_ReservesFitTolerance()
        {
            // ちょうど幅いっぱい（14 文字 = 140px）の行は、余白（FitTolerance）を残すため収まらない扱いにする。
            var composed = PhraseLineComposer.Compose("接続がタイムアウトしました。参加", Measure, 14 * CharWidth);

            Assert.That(composed, Is.EqualTo("接続が\nタイムアウトしました。参加"));
            Assert.That(PhraseLineComposer.Compose("接続がタイムアウトしました。参加", Measure, 14 * CharWidth + PhraseLineComposer.FitTolerance),
                Is.EqualTo("接続がタイムアウトしました。\n参加"));
        }

        [Test]
        public void Compose_TrimsSpaceAtLineEnd()
        {
            var composed = PhraseLineComposer.Compose("ONNX Runtime のバージョンが", Measure, 13 * CharWidth);

            Assert.That(composed, Is.EqualTo("ONNX\nRuntime の\nバージョンが"));
        }

        [Test]
        public void Compose_SegmentLongerThanWidth_IsPlacedOnItsOwnLine()
        {
            var composed = PhraseLineComposer.Compose("接続がタイムアウトしました。", Measure, 5 * CharWidth);

            Assert.That(composed, Is.EqualTo("接続が\nタイムアウトしました。"));
        }

        [Test]
        public void Compose_KeepsExistingNewlines()
        {
            var composed = PhraseLineComposer.Compose("参加コードの長さが違います。\n接続先を確認してください。", Measure, 100f);

            // 1 行 9 文字まで。段落ごとに組み、既存の改行はそのまま残す。
            Assert.That(composed, Is.EqualTo("参加コードの長さが\n違います。\n接続先を\n確認してください。"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Compose_NullOrEmpty_IsReturnedAsIs(string text)
        {
            Assert.That(PhraseLineComposer.Compose(text, Measure, 50f), Is.EqualTo(text));
        }

        [Test]
        public void Compose_TextWithAngleBrackets_IsComposedAsPlainText()
        {
            // #206: 文言は平文として扱う（表示の側でリッチテキストを解釈させない）ので、「<」を含んでも区切りの位置で改行する。
            // 区切り: <size=300>接続が(12)|タイムアウトしました。(11)
            var composed = PhraseLineComposer.Compose("<size=300>接続がタイムアウトしました。", Measure, 15 * CharWidth);

            Assert.That(composed, Is.EqualTo("<size=300>接続が\nタイムアウトしました。"));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void Compose_UnknownWidth_ReturnsTextAsIs(float width)
        {
            const string text = "接続がタイムアウトしました。参加コードや接続先を確認してください。";
            Assert.That(PhraseLineComposer.Compose(text, Measure, width), Is.EqualTo(text));
        }

        [Test]
        public void Compose_NullMeasure_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PhraseLineComposer.Compose("文言", null, 100f));
        }
    }
}
