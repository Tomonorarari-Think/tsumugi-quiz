using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// キャッシュの LRU 管理用インデックス（docs/tts.md §7.2 / §7.3 の <c>index.json</c>）。
    ///
    /// ファイル I/O は持たない純 C# なので EditMode テストで追い出し順を検証できる
    /// （実ファイルとの突き合わせは <see cref="TtsCache"/> の責務）。
    /// エントリ自体は不変（<see cref="TtsCacheEntry"/>）で、インデックスだけが可変。
    /// スレッド安全ではない（<see cref="TtsCache"/> 側で直列化する）。
    /// </summary>
    public sealed class TtsCacheIndex
    {
        /// <summary><c>index.json</c> のスキーマバージョン。</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// 追い出しの目標水準。上限を超えたら「上限の 90% を下回るまで」まとめて削除する
        /// （毎回 1 件ずつ消すとディスク I/O が無駄になる。docs/tts.md §7.3）。
        /// </summary>
        public const double EvictionTargetRatio = 0.9;

        private readonly Dictionary<string, TtsCacheEntry> _entries;

        public TtsCacheIndex()
            => _entries = new Dictionary<string, TtsCacheEntry>(StringComparer.Ordinal);

        private TtsCacheIndex(IEnumerable<TtsCacheEntry> entries) : this()
        {
            foreach (var entry in entries)
            {
                _entries[entry.Key] = entry;
            }
        }

        /// <summary>エントリ数。</summary>
        public int Count => _entries.Count;

        /// <summary>合計バイト数。</summary>
        public long TotalBytes
        {
            get
            {
                var total = 0L;
                foreach (var entry in _entries.Values) total += entry.Bytes;
                return total;
            }
        }

        /// <summary>最終アクセス時刻の古い順（LRU の追い出し順）に並べたエントリ。</summary>
        public IReadOnlyList<TtsCacheEntry> EntriesOldestFirst => OrderOldestFirst().ToArray();

        /// <summary>キーからエントリを引く。</summary>
        public bool TryGet(string key, out TtsCacheEntry entry)
        {
            if (string.IsNullOrEmpty(key))
            {
                entry = null;
                return false;
            }
            return _entries.TryGetValue(key, out entry);
        }

        /// <summary>エントリを追加または置換する。</summary>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> が null</exception>
        /// <exception cref="ArgumentException">エントリが妥当でないとき</exception>
        public void Put(TtsCacheEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (!entry.IsValid()) throw new ArgumentException($"キャッシュエントリが不正です（key={entry.Key}）。", nameof(entry));

            _entries[entry.Key] = entry;
        }

        /// <summary>最終アクセス時刻を更新する。エントリが無ければ false。</summary>
        public bool Touch(string key, DateTime lastUsedUtc)
        {
            if (!TryGet(key, out var entry)) return false;

            _entries[key] = entry.WithLastUsedUtc(lastUsedUtc);
            return true;
        }

        /// <summary>エントリを取り除く。無ければ false。</summary>
        public bool Remove(string key) => !string.IsNullOrEmpty(key) && _entries.Remove(key);

        /// <summary>すべて取り除く。</summary>
        public void Clear() => _entries.Clear();

        /// <summary>
        /// 上限を超えている場合に、削除すべきエントリを古い順に返す。
        /// 超えていなければ空（起動時と書き込み後に呼ぶ）。
        /// </summary>
        /// <param name="maxBytes"><c>tts.cacheMaxBytes</c></param>
        /// <param name="maxEntries"><c>tts.cacheMaxEntries</c></param>
        public IReadOnlyList<TtsCacheEntry> SelectEvictions(long maxBytes, int maxEntries)
        {
            if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "0 以上で指定してください。");
            if (maxEntries < 0) throw new ArgumentOutOfRangeException(nameof(maxEntries), maxEntries, "0 以上で指定してください。");

            var totalBytes = TotalBytes;
            var count = Count;
            if (totalBytes <= maxBytes && count <= maxEntries) return Array.Empty<TtsCacheEntry>();

            var targetBytes = (long)Math.Floor(maxBytes * EvictionTargetRatio);
            var targetEntries = (int)Math.Floor(maxEntries * EvictionTargetRatio);

            var evictions = new List<TtsCacheEntry>();
            foreach (var entry in OrderOldestFirst())
            {
                if (totalBytes <= targetBytes && count <= targetEntries) break;

                evictions.Add(entry);
                totalBytes -= entry.Bytes;
                count--;
            }

            return evictions;
        }

        /// <summary><c>index.json</c> 用の JSON にする。</summary>
        public string ToJson()
        {
            var document = new IndexDocument
            {
                Version = CurrentVersion,
                Entries = OrderOldestFirst().ToArray(),
            };
            return JsonConvert.SerializeObject(document, Formatting.Indented);
        }

        /// <summary>
        /// <c>index.json</c> を読む。壊れている・バージョンが違う場合は false を返し、
        /// <paramref name="error"/> に理由を入れる（呼び出し側はログを出してディレクトリ走査で再構築する）。
        /// 個々のエントリが壊れている場合はその 1 件だけを捨てて残りを採用する。
        /// </summary>
        public static bool TryParse(string json, out TtsCacheIndex index, out string error)
        {
            index = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "index.json が空です。";
                return false;
            }

            IndexDocument document;
            try
            {
                document = JsonConvert.DeserializeObject<IndexDocument>(json);
            }
            catch (JsonException e)
            {
                error = $"index.json を解析できません: {e.Message}";
                return false;
            }

            if (document == null)
            {
                error = "index.json の内容が null です。";
                return false;
            }
            if (document.Version != CurrentVersion)
            {
                error = $"index.json のバージョンが違います（期待 {CurrentVersion}、実際 {document.Version}）。";
                return false;
            }

            var entries = (document.Entries ?? Array.Empty<TtsCacheEntry>())
                .Where(e => e != null && e.IsValid())
                .ToArray();

            index = new TtsCacheIndex(entries);
            return true;
        }

        /// <summary>LRU の基準。時刻が同じ場合はキー順で安定させる。</summary>
        private IEnumerable<TtsCacheEntry> OrderOldestFirst()
            => _entries.Values
                .OrderBy(e => e.LastUsedUtc)
                .ThenBy(e => e.Key, StringComparer.Ordinal);

        /// <summary><c>index.json</c> のルート要素。</summary>
        private sealed class IndexDocument
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("entries")]
            public TtsCacheEntry[] Entries { get; set; }
        }
    }
}
