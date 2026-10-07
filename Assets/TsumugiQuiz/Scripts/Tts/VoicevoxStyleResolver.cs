using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tts
{
    /// <summary>スタイル解決がどの段階で成立したか（docs/tts.md §4.1 のフォールバック順）。</summary>
    public enum VoicevoxStyleMatch
    {
        /// <summary>指定の話者・スタイルがそのまま見つかった。</summary>
        Exact = 0,

        /// <summary>指定の話者の最初の talk スタイルにフォールバックした。</summary>
        SpeakerFallback = 1,

        /// <summary>別の話者の最初の talk スタイルにフォールバックした（UI に警告を出すこと）。</summary>
        AnySpeakerFallback = 2,
    }

    /// <summary>スタイル解決の結果。生成後は不変。</summary>
    public readonly struct VoicevoxStyleResolution
    {
        public VoicevoxStyleResolution(uint styleId, string speakerName, string styleName, VoicevoxStyleMatch match)
        {
            StyleId = styleId;
            SpeakerName = speakerName;
            StyleName = styleName;
            Match = match;
        }

        /// <summary>実際に使うスタイル ID。</summary>
        public uint StyleId { get; }

        /// <summary>解決された話者名。</summary>
        public string SpeakerName { get; }

        /// <summary>解決されたスタイル名。</summary>
        public string StyleName { get; }

        /// <summary>どの段階で解決したか。</summary>
        public VoicevoxStyleMatch Match { get; }
    }

    /// <summary>
    /// メタ情報 JSON から話者名・スタイル名でスタイル ID を解決する（仮決め K16、docs/tts.md §4.1）。
    /// <b>スタイル ID をハードコードしてはいけない</b>。VVM のバージョンで変わりうるため。
    ///
    /// P/Invoke に依存しない純 C# なので EditMode テストで検証できる。
    /// </summary>
    public static class VoicevoxStyleResolver
    {
        /// <summary>既定の話者名（仮決め K16、<see cref="SettingsDefaults.TtsSpeakerName"/> と同じ、#26 統括判断 M6）。</summary>
        public const string DefaultSpeakerName = SettingsDefaults.TtsSpeakerName;

        /// <summary>既定のスタイル名（仮決め K16、<see cref="SettingsDefaults.TtsStyleName"/> と同じ）。</summary>
        public const string DefaultStyleName = SettingsDefaults.TtsStyleName;

        /// <summary>読み上げに使えるスタイルの種別。</summary>
        public const string TalkStyleType = "talk";

        /// <summary>メタ情報 JSON を解析する。壊れていれば <see cref="TtsSetupException"/>。</summary>
        public static IReadOnlyList<VoicevoxSpeakerMeta> ParseMetas(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new TtsSetupException("音声モデルのメタ情報が空です。");
            }

            VoicevoxSpeakerMeta[] metas;
            try
            {
                metas = JsonConvert.DeserializeObject<VoicevoxSpeakerMeta[]>(json);
            }
            catch (JsonException e)
            {
                throw new TtsSetupException($"音声モデルのメタ情報を解析できませんでした: {e.Message}", e);
            }

            if (metas == null)
            {
                throw new TtsSetupException("音声モデルのメタ情報を解析できませんでした（null）。");
            }

            return metas.Where(m => m != null).ToArray();
        }

        /// <summary>メタ情報 JSON から直接解決する。</summary>
        public static VoicevoxStyleResolution Resolve(string metasJson, string speakerName = DefaultSpeakerName, string styleName = DefaultStyleName)
            => Resolve(ParseMetas(metasJson), speakerName, styleName);

        /// <summary>
        /// 話者名・スタイル名からスタイル ID を解決する。
        /// フォールバック順: 指定の話者＋スタイル → 指定の話者の最初の talk スタイル → 最初の talk スタイル。
        /// すべて失敗したら <see cref="TtsSetupException"/>（呼び出し側は読み上げなしで続行する）。
        /// </summary>
        public static VoicevoxStyleResolution Resolve(
            IReadOnlyList<VoicevoxSpeakerMeta> metas,
            string speakerName = DefaultSpeakerName,
            string styleName = DefaultStyleName)
        {
            if (metas == null) throw new ArgumentNullException(nameof(metas));
            if (string.IsNullOrWhiteSpace(speakerName)) throw new ArgumentException("話者名が空です。", nameof(speakerName));
            if (string.IsNullOrWhiteSpace(styleName)) throw new ArgumentException("スタイル名が空です。", nameof(styleName));

            var speakers = metas.Where(m => m != null && m.Styles != null).ToArray();

            foreach (var speaker in speakers.Where(m => m.Name == speakerName))
            {
                var exact = TalkStyles(speaker).FirstOrDefault(s => s.Name == styleName);
                if (exact != null)
                {
                    return new VoicevoxStyleResolution(exact.Id, speaker.Name, exact.Name, VoicevoxStyleMatch.Exact);
                }
            }

            foreach (var speaker in speakers.Where(m => m.Name == speakerName))
            {
                var first = TalkStyles(speaker).FirstOrDefault();
                if (first != null)
                {
                    return new VoicevoxStyleResolution(first.Id, speaker.Name, first.Name, VoicevoxStyleMatch.SpeakerFallback);
                }
            }

            foreach (var speaker in speakers)
            {
                var first = TalkStyles(speaker).FirstOrDefault();
                if (first != null)
                {
                    return new VoicevoxStyleResolution(first.Id, speaker.Name, first.Name, VoicevoxStyleMatch.AnySpeakerFallback);
                }
            }

            var loaded = speakers.Length == 0 ? "(なし)" : string.Join(", ", speakers.Select(m => m.Name));
            throw new TtsSetupException(
                $"話者「{speakerName}」のスタイル「{styleName}」が見つかりません。読み込まれている話者: {loaded}");
        }

        private static IEnumerable<VoicevoxStyleMeta> TalkStyles(VoicevoxSpeakerMeta speaker)
            => speaker.Styles.Where(s => s != null && IsTalkStyle(s));

        private static bool IsTalkStyle(VoicevoxStyleMeta style)
            => string.IsNullOrEmpty(style.Type) || string.Equals(style.Type, TalkStyleType, StringComparison.Ordinal);
    }
}
