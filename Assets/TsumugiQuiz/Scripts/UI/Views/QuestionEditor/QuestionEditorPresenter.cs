using System;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.UI.Views.QuestionEditor
{
    /// <summary>
    /// 問題エディタ（issue #30）の一覧行・確認ダイアログ文言を組み立てる。
    /// Unity API に依存しない純 C# で、EditMode から直接テストできる。
    /// </summary>
    public static class QuestionEditorPresenter
    {
        private const int MaxQuestionTextPreviewLength = 40;

        /// <summary>セット一覧の1行分の表示文字列。</summary>
        public static string SetSummary(QuestionSetFileEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (entry.IsValid)
            {
                return $"{entry.Set.Title}（{entry.Set.Questions.Count}問） - {entry.FileName}.json";
            }

            return $"{entry.FileName}.json（読み込みエラー: {FirstErrorOrDefault(entry)}）";
        }

        /// <summary>問題一覧の1行分の表示文字列（id / type / text の要約）。</summary>
        public static string QuestionSummary(Question question, int displayIndex)
        {
            if (question == null)
            {
                throw new ArgumentNullException(nameof(question));
            }

            var typeLabel = question.Type == QuestionType.Choice ? "選択式" : "自由入力";
            var preview = TruncateForPreview(question.Text);
            return $"{displayIndex + 1}. [{typeLabel}] {question.Id}: {preview}";
        }

        /// <summary>セット削除の確認ダイアログ文言。</summary>
        public static string DeleteSetConfirmMessage(QuestionSetFileEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            var displayName = entry.IsValid ? entry.Set.Title : $"{entry.FileName}.json";
            return $"「{displayName}」を削除しますか？ 元に戻せません。";
        }

        /// <summary>問題削除の確認ダイアログ文言。</summary>
        public static string DeleteQuestionConfirmMessage(Question question)
        {
            if (question == null)
            {
                throw new ArgumentNullException(nameof(question));
            }

            return $"問題「{question.Id}」を削除しますか？ 元に戻せません。";
        }

        private static string TruncateForPreview(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length > MaxQuestionTextPreviewLength
                ? text.Substring(0, MaxQuestionTextPreviewLength) + "…"
                : text;
        }

        private static string FirstErrorOrDefault(QuestionSetFileEntry entry)
            => entry.Errors.Count > 0 ? entry.Errors[0] : "不明なエラー";
    }
}
