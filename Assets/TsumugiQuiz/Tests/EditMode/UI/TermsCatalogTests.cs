using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.UI;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// <see cref="TermsCatalog"/> による規約リソース（Resources/Terms/*.txt）の読み込みと
    /// SHA-256 ハッシュ計算を検証する（issue #37 の受け入れ条件: 規約リソースの読み込み・ハッシュ計算）。
    /// </summary>
    public class TermsCatalogTests
    {
        private static readonly IReadOnlyList<string> ExpectedTermsIds = new[]
        {
            "voicevox-models-terms",
            "voicevox-onnxruntime-terms",
            "tsumugi-voice-credit",
            "tsumugi-illustration-terms",
        };

        [Test]
        public void Entries_ContainsExactlyTheFourRequiredDocuments()
        {
            var ids = TermsCatalog.Entries.Select(e => e.TermsId).ToList();

            CollectionAssert.AreEquivalent(ExpectedTermsIds, ids);
        }

        [Test]
        public void LoadText_EachEntry_ReturnsNonEmptyText()
        {
            foreach (var entry in TermsCatalog.Entries)
            {
                var text = TermsCatalog.LoadText(entry);

                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{entry.TermsId} のテキストが空です。Resources/{entry.ResourcePath}.txt を確認してください。");
            }
        }

        [Test]
        public void LoadRequiredTerms_ReturnsOneDefinitionPerEntry_WithA64CharHash()
        {
            var required = TermsCatalog.LoadRequiredTerms();

            Assert.AreEqual(TermsCatalog.Entries.Count, required.Count);
            foreach (var definition in required)
            {
                Assert.IsFalse(string.IsNullOrEmpty(definition.TermsId));
                Assert.AreEqual(64, definition.Sha256Hash.Length, $"{definition.TermsId} のハッシュ長が想定と異なります。");
            }
        }

        [Test]
        public void LoadRequiredTerms_IsDeterministic_AcrossCalls()
        {
            var first = TermsCatalog.LoadRequiredTerms();
            var second = TermsCatalog.LoadRequiredTerms();

            for (var i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].TermsId, second[i].TermsId);
                Assert.AreEqual(first[i].Sha256Hash, second[i].Sha256Hash);
            }
        }

        [Test]
        public void LoadRequiredTerms_DifferentDocuments_HaveDifferentHashes()
        {
            var required = TermsCatalog.LoadRequiredTerms();
            var distinctHashes = required.Select(t => t.Sha256Hash).Distinct().Count();

            Assert.AreEqual(required.Count, distinctHashes, "4つの規約テキストの内容が偶然同一になっています。");
        }

        /// <summary>
        /// issue #97 の回帰テスト。<see cref="TermsCatalog.LoadRequiredTerms"/>（内部で
        /// <c>Resources.Load</c> を呼ぶ）は <see cref="TtsStatusPanel"/> の「再試行」ボタンの
        /// <c>async void</c> ハンドラからも呼ばれる（<c>ConsentGate.HasUserConsented</c> 経由）。
        /// <c>SynchronizationContext</c> が捕捉されない環境では、その継続がスレッドプール上で走り、
        /// メインスレッド以外から <c>Resources.Load</c> に到達すると
        /// 「Load can only be called from the main thread」で失敗しうる（#97 の実際の症状）。
        ///
        /// #97 の主たる修正は <c>TtsStatusPanel</c> 側（継続を明示的にメインスレッドへ Post し直す）
        /// だが、本メソッド（<see cref="TermsCatalog"/>）にも独立した改善として、初回呼び出し以降は
        /// <c>Resources.Load</c> を呼ばないキャッシュを追加した（同梱リソースは実行中に内容が
        /// 変わらないため）。この回帰テストは <see cref="TermsCatalog.ResetCacheForTesting"/> で
        /// 明示的にキャッシュを冷やしたうえで、メインスレッドでの初回呼び出し → 別スレッドからの
        /// 2 回目の呼び出し、という順序で実際に例外が発生しないことを固定する。
        /// </summary>
        [Test]
        public void LoadRequiredTerms_初回のメインスレッド呼び出し後は別スレッドから呼んでも例外にならない()
        {
            TermsCatalog.ResetCacheForTesting();

            try
            {
                // 1 回目はこのテストメソッド自身（メインスレッド）で呼び、キャッシュを温める。
                var onMainThread = TermsCatalog.LoadRequiredTerms();
                Assert.That(onMainThread, Is.Not.Empty);

                Exception capturedOnWorkerThread = null;
                IReadOnlyList<TermsDefinition> onWorkerThread = null;

                var worker = new Thread(() =>
                {
                    try
                    {
                        onWorkerThread = TermsCatalog.LoadRequiredTerms();
                    }
                    catch (Exception e)
                    {
                        capturedOnWorkerThread = e;
                    }
                });
                worker.Start();
                worker.Join();

                Assert.That(capturedOnWorkerThread, Is.Null,
                    "初回のメインスレッド呼び出し後は、別スレッドからの呼び出しでも Resources.Load に到達せず " +
                    "例外が発生しないこと（#97）。");
                Assert.That(onWorkerThread, Is.Not.Null.And.Count.EqualTo(onMainThread.Count));
            }
            finally
            {
                // 他のテストに「冷えたキャッシュ」の影響を残さない。
                TermsCatalog.ResetCacheForTesting();
            }
        }
    }
}
