using System.Collections.Generic;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// <see cref="RoomPresetStore"/> / <see cref="AppSettingsStore"/> が使うファイル I/O の抽象。
    /// テストではフェイク実装（メモリ上の辞書等）を注入できるようにする。
    /// </summary>
    public interface IRoomFileSystem
    {
        /// <summary>ファイルが存在するか。</summary>
        bool FileExists(string path);

        /// <summary>ディレクトリが存在するか。</summary>
        bool DirectoryExists(string path);

        /// <summary>ディレクトリが無ければ作成する（中間ディレクトリも含む）。</summary>
        void EnsureDirectory(string path);

        /// <summary>ファイルの内容をテキストとして読み込む。</summary>
        string ReadAllText(string path);

        /// <summary>ファイルにテキストを書き込む（上書き）。</summary>
        void WriteAllText(string path, string content);

        /// <summary>ファイルを削除する。存在しない場合は何もしない。</summary>
        void DeleteFile(string path);

        /// <summary>ディレクトリ直下（サブフォルダは対象外）で、拡張子が <c>.json</c> のファイル名（拡張子なし）一覧を返す。</summary>
        IReadOnlyList<string> ListJsonFileNames(string directoryPath);
    }
}
