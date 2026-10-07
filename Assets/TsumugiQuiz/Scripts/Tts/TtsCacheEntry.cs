using System;
using Newtonsoft.Json;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// キャッシュ 1 件分のメタ情報（docs/tts.md §7.2 の <c>index.json</c> の entries 要素）。
    /// 生成後は不変で、最終アクセス時刻の更新は <see cref="WithLastUsedUtc"/> で新しいインスタンスを返す。
    /// </summary>
    public sealed class TtsCacheEntry
    {
        [JsonConstructor]
        public TtsCacheEntry(
            string key, long bytes, double durationSec, DateTime lastUsedUtc, string speakerName, string styleName)
        {
            Key = key;
            Bytes = bytes;
            DurationSec = durationSec;
            LastUsedUtc = ToUtc(lastUsedUtc);
            SpeakerName = speakerName;
            StyleName = styleName;
        }

        /// <summary>キャッシュキー（32 文字の小文字 hex）。</summary>
        [JsonProperty("key")]
        public string Key { get; }

        /// <summary>wav のバイト数。</summary>
        [JsonProperty("bytes")]
        public long Bytes { get; }

        /// <summary>再生時間（秒）。再生同期の <c>durationSec</c> に使う。</summary>
        [JsonProperty("durationSec")]
        public double DurationSec { get; }

        /// <summary>最終アクセス時刻（UTC）。LRU の基準。</summary>
        [JsonProperty("lastUsedUtc")]
        public DateTime LastUsedUtc { get; }

        /// <summary>合成に使った話者名（診断・キャッシュクリア UI 用）。</summary>
        [JsonProperty("speakerName")]
        public string SpeakerName { get; }

        /// <summary>合成に使ったスタイル名（診断・キャッシュクリア UI 用）。</summary>
        [JsonProperty("styleName")]
        public string StyleName { get; }

        /// <summary>最終アクセス時刻だけを差し替えた新しいエントリを返す。</summary>
        public TtsCacheEntry WithLastUsedUtc(DateTime lastUsedUtc)
            => new TtsCacheEntry(Key, Bytes, DurationSec, lastUsedUtc, SpeakerName, StyleName);

        /// <summary>
        /// UTC に正規化する。手で編集された <c>index.json</c> にローカル時刻が入っていても
        /// LRU の比較が壊れないようにするため。
        /// </summary>
        private static DateTime ToUtc(DateTime value)
        {
            switch (value.Kind)
            {
                case DateTimeKind.Utc:
                    return value;
                case DateTimeKind.Local:
                    return value.ToUniversalTime();
                default:
                    return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }
        }

        /// <summary>index.json に載せられる妥当なエントリか（壊れた index の検出に使う）。</summary>
        public bool IsValid()
            => TtsCacheKey.IsValid(Key) && Bytes > 0 && DurationSec >= 0d && !double.IsNaN(DurationSec);
    }
}
