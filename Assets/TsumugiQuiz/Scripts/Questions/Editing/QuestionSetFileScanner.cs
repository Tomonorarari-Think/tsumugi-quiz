using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// 問題エディタ（issue #30）向けに、問題フォルダ直下の *.json を1件ずつファイルパス付きで読み込む。
    /// <see cref="QuestionRepository"/> と違い、検証に失敗したファイルもスキップせず
    /// <see cref="QuestionSetFileEntry"/>（Set が null・Errors 付き）として返す
    /// （壊れたファイルを一覧に出して削除できるようにするため）。
    /// </summary>
    /// <remarks>
    /// ファイル数・1ファイルあたりのサイズの上限は <see cref="QuestionRepository"/> と同じ値
    /// （<see cref="QuestionRepository.MaxFileCount"/> / <see cref="QuestionRepository.MaxFileSizeBytes"/>）を
    /// 単一の出所として共有する（PR #88 レビュー M2。重複定義しない）。
    /// エディタは利用者の操作に応じて都度読み直す想定のため、<see cref="QuestionRepository"/> が持つ
    /// 一時的な <see cref="IOException"/> のリトライは行わない（仮決め: 本ファイル。
    /// 自動再読込を担う <see cref="QuestionLibrary"/> ほどの堅牢さは必要ないという判断）。
    /// バリデーション規則自体は <see cref="QuestionSetValidator"/> を単一の出所として共有する。
    /// </remarks>
    public static class QuestionSetFileScanner
    {
        private const int MaxExcessFileNamesToList = 5;

        private static readonly JsonSerializerSettings ReadSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        public static QuestionSetScanResult ScanFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                throw new ArgumentException("folderPath を指定してください。", nameof(folderPath));
            }

            if (!Directory.Exists(folderPath))
            {
                return new QuestionSetScanResult(Array.Empty<QuestionSetFileEntry>(), Array.Empty<string>());
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                var message = $"フォルダの列挙に失敗しました: {ex.Message}";
                Debug.LogWarning($"[QuestionSetFileScanner] {folderPath}: {message}");
                return new QuestionSetScanResult(Array.Empty<QuestionSetFileEntry>(), new[] { message });
            }

            Array.Sort(files, StringComparer.Ordinal);

            var folderWarnings = new List<string>();
            if (files.Length > QuestionRepository.MaxFileCount)
            {
                var excessFiles = files.Skip(QuestionRepository.MaxFileCount).ToArray();
                var previewNames = string.Join(", ", excessFiles.Take(MaxExcessFileNamesToList).Select(Path.GetFileName));
                if (excessFiles.Length > MaxExcessFileNamesToList)
                {
                    previewNames += $" 他{excessFiles.Length - MaxExcessFileNamesToList}件";
                }

                var message = $"問題フォルダ内のファイル数が上限（{QuestionRepository.MaxFileCount}件）を超えています。"
                    + $"超過分 {excessFiles.Length}件を一覧に表示していません: {previewNames}";
                Debug.LogWarning($"[QuestionSetFileScanner] {folderPath}: {message}");
                folderWarnings.Add(message);
                files = files.Take(QuestionRepository.MaxFileCount).ToArray();
            }

            var entries = new List<QuestionSetFileEntry>(files.Length);
            foreach (var filePath in files)
            {
                entries.Add(LoadEntry(filePath));
            }

            return new QuestionSetScanResult(entries, folderWarnings);
        }

        private static QuestionSetFileEntry LoadEntry(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var lastWriteTimeUtc = SafeGetLastWriteTimeUtc(filePath);

            long fileLength;
            try
            {
                fileLength = new FileInfo(filePath).Length;
            }
            catch (Exception ex)
            {
                return Failure(filePath, fileName, $"ファイル情報の取得に失敗しました: {ex.Message}", lastWriteTimeUtc);
            }

            if (fileLength > QuestionRepository.MaxFileSizeBytes)
            {
                var message = $"ファイルサイズが上限（{QuestionRepository.MaxFileSizeBytes / (1024 * 1024)}MB）を超えています"
                    + $"（実際: {fileLength}バイト）。";
                return Failure(filePath, fileName, message, lastWriteTimeUtc);
            }

            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                return Failure(filePath, fileName, $"読み込みに失敗しました: {ex.Message}", lastWriteTimeUtc);
            }

            QuestionSet set;
            try
            {
                set = JsonConvert.DeserializeObject<QuestionSet>(json, ReadSettings);
            }
            catch (Exception ex)
            {
                return Failure(filePath, fileName, $"JSON の解析に失敗しました: {ex.Message}", lastWriteTimeUtc);
            }

            if (set == null)
            {
                return Failure(filePath, fileName, "JSON の内容が空です。", lastWriteTimeUtc);
            }

            var validator = new QuestionSetValidator();
            var errors = validator.Validate(set, Path.GetDirectoryName(filePath));
            if (errors.Count > 0)
            {
                var messages = new List<string>(errors.Count);
                foreach (var error in errors)
                {
                    messages.Add(error.ToString());
                }

                return new QuestionSetFileEntry(filePath, fileName, null, messages.AsReadOnly(), lastWriteTimeUtc);
            }

            return new QuestionSetFileEntry(filePath, fileName, set, Array.Empty<string>(), lastWriteTimeUtc);
        }

        private static QuestionSetFileEntry Failure(string filePath, string fileName, string message, DateTime lastWriteTimeUtc)
        {
            Debug.LogWarning($"[QuestionSetFileScanner] {filePath}: {message}");
            return new QuestionSetFileEntry(filePath, fileName, null, new[] { message }, lastWriteTimeUtc);
        }

        /// <summary>
        /// 外部変更検出（PR #88 レビュー H3）のための最終更新日時取得。取得自体に失敗しても
        /// 一覧表示は続けたいので、例外は握りつぶして <see cref="DateTime.MinValue"/> にフォールバックする。
        /// </summary>
        private static DateTime SafeGetLastWriteTimeUtc(string filePath)
        {
            try
            {
                return File.GetLastWriteTimeUtc(filePath);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }
    }
}
