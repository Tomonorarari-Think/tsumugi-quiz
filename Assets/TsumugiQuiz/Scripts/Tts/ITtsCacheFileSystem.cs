using System;
using System.Collections.Generic;
using System.IO;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// <see cref="TtsCache"/> が使うファイル I/O。
    /// 実装を差し替えられるようにして、EditMode テストで
    /// 「書き込みが失敗する」「読み込みが失敗する」ケースを再現できるようにする。
    ///
    /// 実装はスレッドセーフである必要はない（<see cref="TtsCache"/> 側で直列化する）。
    /// I/O 失敗は <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> で投げてよい。
    /// キャッシュの読み書き失敗は致命的でないので、<see cref="TtsCache"/> が捕捉してログに落とす。
    /// </summary>
    public interface ITtsCacheFileSystem
    {
        /// <summary>ファイルが存在するか。</summary>
        bool FileExists(string path);

        /// <summary>ファイルのバイト数。存在しなければ -1。</summary>
        long GetFileSize(string path);

        /// <summary>ファイル全体を読む。</summary>
        byte[] ReadAllBytes(string path);

        /// <summary>先頭 <paramref name="maxBytes"/> バイトだけ読む（ヘッダ解析用）。</summary>
        byte[] ReadPrefix(string path, int maxBytes);

        /// <summary>
        /// 一時ファイルへ書いてから置き換える（docs/tts.md §7.3 のアトミック書き込み）。
        /// 親ディレクトリが無ければ作る。
        /// </summary>
        void WriteAllBytesAtomic(string path, byte[] bytes);

        /// <summary>テキストを読む。</summary>
        string ReadAllText(string path);

        /// <summary>テキストを一時ファイル経由で置き換える。</summary>
        void WriteAllTextAtomic(string path, string contents);

        /// <summary>ファイルを削除する。存在しなければ何もしない。</summary>
        void DeleteFile(string path);

        /// <summary>ディレクトリが存在するか。</summary>
        bool DirectoryExists(string path);

        /// <summary>ディレクトリを（必要なら親ごと）作る。</summary>
        void EnsureDirectory(string path);

        /// <summary><paramref name="root"/> 配下を再帰的に走査してパターンに一致するファイルを返す。</summary>
        IReadOnlyList<string> EnumerateFiles(string root, string searchPattern);
    }
}
