using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 自由入力回答の正誤判定。<see cref="AnswerNormalizer"/> で正規化した上で、
    /// 複数正解（answers 配列）のいずれか1つに一致すれば正解とする（docs/question-data.md §5）。
    /// </summary>
    public static class AnswerMatcher
    {
        /// <summary>
        /// 入力文字列が正解候補のいずれかに正規化後一致するかどうかを判定する。
        /// input・answers が null / 空でも例外を送出せず、不正解（false）として扱う。
        /// </summary>
        public static bool IsCorrect(string input, IReadOnlyList<string> answers)
        {
            if (string.IsNullOrEmpty(input) || answers == null)
            {
                return false;
            }

            string normalizedInput = AnswerNormalizer.Normalize(input);
            if (normalizedInput.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < answers.Count; i++)
            {
                string answer = answers[i];
                if (string.IsNullOrEmpty(answer))
                {
                    continue;
                }

                if (AnswerNormalizer.Normalize(answer) == normalizedInput)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
