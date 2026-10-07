using System;
using System.Collections.Generic;
using System.Linq;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// <see cref="QuestionLibrary"/> の読み込み結果。HostSetup 画面等の UI（issue #28/#30、#5 に引き継ぎ）へ
    /// そのまま渡し、読み込めたセット数・スキップされたセットの一覧（ファイル名・エラー内容）・
    /// フォルダ単位のエラー（ファイル数上限超過、フォルダ作成失敗、監視開始失敗等）を
    /// 表示するために使う（issue #29「読み込みエラー一覧表示」）。
    /// </summary>
    public sealed class QuestionLoadReport
    {
        /// <summary>この読み込みが完了した日時（ローカル時刻）。</summary>
        public DateTimeOffset LoadedAt { get; }

        /// <summary>正常に読み込み・検証を通過した問題セット。</summary>
        public IReadOnlyList<QuestionSet> Sets { get; }

        /// <summary>検証エラーまたは読み込みエラーによりスキップされた問題セット。</summary>
        public IReadOnlyList<QuestionSetLoadError> SkippedSets { get; }

        /// <summary>
        /// 特定の1ファイルに紐づかない、フォルダ単位のエラー（ファイル数上限超過、
        /// フォルダ作成失敗、フォルダ監視の開始失敗等。H1/M7）。
        /// </summary>
        public IReadOnlyList<string> FolderErrors { get; }

        /// <summary>
        /// L18: 呼び出し側が渡したリストを後から変更しても本インスタンスの内容が変わらないよう、
        /// コンストラクタで <c>ToArray()</c> により複製して保持する（不変データを優先する方針）。
        /// </summary>
        public QuestionLoadReport(
            DateTimeOffset loadedAt,
            IReadOnlyList<QuestionSet> sets,
            IReadOnlyList<QuestionSetLoadError> skippedSets,
            IReadOnlyList<string> folderErrors)
        {
            if (sets == null)
            {
                throw new ArgumentNullException(nameof(sets));
            }

            if (skippedSets == null)
            {
                throw new ArgumentNullException(nameof(skippedSets));
            }

            if (folderErrors == null)
            {
                throw new ArgumentNullException(nameof(folderErrors));
            }

            LoadedAt = loadedAt;
            Sets = sets.ToArray();
            SkippedSets = skippedSets.ToArray();
            FolderErrors = folderErrors.ToArray();
        }

        /// <summary>
        /// <see cref="QuestionRepositoryResult"/> からレポートを生成する。
        /// <paramref name="additionalFolderErrors"/> には、リポジトリ由来のもの以外の
        /// フォルダ単位のエラー（<see cref="QuestionLibrary"/> のフォルダ作成失敗・監視開始失敗等）を渡せる。
        /// </summary>
        public static QuestionLoadReport FromRepositoryResult(
            DateTimeOffset loadedAt,
            QuestionRepositoryResult result,
            IEnumerable<string> additionalFolderErrors = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var folderErrors = additionalFolderErrors == null
                ? result.FolderErrors
                : result.FolderErrors.Concat(additionalFolderErrors).ToArray();

            return new QuestionLoadReport(loadedAt, result.Sets, result.SkippedSets, folderErrors);
        }
    }
}
