using System;
using System.Text;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 参加コードのエンコード/デコード/正規化のテスト。
    /// 期待値は docs/network-joincode.md §1.7 のテストベクタをそのまま用いる。
    /// </summary>
    public class JoinCodeCodecTests
    {
        private const string SampleCode = "6B01RGA7K1K4";
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        /// <summary>docs/network-joincode.md §1.7 のテストベクタ 6 件。</summary>
        private static readonly object[] Vectors =
        {
            new object[] { "203.0.113.5", 7777, "6B01-RGA7-K1K4" },
            new object[] { "192.168.1.23", 7777, "60N0-0HE7-K12R" },
            new object[] { "198.51.100.200", 50000, "666D-JCHG-TGS5" },
            new object[] { "0.0.0.0", 0, "0000-0000-0000" },
            new object[] { "255.255.255.255", 65535, "7ZZZ-ZZZZ-ZZ9V" },
            new object[] { "100.64.0.1", 7777, "3480-0027-K1G7" },
        };

        // ---------- §1.7 テストベクタ ----------

        [TestCaseSource(nameof(Vectors))]
        public void Encode_MatchesSpecVector(string ip, int port, string expected)
        {
            Assert.That(JoinCodeCodec.Encode(ip, port), Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(Vectors))]
        public void Decode_RestoresOriginalEndpoint(string ip, int port, string code)
        {
            var result = JoinCodeCodec.Decode(code);

            Assert.That(result.Version, Is.EqualTo(0));
            Assert.That(result.Ip, Is.EqualTo(ip));
            Assert.That(result.Port, Is.EqualTo(port));
        }

        [TestCaseSource(nameof(Vectors))]
        public void Decode_AcceptsCodeWithoutHyphens(string ip, int port, string code)
        {
            var result = JoinCodeCodec.Decode(code.Replace("-", string.Empty));

            Assert.That(result.Ip, Is.EqualTo(ip));
            Assert.That(result.Port, Is.EqualTo(port));
        }

        // ---------- §1.6 正規化 ----------

        [TestCase("6b01-rga7-k1k4", TestName = "Normalize_Lowercase")]
        [TestCase("6B01 RGA7 K1K4", TestName = "Normalize_SpaceSeparated")]
        [TestCase("6BO1-RGA7-K1K4", TestName = "Normalize_OInsteadOfZero")]
        [TestCase("6B0I-RGA7-KIK4", TestName = "Normalize_IInsteadOfOne")]
        [TestCase("6B0L-RGA7-KLK4", TestName = "Normalize_LInsteadOfOne")]
        [TestCase("  6B01-RGA7-K1K4  ", TestName = "Normalize_SurroundingWhitespace")]
        [TestCase("6b0l rgA7 kIk4", TestName = "Normalize_Mixed")]
        [TestCase("６Ｂ０１－ＲＧＡ７－Ｋ１Ｋ４", TestName = "Normalize_FullWidth")]
        [TestCase("６ｂ０１－ｒｇａ７－ｋ１ｋ４", TestName = "Normalize_FullWidthLowercase")]
        [TestCase("６Ｂ０１　ＲＧＡ７　Ｋ１Ｋ４", TestName = "Normalize_FullWidthIdeographicSpace")]
        [TestCase("６ＢＯ１－ＲＧＡ７－ＫＩＫ４", TestName = "Normalize_FullWidthWithOAndI")]
        public void Normalize_ProducesCanonicalForm(string input)
        {
            Assert.That(JoinCodeCodec.Normalize(input), Is.EqualTo(SampleCode));
        }

        [TestCase("6b01-rga7-k1k4")]
        [TestCase("6BO1-RGA7-K1K4")]
        [TestCase("6B0L-RGA7-KLK4")]
        [TestCase("６Ｂ０１－ＲＧＡ７－Ｋ１Ｋ４")]
        public void Decode_AcceptsNormalizableInput(string input)
        {
            var result = JoinCodeCodec.Decode(input);

            Assert.That(result.Ip, Is.EqualTo("203.0.113.5"));
            Assert.That(result.Port, Is.EqualTo(7777));
        }

        // ---------- 不正入力 ----------

        [TestCase("6B01RGA7K1K", TestName = "Decode_ElevenCharacters")]
        [TestCase("6B01RGA7K1K44", TestName = "Decode_ThirteenCharacters")]
        [TestCase("", TestName = "Decode_Empty")]
        [TestCase("----", TestName = "Decode_HyphensOnly")]
        public void Decode_RejectsWrongLength(string input)
        {
            var ex = Assert.Throws<JoinCodeException>(() => JoinCodeCodec.Decode(input));
            Assert.That(ex.Error, Is.EqualTo(JoinCodeError.InvalidLength));
        }

        [TestCase("6B01RGA7K1KU", TestName = "Decode_ExcludedLetterU")]
        [TestCase("6B01RGA7K1K*", TestName = "Decode_Symbol")]
        [TestCase("6B01RGA7K1K@", TestName = "Decode_AtSign")]
        public void Decode_RejectsInvalidCharacter(string input)
        {
            var ex = Assert.Throws<JoinCodeException>(() => JoinCodeCodec.Decode(input));
            Assert.That(ex.Error, Is.EqualTo(JoinCodeError.InvalidCharacter));
        }

        [Test]
        public void Normalize_RejectsNull()
        {
            var ex = Assert.Throws<JoinCodeException>(() => JoinCodeCodec.Normalize(null));
            Assert.That(ex.Error, Is.EqualTo(JoinCodeError.InvalidLength));
        }

        [Test]
        public void Decode_DetectsEverySingleCharacterSubstitution()
        {
            for (var position = 0; position < SampleCode.Length; position++)
            {
                foreach (var replacement in Alphabet)
                {
                    if (replacement == SampleCode[position])
                    {
                        continue;
                    }

                    var builder = new StringBuilder(SampleCode);
                    builder[position] = replacement;
                    var mutated = builder.ToString();

                    Assert.Throws<JoinCodeException>(
                        () => JoinCodeCodec.Decode(mutated),
                        "1 文字置換 (位置 " + position + ") が検出されませんでした: " + mutated);
                }
            }
        }

        [Test]
        public void Decode_DetectsEveryAdjacentTransposition()
        {
            for (var position = 0; position < SampleCode.Length - 1; position++)
            {
                if (SampleCode[position] == SampleCode[position + 1])
                {
                    continue;
                }

                var builder = new StringBuilder(SampleCode);
                builder[position] = SampleCode[position + 1];
                builder[position + 1] = SampleCode[position];
                var mutated = builder.ToString();

                Assert.Throws<JoinCodeException>(
                    () => JoinCodeCodec.Decode(mutated),
                    "隣接 2 文字の入れ替え (位置 " + position + "/" + (position + 1) + ") が検出されませんでした: " + mutated);
            }
        }

        [TestCase(1021)]
        [TestCase(1022)]
        [TestCase(1023)]
        public void Decode_RejectsCheckValueOutOfRange(int check)
        {
            // V は正規のペイロード (203.0.113.5:7777) を使い、check だけを 1021..1023 に差し替える。
            const ulong v = 0x0CB0071051E61UL;
            var w = (v << 10) | (uint)check;

            var ex = Assert.Throws<JoinCodeException>(() => JoinCodeCodec.Decode(ToRawCode(w)));
            Assert.That(ex.Error, Is.EqualTo(JoinCodeError.CheckOutOfRange));
        }

        [Test]
        public void Decode_RejectsChecksumMismatch()
        {
            // check を 1020 以下の別の値に差し替えると、チェック不一致として検出される。
            const ulong v = 0x0CB0071051E61UL;
            var w = (v << 10) | 613UL; // 正しい check は 612

            var ex = Assert.Throws<JoinCodeException>(() => JoinCodeCodec.Decode(ToRawCode(w)));
            Assert.That(ex.Error, Is.EqualTo(JoinCodeError.ChecksumMismatch));
        }

        // ---------- TryDecode（例外を投げない API） ----------

        [TestCaseSource(nameof(Vectors))]
        public void TryDecode_ReturnsTrueForValidCode(string ip, int port, string code)
        {
            var succeeded = JoinCodeCodec.TryDecode(code, out var result, out var error);

            Assert.That(succeeded, Is.True);
            Assert.That(error, Is.EqualTo(JoinCodeError.None));
            Assert.That(result.Version, Is.EqualTo(0));
            Assert.That(result.Ip, Is.EqualTo(ip));
            Assert.That(result.Port, Is.EqualTo(port));
        }

        [TestCase("6B01RGA7K1K", JoinCodeError.InvalidLength, TestName = "TryDecode_WrongLength")]
        [TestCase("6B01RGA7K1KU", JoinCodeError.InvalidCharacter, TestName = "TryDecode_InvalidCharacter")]
        [TestCase("6B01RGA7K1K5", JoinCodeError.ChecksumMismatch, TestName = "TryDecode_ChecksumMismatch")]
        [TestCase((string)null, JoinCodeError.InvalidLength, TestName = "TryDecode_Null")]
        public void TryDecode_ReturnsFalseWithReason(string code, JoinCodeError expected)
        {
            var succeeded = JoinCodeCodec.TryDecode(code, out var result, out var error);

            Assert.That(succeeded, Is.False);
            Assert.That(error, Is.EqualTo(expected));
            Assert.That(result, Is.EqualTo(default((int Version, string Ip, int Port))));
        }

        [Test]
        public void TryDecode_ReturnsFalseWhenCheckValueOutOfRange()
        {
            const ulong v = 0x0CB0071051E61UL;
            var w = (v << 10) | 1021UL;

            var succeeded = JoinCodeCodec.TryDecode(ToRawCode(w), out _, out var error);

            Assert.That(succeeded, Is.False);
            Assert.That(error, Is.EqualTo(JoinCodeError.CheckOutOfRange));
        }

        // ---------- プロパティテスト ----------

        [Test]
        public void EncodeDecode_RoundTripsForRandomEndpoints()
        {
            var random = new Random(20260913); // 再現性のため固定シード

            for (var i = 0; i < 1000; i++)
            {
                var ip = random.Next(256) + "." + random.Next(256) + "." + random.Next(256) + "." + random.Next(256);
                var port = random.Next(0, 65536);

                var code = JoinCodeCodec.Encode(ip, port);
                var result = JoinCodeCodec.Decode(code);

                Assert.That(result.Version, Is.EqualTo(0), "version 不一致: " + ip + ":" + port + " -> " + code);
                Assert.That(result.Ip, Is.EqualTo(ip), "ip 不一致: " + ip + ":" + port + " -> " + code);
                Assert.That(result.Port, Is.EqualTo(port), "port 不一致: " + ip + ":" + port + " -> " + code);
            }
        }

        [Test]
        public void Encode_AlwaysProducesTwelveAlphabetCharacters()
        {
            var random = new Random(7777);

            for (var i = 0; i < 200; i++)
            {
                var ip = random.Next(256) + "." + random.Next(256) + "." + random.Next(256) + "." + random.Next(256);
                var code = JoinCodeCodec.Encode(ip, random.Next(0, 65536));

                Assert.That(code.Length, Is.EqualTo(14), "表示形式が XXXX-XXXX-XXXX ではありません: " + code);
                Assert.That(code[4], Is.EqualTo('-'));
                Assert.That(code[9], Is.EqualTo('-'));

                foreach (var ch in code.Replace("-", string.Empty))
                {
                    Assert.That(Alphabet.IndexOf(ch), Is.GreaterThanOrEqualTo(0), "許可外の文字が含まれます: " + code);
                }
            }
        }

        // ---------- Encode の入力検証 ----------

        [TestCase((string)null, TestName = "Encode_NullIp")]
        [TestCase("", TestName = "Encode_EmptyIp")]
        [TestCase("203.0.113", TestName = "Encode_TooFewOctets")]
        [TestCase("203.0.113.5.9", TestName = "Encode_TooManyOctets")]
        [TestCase("203.0.113.256", TestName = "Encode_OctetOutOfRange")]
        [TestCase("203.0.113.-1", TestName = "Encode_NegativeOctet")]
        [TestCase("203.0.113.x", TestName = "Encode_NonNumericOctet")]
        [TestCase("::1", TestName = "Encode_Ipv6")]
        [TestCase("203.007.113.5", TestName = "Encode_LeadingZeroOctet")]
        [TestCase("203.0.113. 5", TestName = "Encode_WhitespaceInOctet")]
        public void Encode_RejectsInvalidIp(string ip)
        {
            var ex = Assert.Throws<JoinCodeException>(() => JoinCodeCodec.Encode(ip, 7777));
            Assert.That(ex.Error, Is.EqualTo(JoinCodeError.InvalidAddress));
        }

        [TestCase(-1)]
        [TestCase(65536)]
        public void Encode_RejectsInvalidPort(int port)
        {
            var ex = Assert.Throws<JoinCodeException>(() => JoinCodeCodec.Encode("203.0.113.5", port));
            Assert.That(ex.Error, Is.EqualTo(JoinCodeError.InvalidPort));
        }

        [TestCase(-1)]
        [TestCase(4)]
        public void Encode_RejectsInvalidVersion(int version)
        {
            var ex = Assert.Throws<JoinCodeException>(() => JoinCodeCodec.Encode("203.0.113.5", 7777, version));
            Assert.That(ex.Error, Is.EqualTo(JoinCodeError.InvalidVersion));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void EncodeDecode_PreservesFutureVersionBits(int version)
        {
            var code = JoinCodeCodec.Encode("203.0.113.5", 7777, version);
            var result = JoinCodeCodec.Decode(code);

            Assert.That(result.Version, Is.EqualTo(version));
            Assert.That(result.Ip, Is.EqualTo("203.0.113.5"));
            Assert.That(result.Port, Is.EqualTo(7777));
        }

        // ---------- TryNormalizePartial（入力途中のプレビュー用、長さチェックなし） ----------

        [TestCase("", "", TestName = "TryNormalizePartial_Empty")]
        [TestCase((string)null, "", TestName = "TryNormalizePartial_Null")]
        [TestCase("6", "6", TestName = "TryNormalizePartial_OneCharacter")]
        [TestCase("6b0", "6B0", TestName = "TryNormalizePartial_Lowercase")]
        [TestCase("6b0-", "6B0", TestName = "TryNormalizePartial_TrailingHyphenStripped")]
        [TestCase("6bo", "6B0", TestName = "TryNormalizePartial_OInsteadOfZero")]
        [TestCase("6bi", "6B1", TestName = "TryNormalizePartial_IInsteadOfOne")]
        [TestCase("6bl", "6B1", TestName = "TryNormalizePartial_LInsteadOfOne")]
        [TestCase("6B01RGA7K1K4", "6B01RGA7K1K4", TestName = "TryNormalizePartial_FullLengthUnchanged")]
        [TestCase("6B01RGA7K1K4X", "6B01RGA7K1K4X", TestName = "TryNormalizePartial_DoesNotEnforceLength")]
        [TestCase("６ｂ０", "6B0", TestName = "TryNormalizePartial_FullWidth")]
        public void TryNormalizePartial_NormalizesWithoutLengthCheck(string input, string expected)
        {
            var succeeded = JoinCodeCodec.TryNormalizePartial(input, out var normalized);

            Assert.That(succeeded, Is.True);
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [Test]
        public void TryNormalizePartial_MatchesNormalizeForCompleteInput()
        {
            const string input = "6b0l rgA7 kIk4";

            var succeeded = JoinCodeCodec.TryNormalizePartial(input, out var normalized);

            Assert.That(succeeded, Is.True);
            Assert.That(normalized, Is.EqualTo(JoinCodeCodec.Normalize(input)));
        }

        /// <summary>60bit の W を、チェックの妥当性に関わらず 12 文字へ符号化するテスト用ヘルパー。</summary>
        private static string ToRawCode(ulong w)
        {
            var chars = new char[12];
            for (var i = 0; i < 12; i++)
            {
                chars[i] = Alphabet[(int)((w >> (5 * (11 - i))) & 31)];
            }

            return new string(chars);
        }
    }
}
