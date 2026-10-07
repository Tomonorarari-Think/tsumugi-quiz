using System;
using System.Security.Cryptography;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 再接続トークン（<see cref="SessionToken"/>）と、そのハッシュ（<see cref="SessionTokenHash"/>）の
    /// 検証（issue #69、docs/network.md §2.3）。
    /// </summary>
    public class SessionTokenTests
    {
        private static SessionToken CreateToken()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                return SessionToken.CreateRandom(rng);
            }
        }

        [Test]
        public void Default_IsNone()
        {
            Assert.IsFalse(default(SessionToken).HasValue);
            Assert.IsFalse(SessionToken.None.HasValue);
            Assert.AreEqual(string.Empty, SessionToken.None.ToHex());
            Assert.AreEqual(0, SessionToken.None.ToBytes().Length);
        }

        [Test]
        public void CreateRandom_ProducesDistinct128BitTokens()
        {
            var first = CreateToken();
            var second = CreateToken();

            Assert.IsTrue(first.HasValue);
            Assert.AreEqual(16, SessionToken.ByteCount);
            Assert.AreEqual(SessionToken.ByteCount, first.ToBytes().Length);
            Assert.AreNotEqual(first, second, "毎回違うトークンが出るはず（128bit の衝突は事実上起きない）。");
        }

        [Test]
        public void CreateRandom_ThrowsForNullGenerator()
        {
            Assert.Throws<ArgumentNullException>(() => SessionToken.CreateRandom(null));
        }

        [Test]
        public void CreateRandom_AllZeroBytes_StillHasValue()
        {
            // 乱数として全 0 が出ても「トークン無し」と混同しないこと。
            using (var rng = new ZeroRandomNumberGenerator())
            {
                var token = SessionToken.CreateRandom(rng);

                Assert.IsTrue(token.HasValue);
                Assert.AreEqual(new string('0', SessionToken.HexLength), token.ToHex());
                Assert.AreNotEqual(SessionToken.None, token);
            }
        }

        [Test]
        public void ToHex_Then_TryParseHex_RoundTrips()
        {
            var token = CreateToken();

            var hex = token.ToHex();
            Assert.AreEqual(SessionToken.HexLength, hex.Length);

            Assert.IsTrue(SessionToken.TryParseHex(hex, out var parsed));
            Assert.AreEqual(token, parsed);
        }

        [Test]
        public void TryParseHex_AcceptsUpperCase()
        {
            var token = CreateToken();

            Assert.IsTrue(SessionToken.TryParseHex(token.ToHex().ToUpperInvariant(), out var parsed));
            Assert.AreEqual(token, parsed);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("0123456789abcdef")]                          // 短すぎる（8 バイト分）
        [TestCase("0123456789abcdef0123456789abcdef0")]         // 長すぎる
        [TestCase("0123456789abcdef0123456789abcdeg")]          // 16 進でない文字
        [TestCase("0123456789abcdef0123456789abcde ")]          // 空白
        public void TryParseHex_RejectsInvalidInput(string hex)
        {
            Assert.IsFalse(SessionToken.TryParseHex(hex, out var token));
            Assert.IsFalse(token.HasValue);
        }

        [Test]
        public void TryFromBytes_RequiresExactLength()
        {
            var source = new byte[SessionToken.ByteCount + 4];
            source[0] = 0xAB;

            Assert.IsTrue(SessionToken.TryFromBytes(source, 0, out var token));
            Assert.IsTrue(token.HasValue);

            Assert.IsFalse(SessionToken.TryFromBytes(source, 5, out _), "末尾まで 16 バイト無ければ失敗。");
            Assert.IsFalse(SessionToken.TryFromBytes(source, -1, out _));
            Assert.IsFalse(SessionToken.TryFromBytes(null, 0, out _));
            Assert.IsFalse(SessionToken.TryFromBytes(new byte[SessionToken.ByteCount - 1], 0, out _));
        }

        [Test]
        public void TryWriteTo_RoundTripsThroughBuffer()
        {
            var token = CreateToken();
            var buffer = new byte[SessionToken.ByteCount + 2];

            Assert.IsTrue(token.TryWriteTo(buffer, 2));
            Assert.IsTrue(SessionToken.TryFromBytes(buffer, 2, out var restored));
            Assert.AreEqual(token, restored);

            Assert.IsFalse(token.TryWriteTo(buffer, 3), "領域が足りなければ書かない。");
            Assert.IsFalse(SessionToken.None.TryWriteTo(buffer, 0), "トークン無しは書けない。");
        }

        [Test]
        public void ToBytes_ReturnsDefensiveCopy()
        {
            var token = CreateToken();

            var bytes = token.ToBytes();
            bytes[0] ^= 0xFF;

            Assert.AreNotEqual(bytes[0], token.ToBytes()[0], "取り出した配列を書き換えても元の値は変わらない。");
        }

        [Test]
        public void ToString_DoesNotLeakTheToken()
        {
            var token = CreateToken();

            StringAssert.DoesNotContain(token.ToHex(), token.ToString());
        }

        [Test]
        public void Hash_MatchesOnlyItsOwnToken()
        {
            var token = CreateToken();
            var other = CreateToken();

            var hash = SessionTokenHash.Of(token);

            Assert.IsFalse(hash.IsEmpty);
            Assert.IsTrue(hash.Matches(token));
            Assert.IsFalse(hash.Matches(other));
            Assert.IsFalse(hash.Matches(SessionToken.None));
        }

        [Test]
        public void Hash_OfNoneToken_IsEmptyAndMatchesNothing()
        {
            var hash = SessionTokenHash.Of(SessionToken.None);

            Assert.IsTrue(hash.IsEmpty);
            Assert.IsTrue(SessionTokenHash.None.IsEmpty);
            Assert.IsFalse(hash.Matches(CreateToken()));
        }

        [Test]
        public void Hash_IsStableForTheSameToken()
        {
            var token = CreateToken();

            Assert.AreEqual(SessionTokenHash.Of(token), SessionTokenHash.Of(token));
            Assert.AreEqual(SessionTokenHash.Of(token).GetHashCode(), SessionTokenHash.Of(token).GetHashCode());
        }

        [Test]
        public void Hash_ToString_DoesNotLeakTheHash()
        {
            var hash = SessionTokenHash.Of(CreateToken());

            StringAssert.Contains("SessionTokenHash", hash.ToString());
        }

        /// <summary>常に 0 のバイト列を返す乱数源（全 0 トークンの扱いを検証するため）。</summary>
        private sealed class ZeroRandomNumberGenerator : RandomNumberGenerator
        {
            public override void GetBytes(byte[] data)
            {
                if (data == null)
                {
                    throw new ArgumentNullException(nameof(data));
                }

                Array.Clear(data, 0, data.Length);
            }
        }
    }
}
