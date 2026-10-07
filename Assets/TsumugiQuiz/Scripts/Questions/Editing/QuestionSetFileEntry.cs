using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// 問題フォルダ内の1ファイルに対応する、問題エディタ用の不変データ（issue #30）。
    /// <see cref="QuestionRepository"/> と異なり、正常に読み込めたセットだけでなく
    /// 読み込み・検証に失敗したファイルも <see cref="FilePath"/> 付きで保持する
    /// （エディタ側で「壊れたファイルを一覧に表示して削除できる」ようにするため）。
    /// </summary>
    public sealed class QuestionSetFileEntry
    {
        /// <summary>ファイルの絶対パス。</summary>
        public string FilePath { get; }

        /// <summary>拡張子を除いたファイル名。</summary>
        public string FileName { get; }

        /// <summary>読み込み・検証に成功した場合の問題セット。失敗した場合は null。</summary>
        public QuestionSet Set { get; }

        /// <summary>読み込み・検証エラーのメッセージ一覧（成功時は空）。</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary><see cref="Set"/> が読み込めているか。</summary>
        public bool IsValid => Set != null;

        /// <summary>
        /// 読み込み時点でのファイルの最終更新日時（UTC）。ファイル情報を取得できなかった場合は
        /// <see cref="DateTime.MinValue"/>。<c>AddEmptyQuestion</c>/<c>RemoveQuestion</c>/<c>MoveQuestion</c> が
        /// 書き込み直前にこの値と実ファイルを比較し、他プロセス・他ウィンドウによる外部変更を検出する
        /// （PR #88 レビュー H3）。呼び出し側が値を渡し忘れて外部変更検出が無効化されることを防ぐため、
        /// 既定値を持たない必須引数にしている（PR #88 レビュー LOW）。
        /// </summary>
        public DateTime LastWriteTimeUtc { get; }

        public QuestionSetFileEntry(
            string filePath,
            string fileName,
            QuestionSet set,
            IReadOnlyList<string> errors,
            DateTime lastWriteTimeUtc)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("filePath を指定してください。", nameof(filePath));
            }

            FilePath = filePath;
            FileName = fileName ?? string.Empty;
            Set = set;
            Errors = errors ?? Array.Empty<string>();
            LastWriteTimeUtc = lastWriteTimeUtc;
        }
    }
}
