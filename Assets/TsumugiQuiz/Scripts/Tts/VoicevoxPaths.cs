using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// voicevox_core のネイティブ DLL・辞書・音声モデルの配置規約（仮決め K24）とその探索。
    ///
    /// 配置（scripts/setup-external.ps1 がコピーする）:
    ///   Assets/Plugins/voicevox_core/x86_64/{voicevox_core.dll, voicevox_onnxruntime.dll}
    ///   Assets/StreamingAssets/voicevox_core/{open_jtalk_dic_utf_8-1.11, models/vvms/*.vvm}
    ///
    /// Unity API に触れない純粋な探索ロジック（static メソッド群）と、
    /// Application.* を使う <see cref="Resolve"/> を分けてあり、前者は EditMode テストで検証する。
    /// </summary>
    public static class VoicevoxPaths
    {
        public const string RootDirName = "voicevox_core";
        public const string DictionaryDirName = "open_jtalk_dic_utf_8-1.11";
        public const string DictionaryDirPrefix = "open_jtalk_dic";
        public const string DictionaryMarkerFileName = "sys.dic";
        public const string ModelsDirName = "models";
        public const string VoiceModelsDirName = "vvms";
        public const string VoiceModelSearchPattern = "*.vvm";

        /// <summary>ファイル名の実体は <c>TsumugiQuiz.Tts.Native.VoicevoxNative</c> 側で定義する。</summary>
        public const string CoreDllFileName = Native.VoicevoxNative.CoreDllFileName;

        /// <inheritdoc cref="CoreDllFileName"/>
        public const string OnnxruntimeDllFileName = Native.VoicevoxNative.OnnxruntimeDllFileName;
        public const string PluginsDirName = "Plugins";
        public const string PluginArchDirName = "x86_64";

        /// <summary>
        /// ネイティブ DLL のディレクトリを決める。
        /// エディタでは Assets/Plugins/voicevox_core/x86_64、ビルド後は &lt;Product&gt;_Data/Plugins/x86_64。
        /// </summary>
        /// <param name="dataPath">Application.dataPath 相当</param>
        /// <param name="isEditor">Application.isEditor 相当</param>
        /// <param name="nativeDirOverride">明示指定。空でなければ最優先</param>
        public static string ResolveNativeDir(string dataPath, bool isEditor, string nativeDirOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(nativeDirOverride))
            {
                return nativeDirOverride;
            }
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                throw new ArgumentException("dataPath が空です。", nameof(dataPath));
            }

            return isEditor
                ? Path.Combine(dataPath, PluginsDirName, RootDirName, PluginArchDirName)
                : Path.Combine(dataPath, PluginsDirName, PluginArchDirName);
        }

        /// <summary>
        /// 辞書・音声モデルを探すルートディレクトリを優先順に返す（docs/tts.md §10.4）。
        /// 1. 設定によるオーバーライド 2. StreamingAssets/voicevox_core 3. persistentDataPath/voicevox_core
        /// </summary>
        public static IReadOnlyList<string> ResolveAssetRoots(
            string streamingAssetsPath, string persistentDataPath, string assetPathOverride = null)
        {
            var roots = new List<string>(3);
            AddIfUsable(roots, assetPathOverride);
            AddIfUsable(roots, CombineRoot(streamingAssetsPath));
            AddIfUsable(roots, CombineRoot(persistentDataPath));
            return roots;
        }

        /// <summary>Open JTalk 辞書ディレクトリを探す。見つからなければ null。</summary>
        /// <exception cref="TtsSetupException">ディレクトリの走査に失敗したとき（権限・I/O）</exception>
        public static string FindDictionaryDir(IReadOnlyList<string> assetRoots)
        {
            if (assetRoots == null) throw new ArgumentNullException(nameof(assetRoots));

            foreach (var root in assetRoots)
            {
                if (string.IsNullOrWhiteSpace(root) || !SafeDirectoryExists(root)) continue;

                foreach (var baseDir in new[] { root, Path.Combine(root, "dict") })
                {
                    if (!SafeDirectoryExists(baseDir)) continue;

                    var exact = Path.Combine(baseDir, DictionaryDirName);
                    if (IsDictionaryDir(exact)) return exact;

                    // 辞書のバージョンが上がってもフォルダ名の接頭辞で拾えるようにする。
                    foreach (var candidate in EnumerateDirectories(baseDir, DictionaryDirPrefix + "*"))
                    {
                        if (IsDictionaryDir(candidate)) return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>読み込む .vvm のパス一覧を返す。0 件なら未配置。</summary>
        /// <exception cref="TtsSetupException">ディレクトリの走査に失敗したとき（権限・I/O）</exception>
        public static IReadOnlyList<string> FindVoiceModelFiles(IReadOnlyList<string> assetRoots)
        {
            if (assetRoots == null) throw new ArgumentNullException(nameof(assetRoots));

            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in assetRoots)
            {
                if (string.IsNullOrWhiteSpace(root) || !SafeDirectoryExists(root)) continue;

                // models/ 配下（models/vvms/0.vvm など）を再帰的に、続いてルート直下を探す。
                foreach (var dir in new[] { Path.Combine(root, ModelsDirName), Path.Combine(root, VoiceModelsDirName) })
                {
                    if (!SafeDirectoryExists(dir)) continue;
                    AddModels(found, seen, EnumerateFiles(dir, SearchOption.AllDirectories));
                }

                AddModels(found, seen, EnumerateFiles(root, SearchOption.TopDirectoryOnly));
            }

            return found;
        }

        /// <summary>voicevox_core.dll と voicevox_onnxruntime.dll が両方そろっているか。</summary>
        public static bool NativeLibrariesExist(string nativeDir)
            => CoreDllExists(nativeDir) && OnnxruntimeDllExists(nativeDir);

        /// <summary>
        /// voicevox_core.dll だけが存在するか。<see cref="NativeLibrariesExist"/> が false のとき、
        /// どちらの DLL が無いのかを切り分けるために使う（<see cref="TtsUnavailableReason.MissingCoreDll"/> /
        /// <see cref="TtsUnavailableReason.MissingOnnxRuntime"/> の判定、docs/tts.md §9）。
        /// </summary>
        public static bool CoreDllExists(string nativeDir)
            => !string.IsNullOrWhiteSpace(nativeDir) && File.Exists(Path.Combine(nativeDir, CoreDllFileName));

        /// <inheritdoc cref="CoreDllExists"/>
        public static bool OnnxruntimeDllExists(string nativeDir)
            => !string.IsNullOrWhiteSpace(nativeDir) && File.Exists(Path.Combine(nativeDir, OnnxruntimeDllFileName));

        /// <summary>Unity の Application.* を使って現在の環境の配置を解決する。</summary>
        public static VoicevoxLocation Resolve(VoicevoxPathOverrides overrides = default)
        {
            var nativeDir = ResolveNativeDir(Application.dataPath, Application.isEditor, overrides.NativeDir);
            var roots = ResolveAssetRoots(
                Application.streamingAssetsPath, Application.persistentDataPath, overrides.AssetPath);
            return VoicevoxLocation.Create(nativeDir, roots);
        }

        private static void AddModels(List<string> found, HashSet<string> seen, string[] files)
        {
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var full = Path.GetFullPath(file);
                if (seen.Add(full)) found.Add(full);
            }
        }

        private static bool IsDictionaryDir(string path)
            => SafeDirectoryExists(path) && File.Exists(Path.Combine(path, DictionaryMarkerFileName));

        /// <summary>パスが不正（無効な文字など）でも例外にせず false を返す。</summary>
        private static bool SafeDirectoryExists(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string[] EnumerateDirectories(string baseDir, string searchPattern)
        {
            try
            {
                return Directory.GetDirectories(baseDir, searchPattern);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new TtsSetupException(
                    $"読み上げ用ファイルのディレクトリを走査できませんでした（{baseDir}）: {e.Message}", e);
            }
        }

        private static string[] EnumerateFiles(string dir, SearchOption option)
        {
            try
            {
                return Directory.GetFiles(dir, VoiceModelSearchPattern, option);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new TtsSetupException(
                    $"音声モデル（{VoiceModelSearchPattern}）を走査できませんでした（{dir}）: {e.Message}", e);
            }
        }

        private static string CombineRoot(string basePath)
            => string.IsNullOrWhiteSpace(basePath) ? null : Path.Combine(basePath, RootDirName);

        private static void AddIfUsable(List<string> roots, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!roots.Contains(path, StringComparer.OrdinalIgnoreCase)) roots.Add(path);
        }
    }
}
