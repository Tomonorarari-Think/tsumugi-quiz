using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TsumugiQuiz.Tts.Native
{
    /// <summary>ネイティブ DLL の検索パス対策として実際に効いた手段（docs/tts-native-api.md §3.3）。</summary>
    public enum NativeLoadStrategy
    {
        /// <summary>何もしていない（Unity の Plugin 解決と絶対パス指定だけに任せた）。</summary>
        None = 0,

        /// <summary>Unity の Plugin 解決に任せた（事前ロードは失敗または未実施）。</summary>
        UnityPluginResolution = 1,

        /// <summary>SetDllDirectoryW でネイティブディレクトリを検索パスに追加した。</summary>
        SetDllDirectory = 2,

        /// <summary>LoadLibraryExW（LOAD_WITH_ALTERED_SEARCH_PATH）で絶対パスから事前ロードした。</summary>
        PreloadAbsolutePath = 3,
    }

    /// <summary>
    /// voicevox_core.dll / voicevox_onnxruntime.dll の検索パス対策（docs/tts-native-api.md §3.3）。
    ///
    /// <b>実測（2026-09-13）では、`voicevox_onnxruntime_load_once` に絶対パスを渡す対策（§3.1）だけで
    /// エディタ・ビルド後の両方で解決できたため、ここの手当ては既定では最小限（事前ロードのみ）</b>。
    /// `SetDllDirectoryW` はプロセス全体の DLL 検索パスを書き換える副作用があるので、
    /// <see cref="Prepare"/> に <c>useSetDllDirectory: true</c> を渡したときだけ使い、
    /// 使った場合も <c>finally</c> で元に戻す。
    /// </summary>
    internal static class NativeLibraryLoader
    {
        private const uint LoadWithAlteredSearchPath = 0x00000008;

        private static readonly object Gate = new object();
        private static string _preparedDirectory;

        /// <summary>直近の <see cref="Prepare"/> で成立した手段。テスト・診断ログ用。</summary>
        public static NativeLoadStrategy LastStrategy { get; private set; } = NativeLoadStrategy.None;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectoryW(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

        /// <summary>
        /// 2 つの DLL を絶対パスで事前ロードする。事前ロードに成功していれば、以降の
        /// DllImport("voicevox_core") は既にロード済みのモジュールで解決される（Windows のモジュール名一致）。
        /// </summary>
        /// <param name="nativeDir">voicevox_core.dll / voicevox_onnxruntime.dll のあるディレクトリ</param>
        /// <param name="useSetDllDirectory">
        /// true なら事前ロードの間だけ <c>SetDllDirectoryW</c> でネイティブディレクトリを検索パスへ追加し、
        /// 終了時に元に戻す。実測では不要なので既定は false。
        /// </param>
        /// <returns>
        /// <c>voicevox_core.dll</c> の事前ロードに成功したら true（<see cref="LastStrategy"/> は
        /// <see cref="NativeLoadStrategy.PreloadAbsolutePath"/>）。
        /// false のときは Unity の Plugin 解決に委ねる（<see cref="NativeLoadStrategy.UnityPluginResolution"/>）。
        /// いずれの場合も失敗として扱わない（絶対パス指定だけで動くのが既定の経路）。
        /// </returns>
        public static bool Prepare(string nativeDir, bool useSetDllDirectory = false)
        {
            if (string.IsNullOrEmpty(nativeDir)) throw new ArgumentException("ネイティブディレクトリが空です。", nameof(nativeDir));

            lock (Gate)
            {
                if (string.Equals(_preparedDirectory, nativeDir, StringComparison.OrdinalIgnoreCase))
                {
                    return LastStrategy == NativeLoadStrategy.PreloadAbsolutePath;
                }

                var corePath = Path.Combine(nativeDir, VoicevoxNative.CoreDllFileName);
                var ortPath = Path.Combine(nativeDir, VoicevoxNative.OnnxruntimeDllFileName);

                if (!Directory.Exists(nativeDir) || !File.Exists(corePath))
                {
                    // 配置が無いときは何も記録しない（呼び出し側が Readiness で判定する）。
                    LastStrategy = NativeLoadStrategy.UnityPluginResolution;
                    return false;
                }

                var searchPathAdded = useSetDllDirectory && SetDllDirectoryW(nativeDir);
                try
                {
                    // ONNX Runtime を先に読むと、voicevox_core 側の LoadLibraryExW が確実に成功する。
                    if (File.Exists(ortPath))
                    {
                        LoadLibraryExW(ortPath, IntPtr.Zero, LoadWithAlteredSearchPath);
                    }

                    var coreHandle = LoadLibraryExW(corePath, IntPtr.Zero, LoadWithAlteredSearchPath);

                    LastStrategy = coreHandle != IntPtr.Zero
                        ? NativeLoadStrategy.PreloadAbsolutePath
                        : searchPathAdded
                            ? NativeLoadStrategy.SetDllDirectory
                            : NativeLoadStrategy.UnityPluginResolution;

                    _preparedDirectory = nativeDir;
                    return coreHandle != IntPtr.Zero;
                }
                finally
                {
                    // プロセス全体の検索パスを書き換えたままにしない。
                    if (searchPathAdded) SetDllDirectoryW(null);
                }
            }
        }
    }
}
