using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// <see cref="ITtsCacheFileSystem"/> の <c>System.IO</c> 実装。
    /// 書き込みは <c>&lt;path&gt;.&lt;プロセス ID&gt;.tmp</c> へ書いてから置き換える（docs/tts.md §7.3）。
    ///
    /// 一時ファイル名にプロセス ID を入れているのは、同じキャッシュディレクトリを
    /// 複数プロセス（Unity Editor と実行中のビルド、複数のテストランナー）が同時に使ったときに
    /// 一時ファイルを踏み合わないようにするため。残骸の掃除でも、生きている別プロセスのものは残す。
    /// </summary>
    public sealed class TtsCacheFileSystem : ITtsCacheFileSystem
    {
        /// <summary>アトミック書き込みに使う一時ファイルの拡張子。</summary>
        public const string TempSuffix = ".tmp";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static readonly int CurrentProcessId =
            System.Diagnostics.Process.GetCurrentProcess().Id;

        /// <summary>このプロセスがアトミック書き込みに使う一時ファイルのパス。</summary>
        public static string BuildTempPath(string path)
            => path + "." + CurrentProcessId.ToString(CultureInfo.InvariantCulture) + TempSuffix;

        /// <summary>
        /// 一時ファイルが「生きている別プロセスのもの」かどうか。
        /// true のものは掃除の対象にしない（書き込み中のファイルを消さないため）。
        /// 名前からプロセス ID を読めない場合は古い形式の残骸とみなして false を返す。
        /// </summary>
        public static bool IsTempFileOfOtherLiveProcess(string path)
        {
            if (!TryParseTempProcessId(path, out var processId)) return false;
            if (processId == CurrentProcessId) return false;

            try
            {
                using (System.Diagnostics.Process.GetProcessById(processId))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // そのプロセスはもう居ない = 残骸。
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryParseTempProcessId(string path, out int processId)
        {
            processId = 0;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var withoutSuffix = path.Substring(0, path.Length - TempSuffix.Length);
            var lastDot = withoutSuffix.LastIndexOf('.');
            if (lastDot < 0 || lastDot == withoutSuffix.Length - 1) return false;

            return int.TryParse(
                withoutSuffix.Substring(lastDot + 1),
                NumberStyles.None, CultureInfo.InvariantCulture, out processId);
        }

        /// <inheritdoc/>
        public bool FileExists(string path) => File.Exists(path);

        /// <inheritdoc/>
        public long GetFileSize(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : -1L;
        }

        /// <inheritdoc/>
        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        /// <inheritdoc/>
        public byte[] ReadPrefix(string path, int maxBytes)
        {
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "1 以上で指定してください。");

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var length = (int)Math.Min(maxBytes, stream.Length);
                var buffer = new byte[length];
                var read = 0;
                while (read < length)
                {
                    var chunk = stream.Read(buffer, read, length - read);
                    if (chunk <= 0) break;
                    read += chunk;
                }

                if (read == length) return buffer;

                var trimmed = new byte[read];
                Array.Copy(buffer, trimmed, read);
                return trimmed;
            }
        }

        /// <inheritdoc/>
        public void WriteAllBytesAtomic(string path, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            EnsureParentDirectory(path);
            var tempPath = BuildTempPath(path);
            File.WriteAllBytes(tempPath, bytes);
            Replace(tempPath, path);
        }

        /// <inheritdoc/>
        public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

        /// <inheritdoc/>
        public void WriteAllTextAtomic(string path, string contents)
        {
            if (contents == null) throw new ArgumentNullException(nameof(contents));

            EnsureParentDirectory(path);
            var tempPath = BuildTempPath(path);
            File.WriteAllText(tempPath, contents, Utf8NoBom);
            Replace(tempPath, path);
        }

        /// <inheritdoc/>
        public void DeleteFile(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        /// <inheritdoc/>
        public bool DirectoryExists(string path) => Directory.Exists(path);

        /// <inheritdoc/>
        public void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> EnumerateFiles(string root, string searchPattern)
        {
            if (!Directory.Exists(root)) return Array.Empty<string>();

            var files = Directory.GetFiles(root, searchPattern, SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static void EnsureParentDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// 一時ファイルで置き換える。<c>File.Move</c> は宛先が存在すると失敗するので、
        /// 存在する場合は <c>File.Replace</c> を使う（どちらも同一ボリューム内ではアトミック）。
        /// </summary>
        private static void Replace(string tempPath, string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch
            {
                // 置き換えに失敗したら一時ファイルを残さない（次回の走査でゴミとして拾わないため）。
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // 一時ファイルの後始末の失敗は無視する（本来の例外を隠さない）。
                }
                catch (UnauthorizedAccessException)
                {
                }

                throw;
            }
        }
    }
}
