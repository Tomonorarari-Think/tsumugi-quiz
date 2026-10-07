using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="JoinCodeError"/> → 日本語メッセージの対応表のテスト（issue #6）。
    /// Join View は <see cref="JoinCodeCodec.TryDecode"/> の戻り値からこの対応表でメッセージを作る。
    /// </summary>
    public class JoinCodeErrorMessagesTests
    {
        [Test]
        public void Create_None_ReturnsEmpty()
        {
            Assert.That(JoinCodeErrorMessages.Create(JoinCodeError.None), Is.Empty);
        }

        [TestCase(JoinCodeError.InvalidLength)]
        [TestCase(JoinCodeError.InvalidCharacter)]
        [TestCase(JoinCodeError.CheckOutOfRange)]
        [TestCase(JoinCodeError.ChecksumMismatch)]
        public void Create_KnownDecodeErrors_ReturnsNonEmptyMessage(JoinCodeError error)
        {
            var message = JoinCodeErrorMessages.Create(error);

            Assert.That(message, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Create_InvalidLength_MentionsLength()
        {
            StringAssert.Contains("長さ", JoinCodeErrorMessages.Create(JoinCodeError.InvalidLength));
        }

        [Test]
        public void Create_InvalidCharacter_MentionsCharacter()
        {
            StringAssert.Contains("文字", JoinCodeErrorMessages.Create(JoinCodeError.InvalidCharacter));
        }

        [TestCase(JoinCodeError.CheckOutOfRange)]
        [TestCase(JoinCodeError.ChecksumMismatch)]
        public void Create_ChecksumRelatedErrors_ProduceSameGenericMessage(JoinCodeError error)
        {
            Assert.That(JoinCodeErrorMessages.Create(error), Is.EqualTo(JoinCodeErrorMessages.InvalidChecksum));
        }

        [TestCase(JoinCodeError.InvalidAddress)]
        [TestCase(JoinCodeError.InvalidPort)]
        [TestCase(JoinCodeError.InvalidVersion)]
        public void Create_EncodeOnlyErrors_FallBackToGenericMessage(JoinCodeError error)
        {
            // Decode 経路では発生しないが、想定外の値が来ても例外を投げずに定型文を返すことを確認する。
            Assert.That(JoinCodeErrorMessages.Create(error), Is.EqualTo(JoinCodeErrorMessages.InvalidChecksum));
        }

        [Test]
        public void Create_ForEveryTryDecodeFailure_MatchesMapping()
        {
            // 実際の TryDecode 失敗パターンを通し、返ってきた JoinCodeError が
            // 期待どおりのメッセージに変換されることを確認する（回帰防止）。
            Assert.That(JoinCodeCodec.TryDecode("6B01RGA7K1K", out _, out var lengthError), Is.False);
            Assert.That(JoinCodeErrorMessages.Create(lengthError), Is.EqualTo(JoinCodeErrorMessages.InvalidLength));

            Assert.That(JoinCodeCodec.TryDecode("6B01RGA7K1KU", out _, out var charError), Is.False);
            Assert.That(JoinCodeErrorMessages.Create(charError), Is.EqualTo(JoinCodeErrorMessages.InvalidCharacter));

            Assert.That(JoinCodeCodec.TryDecode("6B01RGA7K1K5", out _, out var checksumError), Is.False);
            Assert.That(JoinCodeErrorMessages.Create(checksumError), Is.EqualTo(JoinCodeErrorMessages.InvalidChecksum));
        }

        /// <summary>
        /// M-1: <see cref="JoinCodeError"/> の全値（<see cref="Enum.GetValues"/> で列挙）を
        /// <see cref="JoinCodeErrorMessages.Create"/> に通し、例外を投げないこと・
        /// None だけが空文字を返すことを網羅的に確認する（新しい列挙値の追加漏れを防ぐ）。
        /// </summary>
        [Test]
        public void Create_CoversEveryJoinCodeErrorEnumValue()
        {
            foreach (JoinCodeError error in Enum.GetValues(typeof(JoinCodeError)))
            {
                string message = null;
                Assert.DoesNotThrow(() => message = JoinCodeErrorMessages.Create(error), "error=" + error);

                if (error == JoinCodeError.None)
                {
                    Assert.That(message, Is.Empty, "None は空文字を返すはずです。");
                }
                else
                {
                    Assert.That(message, Is.Not.Null.And.Not.Empty, "error=" + error + " のメッセージが空です。");
                }
            }
        }

        [Test]
        public void TryDecode_MessageOverload_MatchesErrorMapping()
        {
            // M-2: メッセージ付きオーバーロードが返す文言も、JoinCodeErrorMessages と一致するはず
            // （codec 側の内部文言と対応表が食い違わないことの回帰テスト）。
            Assert.That(JoinCodeCodec.TryDecode("6B01RGA7K1K", out _, out var lengthError, out var lengthMessage), Is.False);
            Assert.That(lengthMessage, Is.EqualTo(JoinCodeErrorMessages.Create(lengthError)));

            Assert.That(JoinCodeCodec.TryDecode("6B01RGA7K1KU", out _, out var charError, out var charMessage), Is.False);
            Assert.That(charMessage, Is.EqualTo(JoinCodeErrorMessages.Create(charError)));

            Assert.That(JoinCodeCodec.TryDecode("6B01RGA7K1K5", out _, out var checksumError, out var checksumMessage), Is.False);
            Assert.That(checksumMessage, Is.EqualTo(JoinCodeErrorMessages.Create(checksumError)));

            Assert.That(JoinCodeCodec.TryDecode("6B01-RGA7-K1K4", out _, out var noError, out var okMessage), Is.True);
            Assert.That(noError, Is.EqualTo(JoinCodeError.None));
            Assert.That(okMessage, Is.Empty);
        }
    }
}
