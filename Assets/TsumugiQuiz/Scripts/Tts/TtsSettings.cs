using System;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// TTS のアプリ設定（docs/room-settings.md §2 の <c>tts.*</c> のうち「アプリ設定」の行）。
    /// 各 PC ローカルの値で、クライアントへ同期しない。生成後は不変で、変更は <c>With*</c> で新しい値を返す。
    ///
    /// 保存先 <c>Application.persistentDataPath/app-settings.json</c> の読み書きは #26 / #28 が担当する。
    /// 本クラスは既定値と検証（クランプ）だけを持ち、読み込みは <see cref="ITtsSettingsProvider"/> 経由にしてある。
    ///
    /// <c>tts.enabled</c> と <c>tts.speed</c> は<b>ルーム設定</b>なのでここには含めない。
    /// 前者は <see cref="TtsService.Enabled"/>、後者は合成の引数として渡す。
    /// </summary>
    public sealed class TtsSettings
    {
        /// <summary>話者名の既定値（仮決め K16、docs/tts.md §4.1）。</summary>
        public const string DefaultSpeakerName = VoicevoxStyleResolver.DefaultSpeakerName;

        /// <summary>スタイル名の既定値（仮決め K16、docs/tts.md §4.1）。</summary>
        public const string DefaultStyleName = VoicevoxStyleResolver.DefaultStyleName;

        /// <summary>
        /// キャッシュ合計サイズの上限の既定値（200MB、docs/tts.md §7.3、<see cref="SettingsDefaults.TtsCacheMaxBytes"/>
        /// と同じ、#26 統括判断 M6）。
        /// </summary>
        public const long DefaultCacheMaxBytes = SettingsDefaults.TtsCacheMaxBytes;

        /// <summary>キャッシュエントリ数の上限の既定値（docs/tts.md §7.3、<see cref="SettingsDefaults.TtsCacheMaxEntries"/> と同じ）。</summary>
        public const int DefaultCacheMaxEntries = SettingsDefaults.TtsCacheMaxEntries;

        private TtsSettings(
            string speakerName, string styleName, long cacheMaxBytes, int cacheMaxEntries, string assetPathOverride)
        {
            SpeakerName = speakerName;
            StyleName = styleName;
            CacheMaxBytes = cacheMaxBytes;
            CacheMaxEntries = cacheMaxEntries;
            AssetPathOverride = assetPathOverride;
        }

        /// <summary>すべて既定値の設定。</summary>
        public static TtsSettings Default { get; } = new TtsSettings(
            DefaultSpeakerName, DefaultStyleName, DefaultCacheMaxBytes, DefaultCacheMaxEntries, string.Empty);

        /// <summary><c>tts.speakerName</c>。空なら既定値に丸める。</summary>
        public string SpeakerName { get; }

        /// <summary><c>tts.styleName</c>。空なら既定値に丸める。</summary>
        public string StyleName { get; }

        /// <summary><c>tts.cacheMaxBytes</c>。0 以上。</summary>
        public long CacheMaxBytes { get; }

        /// <summary><c>tts.cacheMaxEntries</c>。0 以上。</summary>
        public int CacheMaxEntries { get; }

        /// <summary><c>tts.assetPathOverride</c>。空なら既定の探索順を使う。</summary>
        public string AssetPathOverride { get; }

        /// <summary>
        /// 値を検証・クランプして設定を作る。境界（設定ファイル）から来た値は必ずここを通す。
        /// 不正値は例外にせず既定値へ丸める（設定ファイルの 1 行で起動できなくなるのを避けるため）。
        /// </summary>
        public static TtsSettings Create(
            string speakerName = DefaultSpeakerName,
            string styleName = DefaultStyleName,
            long cacheMaxBytes = DefaultCacheMaxBytes,
            int cacheMaxEntries = DefaultCacheMaxEntries,
            string assetPathOverride = null)
            => new TtsSettings(
                string.IsNullOrWhiteSpace(speakerName) ? DefaultSpeakerName : speakerName.Trim(),
                string.IsNullOrWhiteSpace(styleName) ? DefaultStyleName : styleName.Trim(),
                cacheMaxBytes < 0 ? DefaultCacheMaxBytes : cacheMaxBytes,
                cacheMaxEntries < 0 ? DefaultCacheMaxEntries : cacheMaxEntries,
                assetPathOverride == null ? string.Empty : assetPathOverride.Trim());

        /// <summary>話者・スタイルだけを差し替えた新しい設定を返す。</summary>
        public TtsSettings WithSpeaker(string speakerName, string styleName)
            => Create(speakerName, styleName, CacheMaxBytes, CacheMaxEntries, AssetPathOverride);

        /// <summary>キャッシュ上限だけを差し替えた新しい設定を返す。</summary>
        public TtsSettings WithCacheLimits(long cacheMaxBytes, int cacheMaxEntries)
            => Create(SpeakerName, StyleName, cacheMaxBytes, cacheMaxEntries, AssetPathOverride);

        /// <summary>探索パスのオーバーライドだけを差し替えた新しい設定を返す。</summary>
        public TtsSettings WithAssetPathOverride(string assetPathOverride)
            => Create(SpeakerName, StyleName, CacheMaxBytes, CacheMaxEntries, assetPathOverride);

        /// <summary>ログ用の説明文。</summary>
        public string Describe()
            => $"speaker={SpeakerName}/{StyleName} cacheMaxBytes={CacheMaxBytes} cacheMaxEntries={CacheMaxEntries} " +
               $"assetPathOverride={(string.IsNullOrEmpty(AssetPathOverride) ? "(なし)" : AssetPathOverride)}";
    }

    /// <summary>
    /// アプリ設定から <see cref="TtsSettings"/> を読み込む窓口。
    /// 本 issue（#22）では既定値を返す実装だけを用意し、
    /// <c>app-settings.json</c> を実際に読む実装は #26 / #28 が差し込む。
    /// </summary>
    public interface ITtsSettingsProvider
    {
        /// <summary>
        /// 現在のアプリ設定を読む。読み込みに失敗した場合も例外を投げず、
        /// 既定値へフォールバックした設定を返すこと（読み上げはゲームの必須要素ではない）。
        /// </summary>
        TtsSettings Load();
    }

    /// <summary>常に既定値を返す <see cref="ITtsSettingsProvider"/>。設定ファイルが接続されるまでの既定実装。</summary>
    public sealed class DefaultTtsSettingsProvider : ITtsSettingsProvider
    {
        /// <inheritdoc/>
        public TtsSettings Load() => TtsSettings.Default;
    }

    /// <summary>固定の設定を返す <see cref="ITtsSettingsProvider"/>。テストと呼び出し側からの注入に使う。</summary>
    public sealed class FixedTtsSettingsProvider : ITtsSettingsProvider
    {
        private readonly TtsSettings _settings;

        public FixedTtsSettingsProvider(TtsSettings settings)
            => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        /// <inheritdoc/>
        public TtsSettings Load() => _settings;
    }
}
