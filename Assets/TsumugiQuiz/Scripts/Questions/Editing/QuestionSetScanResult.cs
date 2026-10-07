using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// <see cref="QuestionSetFileScanner.ScanFolder"/> の結果（PR #88 レビュー M2）。
    /// 個々のファイルに紐づく <see cref="Entries"/> と、特定の1ファイルに紐づかない
    /// フォルダ単位の警告（ファイル数上限超過など。<see cref="QuestionRepositoryResult.FolderErrors"/> と同じ考え方）
    /// を分けて持つ。
    /// </summary>
    public sealed class QuestionSetScanResult
    {
        /// <summary>スキャンできた（一覧上限内の）ファイルのエントリ一覧。</summary>
        public IReadOnlyList<QuestionSetFileEntry> Entries { get; }

        /// <summary>フォルダ単位の警告（ファイル数上限超過等）。一覧の先頭に表示する想定。</summary>
        public IReadOnlyList<string> FolderWarnings { get; }

        public QuestionSetScanResult(IReadOnlyList<QuestionSetFileEntry> entries, IReadOnlyList<string> folderWarnings)
        {
            Entries = entries ?? Array.Empty<QuestionSetFileEntry>();
            FolderWarnings = folderWarnings ?? Array.Empty<string>();
        }
    }
}
