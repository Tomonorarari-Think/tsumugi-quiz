using System;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// キャッシュの上限（docs/tts.md §7.3 の <c>tts.cacheMaxBytes</c> / <c>tts.cacheMaxEntries</c>）。
    /// 生成後は不変。
    /// </summary>
    public readonly struct TtsCacheLimits
    {
        public TtsCacheLimits(long maxBytes, int maxEntries)
        {
            if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "0 以上で指定してください。");
            if (maxEntries < 0) throw new ArgumentOutOfRangeException(nameof(maxEntries), maxEntries, "0 以上で指定してください。");

            MaxBytes = maxBytes;
            MaxEntries = maxEntries;
        }

        /// <summary>既定の上限（200MB / 5000 件）。</summary>
        public static TtsCacheLimits Default { get; } =
            new TtsCacheLimits(TtsSettings.DefaultCacheMaxBytes, TtsSettings.DefaultCacheMaxEntries);

        /// <summary>合計サイズの上限（バイト）。</summary>
        public long MaxBytes { get; }

        /// <summary>エントリ数の上限。</summary>
        public int MaxEntries { get; }

        /// <summary>アプリ設定から上限を取り出す。</summary>
        public static TtsCacheLimits FromSettings(TtsSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return new TtsCacheLimits(settings.CacheMaxBytes, settings.CacheMaxEntries);
        }

        /// <summary>ログ用の説明文。</summary>
        public override string ToString() => $"maxBytes={MaxBytes} maxEntries={MaxEntries}";
    }
}
