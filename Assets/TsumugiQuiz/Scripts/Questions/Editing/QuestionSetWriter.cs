using System;
using System.IO;
using System.Text;
using TsumugiQuiz.Core;
using UnityEngine;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// <see cref="QuestionSetSerializer"/> で組み立てた JSON をファイルへ書き込む（issue #30）。
    /// 例外を握りつぶさず、失敗時はユーザー向けメッセージと詳細ログの両方を残す。
    /// </summary>
    public static class QuestionSetWriter
    {
        // BOM なしの UTF-8（他の問題セット JSON・docs/samples/sample-questions.json と揃える）。
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// 既存ファイルの更新（追加・削除・並び替え等）用の書き込み。
        /// <see cref="AtomicFileWriter"/>（PR #88 レビュー H1。<c>FileSystemRoomFileSystem</c> と共有）により、
        /// 書き込み途中でプロセスが強制終了しても対象ファイルが壊れた状態のまま残らない。
        /// 対象ファイルが存在しない場合は新規作成として扱う（呼び出し側が既存ファイルの更新であることを
        /// 保証していること。新規ファイルの作成には衝突検出のできる <see cref="TryWriteNew"/> を使うこと）。
        /// </summary>
        public static bool TryWrite(string filePath, QuestionSet set, out string errorMessage)
        {
            try
            {
                var json = QuestionSetSerializer.Serialize(set);
                AtomicFileWriter.WriteAllText(filePath, json, Utf8NoBom);
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestionSetWriter] 書き込みに失敗しました: {filePath}: {ex.Message}");
                errorMessage = $"ファイルの書き込みに失敗しました: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 新規ファイルの作成専用（PR #88 レビュー H3・LOW）。まず一時ファイル（<c>.tmp</c>）へ内容を
        /// 書き切ってから <see cref="File.Move(string, string)"/>（上書きなし）で対象パスへ配置する。
        /// これにより、書き込み途中でプロセスが強制終了しても対象パスには何も現れない（アトミック性）と、
        /// 対象パスに既にファイルが存在する場合は <see cref="File.Move(string, string)"/> 自体が失敗する
        /// （衝突検出）の両方を、直接 <see cref="FileMode.CreateNew"/> で書き込む場合より安全に両立する
        /// （新規作成・複製は in-memory の一覧からファイル名を採番するため、他プロセス・前回操作で
        /// 既に同名ファイルができている場合に黙って上書きしてしまうことを防ぐ）。
        ///
        /// 一時ファイル名には <see cref="Guid.NewGuid"/> の先頭8文字を含める（PR #93 レビュー L6、issue #32。
        /// 文字数は PR #103 レビュー L7）。同じ対象パスに対して2プロセス（別ウィンドウ・別ユーザー）が
        /// 同時に新規作成すると、固定名（<c>filePath + ".tmp"</c>）では同じ一時ファイルを奪い合う可能性がある。
        /// 8文字（16進数で約42億通り）でも同時発生の衝突を避けるには十分で、ファイル名を必要以上に
        /// 長くしない。
        /// </summary>
        public static bool TryWriteNew(string filePath, QuestionSet set, out string errorMessage)
        {
            var tempPath = filePath + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";

            try
            {
                var json = QuestionSetSerializer.Serialize(set);
                File.WriteAllText(tempPath, json, Utf8NoBom);

                try
                {
                    File.Move(tempPath, filePath);
                }
                catch (IOException ex) when (File.Exists(filePath))
                {
                    // 衝突（対象パスに既にファイルが存在する）。IOException は他の理由でも起こりうるため、
                    // File.Exists で実際に衝突かどうかを判別してからメッセージを分岐する（PR #88 レビュー LOW）。
                    Debug.LogWarning($"[QuestionSetWriter] 新規作成に失敗しました（既に存在します）: {filePath}: {ex.Message}");
                    errorMessage = "ファイルが既に存在するため作成できませんでした。一覧が古い可能性があります。再読込してください。";
                    return false;
                }

                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestionSetWriter] 新規作成に失敗しました: {filePath}: {ex.Message}");
                errorMessage = $"ファイルの作成に失敗しました: {ex.Message}";
                return false;
            }
            finally
            {
                // File.Move が成功していれば tempPath は既に存在しないので何もしない。
                // 失敗時（衝突・その他の例外）は中途半端な .tmp を残さない。
                TryDeleteTempFile(tempPath);
            }
        }

        /// <summary>失敗時の一時ファイルの後始末（失敗しても何もしない）。</summary>
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
