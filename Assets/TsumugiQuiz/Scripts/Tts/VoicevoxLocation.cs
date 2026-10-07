using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TsumugiQuiz.Tts
{
    /// <summary>TTS が利用可能かどうかの自己診断結果（docs/tts.md §10.4）。</summary>
    public enum TtsReadiness
    {
        /// <summary>すべてそろっている。</summary>
        Ready = 0,

        /// <summary>voicevox_core.dll / voicevox_onnxruntime.dll が無い。</summary>
        MissingNative = 1,

        /// <summary>Open JTalk 辞書が無い。</summary>
        MissingDictionary = 2,

        /// <summary>.vvm が 1 つも無い。</summary>
        MissingModels = 3,
    }

    /// <summary>配置パスのオーバーライド（設定 tts.assetPathOverride 相当）。</summary>
    public readonly struct VoicevoxPathOverrides
    {
        /// <summary>辞書・音声モデルを置いたディレクトリ（voicevox_core 相当のルート）。</summary>
        public string AssetPath { get; }

        /// <summary>ネイティブ DLL を置いたディレクトリ。</summary>
        public string NativeDir { get; }

        public VoicevoxPathOverrides(string assetPath, string nativeDir = null)
        {
            AssetPath = assetPath;
            NativeDir = nativeDir;
        }
    }

    /// <summary>
    /// 解決済みの voicevox_core 配置。生成後は不変。
    /// </summary>
    public sealed class VoicevoxLocation
    {
        private readonly string[] _voiceModelFiles;

        private VoicevoxLocation(string nativeDir, string dictionaryDir, string[] voiceModelFiles)
        {
            NativeDir = nativeDir;
            DictionaryDir = dictionaryDir;
            _voiceModelFiles = voiceModelFiles;
        }

        /// <summary>ネイティブ DLL のディレクトリ。</summary>
        public string NativeDir { get; }

        /// <summary>Open JTalk 辞書ディレクトリ。未配置なら null。</summary>
        public string DictionaryDir { get; }

        /// <summary>読み込む .vvm のパス一覧。</summary>
        public IReadOnlyList<string> VoiceModelFiles => _voiceModelFiles;

        /// <summary>起動時の自己診断結果。</summary>
        public TtsReadiness Readiness
        {
            get
            {
                if (!VoicevoxPaths.NativeLibrariesExist(NativeDir)) return TtsReadiness.MissingNative;
                if (string.IsNullOrEmpty(DictionaryDir)) return TtsReadiness.MissingDictionary;
                if (_voiceModelFiles.Length == 0) return TtsReadiness.MissingModels;
                return TtsReadiness.Ready;
            }
        }

        /// <summary>与えられたネイティブディレクトリと探索ルートから配置を解決する。</summary>
        public static VoicevoxLocation Create(string nativeDir, IReadOnlyList<string> assetRoots)
        {
            if (string.IsNullOrWhiteSpace(nativeDir)) throw new ArgumentException("ネイティブディレクトリが空です。", nameof(nativeDir));
            if (assetRoots == null) throw new ArgumentNullException(nameof(assetRoots));

            var dictionaryDir = VoicevoxPaths.FindDictionaryDir(assetRoots);
            var models = VoicevoxPaths.FindVoiceModelFiles(assetRoots).ToArray();
            return new VoicevoxLocation(nativeDir, dictionaryDir, models);
        }

        /// <summary>ログ用の説明文。ユーザー向け文言ではなく詳細ログに使う。</summary>
        public string Describe()
        {
            var models = _voiceModelFiles.Length == 0
                ? "(なし)"
                : string.Join(", ", _voiceModelFiles.Select(Path.GetFileName));
            return $"nativeDir={NativeDir} / dictionaryDir={DictionaryDir ?? "(なし)"} / vvm={models} / readiness={Readiness}";
        }
    }
}
