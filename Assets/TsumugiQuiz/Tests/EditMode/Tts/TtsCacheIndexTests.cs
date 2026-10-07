using System;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// LRU インデックスの検証（docs/tts.md §7.2 / §7.3、§11.1）。
    /// </summary>
    public sealed class TtsCacheIndexTests
    {
        private static readonly DateTime Epoch = new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);

        private static string KeyOf(int n) => n.ToString("x2").PadLeft(2, '0') + new string('0', 30);

        private static TtsCacheEntry Entry(int n, long bytes = 1000, int ageMinutes = 0)
            => new TtsCacheEntry(KeyOf(n), bytes, 1.5d, Epoch.AddMinutes(-ageMinutes), "春日部つむぎ", "ノーマル");

        [Test]
        public void 追加したエントリを引ける()
        {
            var index = new TtsCacheIndex();
            index.Put(Entry(1));

            Assert.That(index.TryGet(KeyOf(1), out var entry), Is.True);
            Assert.That(entry.Bytes, Is.EqualTo(1000));
            Assert.That(index.Count, Is.EqualTo(1));
            Assert.That(index.TotalBytes, Is.EqualTo(1000));
            Assert.That(index.TryGet(KeyOf(2), out _), Is.False);
        }

        [Test]
        public void 同じキーは置換される()
        {
            var index = new TtsCacheIndex();
            index.Put(Entry(1, bytes: 1000));
            index.Put(Entry(1, bytes: 2000));

            Assert.That(index.Count, Is.EqualTo(1));
            Assert.That(index.TotalBytes, Is.EqualTo(2000));
        }

        [Test]
        public void 不正なエントリは受け付けない()
        {
            var index = new TtsCacheIndex();

            Assert.Throws<ArgumentNullException>(() => index.Put(null));
            Assert.Throws<ArgumentException>(
                () => index.Put(new TtsCacheEntry("壊れたキー", 100, 1d, Epoch, null, null)));
            Assert.Throws<ArgumentException>(
                () => index.Put(new TtsCacheEntry(KeyOf(1), 0, 1d, Epoch, null, null)), "サイズ 0 は不正");
        }

        [Test]
        public void 最終アクセス時刻の古い順に並ぶ()
        {
            var index = new TtsCacheIndex();
            index.Put(Entry(1, ageMinutes: 10));
            index.Put(Entry(2, ageMinutes: 30));
            index.Put(Entry(3, ageMinutes: 20));

            var keys = index.EntriesOldestFirst.Select(e => e.Key).ToArray();

            Assert.That(keys, Is.EqualTo(new[] { KeyOf(2), KeyOf(3), KeyOf(1) }));
        }

        [Test]
        public void Touchで最終アクセス時刻が更新され順序が変わる()
        {
            var index = new TtsCacheIndex();
            index.Put(Entry(1, ageMinutes: 30));
            index.Put(Entry(2, ageMinutes: 10));

            Assert.That(index.Touch(KeyOf(1), Epoch), Is.True);
            Assert.That(index.Touch(KeyOf(9), Epoch), Is.False, "存在しないキー");

            var keys = index.EntriesOldestFirst.Select(e => e.Key).ToArray();
            Assert.That(keys, Is.EqualTo(new[] { KeyOf(2), KeyOf(1) }));
        }

        [Test]
        public void 上限以内なら追い出さない()
        {
            var index = new TtsCacheIndex();
            index.Put(Entry(1, bytes: 500));
            index.Put(Entry(2, bytes: 500));

            Assert.That(index.SelectEvictions(maxBytes: 1000, maxEntries: 10), Is.Empty);
        }

        /// <summary>上限を超えたら「上限の 90% を下回るまで」まとめて削除する（docs/tts.md §7.3）。</summary>
        [Test]
        public void バイト数超過で90パーセントを下回るまで古い順に追い出す()
        {
            var index = new TtsCacheIndex();
            for (var i = 1; i <= 10; i++)
            {
                index.Put(Entry(i, bytes: 100, ageMinutes: 100 - (i * 5)));
            }

            // 合計 1000 バイト。上限 900 → 目標は 810 以下なので 2 件（古い順）削除される。
            var evictions = index.SelectEvictions(maxBytes: 900, maxEntries: 100);

            Assert.That(evictions.Count, Is.EqualTo(2));
            Assert.That(evictions.Select(e => e.Key), Is.EqualTo(new[] { KeyOf(1), KeyOf(2) }),
                "最終アクセスが古いものから消えること");
        }

        [Test]
        public void エントリ数超過で90パーセントを下回るまで古い順に追い出す()
        {
            var index = new TtsCacheIndex();
            for (var i = 1; i <= 11; i++)
            {
                index.Put(Entry(i, bytes: 10, ageMinutes: 100 - (i * 5)));
            }

            // 11 件。上限 10 → 目標は 9 件以下なので 2 件削除される。
            var evictions = index.SelectEvictions(maxBytes: long.MaxValue, maxEntries: 10);

            Assert.That(evictions.Count, Is.EqualTo(2));
            Assert.That(evictions.Select(e => e.Key), Is.EqualTo(new[] { KeyOf(1), KeyOf(2) }));
        }

        [Test]
        public void 上限が0ならすべて追い出す()
        {
            var index = new TtsCacheIndex();
            index.Put(Entry(1));
            index.Put(Entry(2));

            Assert.That(index.SelectEvictions(maxBytes: 0, maxEntries: 0).Count, Is.EqualTo(2));
        }

        [Test]
        public void 上限に負の値は渡せない()
        {
            var index = new TtsCacheIndex();

            Assert.Throws<ArgumentOutOfRangeException>(() => index.SelectEvictions(-1, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => index.SelectEvictions(10, -1));
        }

        [Test]
        public void JSONに書き出して読み戻せる()
        {
            var index = new TtsCacheIndex();
            index.Put(Entry(1, bytes: 111, ageMinutes: 5));
            index.Put(Entry(2, bytes: 222, ageMinutes: 1));

            Assert.That(TtsCacheIndex.TryParse(index.ToJson(), out var restored, out var error), Is.True, error);
            Assert.That(restored.Count, Is.EqualTo(2));
            Assert.That(restored.TotalBytes, Is.EqualTo(333));
            Assert.That(restored.TryGet(KeyOf(1), out var entry), Is.True);
            Assert.That(entry.LastUsedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(entry.LastUsedUtc, Is.EqualTo(Epoch.AddMinutes(-5)));
            Assert.That(entry.SpeakerName, Is.EqualTo("春日部つむぎ"));
            Assert.That(restored.EntriesOldestFirst.First().Key, Is.EqualTo(KeyOf(1)), "順序も保たれること");
        }

        [TestCase("", "空")]
        [TestCase("   ", "空白のみ")]
        [TestCase("{ これは JSON ではない", "壊れた JSON")]
        [TestCase("null", "null リテラル")]
        [TestCase("{\"version\":999,\"entries\":[]}", "バージョン不一致")]
        public void 壊れたJSONは読み込みに失敗して理由を返す(string json, string description)
        {
            Assert.That(TtsCacheIndex.TryParse(json, out var index, out var error), Is.False, description);
            Assert.That(index, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void 壊れたエントリだけを捨てて残りを採用する()
        {
            var json = "{\"version\":1,\"entries\":[" +
                       "{\"key\":\"" + KeyOf(1) + "\",\"bytes\":100,\"durationSec\":1.0,\"lastUsedUtc\":\"2026-09-13T00:00:00Z\"}," +
                       "{\"key\":\"壊れている\",\"bytes\":100,\"durationSec\":1.0,\"lastUsedUtc\":\"2026-09-13T00:00:00Z\"}," +
                       "{\"key\":\"" + KeyOf(2) + "\",\"bytes\":0,\"durationSec\":1.0,\"lastUsedUtc\":\"2026-09-13T00:00:00Z\"}" +
                       "]}";

            Assert.That(TtsCacheIndex.TryParse(json, out var index, out var error), Is.True, error);
            Assert.That(index.Count, Is.EqualTo(1));
            Assert.That(index.TryGet(KeyOf(1), out _), Is.True);
        }

        [Test]
        public void RemoveとClearが効く()
        {
            var index = new TtsCacheIndex();
            index.Put(Entry(1));
            index.Put(Entry(2));

            Assert.That(index.Remove(KeyOf(1)), Is.True);
            Assert.That(index.Remove(KeyOf(1)), Is.False);
            Assert.That(index.Count, Is.EqualTo(1));

            index.Clear();
            Assert.That(index.Count, Is.EqualTo(0));
            Assert.That(index.TotalBytes, Is.EqualTo(0));
        }
    }
}
