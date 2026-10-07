using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// <see cref="IRoomFileSystem"/> の実体（<c>System.IO</c> によるローカルファイルアクセス）。
    /// <c>TsumugiQuiz.Room</c> は Unity API に依存しないため、<c>Application.persistentDataPath</c> のような
    /// Unity 由来のパス解決はできない。呼び出し側が絶対パスを渡すこと。
    /// </summary>
    public sealed class FileSystemRoomFileSystem : IRoomFileSystem
    {
        /// <inheritdoc/>
        public bool FileExists(string path) => File.Exists(path);

        /// <inheritdoc/>
        public bool DirectoryExists(string path) => Directory.Exists(path);

        /// <inheritdoc/>
        public void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <inheritdoc/>
        public string ReadAllText(string path) => File.ReadAllText(path);

        /// <summary>
        /// アトミックに書き込む（<c>JsonConsentStorage.Save</c> の先例と同じ方式、#26 統括判断 H3）。
        /// 実体は <see cref="AtomicFileWriter"/>（PR #88 レビュー H1 で Core へ切り出し、
        /// <c>TsumugiQuiz.Questions.Editing.QuestionSetWriter</c> と共有）。
        /// </summary>
        /// <inheritdoc/>
        public void WriteAllText(string path, string content) => AtomicFileWriter.WriteAllText(path, content);

        /// <inheritdoc/>
        public void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> ListJsonFileNames(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return System.Array.Empty<string>();
            }

            return Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, System.StringComparer.Ordinal)
                .ToList();
        }
    }
}
