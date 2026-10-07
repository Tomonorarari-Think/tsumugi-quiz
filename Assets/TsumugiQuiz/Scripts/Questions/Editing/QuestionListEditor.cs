using System;
using System.Collections.Generic;
using System.Linq;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// 問題エディタ（issue #30）の「問題一覧」に対する追加・削除・並び替えの純ロジック。
    /// docs/question-data.md §2 が定める「questions は1件以上」を守るため、削除可否の判定
    /// （<see cref="CanRemoveQuestion"/>）もここに集約する。
    /// 既存の <see cref="Question"/> / <see cref="QuestionSet"/> は不変データなので、
    /// すべてのメソッドは変更後の新しいコレクションを返す（既存のリストは変更しない）。
    /// </summary>
    public static class QuestionListEditor
    {
        /// <summary>問題セットに最低限必要な問題数（docs/question-data.md §2）。</summary>
        public const int MinQuestionCount = 1;

        private const string DefaultQuestionText = "新しい問題";
        private const string DefaultAnswerText = "回答";

        /// <summary>現在の問題数から、これ以上削除しても schemaVersion 上の制約を満たせるか。</summary>
        public static bool CanRemoveQuestion(int currentCount) => currentCount > MinQuestionCount;

        /// <summary>
        /// 追加用の空の問題を1件作る。<c>freeText</c> かつ最小限の内容（バリデーションを通過する値）にし、
        /// 詳細な編集は #31 の編集フォームに委ねる。
        /// </summary>
        public static Question CreateEmptyQuestion(IReadOnlyList<Question> existingQuestions)
        {
            var existingIds = (existingQuestions ?? Array.Empty<Question>()).Select(q => q.Id);
            var id = QuestionEditorNaming.NextQuestionId(existingIds);

            return new Question(
                id: id,
                type: QuestionType.FreeText,
                text: DefaultQuestionText,
                answers: new[] { DefaultAnswerText });
        }

        public static IReadOnlyList<Question> AddQuestion(IReadOnlyList<Question> questions, Question newQuestion)
        {
            if (questions == null)
            {
                throw new ArgumentNullException(nameof(questions));
            }

            if (newQuestion == null)
            {
                throw new ArgumentNullException(nameof(newQuestion));
            }

            var result = new List<Question>(questions.Count + 1);
            result.AddRange(questions);
            result.Add(newQuestion);
            return result.AsReadOnly();
        }

        public static IReadOnlyList<Question> RemoveQuestion(IReadOnlyList<Question> questions, string questionId)
        {
            if (questions == null)
            {
                throw new ArgumentNullException(nameof(questions));
            }

            return questions.Where(q => q.Id != questionId).ToArray();
        }

        /// <summary>
        /// <paramref name="fromIndex"/> の問題を <paramref name="toIndex"/> の位置へ移動する（並び替え）。
        /// </summary>
        public static IReadOnlyList<Question> Move(IReadOnlyList<Question> questions, int fromIndex, int toIndex)
        {
            if (questions == null)
            {
                throw new ArgumentNullException(nameof(questions));
            }

            if (fromIndex < 0 || fromIndex >= questions.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(fromIndex));
            }

            if (toIndex < 0 || toIndex >= questions.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(toIndex));
            }

            if (fromIndex == toIndex)
            {
                return questions.ToArray();
            }

            var list = new List<Question>(questions);
            var item = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, item);
            return list.ToArray();
        }
    }
}
