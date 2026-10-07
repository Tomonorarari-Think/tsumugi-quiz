using System;
using System.Collections.Generic;
using System.Linq;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// 編集フォーム（issue #31）の保存前バリデーション。ルールそのものは
    /// <see cref="QuestionSetValidator"/>（docs/question-data.md §2）を単一の出所として使う。
    /// 「候補の問題1件を差し替えたセット」を検証することで、他の問題を含む全体整合性
    /// （例: questions が1件以上であること）まで確認したうえで、対象問題に関するエラーだけを返す。
    /// </summary>
    public static class QuestionFormValidation
    {
        /// <summary>
        /// <paramref name="candidateQuestion"/> を <paramref name="currentSet"/> 内の同じ id の問題と
        /// 差し替えたセットを検証し、その問題に関するエラー（<see cref="ValidationError.QuestionId"/> が
        /// null またはこの問題の id のもの）だけを返す。
        /// </summary>
        public static IReadOnlyList<ValidationError> Validate(
            QuestionSet currentSet, Question candidateQuestion, string setBaseDirectory)
        {
            if (currentSet == null)
            {
                throw new ArgumentNullException(nameof(currentSet));
            }

            if (candidateQuestion == null)
            {
                throw new ArgumentNullException(nameof(candidateQuestion));
            }

            var updatedQuestions = currentSet.Questions
                .Select(q => q.Id == candidateQuestion.Id ? candidateQuestion : q)
                .ToArray();
            var updatedSet = new QuestionSet(
                currentSet.SchemaVersion, currentSet.SetId, currentSet.Title, currentSet.Description, updatedQuestions);

            var allErrors = new QuestionSetValidator().Validate(updatedSet, setBaseDirectory);
            return allErrors
                .Where(e => e.QuestionId == null || e.QuestionId == candidateQuestion.Id)
                .ToArray();
        }

        /// <summary>
        /// <see cref="ValidationError.Message"/>（<see cref="QuestionSetValidator"/> が生成する固定の
        /// 日本語文面）から、フォーム上でハイライトすべきフィールドを推定する。
        /// <see cref="QuestionSetValidator"/> のメッセージ文言を変更した場合はここも見直すこと。
        /// </summary>
        public static QuestionFormField MapField(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return QuestionFormField.Other;
            }

            // readingText は text より先に判定する（"readingText" は大文字 T を含むため
            // 実際には "text"（小文字）の部分文字列と一致しないが、意図を明確にするため順序を保つ）。
            if (message.Contains("readingText"))
            {
                return QuestionFormField.ReadingText;
            }

            if (message.Contains("text"))
            {
                return QuestionFormField.Text;
            }

            if (message.Contains("answers"))
            {
                return QuestionFormField.Answers;
            }

            // correctIndex は choices より先に判定する。"correctIndex が範囲外です（...choices件数: N）"
            // のように、correctIndex のエラーメッセージ自身が "choices" という語を含むことがあるため。
            if (message.Contains("correctIndex"))
            {
                return QuestionFormField.CorrectIndex;
            }

            if (message.Contains("choices"))
            {
                return QuestionFormField.Choices;
            }

            if (message.Contains("imagePath"))
            {
                return QuestionFormField.ImagePath;
            }

            if (message.Contains("tags"))
            {
                return QuestionFormField.Tags;
            }

            if (message.Contains("difficulty"))
            {
                return QuestionFormField.Difficulty;
            }

            if (message.Contains("type"))
            {
                return QuestionFormField.Type;
            }

            return QuestionFormField.Other;
        }
    }
}
