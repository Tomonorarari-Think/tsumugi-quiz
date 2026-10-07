using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TsumugiQuiz.Tts.Native;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// 読み込む .vvm をどう絞り込むか（#22 統括メモ (2)）。既定値（<see cref="All"/>）は全件読み込み。
    /// 生成後は不変。
    /// </summary>
    public readonly struct VoicevoxModelSelection
    {
        public VoicevoxModelSelection(string speakerName, string styleName)
        {
            SpeakerName = speakerName;
            StyleName = styleName;
        }

        /// <summary>絞り込みをしない（見つかった .vvm をすべて読み込む）。</summary>
        public static VoicevoxModelSelection All => default;

        /// <summary>絞り込みに使う話者名。</summary>
        public string SpeakerName { get; }

        /// <summary>絞り込みに使うスタイル名。</summary>
        public string StyleName { get; }

        /// <summary>全件読み込みか。</summary>
        public bool IsAll => string.IsNullOrWhiteSpace(SpeakerName) || string.IsNullOrWhiteSpace(StyleName);

        /// <summary>アプリ設定から絞り込み条件を作る。</summary>
        public static VoicevoxModelSelection FromSettings(TtsSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return new VoicevoxModelSelection(settings.SpeakerName, settings.StyleName);
        }
    }

    /// <summary>
    /// 読み込む .vvm を設定（<c>tts.speakerName</c> / <c>tts.styleName</c>）で絞り込む（#22 統括メモ (2)）。
    ///
    /// 全件読み込むと、必要のない話者のモデルまでメモリに載る（0.vvm には四国めたん・ずんだもん・
    /// 雨晴はうも入っている。docs/tts.md §4.2）。各 .vvm のメタ情報を先に読み、
    /// 目的の話者・スタイルを含むものだけを <c>voicevox_synthesizer_load_voice_model</c> に渡す。
    ///
    /// 絞り込み順（docs/tts.md §4.1 のフォールバック順に対応）:
    /// <list type="number">
    ///   <item><description>指定の話者＋スタイルを含む .vvm</description></item>
    ///   <item><description>指定の話者の talk スタイルを含む .vvm</description></item>
    ///   <item><description>talk スタイルを 1 つ以上含む .vvm（別の話者でも読み上げは成立する）</description></item>
    ///   <item><description>どれも該当しなければ<b>全件</b>（判定に失敗しても読み上げを試せるようにする）</description></item>
    /// </list>
    ///
    /// メタ情報の取得を <see cref="Select"/> の引数（デリゲート）にしてあるので、
    /// P/Invoke を使わない EditMode テストで絞り込み順を検証できる。
    /// </summary>
    public static class VoicevoxModelSelector
    {
        /// <summary>
        /// メタ情報を読む関数を与えて絞り込む。
        /// </summary>
        /// <param name="modelFiles">候補の .vvm パス</param>
        /// <param name="speakerName">話者名</param>
        /// <param name="styleName">スタイル名</param>
        /// <param name="readMetasJson">
        /// .vvm のパスからメタ情報 JSON を返す関数。読めない場合は null / 空を返すか例外を投げてよい
        /// （その .vvm は「判定不能」として扱う）。
        /// </param>
        public static IReadOnlyList<string> Select(
            IReadOnlyList<string> modelFiles, string speakerName, string styleName, Func<string, string> readMetasJson)
        {
            if (modelFiles == null) throw new ArgumentNullException(nameof(modelFiles));
            if (readMetasJson == null) throw new ArgumentNullException(nameof(readMetasJson));
            if (string.IsNullOrWhiteSpace(speakerName)) throw new ArgumentException("話者名が空です。", nameof(speakerName));
            if (string.IsNullOrWhiteSpace(styleName)) throw new ArgumentException("スタイル名が空です。", nameof(styleName));
            if (modelFiles.Count <= 1) return modelFiles;

            var exact = new List<string>();
            var sameSpeaker = new List<string>();
            var anyTalk = new List<string>();

            foreach (var modelFile in modelFiles)
            {
                IReadOnlyList<VoicevoxSpeakerMeta> metas;
                try
                {
                    var json = readMetasJson(modelFile);
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    metas = VoicevoxStyleResolver.ParseMetas(json);
                }
                catch (TtsSetupException)
                {
                    // メタ情報が読めない .vvm は候補に入れない（全件フォールバックで拾われる）。
                    continue;
                }
                catch (Native.VoicevoxException)
                {
                    continue;
                }

                var match = Classify(metas, speakerName, styleName);
                switch (match)
                {
                    case VoicevoxStyleMatch.Exact:
                        exact.Add(modelFile);
                        break;
                    case VoicevoxStyleMatch.SpeakerFallback:
                        sameSpeaker.Add(modelFile);
                        break;
                    case VoicevoxStyleMatch.AnySpeakerFallback:
                        anyTalk.Add(modelFile);
                        break;
                }
            }

            if (exact.Count > 0) return exact;
            if (sameSpeaker.Count > 0) return sameSpeaker;
            if (anyTalk.Count > 0) return anyTalk;
            return modelFiles;
        }

        /// <summary>
        /// ネイティブ API（<c>voicevox_voice_model_file_open</c> →
        /// <c>voicevox_voice_model_file_create_metas_json</c>）でメタ情報を読み、絞り込む。
        /// voicevox_core.dll がロードできる状態で呼ぶこと。
        /// </summary>
        internal static IReadOnlyList<string> SelectWithNative(
            IReadOnlyList<string> modelFiles, string speakerName, string styleName)
            => Select(modelFiles, speakerName, styleName, ReadMetasJsonWithNative);

        /// <summary>1 つの .vvm からメタ情報 JSON を読む。</summary>
        /// <exception cref="VoicevoxException">.vvm を開けないとき</exception>
        internal static string ReadMetasJsonWithNative(string modelFile)
        {
            Vv.Check(
                VoicevoxNative.voicevox_voice_model_file_open(Utf8.ToNullTerminated(modelFile), out var model),
                $"音声モデルのメタ情報の読み込み（{Path.GetFileName(modelFile)}）");

            try
            {
                var ptr = VoicevoxNative.voicevox_voice_model_file_create_metas_json(model);
                try
                {
                    return Utf8.FromPtr(ptr);
                }
                finally
                {
                    if (ptr != IntPtr.Zero) VoicevoxNative.voicevox_json_free(ptr);
                }
            }
            finally
            {
                VoicevoxNative.voicevox_voice_model_file_delete(model);
            }
        }

        /// <summary>この .vvm が、指定の話者・スタイルに対してどの段階で一致するか。</summary>
        private static VoicevoxStyleMatch? Classify(
            IReadOnlyList<VoicevoxSpeakerMeta> metas, string speakerName, string styleName)
        {
            try
            {
                var resolution = VoicevoxStyleResolver.Resolve(metas, speakerName, styleName);
                return resolution.Match;
            }
            catch (TtsSetupException)
            {
                // talk スタイルが 1 つも無い（歌唱専用 s0.vvm など）。
                return null;
            }
        }
    }
}
