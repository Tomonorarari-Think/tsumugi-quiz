using System;
using System.Security.Cryptography;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Tests.Shared.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// クライアント側の再接続トークン保管（<see cref="SessionTokenStore"/>）の検証
    /// （issue #69）。保存・取り出し・期限切れ・件数上限を網羅する。
    /// </summary>
    public class SessionTokenStoreTests
    {
        private static readonly DateTime Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private const string HostA = "192.168.0.2:7777";
        private const string HostB = "203.0.113.9:7777";

        private static SessionToken CreateToken()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                return SessionToken.CreateRandom(rng);
            }
        }

        [Test]
        public void Constructor_RejectsNullStorageAndNonPositiveLifetime()
        {
            Assert.Throws<ArgumentNullException>(() => new SessionTokenStore(null));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SessionTokenStore(new FakeSessionTokenStorage(), TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SessionTokenStore(new FakeSessionTokenStorage(), TimeSpan.FromHours(-1)));
        }

        [Test]
        public void DefaultLifetime_Is24Hours()
        {
            var store = new SessionTokenStore(new FakeSessionTokenStorage());

            Assert.AreEqual(TimeSpan.FromHours(24), store.Lifetime);
            Assert.AreEqual(24.0, SessionTokenStore.DefaultLifetimeHours, 1e-9);
        }

        [Test]
        public void Save_Then_TryGet_ReturnsTheSameToken()
        {
            var storage = new FakeSessionTokenStorage();
            var store = new SessionTokenStore(storage);
            var token = CreateToken();

            Assert.IsTrue(store.Save(HostA, token, Now));
            Assert.IsTrue(store.TryGet(HostA, Now, out var restored));
            Assert.AreEqual(token, restored);
            Assert.AreEqual(1, storage.Count);
        }

        [Test]
        public void TryGet_ForUnknownHost_ReturnsFalse()
        {
            var store = new SessionTokenStore(new FakeSessionTokenStorage());
            store.Save(HostA, CreateToken(), Now);

            Assert.IsFalse(store.TryGet(HostB, Now, out var token));
            Assert.IsFalse(token.HasValue);
        }

        [Test]
        public void TryGet_AfterExpiry_ReturnsFalse()
        {
            var store = new SessionTokenStore(new FakeSessionTokenStorage(), TimeSpan.FromHours(24));
            store.Save(HostA, CreateToken(), Now);

            Assert.IsTrue(store.TryGet(HostA, Now.AddHours(23.9), out _), "期限内なら取り出せる。");
            Assert.IsFalse(store.TryGet(HostA, Now.AddHours(24.1), out _), "24 時間を過ぎたら使わない。");
        }

        [Test]
        public void Save_ReplacesTheTokenOfTheSameHost()
        {
            var storage = new FakeSessionTokenStorage();
            var store = new SessionTokenStore(storage);

            store.Save(HostA, CreateToken(), Now);
            var latest = CreateToken();
            store.Save(HostA, latest, Now.AddMinutes(1));

            Assert.AreEqual(1, storage.Count, "同じホストのレコードは 1 件だけ持つ。");
            Assert.IsTrue(store.TryGet(HostA, Now.AddMinutes(2), out var restored));
            Assert.AreEqual(latest, restored);
        }

        [Test]
        public void Save_KeepsTokensOfOtherHosts()
        {
            var store = new SessionTokenStore(new FakeSessionTokenStorage());
            var tokenA = CreateToken();
            var tokenB = CreateToken();

            store.Save(HostA, tokenA, Now);
            store.Save(HostB, tokenB, Now);

            Assert.IsTrue(store.TryGet(HostA, Now, out var restoredA));
            Assert.IsTrue(store.TryGet(HostB, Now, out var restoredB));
            Assert.AreEqual(tokenA, restoredA);
            Assert.AreEqual(tokenB, restoredB);
        }

        [Test]
        public void Save_DropsExpiredRecords()
        {
            var storage = new FakeSessionTokenStorage();
            var store = new SessionTokenStore(storage);

            store.Save(HostA, CreateToken(), Now);
            store.Save(HostB, CreateToken(), Now.AddHours(25));

            Assert.AreEqual(1, storage.Count, "期限切れのレコードは保存のついでに捨てる。");
            Assert.IsFalse(store.TryGet(HostA, Now.AddHours(25), out _));
        }

        [Test]
        public void Save_RejectsInvalidHostKeyOrToken()
        {
            var storage = new FakeSessionTokenStorage();
            var store = new SessionTokenStore(storage);

            Assert.IsFalse(store.Save(null, CreateToken(), Now));
            Assert.IsFalse(store.Save(string.Empty, CreateToken(), Now));
            Assert.IsFalse(store.Save("ポートが無い", CreateToken(), Now));
            Assert.IsFalse(store.Save(HostA, SessionToken.None, Now));
            Assert.AreEqual(0, storage.SaveCount, "不正な入力では保存しない。");
        }

        [Test]
        public void Remove_DeletesOnlyTheGivenHost()
        {
            var store = new SessionTokenStore(new FakeSessionTokenStorage());
            store.Save(HostA, CreateToken(), Now);
            store.Save(HostB, CreateToken(), Now);

            Assert.IsTrue(store.Remove(HostA, Now));
            Assert.IsFalse(store.TryGet(HostA, Now, out _));
            Assert.IsTrue(store.TryGet(HostB, Now, out _));

            Assert.IsFalse(store.Remove(HostA, Now), "既に無いものは false。");
        }

        [Test]
        public void LoadValid_SkipsBrokenAndExpiredRecords()
        {
            var storage = new FakeSessionTokenStorage();
            storage.Seed(
                new SessionTokenRecord(HostA, CreateToken(), Now.AddHours(1)),    // 有効
                new SessionTokenRecord(HostB, CreateToken(), Now.AddHours(-1)),   // 期限切れ
                new SessionTokenRecord("キーが不正", CreateToken(), Now.AddHours(1)),
                new SessionTokenRecord(HostB, SessionToken.None, Now.AddHours(1)));

            var store = new SessionTokenStore(storage);
            var valid = store.LoadValid(Now);

            Assert.AreEqual(1, valid.Count);
            Assert.AreEqual(HostA, valid[0].HostKey);
        }

        [Test]
        public void Save_TrimsToMaxRecordCount()
        {
            var storage = new FakeSessionTokenStorage();
            var store = new SessionTokenStore(storage);

            for (var i = 0; i < SessionTokenStore.MaxRecordCount + 5; i++)
            {
                // 少しずつ時刻をずらして保存すると、期限も順にずれる。
                store.Save($"10.0.0.{i}:7777", CreateToken(), Now.AddSeconds(i));
            }

            Assert.AreEqual(SessionTokenStore.MaxRecordCount, storage.Count);

            // 最後に保存したものは残っている。
            var lastHost = $"10.0.0.{SessionTokenStore.MaxRecordCount + 4}:7777";
            Assert.IsTrue(store.TryGet(lastHost, Now.AddMinutes(1), out _));
        }

        [Test]
        public void HostKey_IsBuiltFromAddressAndPort()
        {
            Assert.IsTrue(SessionTokenHostKey.TryCreate("192.168.0.2", 7777, out var key));
            Assert.AreEqual("192.168.0.2:7777", key);

            Assert.IsTrue(SessionTokenHostKey.TryCreate("  ExAmple.Local  ", 7777, out var normalized));
            Assert.AreEqual("example.local:7777", normalized, "前後の空白と大文字小文字を正規化する。");

            Assert.IsFalse(SessionTokenHostKey.TryCreate(null, 7777, out _));
            Assert.IsFalse(SessionTokenHostKey.TryCreate("   ", 7777, out _));
            Assert.IsFalse(SessionTokenHostKey.TryCreate("192.168.0.2", 0, out _));
            Assert.IsFalse(SessionTokenHostKey.TryCreate(new string('a', SessionTokenHostKey.MaxLength), 7777, out _));
        }

        [Test]
        public void Record_NormalizesTimeToUtc()
        {
            var local = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
            var record = new SessionTokenRecord(HostA, CreateToken(), local);

            Assert.AreEqual(DateTimeKind.Utc, record.ExpiresAtUtc.Kind);
            Assert.AreEqual(local.ToUniversalTime(), record.ExpiresAtUtc, "Local は UTC へ変換する。");
            Assert.IsTrue(record.IsValid);
        }

        [Test]
        public void Record_TreatsUnspecifiedKindAsUtc()
        {
            // レビュー L4: 保存ファイル（手編集された JSON など）から来る Unspecified は
            // ローカル時刻ではなく UTC として解釈する（環境のタイムゾーンで期限が変わらないように）。
            var unspecified = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
            var record = new SessionTokenRecord(HostA, CreateToken(), unspecified);

            Assert.AreEqual(DateTimeKind.Utc, record.ExpiresAtUtc.Kind);
            Assert.AreEqual(unspecified.Ticks, record.ExpiresAtUtc.Ticks, "時刻の値そのものは動かさない。");

            // 判定側（IsAliveAt / ストア）でも同じ解釈になること。
            Assert.IsTrue(record.IsAliveAt(new DateTime(2026, 1, 1, 11, 59, 0, DateTimeKind.Unspecified)));
            Assert.IsFalse(record.IsAliveAt(new DateTime(2026, 1, 1, 12, 1, 0, DateTimeKind.Unspecified)));

            var storage = new FakeSessionTokenStorage();
            storage.Seed(record);
            var store = new SessionTokenStore(storage);

            Assert.IsTrue(store.TryGet(HostA, new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Unspecified), out _));
            Assert.IsFalse(store.TryGet(HostA, new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Unspecified), out _));
        }
    }
}
