using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// 問題エディタの画像選択 UI（issue #31、docs/question-data.md §8）向けに、
    /// <c>Questions/images/</c> フォルダ内の画像ファイル一覧を組み立てる純ロジック。
    /// 実際のフォルダ探索は行うが Unity API には依存しない。
    /// </summary>
    public static class QuestionImageFolder
    {
        /// <summary>問題フォルダ配下の画像フォルダ名（docs/question-data.md §4/§8）。</summary>
        public const string ImagesFolderName = "images";

        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>問題フォルダ（セット直下のフォルダ）から見た画像フォルダの絶対パス。</summary>
        public static string GetImagesFolderPath(string questionsFolderPath)
        {
            if (string.IsNullOrEmpty(questionsFolderPath))
            {
                throw new ArgumentException("questionsFolderPath を指定してください。", nameof(questionsFolderPath));
            }

            return Path.Combine(questionsFolderPath, ImagesFolderName);
        }

        /// <summary>
        /// 画像フォルダ直下（1階層のみ、docs/question-data.md §4 の問題ファイルと同様の方針）の
        /// PNG/JPG ファイルを、問題セットからの相対パス（<c>images/xxx.png</c>）の形で列挙する。
        /// フォルダが存在しない場合は空。ファイル名の昇順（Ordinal）で安定した順序にする。
        /// 件数は <see cref="QuestionLimits.MaxQuestionImageFileCount"/> で打ち切り、超過分は
        /// フォルダ単位の警告として返す（PR #93 レビュー M3。<see cref="QuestionSetFileScanner"/> と同じ作法）。
        /// </summary>
        public static QuestionImageScanResult ListImageRelativePaths(string imagesFolderPath)
        {
            if (string.IsNullOrEmpty(imagesFolderPath) || !Directory.Exists(imagesFolderPath))
            {
                return QuestionImageScanResult.Empty;
            }

            var allPaths = Directory.EnumerateFiles(imagesFolderPath, "*", SearchOption.TopDirectoryOnly)
                .Where(HasAllowedExtension)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => ImagesFolderName + "/" + name)
                .ToArray();

            if (allPaths.Length <= QuestionLimits.MaxQuestionImageFileCount)
            {
                return new QuestionImageScanResult(allPaths, Array.Empty<string>());
            }

            var warning = $"画像フォルダ内のファイル数が上限（{QuestionLimits.MaxQuestionImageFileCount}件）を"
                + $"超えたため一部のみ表示しています（全{allPaths.Length}件）。";
            return new QuestionImageScanResult(
                allPaths.Take(QuestionLimits.MaxQuestionImageFileCount).ToArray(),
                new[] { warning });
        }

        private static bool HasAllowedExtension(string filePath)
        {
            string extension;
            try
            {
                extension = Path.GetExtension(filePath);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            var lowered = extension.ToLowerInvariant();
            return AllowedExtensions.Contains(lowered);
        }
    }
}
