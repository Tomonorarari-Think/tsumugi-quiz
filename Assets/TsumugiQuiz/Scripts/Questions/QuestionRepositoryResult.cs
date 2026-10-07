using System.Collections.Generic;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// <see cref="QuestionRepository.LoadAll"/> の結果。正常に読み込めたセット、
    /// スキップされたセット（ファイルパス・エラー内容つき）に加え、特定の1ファイルに
    /// 紐づかないフォルダ単位のエラー（ファイル数上限超過など）を保持する。
    /// </summary>
    public sealed class QuestionRepositoryResult
    {
        /// <summary>正常に読み込み・検証を通過した問題セット。</summary>
        public IReadOnlyList<QuestionSet> Sets { get; }

        /// <summary>検証エラーまたは読み込みエラーによりスキップされた問題セット。</summary>
        public IReadOnlyList<QuestionSetLoadError> SkippedSets { get; }

        /// <summary>
        /// 個々のファイルに紐づかない、フォルダ単位のエラー（ファイル数上限超過・
        /// フォルダ列挙失敗等）のメッセージ一覧。
        /// </summary>
        public IReadOnlyList<string> FolderErrors { get; }

        public QuestionRepositoryResult(
            IReadOnlyList<QuestionSet> sets,
            IReadOnlyList<QuestionSetLoadError> skippedSets,
            IReadOnlyList<string> folderErrors)
        {
            Sets = sets;
            SkippedSets = skippedSets;
            FolderErrors = folderErrors;
        }
    }
}
