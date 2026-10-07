using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="TermsHasher"/>（規約テキストの SHA-256 計算）を検証する。
    /// </summary>
    public class TermsHasherTests
    {
        [Test]
        public void ComputeSha256Hex_KnownInput_MatchesKnownTestVector()
        {
            // NIST が公開している既知テストベクタ: SHA-256("abc")
            var hash = TermsHasher.ComputeSha256Hex("abc");

            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        }

        [Test]
        public void ComputeSha256Hex_EmptyString_ReturnsA64CharHexString()
        {
            var hash = TermsHasher.ComputeSha256Hex(string.Empty);

            Assert.AreEqual(64, hash.Length);
            Assert.AreNotEqual(TermsHasher.ComputeSha256Hex("abc"), hash);
        }

        [Test]
        public void ComputeSha256Hex_SameInput_IsDeterministic()
        {
            var hash1 = TermsHasher.ComputeSha256Hex("同じ内容のテキスト");
            var hash2 = TermsHasher.ComputeSha256Hex("同じ内容のテキスト");

            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void ComputeSha256Hex_DifferentInput_ReturnsDifferentHash()
        {
            var hash1 = TermsHasher.ComputeSha256Hex("テキストA");
            var hash2 = TermsHasher.ComputeSha256Hex("テキストB");

            Assert.AreNotEqual(hash1, hash2);
        }

        [Test]
        public void ComputeSha256Hex_NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TermsHasher.ComputeSha256Hex(null));
        }

        [Test]
        public void ComputeSha256Hex_CrlfAndLf_ProduceSameHash()
        {
            // M-1: 改行コードの違い（Windows で保存し直した等）だけで再同意（FR-76）を
            // 誤って要求しないよう、CRLF と LF は同一ハッシュになる。
            var lf = "1行目\n2行目\n3行目";
            var crlf = "1行目\r\n2行目\r\n3行目";

            Assert.AreEqual(TermsHasher.ComputeSha256Hex(lf), TermsHasher.ComputeSha256Hex(crlf));
        }

        [Test]
        public void ComputeSha256Hex_LeadingBom_IsIgnored()
        {
            // M-1: 先頭 BOM の有無でハッシュが変わらないようにする。
            var withBom = "﻿規約本文";
            var withoutBom = "規約本文";

            Assert.AreEqual(TermsHasher.ComputeSha256Hex(withoutBom), TermsHasher.ComputeSha256Hex(withBom));
        }

        [Test]
        public void ComputeSha256HexForTermsBody_HeaderChange_DoesNotChangeHash()
        {
            // M-1: "---" より前（出典 URL・取得日等のヘッダー）を書き換えても、本文が同じならハッシュは変わらない。
            var original = "出典: https://example.com/a\n取得日: 2026-01-01\n\n---\n\n本文A\n本文B";
            var headerEdited = "出典: https://example.com/b（URL変更）\n取得日: 2026-09-13（更新）\n\n---\n\n本文A\n本文B";

            Assert.AreEqual(
                TermsHasher.ComputeSha256HexForTermsBody(original),
                TermsHasher.ComputeSha256HexForTermsBody(headerEdited));
        }

        [Test]
        public void ComputeSha256HexForTermsBody_BodyChange_ChangesHash()
        {
            var text1 = "ヘッダー\n\n---\n\n本文A";
            var text2 = "ヘッダー\n\n---\n\n本文B";

            Assert.AreNotEqual(
                TermsHasher.ComputeSha256HexForTermsBody(text1),
                TermsHasher.ComputeSha256HexForTermsBody(text2));
        }

        [Test]
        public void ComputeSha256HexForTermsBody_CrlfAndLf_ProduceSameHash()
        {
            var lf = "出典: dummy\n\n---\n\n本文A\n本文B";
            var crlf = "出典: dummy\r\n\r\n---\r\n\r\n本文A\r\n本文B";

            Assert.AreEqual(
                TermsHasher.ComputeSha256HexForTermsBody(lf),
                TermsHasher.ComputeSha256HexForTermsBody(crlf));
        }

        [Test]
        public void ComputeSha256HexForTermsBody_NoSeparator_FallsBackToWholeText()
        {
            // "---" 区切りが無いフォーマット不備のケースでも例外にはせず、全文を対象にする。
            var text = "区切りが無いテキスト";

            var hash = TermsHasher.ComputeSha256HexForTermsBody(text);

            Assert.AreEqual(TermsHasher.ComputeSha256Hex(text), hash);
        }

        [Test]
        public void ComputeSha256HexForTermsBody_NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TermsHasher.ComputeSha256HexForTermsBody(null));
        }
    }
}
