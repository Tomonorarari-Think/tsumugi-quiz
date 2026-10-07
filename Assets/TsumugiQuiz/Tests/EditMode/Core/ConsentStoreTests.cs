using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="ConsentStore"/> の同意保存・全同意判定・ハッシュ変化での再同意・撤回を検証する
    /// （requirements.md FR-71〜FR-76、issue #37 の受け入れ条件）。
    /// </summary>
    public class ConsentStoreTests
    {
        private const string AppVersion = "0.1.0";
        private static readonly DateTime AcceptedAtUtc = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

        private static IReadOnlyList<TermsDefinition> SingleTerm(string hash = "hash-a")
        {
            return new[] { new TermsDefinition("terms-a", hash) };
        }

        private static IReadOnlyList<TermsDefinition> TwoTerms()
        {
            return new[]
            {
                new TermsDefinition("terms-a", "hash-a"),
                new TermsDefinition("terms-b", "hash-b"),
            };
        }

        [Test]
        public void HasAcceptedAll_NoRecords_ReturnsFalse()
        {
            var store = new ConsentStore(new FakeConsentStorage());

            Assert.IsFalse(store.HasAcceptedAll(SingleTerm()));
        }

        [Test]
        public void RecordConsent_ThenHasAcceptedAll_ReturnsTrue()
        {
            var store = new ConsentStore(new FakeConsentStorage());
            var terms = SingleTerm();

            store.RecordConsent(terms, AppVersion, AcceptedAtUtc);

            Assert.IsTrue(store.HasAcceptedAll(terms));
        }

        [Test]
        public void RecordConsent_SavesRecordsWithGivenAcceptedAtAndAppVersion()
        {
            var storage = new FakeConsentStorage();
            var store = new ConsentStore(storage);
            var terms = SingleTerm();

            store.RecordConsent(terms, AppVersion, AcceptedAtUtc);

            var records = store.LoadRecords();
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("terms-a", records[0].TermsId);
            Assert.AreEqual("hash-a", records[0].Sha256Hash);
            Assert.AreEqual(AcceptedAtUtc, records[0].AcceptedAtUtc);
            Assert.AreEqual(AppVersion, records[0].AppVersion);
        }

        [Test]
        public void HasAcceptedAll_OneOfMultipleTermsMissing_ReturnsFalse()
        {
            var store = new ConsentStore(new FakeConsentStorage());
            store.RecordConsent(new[] { new TermsDefinition("terms-a", "hash-a") }, AppVersion, AcceptedAtUtc);

            // terms-b にはまだ同意していないため、2件要求すると false になる。
            Assert.IsFalse(store.HasAcceptedAll(TwoTerms()));
        }

        [Test]
        public void HasAcceptedAll_AllTermsAccepted_ReturnsTrue()
        {
            var store = new ConsentStore(new FakeConsentStorage());
            var terms = TwoTerms();

            store.RecordConsent(terms, AppVersion, AcceptedAtUtc);

            Assert.IsTrue(store.HasAcceptedAll(terms));
        }

        [Test]
        public void HasAcceptedAll_HashChanged_RequiresReconsent()
        {
            // FR-76: 同梱テキストが更新されて SHA-256 が変わった場合、既存の同意記録は無効化される。
            var store = new ConsentStore(new FakeConsentStorage());
            store.RecordConsent(SingleTerm("old-hash"), AppVersion, AcceptedAtUtc);

            var updatedTerms = SingleTerm("new-hash");

            Assert.IsFalse(store.HasAcceptedAll(updatedTerms));
        }

        [Test]
        public void Revoke_AfterConsent_HasAcceptedAllReturnsFalse()
        {
            // FR-75: 撤回すると次回判定（＝次回起動時の再提示判定）で未同意扱いになる。
            var store = new ConsentStore(new FakeConsentStorage());
            var terms = SingleTerm();
            store.RecordConsent(terms, AppVersion, AcceptedAtUtc);
            Assert.IsTrue(store.HasAcceptedAll(terms));

            store.Revoke();

            Assert.IsFalse(store.HasAcceptedAll(terms));
            Assert.AreEqual(0, store.LoadRecords().Count);
        }

        [Test]
        public void Revoke_WithNoExistingConsent_DoesNotThrow()
        {
            var store = new ConsentStore(new FakeConsentStorage());

            Assert.DoesNotThrow(() => store.Revoke());
        }

        [Test]
        public void Constructor_NullStorage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ConsentStore(null));
        }

        [Test]
        public void HasAcceptedAll_NullRequiredTerms_Throws()
        {
            var store = new ConsentStore(new FakeConsentStorage());

            Assert.Throws<ArgumentNullException>(() => store.HasAcceptedAll(null));
        }

        [Test]
        public void HasAcceptedAll_EmptyRequiredTerms_Throws()
        {
            var store = new ConsentStore(new FakeConsentStorage());

            Assert.Throws<ArgumentException>(() => store.HasAcceptedAll(Array.Empty<TermsDefinition>()));
        }

        [Test]
        public void RecordConsent_EmptyAppVersion_Throws()
        {
            var store = new ConsentStore(new FakeConsentStorage());

            Assert.Throws<ArgumentException>(() => store.RecordConsent(SingleTerm(), string.Empty, AcceptedAtUtc));
        }

        [Test]
        public void CorruptedStorage_LoadReturnsEmpty_TreatedAsNotConsented()
        {
            // JsonConsentStorage 側で「壊れた JSON は空扱いにする」契約を守っている前提で、
            // ConsentStore が空の記録を安全に「未同意」として扱えることを確認する（破損 JSON の扱い）。
            var storage = new FakeConsentStorage();
            var store = new ConsentStore(storage);

            // 何も保存していない状態 = 破損読み込み後の空リストと同じ状況を模している。
            Assert.IsFalse(store.HasAcceptedAll(SingleTerm()));
            Assert.AreEqual(0, store.LoadRecords().Count);
        }
    }
}
