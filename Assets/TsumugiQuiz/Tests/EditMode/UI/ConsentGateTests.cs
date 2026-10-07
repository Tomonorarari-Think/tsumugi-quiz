using System;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Tests.EditMode.Core;
using TsumugiQuiz.UI;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// <see cref="ConsentGate.HasUserConsented"/> を、<see cref="ConsentGate.SetStorageFactoryForTesting"/>
    /// で永続化をフェイクに差し替えて検証する（M-7）。実ファイル（consent.json）には触れない。
    /// </summary>
    public class ConsentGateTests
    {
        [TearDown]
        public void ResetStorageFactory()
        {
            // 他のテスト（実際の JsonConsentStorage を使うもの）に影響を残さないよう、必ず既定に戻す。
            ConsentGate.SetStorageFactoryForTesting(null);
        }

        [Test]
        public void HasUserConsented_NoRecords_ReturnsFalse()
        {
            var fake = new FakeConsentStorage();
            ConsentGate.SetStorageFactoryForTesting(() => fake);

            Assert.IsFalse(ConsentGate.HasUserConsented());
        }

        [Test]
        public void HasUserConsented_AllCurrentTermsAccepted_ReturnsTrue()
        {
            var fake = new FakeConsentStorage();
            ConsentGate.SetStorageFactoryForTesting(() => fake);
            var store = new ConsentStore(fake);

            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), "0.1.0", DateTime.UtcNow);

            Assert.IsTrue(ConsentGate.HasUserConsented());
        }

        [Test]
        public void HasUserConsented_RecordedWithStaleHash_ReturnsFalse()
        {
            // consent.json 相当のレコードは存在するが、ハッシュが現在の規約テキストと一致しない状態
            // （規約テキストが更新された、いわば FR-76 の再同意条件）を模す。
            var fake = new FakeConsentStorage();
            ConsentGate.SetStorageFactoryForTesting(() => fake);
            var store = new ConsentStore(fake);

            var staleTerms = new List<TermsDefinition>();
            foreach (var entry in TermsCatalog.Entries)
            {
                staleTerms.Add(new TermsDefinition(entry.TermsId, "stale-hash-does-not-match-current-text"));
            }

            store.RecordConsent(staleTerms, "0.1.0", DateTime.UtcNow);

            Assert.IsFalse(ConsentGate.HasUserConsented());
        }

        [Test]
        public void HasUserConsented_AfterRevoke_ReturnsFalse()
        {
            var fake = new FakeConsentStorage();
            ConsentGate.SetStorageFactoryForTesting(() => fake);
            var store = new ConsentStore(fake);
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), "0.1.0", DateTime.UtcNow);
            Assert.IsTrue(ConsentGate.HasUserConsented());

            store.Revoke();

            Assert.IsFalse(ConsentGate.HasUserConsented());
        }

        [Test]
        public void SetStorageFactoryForTesting_Null_RestoresDefaultFactory()
        {
            var fake = new FakeConsentStorage();
            ConsentGate.SetStorageFactoryForTesting(() => fake);
            ConsentGate.SetStorageFactoryForTesting(null);

            // 既定に戻った後は JsonConsentStorage を使うため、フェイクへの書き込みは反映されないはず。
            Assert.AreEqual(0, fake.SaveCallCount);
            Assert.DoesNotThrow(() => ConsentGate.CreateDefaultStore());
        }
    }
}
