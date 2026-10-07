using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using TsumugiQuiz.Core;
using UnityEngine;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// 問題セット JSON フォルダ（既定: %USERPROFILE%\Documents\TsumugiQuiz\Questions\、1階層のみ）を
    /// 読み込み、docs/question-data.md §1/§2 の規則で検証する。
    /// 読み込み（JSON 構文・未知フィールド）・検証のいずれに失敗した場合もセット単位でスキップし、
    /// ログに記録する（1問の不正で他の問題まで巻き込まない設計。詳細は docs/question-data.md §2「エラー時の扱い」）。
    /// パースエラーと検証エラーは区別せず同じログレベル（Warning）で扱う。
    /// 特定の1ファイルに紐づかないフォルダ単位のエラー（ファイル数上限超過等）は
    /// <see cref="QuestionRepositoryResult.FolderErrors"/> に分けて記録する（<see cref="QuestionSetLoadError"/> には含めない）。
    /// </summary>
    public sealed class QuestionRepository
    {
        // additionalProperties: false 相当。スキーマに無いフィールドがあれば読み込みエラーとして扱う。
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        // issue #29（PR #42 統括申し送り L6）: ファイル数・1ファイルあたりのサイズ上限。
        // 超過分はスキップし、フォルダ単位のエラー一覧（超過分は先頭5件のみ列挙）に理由を記録する。
        // 問題エディタ（issue #30）の QuestionSetFileScanner も同じ上限を使う（PR #88 レビュー M2/LOW。
        // 実際の値は QuestionLimits を単一の出所とし、ここではその別名として公開する）。
        public const int MaxFileCount = QuestionLimits.MaxQuestionSetFileCount;
        public const long MaxFileSizeBytes = QuestionLimits.MaxQuestionSetFileSizeBytes;
        private const int MaxExcessFileNamesToList = 5;

        // 他プロセスがファイルを開いている等、一時的な IOException のみ短くリトライする
        // （FileNotFoundException / DirectoryNotFoundException は対象外＝即座にスキップ扱い）。
        private const int MaxIoRetryCount = 2;
        private static readonly TimeSpan IoRetryDelay = TimeSpan.FromMilliseconds(50);

        private readonly string _questionsFolderPath;
        private readonly QuestionSetValidator _validator;

        /// <summary>既定のフォルダ（Documents\TsumugiQuiz\Questions）から読み込む。</summary>
        public QuestionRepository()
            : this(GetDefaultQuestionsFolderPath())
        {
        }

        /// <summary>読み込み元フォルダを明示的に指定する（テスト等で利用）。</summary>
        public QuestionRepository(string questionsFolderPath)
        {
            if (string.IsNullOrEmpty(questionsFolderPath))
            {
                throw new ArgumentException("questionsFolderPath を指定してください。", nameof(questionsFolderPath));
            }

            _questionsFolderPath = questionsFolderPath;
            _validator = new QuestionSetValidator();
        }

        /// <summary>docs/question-data.md §4 / 仮決め K8 で定義された既定の問題フォルダ。</summary>
        public static string GetDefaultQuestionsFolderPath()
        {
            return DocumentsPaths.Combine("TsumugiQuiz", "Questions");
        }

        /// <summary>
        /// フォルダ直下（サブフォルダは対象外）の *.json をすべて読み込む。
        /// フォルダ自体が存在しない場合は警告をログに出し、<see cref="QuestionRepositoryResult.FolderErrors"/>
        /// にも記録したうえで空の結果を返す。
        /// </summary>
        public QuestionRepositoryResult LoadAll()
        {
            var loadedSets = new List<QuestionSet>();
            var skippedSets = new List<QuestionSetLoadError>();
            var folderErrors = new List<string>();

            if (!Directory.Exists(_questionsFolderPath))
            {
                var message = $"問題フォルダが見つかりません: {_questionsFolderPath}";
                Debug.LogWarning($"[QuestionRepository] {message}");
                folderErrors.Add(message);
                return new QuestionRepositoryResult(loadedSets.AsReadOnly(), skippedSets.AsReadOnly(), folderErrors.AsReadOnly());
            }

            string[] jsonFiles;
            try
            {
                jsonFiles = Directory.GetFiles(_questionsFolderPath, "*.json", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                // UnauthorizedAccessException / PathTooLongException 等。フォルダ列挙自体の失敗は
                // 個々のセットのスキップではなく、フォルダ単位のエラーとして扱う。
                var message = $"問題フォルダの列挙に失敗しました: {ex.Message}";
                Debug.LogWarning($"[QuestionRepository] {_questionsFolderPath}: {message}");
                folderErrors.Add(message);
                return new QuestionRepositoryResult(loadedSets.AsReadOnly(), skippedSets.AsReadOnly(), folderErrors.AsReadOnly());
            }

            Array.Sort(jsonFiles, StringComparer.Ordinal);

            if (jsonFiles.Length > MaxFileCount)
            {
                var excessFiles = jsonFiles.Skip(MaxFileCount).ToArray();
                var previewNames = string.Join(", ", excessFiles.Take(MaxExcessFileNamesToList).Select(Path.GetFileName));
                if (excessFiles.Length > MaxExcessFileNamesToList)
                {
                    previewNames += $" 他{excessFiles.Length - MaxExcessFileNamesToList}件";
                }

                var message = $"問題フォルダ内のファイル数が上限（{MaxFileCount}件）を超えています。超過分 {excessFiles.Length}件をスキップしました: {previewNames}";
                Debug.LogWarning($"[QuestionRepository] {_questionsFolderPath}: {message}");
                folderErrors.Add(message);
                jsonFiles = jsonFiles.Take(MaxFileCount).ToArray();
            }

            foreach (var filePath in jsonFiles)
            {
                LoadOne(filePath, loadedSets, skippedSets);
            }

            return new QuestionRepositoryResult(loadedSets.AsReadOnly(), skippedSets.AsReadOnly(), folderErrors.AsReadOnly());
        }

        private void LoadOne(string filePath, List<QuestionSet> loadedSets, List<QuestionSetLoadError> skippedSets)
        {
            // JSON の読み込み・パース・検証をまとめて try で囲み、想定外の例外（検証ロジック内の
            // バグ等も含む）が発生してもこのファイルだけをスキップして処理を継続する。
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MaxFileSizeBytes)
                {
                    var message = $"ファイルサイズが上限（{MaxFileSizeBytes / (1024 * 1024)}MB）を超えています（実際: {fileInfo.Length}バイト）。";
                    Debug.LogWarning($"[QuestionRepository] {filePath}: {message}");
                    skippedSets.Add(new QuestionSetLoadError(filePath, new[] { message }));
                    return;
                }

                if (!TryReadAllTextWithRetry(filePath, out var json, out var readError))
                {
                    Debug.LogWarning($"[QuestionRepository] {filePath}: {readError}");
                    skippedSets.Add(new QuestionSetLoadError(filePath, new[] { readError }));
                    return;
                }

                var set = JsonConvert.DeserializeObject<QuestionSet>(json, SerializerSettings);

                if (set == null)
                {
                    const string message = "JSON の内容が空です。";
                    Debug.LogWarning($"[QuestionRepository] {filePath}: {message}");
                    skippedSets.Add(new QuestionSetLoadError(filePath, new[] { message }));
                    return;
                }

                var baseDirectory = Path.GetDirectoryName(filePath);
                var errors = _validator.Validate(set, baseDirectory);

                if (errors.Count > 0)
                {
                    var messages = new List<string>(errors.Count);
                    foreach (var error in errors)
                    {
                        messages.Add(error.ToString());
                        Debug.LogWarning($"[QuestionRepository] {filePath}: {error}");
                    }

                    skippedSets.Add(new QuestionSetLoadError(filePath, messages.AsReadOnly()));
                    return;
                }

                loadedSets.Add(set);
            }
            catch (Exception ex)
            {
                var message = $"読み込みに失敗しました: {ex.Message}";
                Debug.LogWarning($"[QuestionRepository] {filePath}: {message}");
                skippedSets.Add(new QuestionSetLoadError(filePath, new[] { message }));
            }
        }

        /// <summary>
        /// M9: 他プロセスがファイルを開いている等の一時的な <see cref="IOException"/> のみ、
        /// 短い間隔を空けて最大 <see cref="MaxIoRetryCount"/> 回までリトライする。
        /// ファイルが存在しない等の恒常的なエラー（<see cref="FileNotFoundException"/> 等）は
        /// リトライせず、通常の catch 節（呼び出し元）に処理を委ねる。
        /// </summary>
        private static bool TryReadAllTextWithRetry(string filePath, out string content, out string errorMessage)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    content = File.ReadAllText(filePath);
                    errorMessage = null;
                    return true;
                }
                catch (IOException ex) when (IsTransientIoException(ex) && attempt < MaxIoRetryCount)
                {
                    Thread.Sleep(IoRetryDelay);
                }
                catch (IOException ex)
                {
                    content = null;
                    errorMessage = IsTransientIoException(ex)
                        ? $"一時的に読み取れませんでした（他アプリが使用中の可能性があります）: {ex.Message}"
                        : $"読み込みに失敗しました: {ex.Message}";
                    return false;
                }
            }
        }

        /// <summary>
        /// 恒常的なエラー（ファイル自体が存在しない等）はリトライしても解消しないため、
        /// 「他アプリが使用中」等の一時的な IOException とは区別する。
        /// </summary>
        private static bool IsTransientIoException(IOException ex)
        {
            return !(ex is FileNotFoundException) && !(ex is DirectoryNotFoundException) && !(ex is PathTooLongException);
        }
    }
}
