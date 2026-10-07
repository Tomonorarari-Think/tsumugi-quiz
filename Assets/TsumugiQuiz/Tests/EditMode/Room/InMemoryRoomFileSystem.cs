using System.Collections.Generic;
using System.IO;
using System.Linq;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="IRoomFileSystem"/> のメモリ上フェイク実装（テスト用）。実ファイルには一切触れない。
    /// <see cref="ThrowOnRead"/> / <see cref="ThrowOnWrite"/> で I/O 例外（H4）のテストに使える。
    /// </summary>
    public sealed class InMemoryRoomFileSystem : IRoomFileSystem
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
        private readonly HashSet<string> _directories = new HashSet<string>();

        /// <summary>true にすると <see cref="ReadAllText"/> が <see cref="IOException"/> を投げる。</summary>
        public bool ThrowOnRead { get; set; }

        /// <summary>true にすると <see cref="WriteAllText"/> が <see cref="IOException"/> を投げる。</summary>
        public bool ThrowOnWrite { get; set; }

        public bool FileExists(string path) => _files.ContainsKey(path);

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public void EnsureDirectory(string path) => _directories.Add(path);

        public string ReadAllText(string path)
        {
            if (ThrowOnRead)
            {
                throw new IOException("テスト用に注入した読み込みエラー。");
            }

            return _files[path];
        }

        public void WriteAllText(string path, string content)
        {
            if (ThrowOnWrite)
            {
                throw new IOException("テスト用に注入した書き込みエラー。");
            }

            _files[path] = content;
        }

        public void DeleteFile(string path) => _files.Remove(path);

        public IReadOnlyList<string> ListJsonFileNames(string directoryPath)
        {
            var prefix = directoryPath.TrimEnd('/', '\\') + "\\";
            return _files.Keys
                .Where(p => p.StartsWith(prefix) && p.EndsWith(".json"))
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .OrderBy(name => name, System.StringComparer.Ordinal)
                .ToList();
        }
    }
}
