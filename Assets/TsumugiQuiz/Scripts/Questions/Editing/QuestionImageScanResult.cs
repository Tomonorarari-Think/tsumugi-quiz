using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// <see cref="QuestionImageFolder.ListImageRelativePaths"/> の結果（PR #93 レビュー M3）。
    /// 一覧に出す画像の相対パスと、特定の1ファイルに紐づかないフォルダ単位の警告
    /// （件数上限による打ち切りなど）を分けて持つ。
    /// <see cref="QuestionSetScanResult"/> と同じ作法。
    /// </summary>
    public sealed class QuestionImageScanResult
    {
        /// <summary>一覧上限内の画像の、問題セットからの相対パス（例: <c>images/q1.png</c>）。</summary>
        public IReadOnlyList<string> RelativePaths { get; }

        /// <summary>フォルダ単位の警告（件数上限超過等）。画像一覧の近くに表示する想定。</summary>
        public IReadOnlyList<string> FolderWarnings { get; }

        public QuestionImageScanResult(IReadOnlyList<string> relativePaths, IReadOnlyList<string> folderWarnings)
        {
            RelativePaths = relativePaths ?? Array.Empty<string>();
            FolderWarnings = folderWarnings ?? Array.Empty<string>();
        }

        /// <summary>空の結果（フォルダが無い・列挙できない場合）。</summary>
        public static QuestionImageScanResult Empty { get; } =
            new QuestionImageScanResult(Array.Empty<string>(), Array.Empty<string>());
    }
}
