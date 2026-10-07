using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// <see cref="TtsStatusMessages"/> の完全性の検証（#25）。
    /// <see cref="TtsUnavailableReason"/> の全値に文言があること、
    /// ユーザー向け文言に ResultCode の数値・<c>[数値 名前]</c> 形式・例外の型名が出ないこと
    /// （docs/tts.md §9.1、#25 H-3）を確かめる。
    ///
    /// <b>H-3 の方針</b>: ONNX Runtime の対応バージョン番号（<see cref="TtsStatusMessage.Detail"/>）だけは
    /// 数値を許容するため、「数値を一切含まない」ではなく「ResultCode 由来の痕跡・例外型名を含まない」に限定する。
    /// ただし <see cref="TtsStatusMessage.Headline"/> / <see cref="TtsStatusMessage.Guidance"/> は
    /// 元々数値を必要としない固定文言なので、こちらは従来どおり数値を含まないことも確かめる。
    /// </summary>
    public sealed class TtsStatusMessagesTests
    {
        private static readonly Regex DigitPattern = new Regex(@"\d", RegexOptions.Compiled);

        /// <summary>ResultCode のログ表記「[29 InitInferenceRuntime]」のような形式。</summary>
        private static readonly Regex ResultCodeBracketPattern =
            new Regex(@"\[\s*-?\d+\s+[A-Za-z]+\s*\]", RegexOptions.Compiled);

        private static readonly Regex ResultCodeWordPattern =
            new Regex(@"\bResultCode\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ExceptionTypeNamePattern =
            new Regex(@"\bException\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static TtsUnavailableReason[] AllReasons
            => (TtsUnavailableReason[])Enum.GetValues(typeof(TtsUnavailableReason));

        [Test]
        public void 全列挙値に文言が定義されている()
        {
            foreach (var reason in AllReasons)
            {
                Assert.DoesNotThrow(() => TtsStatusMessages.For(reason), $"{reason} の文言が未定義です。");
            }
        }

        [Test]
        public void 見出しと対処案内はともに空でない()
        {
            foreach (var reason in AllReasons)
            {
                var message = TtsStatusMessages.For(reason);

                Assert.That(message.Headline, Is.Not.Null.And.Not.Empty, $"{reason} の Headline が空です。");
                Assert.That(message.Guidance, Is.Not.Null.And.Not.Empty, $"{reason} の Guidance が空です。");
            }
        }

        [Test]
        public void detail省略時は見出しと対処案内に数値を含まない()
        {
            foreach (var reason in AllReasons)
            {
                var message = TtsStatusMessages.For(reason);

                Assert.That(DigitPattern.IsMatch(message.Headline), Is.False,
                    $"{reason} の Headline に数値が含まれています: '{message.Headline}'");
                Assert.That(DigitPattern.IsMatch(message.Guidance), Is.False,
                    $"{reason} の Guidance に数値が含まれています: '{message.Guidance}'");
                Assert.That(message.Detail, Is.Null, $"{reason} は detail 省略時 Detail が null であること");
            }
        }

        /// <summary>
        /// OnnxRuntimeVersionMismatch の Detail は対応バージョン番号を含んでよいが（H-3）、
        /// ResultCode 由来の痕跡・例外の型名は全パターンで含んではいけない。
        /// </summary>
        [Test]
        public void 全パターンでResultCodeの痕跡と例外型名を含まない()
        {
            foreach (var reason in AllReasons)
            {
                AssertNoForbiddenPattern(reason, null);
            }

            AssertNoForbiddenPattern(
                TtsUnavailableReason.OnnxRuntimeVersionMismatch, "対応バージョンは 1.4 以上 1.6 以下です。");
        }

        [Test]
        public void OnnxバージョンのDetailは数値を含んでもよい()
        {
            var message = TtsStatusMessages.For(
                TtsUnavailableReason.OnnxRuntimeVersionMismatch, "対応バージョンは 1.4 以上 1.6 以下です。");

            Assert.That(message.Detail, Is.EqualTo("対応バージョンは 1.4 以上 1.6 以下です。"));
        }

        [Test]
        public void 未定義の値は例外になる()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TtsStatusMessages.For((TtsUnavailableReason)999));
        }

        private static void AssertNoForbiddenPattern(TtsUnavailableReason reason, string detail)
        {
            var message = TtsStatusMessages.For(reason, detail);
            var all = message.Headline + " " + message.Guidance + " " + message.Detail;

            Assert.That(ResultCodeBracketPattern.IsMatch(all), Is.False,
                $"{reason} に ResultCode の '[数値 名前]' 形式が含まれています: '{all}'");
            Assert.That(ResultCodeWordPattern.IsMatch(all), Is.False,
                $"{reason} に 'ResultCode' という語が含まれています: '{all}'");
            Assert.That(ExceptionTypeNamePattern.IsMatch(all), Is.False,
                $"{reason} に例外の型名（'Exception'）が含まれています: '{all}'");
        }
    }
}
