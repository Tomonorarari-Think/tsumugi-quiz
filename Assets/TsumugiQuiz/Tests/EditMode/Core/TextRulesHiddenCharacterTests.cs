using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 見えない文字と積み重ねた結合記号の共通の規則（<see cref="TextRules.RemoveHiddenCharacters"/> /
    /// <see cref="TextRules.ContainsHiddenCharacters"/>、issue #209、docs/network.md §9）を検証する。
    /// 表示の整形（<see cref="DisplayTextSanitizer"/>）・承認時の名前の検証（<c>PlayerNameValidator</c>）・
    /// 問題データの表示（<c>GameViewPresenter</c>）がこの規則を共有する。
    /// </summary>
    public class TextRulesHiddenCharacterTests
    {
        // 双方向の制御と幅のない区切り（#206 から除いていたもの）。
        [TestCase("\u202A")]
        [TestCase("\u202B")]
        [TestCase("\u202C")]
        [TestCase("\u202D")]
        [TestCase("\u202E")]
        [TestCase("\u2066")]
        [TestCase("\u2067")]
        [TestCase("\u2068")]
        [TestCase("\u2069")]
        [TestCase("\u200E")]
        [TestCase("\u200F")]
        [TestCase("\u061C")]
        [TestCase("\u200B")]
        [TestCase("\u2060")]
        [TestCase("\uFEFF")]
        // #209 で追加した書式文字（カテゴリ Cf）。
        [TestCase("\u00AD")]
        [TestCase("\u2061")]
        [TestCase("\u2062")]
        [TestCase("\u2063")]
        [TestCase("\u2064")]
        [TestCase("\u206A")]
        [TestCase("\u206B")]
        [TestCase("\u206C")]
        [TestCase("\u206D")]
        [TestCase("\u206E")]
        [TestCase("\u206F")]
        [TestCase("\u180E")]
        [TestCase("\uFFF9")]
        [TestCase("\uFFFA")]
        [TestCase("\uFFFB")]
        [TestCase("\U000E0001")]
        [TestCase("\U0001BCA0")]
        [TestCase("\U0001D173")]
        // 上に挙げていない書式文字（カテゴリ Cf）も、残す書式文字（ZWJ・ZWNJ・旗のタグ文字・Prepended_Concatenation_Mark）以外は除く。
        [TestCase("\U000E0041")]
        // 見えない結合記号（カテゴリ Mn）。
        [TestCase("\u034F")]
        [TestCase("\u17B4")]
        [TestCase("\u17B5")]
        // 空白に見えるハングルの字母（カテゴリ Lo）。
        [TestCase("\u3164")]
        [TestCase("\u115F")]
        [TestCase("\u1160")]
        [TestCase("\uFFA0")]
        public void RemoveHiddenCharacters_RemovesInvisibleCharacters(string hidden)
        {
            Assert.That(TextRules.RemoveHiddenCharacters("つむ" + hidden + "ぎ"), Is.EqualTo("つむぎ"));
            Assert.That(TextRules.ContainsHiddenCharacters("つむ" + hidden + "ぎ"), Is.True);
        }

        [TestCase("👨\u200D👩\u200D👧")]
        [TestCase("a\u200Cb")]
        [TestCase("葛\uFE00")]
        [TestCase("❤\uFE0F")]
        [TestCase("1\uFE0F\u20E3")]
        [TestCase("👍\U0001F3FD")]
        [TestCase("🇯🇵")]
        [TestCase("か\u3099")]
        [TestCase("つむぎ")]
        [TestCase("Tsumugi Kasukabe")]
        [TestCase("あ\nい  う\u3000え")]
        public void RemoveHiddenCharacters_KeepsJoinersVariationSelectorsAndEmojiSequences(string text)
        {
            // 絵文字の ZWJ 連結・異体字セレクタ・肌色の修飾・地域指示記号・濁点の結合文字は、正当な名前や文を壊すので残す。
            // 改行・空白は整えない（まとめるのは表示の整形の役目）。
            Assert.That(TextRules.RemoveHiddenCharacters(text), Is.EqualTo(text));
            Assert.That(TextRules.ContainsHiddenCharacters(text), Is.False);
        }

        [Test]
        public void RemoveHiddenCharacters_KeepsTheThreeRgiFlagTagSequences()
        {
            // RGI_Emoji_Tag_Sequence の 3 つ（イングランド・スコットランド・ウェールズ）は残す（PR #211 レビュー L-3）。
            foreach (var flag in new[] { EnglandFlag, ScotlandFlag, WalesFlag })
            {
                Assert.That(TextRules.RemoveHiddenCharacters(flag), Is.EqualTo(flag));
                Assert.That(TextRules.ContainsHiddenCharacters(flag), Is.False);
                Assert.That(TextRules.RemoveHiddenCharacters("つむぎ" + flag + "a"), Is.EqualTo("つむぎ" + flag + "a"));
            }
        }

        [Test]
        public void RemoveHiddenCharacters_RemovesTagCharactersOutsideTheRgiFlags()
        {
            // RGI の 3 つと完全に一致しないタグ文字は、U+1F3F4 の後でも見えないので除く（PR #211 レビュー L-3）。
            var hiddenTags = "つむぎ" + FromCodePoints(0xE0067, 0xE0062, 0xE007F);
            var cancelTagOnly = FromCodePoints(0x1F3F4, 0xE007F);
            var texasTags = FromCodePoints(0x1F3F4, 0xE0075, 0xE0073, 0xE0074, 0xE0078, 0xE007F);
            var unterminated = FromCodePoints(0x1F3F4, 0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074);
            var interrupted = FromCodePoints(0x1F3F4, 0xE0067, 0xE0062) + "a" + FromCodePoints(0xE0073, 0xE0063, 0xE0074, 0xE007F);

            Assert.That(TextRules.RemoveHiddenCharacters(hiddenTags), Is.EqualTo("つむぎ"));
            Assert.That(TextRules.RemoveHiddenCharacters(cancelTagOnly), Is.EqualTo(FromCodePoints(0x1F3F4)));
            Assert.That(TextRules.RemoveHiddenCharacters(texasTags), Is.EqualTo(FromCodePoints(0x1F3F4)));
            Assert.That(TextRules.RemoveHiddenCharacters(unterminated), Is.EqualTo(FromCodePoints(0x1F3F4)));
            Assert.That(TextRules.RemoveHiddenCharacters(interrupted), Is.EqualTo(FromCodePoints(0x1F3F4) + "a"));
        }

        [TestCase("\u0600\u0661\u0662")]
        [TestCase("\u0605\u0661")]
        [TestCase("\u06DD\u0661")]
        [TestCase("\u070F\u0710")]
        [TestCase("\u0890\u0661")]
        [TestCase("\u0891\u0661")]
        [TestCase("\u08E2\u0661")]
        [TestCase("\U000110BD1")]
        [TestCase("\U000110CD1")]
        public void RemoveHiddenCharacters_KeepsPrependedConcatenationMarks(string text)
        {
            // Prepended_Concatenation_Mark（UCD 18.0 PropList.txt）はカテゴリ Cf だが、数字の上に付く目に見える記号なので残す
            // （PR #211 レビュー L-1）。
            Assert.That(TextRules.RemoveHiddenCharacters(text), Is.EqualTo(text));
            Assert.That(TextRules.ContainsHiddenCharacters(text), Is.False);
        }

        [TestCase("\u200Dつむぎ", "つむぎ")]
        [TestCase("つむぎ\u200D", "つむぎ")]
        [TestCase("つむぎ\u200C", "つむぎ")]
        [TestCase("\u200C\u200Dつむぎ\u200D\u200D", "つむぎ")]
        [TestCase("つむ\u200D ぎ", "つむ ぎ")]
        [TestCase("つむ \u200Dぎ", "つむ ぎ")]
        [TestCase("つむぎ\u200D\u200B", "つむぎ")]
        public void RemoveHiddenCharacters_RemovesJoinersWithNothingToJoin(string text, string expected)
        {
            // 先頭・末尾・空白の隣の ZWJ / ZWNJ は、つなぐ相手がなく見えない（#209 コメント）。
            Assert.That(TextRules.RemoveHiddenCharacters(text), Is.EqualTo(expected));
            Assert.That(TextRules.ContainsHiddenCharacters(text), Is.True);
        }

        [TestCase("\u0301つむぎ", "つむぎ")]
        [TestCase("つむ \u0301ぎ", "つむ ぎ")]
        [TestCase("つむ\n\u0301\u0301ぎ", "つむ\nぎ")]
        [TestCase("\u200B\u0301つむぎ", "つむぎ")]
        public void RemoveHiddenCharacters_RemovesCombiningMarksWithoutABase(string text, string expected)
        {
            Assert.That(TextRules.RemoveHiddenCharacters(text), Is.EqualTo(expected));
        }

        [Test]
        public void RemoveHiddenCharacters_LimitsCombiningMarksPerBase()
        {
            var four = "a" + new string('\u0301', TextRules.MaxCombiningMarksPerBase);
            var five = "a" + new string('\u0301', TextRules.MaxCombiningMarksPerBase + 1);

            Assert.That(TextRules.RemoveHiddenCharacters(four + "b"), Is.EqualTo(four + "b"));
            Assert.That(TextRules.ContainsHiddenCharacters(four), Is.False);
            Assert.That(TextRules.RemoveHiddenCharacters(five + "b"), Is.EqualTo(four + "b"));
            Assert.That(TextRules.ContainsHiddenCharacters(five), Is.True);
        }

        [Test]
        public void RemoveHiddenCharacters_JoinersDoNotResetTheCombiningMarkCount()
        {
            // 残す ZWJ を挟んでも数は戻らない（挟んで結合記号を積み増せないようにする。#206）。
            var text = "a" + new string('\u0301', 4) + "\u200D" + new string('\u0301', 4) + "b";

            Assert.That(TextRules.RemoveHiddenCharacters(text), Is.EqualTo("a" + new string('\u0301', 4) + "\u200Db"));
        }

        [Test]
        public void RemoveHiddenCharacters_NullOrEmpty_ReturnsEmpty()
        {
            Assert.That(TextRules.RemoveHiddenCharacters(null), Is.EqualTo(string.Empty));
            Assert.That(TextRules.RemoveHiddenCharacters(string.Empty), Is.EqualTo(string.Empty));
            Assert.That(TextRules.ContainsHiddenCharacters(null), Is.False);
        }

        [TestCase("🏴\u200D\U000E0067 ")]
        [TestCase("a\u200D\u200D ")]
        [TestCase("\u200D\u0301a")]
        [TestCase(" \u200D\u0301")]
        [TestCase("a\u0301\u0301\u0301\u0301\u0301\u200D\u0301\u0301\u0301\u0301\u0301")]
        [TestCase("🏴\u200B\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F")]
        [TestCase("a\u200B\u200Db\u200C\u3164 \u0301")]
        public void RemoveHiddenCharacters_IsIdempotent(string text)
        {
            // PR #211 レビュー M-2: 2 回当てても結果は変わらない（承認を通った名前が表示で変わらないことの前提）。
            var once = TextRules.RemoveHiddenCharacters(text);

            Assert.That(TextRules.RemoveHiddenCharacters(once), Is.EqualTo(once));
            Assert.That(TextRules.ContainsHiddenCharacters(once), Is.False);
        }

        [TestCase("Tie\u0302\u0301ng Vie\u0323\u0302t")] // ベトナム語（分解形 NFD。基底文字あたり 2 個）
        [TestCase("\u0645\u064F\u062D\u064E\u0645\u0651\u064E\u062F")] // アラビア語（母音記号とシャッダ）
        [TestCase("\u05D8\u05BC\u05B5\u0595")] // SBL Hebrew の例: テト + ダゲシュ + ツェレ + ザケフ・ガドル
        [TestCase("\u0915\u094D\u200D\u0937")] // デーヴァナーガリーの半字形の指定（子音 + ヴィラーマ + ZWJ + 子音）
        [TestCase("\U0001F469\U0001F3FD\u200D\U0001F4BB")] // 職業 + 肌色の ZWJ 連結
        [TestCase("#\uFE0F\u20E3")] // キーキャップ
        public void RemoveHiddenCharacters_KeepsLegitimateText(string text)
        {
            // PR #211 レビュー L-8: 正当な表記は変えない。
            Assert.That(TextRules.RemoveHiddenCharacters(text), Is.EqualTo(text));
            Assert.That(TextRules.ContainsHiddenCharacters(text), Is.False);
        }

        [Test]
        public void RemoveHiddenCharacters_KeepsLoneSurrogates()
        {
            // 単独のサロゲートは見えない文字の規則の対象外で、そのまま返す（例外にしない）。
            // 名前は PlayerNameValidator が別に拒否し、表示の整形（DisplayTextSanitizer）は取り除く。
            Assert.That(TextRules.RemoveHiddenCharacters("あ\uD800い"), Is.EqualTo("あ\uD800い"));
            Assert.That(TextRules.RemoveHiddenCharacters("あ\uDC00"), Is.EqualTo("あ\uDC00"));
            Assert.That(TextRules.RemoveHiddenCharacters("あ\uD800"), Is.EqualTo("あ\uD800"));
        }

        internal static readonly string EnglandFlag = FromCodePoints(0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F);
        internal static readonly string ScotlandFlag = FromCodePoints(0x1F3F4, 0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074, 0xE007F);
        internal static readonly string WalesFlag = FromCodePoints(0x1F3F4, 0xE0067, 0xE0062, 0xE0077, 0xE006C, 0xE0073, 0xE007F);

        internal static string FromCodePoints(params int[] codePoints)
            => string.Concat(codePoints.Select(char.ConvertFromUtf32));
    }
}
