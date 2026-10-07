using System;
using System.IO;
using System.Text;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// テキストファイルへのアトミックな書き込みを共通化するヘルパー（PR #88 レビュー H1）。
    /// 一時ファイル（<c>.tmp</c>）へ書き切ってから <see cref="File.Replace(string, string, string)"/>
    /// （既存ファイルがある場合）または <see cref="File.Move(string, string)"/>（無い場合）で置き換えるため、
    /// 書き込み途中でプロセスが強制終了しても対象ファイルが壊れた状態のまま残らない。
    /// 元は <c>TsumugiQuiz.Room.FileSystemRoomFileSystem.WriteAllText</c>（#26 統括判断 H3/M1）にあった
    /// 実装で、<c>TsumugiQuiz.Questions.Editing.QuestionSetWriter</c>（#30）と共有するため Core へ切り出した。
    /// </summary>
    public static class AtomicFileWriter
    {
        /// <summary>
        /// <paramref name="path"/> へ <paramref name="content"/> をアトミックに書き込む。
        /// <paramref name="encoding"/> を省略した場合は <see cref="File.WriteAllText(string, string)"/> の既定
        /// （BOM なし UTF-8。PR #88 レビュー LOW: 従来のコメントは誤りだった。
        /// https://learn.microsoft.com/dotnet/api/system.io.file.writealltext ）を使う。
        /// </summary>
        /// <exception cref="IOException">一時ファイルの書き込み・置き換えに失敗したとき。</exception>
        /// <exception cref="UnauthorizedAccessException">書き込み権限が無いとき。</exception>
        public static void WriteAllText(string path, string content, Encoding encoding = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("path を指定してください。", nameof(path));
            }

            var tempPath = path + ".tmp";

            try
            {
                if (encoding != null)
                {
                    File.WriteAllText(tempPath, content, encoding);
                }
                else
                {
                    File.WriteAllText(tempPath, content);
                }

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
                // 置き換えに失敗したら中途半端な .tmp を残さない。後始末そのものが失敗しても、
                // 元の例外を覆い隠さないよう握りつぶす。
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        /// <summary>置き換えに失敗したときの一時ファイルの後始末（失敗しても何もしない）。</summary>
        private static void TryDeleteTempFile(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception)
            {
                // 元の例外を隠さないよう、ここでは何もしない。
            }
        }
    }
}
