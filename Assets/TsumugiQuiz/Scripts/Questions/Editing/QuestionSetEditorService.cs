using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// 問題エディタ（issue #30）の「セット一覧」に対する新規作成・削除・複製を扱う。
    /// 実データの読み書きは <see cref="QuestionSetFileScanner"/> / <see cref="QuestionSetWriter"/> を経由し、
    /// 境界（保存直前）の検証は <see cref="QuestionSetValidator"/> を使う（CLAUDE.md）。
    /// 「問題一覧」（追加・削除・並び替え）は <see cref="QuestionSetEditorService.Questions.cs"/> に分ける。
    /// </summary>
    public sealed partial class QuestionSetEditorService
    {
        private const int DefaultSchemaVersion = 1;
        private const string DefaultNewSetTitle = "新しい問題セット";

        /// <summary>読み書きの対象フォルダ（既定: <see cref="QuestionRepository.GetDefaultQuestionsFolderPath"/>）。</summary>
        public string FolderPath { get; }

        /// <summary>
        /// 直近の <see cref="ListSets"/> で見つかったフォルダ単位の警告（ファイル数上限超過など。
        /// PR #88 レビュー M2）。<see cref="ListSets"/> を呼ぶたびに更新される。
        /// </summary>
        public IReadOnlyList<string> FolderWarnings { get; private set; } = Array.Empty<string>();

        public QuestionSetEditorService(string folderPath = null)
        {
            FolderPath = string.IsNullOrEmpty(folderPath)
                ? QuestionRepository.GetDefaultQuestionsFolderPath()
                : folderPath;
        }

        /// <summary>画像選択 UI（issue #31）が使う画像フォルダ（<c>Questions/images</c>）の絶対パス。</summary>
        public string ImagesFolderPath => QuestionImageFolder.GetImagesFolderPath(FolderPath);

        /// <summary>
        /// 直近の <see cref="ListImages"/> で見つかった画像フォルダ単位の警告（件数上限超過など。
        /// PR #93 レビュー M3）。<see cref="FolderWarnings"/> と同じ作法で、<see cref="ListImages"/> を
        /// 呼ぶたびに更新される。
        /// </summary>
        public IReadOnlyList<string> ImageFolderWarnings { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// 画像フォルダ内の画像一覧（問題セットからの相対パス）を返す。フォルダが存在しない場合は
        /// 作成してから空の一覧を返す（<see cref="ListSets"/> と同じ方針）。
        /// 件数上限による打ち切りが起きた場合は <see cref="ImageFolderWarnings"/> に警告が入る。
        /// </summary>
        public IReadOnlyList<string> ListImages()
        {
            if (!EnsureImagesFolderExists())
            {
                ImageFolderWarnings = Array.Empty<string>();
                return Array.Empty<string>();
            }

            var result = QuestionImageFolder.ListImageRelativePaths(ImagesFolderPath);
            ImageFolderWarnings = result.FolderWarnings;
            return result.RelativePaths;
        }

        private bool EnsureImagesFolderExists()
        {
            try
            {
                Directory.CreateDirectory(ImagesFolderPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestionSetEditorService] 画像フォルダを作成できませんでした: {ImagesFolderPath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// フォルダ内の問題セットファイルを一覧する。フォルダが存在しない場合は作成してから空の一覧を返す
        /// （エディタから「新規作成」できるようにするため）。
        /// </summary>
        public IReadOnlyList<QuestionSetFileEntry> ListSets()
        {
            if (!EnsureFolderExists())
            {
                FolderWarnings = Array.Empty<string>();
                return Array.Empty<QuestionSetFileEntry>();
            }

            var result = QuestionSetFileScanner.ScanFolder(FolderPath);
            FolderWarnings = result.FolderWarnings;
            return result.Entries;
        }

        /// <summary>
        /// <see cref="FolderPath"/> が存在することを保証する。<see cref="ListSets"/> 経由の呼び出しに限らず、
        /// 新規作成・複製（新しいファイルを書き込む操作）も、フォルダがまだ無い状態で直接呼ばれた場合に
        /// 備えて自前で保証する。
        /// </summary>
        private bool EnsureFolderExists()
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestionSetEditorService] 問題フォルダを作成できませんでした: {FolderPath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// <paramref name="filePath"/> が <see cref="FolderPath"/> の直下（配下）であることを確かめる
        /// （PR #88 レビュー M4）。壊れた <see cref="QuestionSetFileEntry"/> や、想定外の絶対パスが
        /// 紛れ込んだ場合に、フォルダ外のファイルを誤って削除・上書きしないための防御。
        /// </summary>
        private bool IsWithinFolder(string filePath)
        {
            try
            {
                var folderFullPath = Path.GetFullPath(FolderPath);
                if (!folderFullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    folderFullPath += Path.DirectorySeparatorChar;
                }

                var fileFullPath = Path.GetFullPath(filePath);
                return fileFullPath.StartsWith(folderFullPath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                // 不正な文字・長すぎるパス等、Path.GetFullPath が例外を投げるケース（PR #88 レビュー LOW）。
                // 「配下と確認できない」= 安全側に倒して false（フォルダ外扱い）にする。
                Debug.LogWarning($"[QuestionSetEditorService] パスの検証に失敗しました: {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 空の問題を1件持つ、新しい問題セットファイルを作成する。
        /// setId・ファイル名は既存のものと衝突しないよう採番する（<see cref="QuestionEditorNaming"/>）。
        /// </summary>
        public QuestionSetEditorResult CreateNewSet(IReadOnlyList<QuestionSetFileEntry> currentEntries)
        {
            if (!EnsureFolderExists())
            {
                return QuestionSetEditorResult.Fail($"問題フォルダを作成できませんでした: {FolderPath}");
            }

            currentEntries ??= Array.Empty<QuestionSetFileEntry>();

            var fileName = QuestionEditorNaming.NextFileNameForNew(currentEntries.Select(e => e.FileName));
            var setId = QuestionEditorNaming.NextSetIdForNew(ValidSetIds(currentEntries));

            var initialQuestion = QuestionListEditor.CreateEmptyQuestion(Array.Empty<Question>());
            var set = new QuestionSet(
                DefaultSchemaVersion,
                setId,
                DefaultNewSetTitle,
                string.Empty,
                new[] { initialQuestion });

            // 保存直前の境界での検証（CLAUDE.md）。固定値から組み立てているため通常は失敗しないが、
            // 「読み込めたのに保存できない」状態を作らないという方針（issue #30）を一貫させる（PR #88 レビュー H4）。
            var validationErrors = new QuestionSetValidator().Validate(set, FolderPath);
            if (validationErrors.Count > 0)
            {
                var message = string.Join(" / ", validationErrors.Select(e => e.ToString()));
                Debug.LogError($"[QuestionSetEditorService] 新規作成時の検証に失敗しました: {message}");
                return QuestionSetEditorResult.Fail($"新規作成時の検証でエラーが発生しました: {message}");
            }

            var filePath = Path.Combine(FolderPath, fileName + ".json");

            // FileMode.CreateNew を使う TryWriteNew で「存在したら失敗」にする（PR #88 レビュー H3）。
            // currentEntries は呼び出し側（View）が保持する in-memory の一覧であり、他プロセス・
            // 前回操作によって同名ファイルが実際には既に存在している可能性があるため、
            // 採番だけに頼らず書き込み時にも衝突を検出する。
            if (!QuestionSetWriter.TryWriteNew(filePath, set, out var error))
            {
                return QuestionSetEditorResult.Fail(error);
            }

            var lastWriteTimeUtc = SafeGetLastWriteTimeUtc(filePath);
            return QuestionSetEditorResult.Ok(new QuestionSetFileEntry(filePath, fileName, set, Array.Empty<string>(), lastWriteTimeUtc));
        }

        /// <summary>ファイルを削除する。壊れたファイル（<see cref="QuestionSetFileEntry.IsValid"/> が false）も削除できる。</summary>
        public QuestionSetEditorResult DeleteSet(QuestionSetFileEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (!IsWithinFolder(entry.FilePath))
            {
                Debug.LogError($"[QuestionSetEditorService] 問題フォルダ外のファイルは削除できません: {entry.FilePath}");
                return QuestionSetEditorResult.Fail("不正なファイルパスです。");
            }

            try
            {
                if (File.Exists(entry.FilePath))
                {
                    File.Delete(entry.FilePath);
                }

                return QuestionSetEditorResult.Ok(null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestionSetEditorService] 削除に失敗しました: {entry.FilePath}: {ex.Message}");
                return QuestionSetEditorResult.Fail($"削除に失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// セットを複製する。setId・ファイル名・タイトルを変更した新しいファイルを作る。
        /// 壊れたファイル（<see cref="QuestionSetFileEntry.IsValid"/> が false）はバイトのまま複製する
        /// （中身を解釈できないため setId 等の書き換えは行わない）。
        /// </summary>
        public QuestionSetEditorResult DuplicateSet(QuestionSetFileEntry source, IReadOnlyList<QuestionSetFileEntry> currentEntries)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (!EnsureFolderExists())
            {
                return QuestionSetEditorResult.Fail($"問題フォルダを作成できませんでした: {FolderPath}");
            }

            currentEntries ??= Array.Empty<QuestionSetFileEntry>();

            var newFileName = QuestionEditorNaming.NextFileNameForDuplicate(source.FileName, currentEntries.Select(e => e.FileName));
            var newFilePath = Path.Combine(FolderPath, newFileName + ".json");

            if (source.Set == null)
            {
                return DuplicateRawFile(source, newFilePath, newFileName);
            }

            var newSetId = QuestionEditorNaming.NextSetIdForDuplicate(source.Set.SetId, ValidSetIds(currentEntries));
            var newTitle = QuestionEditorNaming.DuplicateTitle(source.Set.Title);
            var newSet = new QuestionSet(
                source.Set.SchemaVersion,
                newSetId,
                newTitle,
                source.Set.Description,
                source.Set.Questions);

            // 保存直前の境界での検証（PR #88 レビュー H4）。タイトルは QuestionEditorNaming.DuplicateTitle で
            // 100文字に収まるよう切り詰め済みだが、他のフィールド（例えば元セットの imagePath が
            // 複製先フォルダに存在しない等）で失敗しうるため、書き込み前に一般的な検証を通す。
            var validationErrors = new QuestionSetValidator().Validate(newSet, FolderPath);
            if (validationErrors.Count > 0)
            {
                var message = string.Join(" / ", validationErrors.Select(e => e.ToString()));
                Debug.LogError($"[QuestionSetEditorService] 複製時の検証に失敗しました: {message}");
                return QuestionSetEditorResult.Fail($"複製時の検証でエラーが発生しました: {message}");
            }

            if (!QuestionSetWriter.TryWriteNew(newFilePath, newSet, out var error))
            {
                return QuestionSetEditorResult.Fail(error);
            }

            var lastWriteTimeUtc = SafeGetLastWriteTimeUtc(newFilePath);
            return QuestionSetEditorResult.Ok(new QuestionSetFileEntry(newFilePath, newFileName, newSet, Array.Empty<string>(), lastWriteTimeUtc));
        }

        private static QuestionSetEditorResult DuplicateRawFile(QuestionSetFileEntry source, string newFilePath, string newFileName)
        {
            try
            {
                File.Copy(source.FilePath, newFilePath, overwrite: false);
                var lastWriteTimeUtc = SafeGetLastWriteTimeUtc(newFilePath);
                return QuestionSetEditorResult.Ok(new QuestionSetFileEntry(newFilePath, newFileName, null, source.Errors, lastWriteTimeUtc));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestionSetEditorService] 複製に失敗しました: {source.FilePath} -> {newFilePath}: {ex.Message}");
                return QuestionSetEditorResult.Fail($"複製に失敗しました: {ex.Message}");
            }
        }

        private static IEnumerable<string> ValidSetIds(IReadOnlyList<QuestionSetFileEntry> entries)
            => entries.Where(e => e.Set != null).Select(e => e.Set.SetId);

        /// <summary>書き込み直後の <see cref="QuestionSetFileEntry.LastWriteTimeUtc"/> 取得（失敗しても握りつぶす）。</summary>
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
