using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TsumugiQuiz.Questions.Images;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// docs/question-data.md §1（型・件数・文字数などスキーマそのものが定める制約）と
    /// §2（追加のバリデーション規則）を適用する。
    /// エラーが1件でも見つかった場合、呼び出し側（<see cref="QuestionRepository"/>）は
    /// そのセット全体をスキップする方針（セット単位のスキップ）を取る。
    /// </summary>
    public sealed class QuestionSetValidator
    {
        private const int SupportedSchemaVersion = 1;

        // 問題セット・問題 1 件あたりの上限は QuestionLimits（docs/question-data.md §1 / §2）を
        // 単一の出所として参照する。
        private const int MaxTitleLength = QuestionLimits.MaxTitleLength;
        private const int MaxDescriptionLength = QuestionLimits.MaxDescriptionLength;
        // 配信用 DTO（TsumugiQuiz.Network.QuestionDto）も同じ値を使うため、
        // 「読み込めたのに配信できない問題」が生まれない（#13）。
        private const int MaxIdLength = QuestionLimits.MaxIdLength;
        private const int MaxTextLength = QuestionLimits.MaxTextLength;
        private const int MaxReadingTextLength = QuestionLimits.MaxReadingTextLength;
        private const int MaxAnswerLength = QuestionLimits.MaxAnswerLength;
        private const int MaxAnswerCount = QuestionLimits.MaxAnswerCount;
        private const int MaxChoiceLength = QuestionLimits.MaxChoiceLength;
        private const int MinChoiceCount = QuestionLimits.MinChoiceCount;
        private const int MaxChoiceCount = QuestionLimits.MaxChoiceCount;
        private const int MaxTagCount = QuestionLimits.MaxTagCount;
        private const int MaxTagLength = QuestionLimits.MaxTagLength;
        private const int MinDifficulty = QuestionLimits.MinDifficulty;
        private const int MaxDifficulty = QuestionLimits.MaxDifficulty;

        private static readonly Regex SetIdPattern = new Regex(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

        /// <summary>
        /// 問題セットを検証し、見つかったエラーを一覧で返す（空の場合は検証を通過）。
        /// </summary>
        /// <param name="set">検証対象の問題セット。</param>
        /// <param name="setBaseDirectory">
        /// imagePath の相対パス解決の基準ディレクトリ（セットファイルの配置先）。
        /// null / 空文字の場合は画像の実在チェックを省略する（拡張子チェックのみ行う）。
        /// </param>
        public IReadOnlyList<ValidationError> Validate(QuestionSet set, string setBaseDirectory)
        {
            if (set == null)
            {
                return new[] { new ValidationError("問題セットが読み込めませんでした（null）。") };
            }

            var errors = new List<ValidationError>();

            if (set.SchemaVersion != SupportedSchemaVersion)
            {
                errors.Add(new ValidationError(
                    $"schemaVersion が {SupportedSchemaVersion} ではありません（実際: {set.SchemaVersion}）。"));
            }

            if (string.IsNullOrEmpty(set.SetId))
            {
                errors.Add(new ValidationError("setId が指定されていません。"));
            }
            else if (!SetIdPattern.IsMatch(set.SetId))
            {
                errors.Add(new ValidationError(
                    $"setId の形式が不正です（英数字・_・- のみ使用可能。実際: {set.SetId}）。"));
            }

            if (string.IsNullOrEmpty(set.Title))
            {
                errors.Add(new ValidationError("title が指定されていません。"));
            }
            else if (set.Title.Length > MaxTitleLength)
            {
                errors.Add(new ValidationError(
                    $"title が {MaxTitleLength} 文字を超えています（実際: {set.Title.Length} 文字）。"));
            }

            if (set.Description != null && set.Description.Length > MaxDescriptionLength)
            {
                errors.Add(new ValidationError(
                    $"description が {MaxDescriptionLength} 文字を超えています（実際: {set.Description.Length} 文字）。"));
            }

            if (set.Questions == null || set.Questions.Count == 0)
            {
                errors.Add(new ValidationError("questions が1件も存在しません。"));
                return errors.AsReadOnly();
            }

            errors.AddRange(FindDuplicateIdErrors(set.Questions));

            foreach (var question in set.Questions)
            {
                errors.AddRange(ValidateQuestion(question, setBaseDirectory));
            }

            return errors.AsReadOnly();
        }

        private static IEnumerable<ValidationError> FindDuplicateIdErrors(IReadOnlyList<Question> questions)
        {
            return questions
                .Select(q => q.Id)
                .Where(id => !string.IsNullOrEmpty(id))
                .GroupBy(id => id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => new ValidationError($"id が重複しています: {group.Key}", group.Key));
        }

        /// <summary>
        /// タグの件数・長さを検証する（docs/question-data.md §1 / §2）。
        /// 配信用 DTO の上限（<see cref="QuestionLimits.MaxTagCount"/> /
        /// <see cref="QuestionLimits.MaxTagLength"/>）と同じ値を使う。
        /// </summary>
        private static IEnumerable<ValidationError> ValidateTags(Question question, string id)
        {
            var tags = question.Tags;
            if (tags == null || tags.Count == 0)
            {
                yield break;
            }

            if (tags.Count > MaxTagCount)
            {
                yield return new ValidationError(
                    $"tags は{MaxTagCount}件以内である必要があります（実際: {tags.Count}件）。", id);
            }

            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (tag != null && tag.Length > MaxTagLength)
                {
                    yield return new ValidationError(
                        $"tags の要素が {MaxTagLength} 文字を超えています（実際: {tag.Length} 文字）。", id);
                }
            }
        }

        private static IEnumerable<ValidationError> ValidateQuestion(Question question, string setBaseDirectory)
        {
            var errors = new List<ValidationError>();
            var id = question.Id;

            if (string.IsNullOrEmpty(id))
            {
                errors.Add(new ValidationError("id が指定されていません。"));
            }
            else if (id.Length > MaxIdLength)
            {
                errors.Add(new ValidationError(
                    $"id が {MaxIdLength} 文字を超えています（実際: {id.Length} 文字）。", id));
            }

            if (string.IsNullOrEmpty(question.Text))
            {
                errors.Add(new ValidationError("text が指定されていません。", id));
            }
            else if (question.Text.Length > MaxTextLength)
            {
                errors.Add(new ValidationError(
                    $"text が {MaxTextLength} 文字を超えています（実際: {question.Text.Length} 文字）。", id));
            }

            if (question.ReadingText != null && question.ReadingText.Length > MaxReadingTextLength)
            {
                errors.Add(new ValidationError(
                    $"readingText が {MaxReadingTextLength} 文字を超えています（実際: {question.ReadingText.Length} 文字）。",
                    id));
            }

            errors.AddRange(ValidateTags(question, id));

            if (question.Difficulty < MinDifficulty || question.Difficulty > MaxDifficulty)
            {
                errors.Add(new ValidationError(
                    $"difficulty は{MinDifficulty}〜{MaxDifficulty}の範囲である必要があります（実際: {question.Difficulty}）。", id));
            }

            if (!question.Type.HasValue)
            {
                errors.Add(new ValidationError("type が指定されていません。", id));
            }
            else
            {
                switch (question.Type.Value)
                {
                    case QuestionType.FreeText:
                        errors.AddRange(ValidateFreeText(question, id));
                        break;
                    case QuestionType.Choice:
                        errors.AddRange(ValidateChoice(question, id));
                        break;
                    default:
                        errors.Add(new ValidationError($"type が不正です（実際: {question.Type}）。", id));
                        break;
                }
            }

            if (!string.IsNullOrEmpty(question.ImagePath))
            {
                errors.AddRange(ValidateImage(question.ImagePath, setBaseDirectory, id));
            }

            return errors;
        }

        private static IEnumerable<ValidationError> ValidateFreeText(Question question, string id)
        {
            var errors = new List<ValidationError>();

            if (question.Answers == null || question.Answers.Count == 0)
            {
                errors.Add(new ValidationError("freeText には answers が1件以上必要です。", id));
            }
            else
            {
                if (question.Answers.Count > MaxAnswerCount)
                {
                    errors.Add(new ValidationError(
                        $"answers は{MaxAnswerCount}件以内である必要があります（実際: {question.Answers.Count}件）。", id));
                }

                foreach (var answer in question.Answers)
                {
                    if (string.IsNullOrEmpty(answer))
                    {
                        errors.Add(new ValidationError("answers に空文字列を含めることはできません。", id));
                    }
                    else if (answer.Length > MaxAnswerLength)
                    {
                        errors.Add(new ValidationError(
                            $"answers の要素が {MaxAnswerLength} 文字を超えています（実際: {answer.Length} 文字）。", id));
                    }
                }
            }

            if (question.Choices != null && question.Choices.Count > 0)
            {
                errors.Add(new ValidationError("freeText に choices を指定することはできません。", id));
            }

            if (question.CorrectIndex.HasValue)
            {
                errors.Add(new ValidationError("freeText に correctIndex を指定することはできません。", id));
            }

            return errors;
        }

        private static IEnumerable<ValidationError> ValidateChoice(Question question, string id)
        {
            var errors = new List<ValidationError>();
            var choiceCount = question.Choices?.Count ?? 0;

            if (choiceCount < MinChoiceCount || choiceCount > MaxChoiceCount)
            {
                errors.Add(new ValidationError(
                    $"choices は{MinChoiceCount}〜{MaxChoiceCount}件である必要があります（実際: {choiceCount}件）。", id));
            }

            if (question.Choices != null)
            {
                foreach (var choice in question.Choices)
                {
                    if (string.IsNullOrEmpty(choice))
                    {
                        errors.Add(new ValidationError("choices に空文字列を含めることはできません。", id));
                    }
                    else if (choice.Length > MaxChoiceLength)
                    {
                        errors.Add(new ValidationError(
                            $"choices の要素が {MaxChoiceLength} 文字を超えています（実際: {choice.Length} 文字）。", id));
                    }
                }
            }

            if (!question.CorrectIndex.HasValue)
            {
                errors.Add(new ValidationError("choice には correctIndex が必要です。", id));
            }
            else if (question.CorrectIndex.Value < 0 || question.CorrectIndex.Value >= choiceCount)
            {
                errors.Add(new ValidationError(
                    $"correctIndex が範囲外です（実際: {question.CorrectIndex.Value}、choices件数: {choiceCount}）。", id));
            }

            if (question.Answers != null && question.Answers.Count > 0)
            {
                errors.Add(new ValidationError("choice に answers を指定することはできません。", id));
            }

            return errors;
        }

        /// <summary>
        /// 画像（<c>imagePath</c>）を検証する。規則（絶対パス禁止・拡張子・セットフォルダ外の禁止・
        /// 実在・サイズ上限）は <see cref="QuestionImagePathResolver"/> を単一の出所として参照し、
        /// 配信時の読み出し（#16）と同じ判断になるようにする。
        /// </summary>
        private static IEnumerable<ValidationError> ValidateImage(string imagePath, string setBaseDirectory, string id)
        {
            var errors = new List<ValidationError>();

            var formatError = QuestionImagePathResolver.ValidateFormat(imagePath);
            if (formatError != ImagePathError.None)
            {
                errors.Add(new ValidationError(QuestionImagePathResolver.Describe(formatError, imagePath), id));

                // 絶対パス（例: C:\Windows\x.png）はセットフォルダの外を直接指すため、
                // 存在チェックへ進まずここで打ち切る。
                if (formatError == ImagePathError.Rooted)
                {
                    return errors;
                }
            }

            if (string.IsNullOrEmpty(setBaseDirectory))
            {
                // 基準フォルダが分からない場合は実在・サイズを確かめられないので、形式の検証だけで終える。
                return errors;
            }

            if (!QuestionImagePathResolver.TryResolveExisting(
                    setBaseDirectory, imagePath, out var fullPath, out var fileLengthBytes, out var resolveError))
            {
                errors.Add(new ValidationError(
                    resolveError == ImagePathError.TooLarge
                        ? $"imagePath の画像ファイルが2MBを超えています（実際: {fileLengthBytes}バイト）: {imagePath}"
                        : QuestionImagePathResolver.Describe(resolveError, imagePath),
                    id));

                return errors;
            }

            errors.AddRange(ValidateImageContent(fullPath, imagePath, id));
            return errors;
        }

        /// <summary>
        /// 画像の中身（先頭バイト）を見て、PNG / JPG であることと解像度の上限を確かめる
        /// （docs/question-data.md §2、docs/network.md §9、#16）。
        /// 配信時（<c>QuestionDistributor</c>）と同じ判定を読み込み時にも行い、
        /// 「読み込めたのに配信できない問題」を作らない。
        /// </summary>
        private static IEnumerable<ValidationError> ValidateImageContent(
            string fullPath, string imagePath, string id)
        {
            if (!QuestionImagePathResolver.TryReadHeader(fullPath, out var header))
            {
                yield return new ValidationError($"imagePath の画像ファイルを読み取れません: {imagePath}", id);
                yield break;
            }

            if (!ImageFormatProbe.TryProbe(header, out var format, out var width, out var height))
            {
                if (format == ImageFormat.Unknown)
                {
                    yield return new ValidationError(
                        $"imagePath の画像が PNG/JPG ではありません（拡張子ではなく中身で判定しています）: {imagePath}", id);
                }

                // PNG / JPG だが先頭 64KB では解像度を読めなかった場合は、ここでは判定しない
                // （配信時に全体を読んで確かめる）。
                yield break;
            }

            if (width <= 0 || height <= 0)
            {
                yield return new ValidationError(
                    $"imagePath の画像の解像度を読み取れません（実際: {width}x{height}）: {imagePath}", id);
                yield break;
            }

            if (width > QuestionLimits.MaxImageDimension || height > QuestionLimits.MaxImageDimension)
            {
                yield return new ValidationError(
                    $"imagePath の画像の幅・高さは{QuestionLimits.MaxImageDimension}px 以内である必要があります"
                    + $"（実際: {width}x{height}）: {imagePath}",
                    id);
            }
        }
    }
}
