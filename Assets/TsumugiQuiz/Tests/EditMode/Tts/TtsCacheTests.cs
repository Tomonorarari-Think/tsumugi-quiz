using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using TsumugiQuiz.Tests.Shared.Core;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// ディスクキャッシュの検証（docs/tts.md §7.2 / §7.3）。
    /// 一時ディレクトリと実ファイル I/O（<see cref="TtsCacheFileSystem"/>）を使う。
    /// </summary>
    public sealed class TtsCacheTests
    {
        private static readonly DateTime Epoch = new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);

        private string _root;
        private List<string> _warnings;
        private DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "TsumugiQuizTtsCacheTests", Guid.NewGuid().ToString("N"));
            _warnings = new List<string>();
            _now = Epoch;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private TtsCache CreateCache(long maxBytes = 1024 * 1024, int maxEntries = 100)
            => new TtsCache(
                _root,
                new TtsCacheLimits(maxBytes, maxEntries),
                fileSystem: new TtsCacheFileSystem(),
                logWarning: _warnings.Add,
                utcNow: () => _now);

        private static string KeyOf(int n) => n.ToString("x2").PadLeft(2, '0') + new string('0', 30);

        private static byte[] Wav(int frameCount = 240) => TestWavFactory.Create(frameCount);

        [Test]
        public void 保存したwavを読み戻せる()
        {
            var cache = CreateCache();
            var wav = Wav();

            cache.Put(KeyOf(1), wav, durationSec: 0.5d, "春日部つむぎ", "ノーマル");

            Assert.That(cache.TryGet(KeyOf(1), out var loaded, out var durationSec), Is.True);
            Assert.That(loaded, Is.EqualTo(wav));
            Assert.That(durationSec, Is.EqualTo(0.5d));
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.TotalBytes, Is.EqualTo(wav.Length));
        }

        [Test]
        public void wavは先頭2文字のサブディレクトリに置かれる()
        {
            var cache = CreateCache();
            cache.Put(KeyOf(1), Wav(), 0.5d, null, null);

            var expected = Path.Combine(_root, KeyOf(1).Substring(0, 2), KeyOf(1) + ".wav");

            Assert.That(cache.GetWavPath(KeyOf(1)), Is.EqualTo(expected));
            Assert.That(File.Exists(expected), Is.True);
            Assert.That(File.Exists(Path.Combine(_root, TtsCache.IndexFileName)), Is.True);
        }

        [Test]
        public void 一時ファイルを残さない()
        {
            var cache = CreateCache();
            cache.Put(KeyOf(1), Wav(), 0.5d, null, null);

            var temps = Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories);

            Assert.That(temps, Is.Empty);
        }

        [Test]
        public void 未登録のキーはミスになる()
        {
            var cache = CreateCache();

            Assert.That(cache.TryGet(KeyOf(1), out _, out _), Is.False);
            Assert.That(cache.Contains(KeyOf(1)), Is.False);
            Assert.That(cache.TryGet("壊れたキー", out _, out _), Is.False);
        }

        [Test]
        public void ファイルが手で消されていたらミスになり索引からも消える()
        {
            var cache = CreateCache();
            cache.Put(KeyOf(1), Wav(), 0.5d, null, null);
            File.Delete(cache.GetWavPath(KeyOf(1)));

            Assert.That(cache.TryGet(KeyOf(1), out _, out _), Is.False);
            Assert.That(cache.Count, Is.EqualTo(0));
        }

        [Test]
        public void 上限超過でLRU順に追い出される()
        {
            // 1 件あたり 44 + 480 = 524 バイト。上限 1600 バイト（目標 1440）。
            var cache = CreateCache(maxBytes: 1600, maxEntries: 100);

            for (var i = 1; i <= 3; i++)
            {
                _now = Epoch.AddMinutes(i);
                cache.Put(KeyOf(i), Wav(), 0.5d, null, null);
            }

            // ここまで 1572 バイト（上限内）。1 件足すと 2096 バイトで超過し、目標 1440 を下回るまで消す。
            _now = Epoch.AddMinutes(4);
            cache.Put(KeyOf(4), Wav(), 0.5d, null, null);

            Assert.That(cache.TryGet(KeyOf(1), out _, out _), Is.False, "最も古い 1 件目が消えること");
            Assert.That(cache.TryGet(KeyOf(4), out _, out _), Is.True, "直近の保存は残ること");
            Assert.That(cache.TotalBytes, Is.LessThanOrEqualTo(1440));
            Assert.That(File.Exists(cache.GetWavPath(KeyOf(1))), Is.False, "ファイルも消えること");
        }

        [Test]
        public void 参照した順でLRUの新しさが変わる()
        {
            var cache = CreateCache(maxBytes: 1600, maxEntries: 100);
            for (var i = 1; i <= 3; i++)
            {
                _now = Epoch.AddMinutes(i);
                cache.Put(KeyOf(i), Wav(), 0.5d, null, null);
            }

            // 1 件目を参照して「最近使った」ことにする。
            _now = Epoch.AddMinutes(10);
            Assert.That(cache.TryGet(KeyOf(1), out _, out _), Is.True);

            _now = Epoch.AddMinutes(11);
            cache.Put(KeyOf(4), Wav(), 0.5d, null, null);

            Assert.That(cache.TryGet(KeyOf(1), out _, out _), Is.True, "参照済みの 1 件目は残ること");
            Assert.That(cache.TryGet(KeyOf(2), out _, out _), Is.False, "次に古い 2 件目が消えること");
        }

        [Test]
        public void エントリ数の上限でも追い出される()
        {
            var cache = CreateCache(maxBytes: long.MaxValue, maxEntries: 2);

            for (var i = 1; i <= 3; i++)
            {
                _now = Epoch.AddMinutes(i);
                cache.Put(KeyOf(i), Wav(), 0.5d, null, null);
            }

            Assert.That(cache.Count, Is.LessThanOrEqualTo(1), "上限 2 の 90% = 1 件以下まで削る");
            Assert.That(cache.TryGet(KeyOf(3), out _, out _), Is.True);
        }

        [Test]
        public void 壊れたindexからディレクトリ走査で再構築する()
        {
            var cache = CreateCache();
            cache.Put(KeyOf(1), Wav(frameCount: 240), 0.01d, null, null);
            cache.Put(KeyOf(2), Wav(frameCount: 480), 0.02d, null, null);
            cache.Flush();

            File.WriteAllText(Path.Combine(_root, TtsCache.IndexFileName), "{ これは壊れた JSON", Encoding.UTF8);

            var reopened = CreateCache();

            Assert.That(reopened.Count, Is.EqualTo(2));
            Assert.That(reopened.IndexWasRebuilt, Is.True);
            Assert.That(reopened.TryGet(KeyOf(1), out _, out var duration1), Is.True);
            Assert.That(duration1, Is.EqualTo(240d / TestWavFactory.DefaultSampleRate).Within(1e-6),
                "durationSec が wav ヘッダから復元されること");
            Assert.That(reopened.TryGet(KeyOf(2), out _, out var duration2), Is.True);
            Assert.That(duration2, Is.EqualTo(480d / TestWavFactory.DefaultSampleRate).Within(1e-6));
            Assert.That(_warnings, Has.Some.Contains(TtsCache.IndexFileName));
        }

        [Test]
        public void indexが無ければ走査で作られる()
        {
            var cache = CreateCache();
            cache.Put(KeyOf(1), Wav(), 0.01d, null, null);
            cache.Flush();

            File.Delete(Path.Combine(_root, TtsCache.IndexFileName));

            var reopened = CreateCache();

            Assert.That(reopened.Count, Is.EqualTo(1));
            Assert.That(reopened.IndexWasRebuilt, Is.True);
        }

        [Test]
        public void 再構築時に壊れたwavはスキップして削除される()
        {
            var cache = CreateCache();
            cache.Put(KeyOf(1), Wav(), 0.01d, null, null);
            cache.Flush();

            // ヘッダが壊れた wav と、キー形式に合わないファイルを混ぜる。
            var brokenPath = Path.Combine(_root, KeyOf(2).Substring(0, 2), KeyOf(2) + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(brokenPath));
            File.WriteAllBytes(brokenPath, Encoding.ASCII.GetBytes("これは wav ではありません"));

            var junkPath = Path.Combine(_root, "aa", "not-a-key.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(junkPath));
            File.WriteAllBytes(junkPath, Wav());

            var emptyPath = Path.Combine(_root, KeyOf(3).Substring(0, 2), KeyOf(3) + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(emptyPath));
            File.WriteAllBytes(emptyPath, Array.Empty<byte>());

            File.Delete(Path.Combine(_root, TtsCache.IndexFileName));
            var reopened = CreateCache();

            Assert.That(reopened.Count, Is.EqualTo(1), "正常な 1 件だけが索引に載ること");
            Assert.That(reopened.TryGet(KeyOf(1), out _, out _), Is.True);
            Assert.That(File.Exists(brokenPath), Is.False, "壊れた wav は削除されること");
            Assert.That(File.Exists(emptyPath), Is.False, "空ファイルは削除されること");
            Assert.That(File.Exists(junkPath), Is.True, "キー形式でないファイルは触らないこと");
        }

        [Test]
        public void Invalidateでエントリとファイルが消える()
        {
            var cache = CreateCache();
            cache.Put(KeyOf(1), Wav(), 0.5d, null, null);
            var path = cache.GetWavPath(KeyOf(1));

            cache.Invalidate(KeyOf(1));

            Assert.That(cache.Count, Is.EqualTo(0));
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void Clearで全件消える()
        {
            var cache = CreateCache();
            cache.Put(KeyOf(1), Wav(), 0.5d, null, null);
            cache.Put(KeyOf(2), Wav(), 0.5d, null, null);

            cache.Clear();

            Assert.That(cache.Count, Is.EqualTo(0));
            Assert.That(cache.TotalBytes, Is.EqualTo(0));
            Assert.That(Directory.GetFiles(_root, "*.wav", SearchOption.AllDirectories), Is.Empty);
        }

        [Test]
        public void Containsは本体を読まずに命中を判定する()
        {
            var cache = CreateCache();
            cache.Put(KeyOf(1), Wav(), 0.5d, null, null);

            Assert.That(cache.Contains(KeyOf(1)), Is.True);
            Assert.That(cache.Contains(KeyOf(2)), Is.False);

            File.Delete(cache.GetWavPath(KeyOf(1)));
            Assert.That(cache.Contains(KeyOf(1)), Is.False);
        }

        [Test]
        public void 空のwavや不正なキーは保存せず警告を出す()
        {
            var cache = CreateCache();

            cache.Put(KeyOf(1), Array.Empty<byte>(), 0.5d, null, null);
            cache.Put("壊れたキー", Wav(), 0.5d, null, null);

            Assert.That(cache.Count, Is.EqualTo(0));
            Assert.That(_warnings.Count, Is.EqualTo(2));
        }

        [Test]
        public void 書き込みが失敗しても例外にならない()
        {
            var cache = new TtsCache(
                _root,
                TtsCacheLimits.Default,
                fileSystem: new ThrowingFileSystem(),
                logWarning: _warnings.Add,
                utcNow: () => _now);

            Assert.DoesNotThrow(() => cache.Put(KeyOf(1), Wav(), 0.5d, null, null));
            Assert.That(cache.TryGet(KeyOf(1), out _, out _), Is.False);
            Assert.That(_warnings, Is.Not.Empty);
        }

        [Test]
        public void ルートディレクトリが空なら例外になる()
        {
            Assert.Throws<ArgumentException>(() => new TtsCache("  ", TtsCacheLimits.Default));
        }

        /// <summary>すべての書き込みが失敗する I/O 実装（キャッシュ失敗が致命的でないことの検証用）。</summary>
        private sealed class ThrowingFileSystem : ITtsCacheFileSystem
        {
            public bool FileExists(string path) => false;

            public long GetFileSize(string path) => -1L;

            public byte[] ReadAllBytes(string path) => throw new IOException("読み込みできません（テスト）。");

            public byte[] ReadPrefix(string path, int maxBytes) => throw new IOException("読み込みできません（テスト）。");

            public void WriteAllBytesAtomic(string path, byte[] bytes) => throw new IOException("書き込みできません（テスト）。");

            public string ReadAllText(string path) => throw new IOException("読み込みできません（テスト）。");

            public void WriteAllTextAtomic(string path, string contents) => throw new IOException("書き込みできません（テスト）。");

            public void DeleteFile(string path) => throw new IOException("削除できません（テスト）。");

            public bool DirectoryExists(string path) => false;

            public void EnsureDirectory(string path)
            {
            }

            public IReadOnlyList<string> EnumerateFiles(string root, string searchPattern) => Array.Empty<string>();
        }
    }
}
