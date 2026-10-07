using System;
using System.Collections.Generic;
using System.Linq;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// 問題エディタ（issue #30）が新規作成・複製のときに使う ID / ファイル名 / タイトルの採番ロジック。
    /// Unity API に依存しない純 C# で、EditMode から直接テストできる。
    /// </summary>
    public static class QuestionEditorNaming
    {
        private const string NewSetIdBase = "new-set";
        private const string NewFileNameBase = "new-set";
        private const string QuestionIdPrefix = "question-";
        private const string DuplicateSuffix = "-copy";
        private const string DuplicateTitleSuffix = "のコピー";

        /// <summary>
        /// 新しい問題の id を採番する。「question-1」から始め、既存の id と衝突しない最小の番号を選ぶ。
        /// </summary>
        public static string NextQuestionId(IEnumerable<string> existingIds)
        {
            var set = new HashSet<string>(existingIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            var index = 1;
            string candidate;
            do
            {
                candidate = QuestionIdPrefix + index;
                index++;
            }
            while (set.Contains(candidate));

            return candidate;
        }

        /// <summary>新規セット作成時の setId（"new-set" から。大文字小文字を区別する）。</summary>
        public static string NextSetIdForNew(IEnumerable<string> existingSetIds)
            => MakeUnique(NewSetIdBase, existingSetIds, StringComparer.Ordinal);

        /// <summary>新規セット作成時のファイル名（拡張子なし。Windows のファイル名は大文字小文字を区別しない）。</summary>
        public static string NextFileNameForNew(IEnumerable<string> existingFileNames)
            => MakeUnique(NewFileNameBase, existingFileNames, StringComparer.OrdinalIgnoreCase);

        /// <summary>複製時の setId（元の setId + "-copy"、衝突時は "-copy-2" 以降）。</summary>
        public static string NextSetIdForDuplicate(string originalSetId, IEnumerable<string> existingSetIds)
            => MakeUnique(originalSetId + DuplicateSuffix, existingSetIds, StringComparer.Ordinal);

        /// <summary>複製時のファイル名（元のファイル名 + "-copy"、衝突時は "-copy-2" 以降）。</summary>
        public static string NextFileNameForDuplicate(string originalFileName, IEnumerable<string> existingFileNames)
            => MakeUnique(originalFileName + DuplicateSuffix, existingFileNames, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 複製時のタイトル（元のタイトル + "のコピー"）。<see cref="QuestionLimits.MaxTitleLength"/> を
        /// 超える場合は、末尾の "のコピー" は必ず残したまま元タイトル側を切り詰める（PR #88 レビュー H4。
        /// 切り詰めずにそのまま書き込むと <c>QuestionSetValidator</c> の title 文字数チェックで
        /// 保存が失敗してしまうため）。
        /// </summary>
        public static string DuplicateTitle(string originalTitle)
        {
            originalTitle ??= string.Empty;
            var combined = originalTitle + DuplicateTitleSuffix;

            if (combined.Length <= QuestionLimits.MaxTitleLength)
            {
                return combined;
            }

            var allowedOriginalLength = QuestionLimits.MaxTitleLength - DuplicateTitleSuffix.Length;
            if (allowedOriginalLength <= 0)
            {
                // MaxTitleLength がサフィックス長より短いという、現行値（100文字）では起こり得ない
                // 極端なケースへの保険。サフィックス自体を切り詰める。
                return TruncateAtCharBoundary(DuplicateTitleSuffix, QuestionLimits.MaxTitleLength);
            }

            return TruncateAtCharBoundary(originalTitle, allowedOriginalLength) + DuplicateTitleSuffix;
        }

        /// <summary>
        /// サロゲートペア（絵文字等、UTF-16 で2 char 1文字になる文字）の途中で切らないように
        /// <paramref name="text"/> を <paramref name="maxLength"/> 文字（UTF-16 char 単位）以内へ切り詰める
        /// （PR #88 レビュー LOW）。境界がペアの間に来る場合は1文字分手前で切る。
        /// </summary>
        private static string TruncateAtCharBoundary(string text, int maxLength)
        {
            if (maxLength <= 0)
            {
                return string.Empty;
            }

            if (text.Length <= maxLength)
            {
                return text;
            }

            if (char.IsHighSurrogate(text[maxLength - 1]) && char.IsLowSurrogate(text[maxLength]))
            {
                maxLength -= 1;
            }

            return text.Substring(0, maxLength);
        }

        private static string MakeUnique(string baseValue, IEnumerable<string> existingValues, StringComparer comparer)
        {
            var values = (existingValues ?? Enumerable.Empty<string>()).ToArray();

            if (!values.Contains(baseValue, comparer))
            {
                return baseValue;
            }

            var index = 2;
            string candidate;
            do
            {
                candidate = $"{baseValue}-{index}";
                index++;
            }
            while (values.Contains(candidate, comparer));

            return candidate;
        }
    }
}
