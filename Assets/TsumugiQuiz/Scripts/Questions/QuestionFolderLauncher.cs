using System;
using System.Diagnostics;
using System.IO;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// 問題フォルダをエクスプローラーで開くユーティリティ。「フォルダを開く」ボタン（issue #28/#30、
    /// #5 に引き継ぎの UI から呼び出される想定。本 issue #29 ではユーティリティのみ実装する）。
    /// フォルダの作成は行わない（<see cref="QuestionLibrary"/> の責務。M5/M6）。
    /// 存在しないフォルダを渡された場合は何もせず false を返す。
    /// 実際のプロセス起動処理は差し替え可能にしてあり、テストでは実際に explorer を起動せずに検証できる。
    /// </summary>
    public static class QuestionFolderLauncher
    {
        /// <summary>
        /// 指定フォルダをエクスプローラーで開く。
        /// </summary>
        /// <param name="folderPath">開くフォルダのパス（相対パスも <see cref="Path.GetFullPath(string)"/> で正規化する）。</param>
        /// <param name="processStarter">
        /// 実際にプロセスを起動する処理。既定は shell 経由でフォルダを開く（<see cref="StartExplorer"/>）。
        /// テストではダミーの処理を注入して検証する。
        /// </param>
        /// <returns>
        /// フォルダが実在し、プロセス起動にも成功した場合 true。
        /// フォルダが存在しない（ディレクトリではない場合も含む）場合、またはプロセス起動に失敗した場合は false。
        /// </returns>
        public static bool OpenInExplorer(string folderPath, Action<string> processStarter = null)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                throw new ArgumentException("folderPath を指定してください。", nameof(folderPath));
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(folderPath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[QuestionFolderLauncher] パスが不正です: {folderPath}: {ex.Message}");
                return false;
            }

            if (!Directory.Exists(fullPath))
            {
                UnityEngine.Debug.LogWarning($"[QuestionFolderLauncher] フォルダが存在しないため開けません（作成はこのユーティリティの責務ではありません）: {fullPath}");
                return false;
            }

            try
            {
                var starter = processStarter ?? StartExplorer;
                starter(fullPath);
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[QuestionFolderLauncher] フォルダを開けませんでした: {fullPath}: {ex.Message}");
                return false;
            }
        }

        private static void StartExplorer(string folderPath)
        {
            // M5: explorer.exe を明示的に指定するのではなく、フォルダパス自体を FileName にして
            // シェル経由（UseShellExecute）で開く。エクスプローラー以外が既定のファイラーに
            // 設定されている環境でもユーザーの既定動作に従う。
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true,
            });
        }
    }
}
