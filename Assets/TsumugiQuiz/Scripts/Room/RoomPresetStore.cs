using System;
using System.Collections.Generic;
using System.IO;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// ユーザー保存プリセットの一覧・保存・読込・削除（docs/room-settings.md §3）。
    /// 保存先は既定で <c>%USERPROFILE%\Documents\TsumugiQuiz\Presets\&lt;name&gt;.json</c>。
    /// 組み込み 3 プリセット（<see cref="RoomPreset.BuiltIns"/>）はファイルを持たないため、本クラスの対象外。
    /// </summary>
    public sealed class RoomPresetStore
    {
        /// <summary>プリセット名（ファイル名）の長さ上限（#26 統括判断 L16）。</summary>
        public const int MaxNameLength = 100;

        /// <summary>
        /// Windows の予約デバイス名（拡張子を除いた完全一致で、大文字小文字を区別しない、#26 統括判断 L16）。
        /// これらをファイル名にすると環境によって作成・書き込みに失敗するため、事前に拒否する。
        /// </summary>
        private static readonly string[] ReservedDeviceNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private readonly string _folderPath;
        private readonly IRoomFileSystem _fileSystem;

        /// <summary>
        /// ストアを生成する。
        /// </summary>
        /// <param name="folderPath">プリセットフォルダ。null / 空なら <see cref="GetDefaultFolderPath"/>。</param>
        /// <param name="fileSystem">ファイル I/O の実装。null なら <see cref="FileSystemRoomFileSystem"/>。</param>
        public RoomPresetStore(string folderPath = null, IRoomFileSystem fileSystem = null)
        {
            _folderPath = string.IsNullOrWhiteSpace(folderPath) ? GetDefaultFolderPath() : folderPath;
            _fileSystem = fileSystem ?? new FileSystemRoomFileSystem();
        }

        /// <summary>docs/room-settings.md §3 で定義された既定のプリセットフォルダ。</summary>
        public static string GetDefaultFolderPath() => DocumentsPaths.Combine("TsumugiQuiz", "Presets");

        /// <summary>
        /// ユーザーが保存したプリセット名の一覧（名前順）。フォルダが無ければ空。
        /// 列挙中に I/O 例外が起きた場合も例外を投げず空を返す（#26 統括判断 H4）。
        /// </summary>
        public IReadOnlyList<string> ListUserPresetNames()
        {
            try
            {
                return _fileSystem.ListJsonFileNames(_folderPath);
            }
            catch (Exception ex) when (IsRecoverableIoException(ex))
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// <see cref="ListUserPresetNames"/> と同じ一覧を返すが、I/O 例外が起きた場合に空の一覧へ
        /// フォールバックした旨を <paramref name="warnings"/> で呼び出し側に伝える（PR #92 レビュー M8）。
        /// 既存の <see cref="ListUserPresetNames"/> は警告を握りつぶす契約のまま変更していない。
        /// </summary>
        public IReadOnlyList<string> ListUserPresetNamesWithWarnings(out IReadOnlyList<string> warnings)
        {
            try
            {
                warnings = Array.Empty<string>();
                return _fileSystem.ListJsonFileNames(_folderPath);
            }
            catch (Exception ex) when (IsRecoverableIoException(ex))
            {
                warnings = new[] { $"プリセット一覧の取得に失敗しました: {ex.GetType().Name}: {ex.Message}" };
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// プリセットをファイルに保存する（既存があれば上書き）。
        /// I/O 例外（<see cref="IOException"/> / <see cref="UnauthorizedAccessException"/>）は投げず、
        /// <see cref="RoomPresetSaveResult.Success"/> = false として返す（#26 統括判断 H4）。
        /// </summary>
        /// <exception cref="ArgumentException"><see cref="RoomPreset.Name"/> がファイル名として使えないとき。</exception>
        public RoomPresetSaveResult Save(RoomPreset preset)
        {
            if (preset == null)
            {
                throw new ArgumentNullException(nameof(preset));
            }

            var path = ResolvePath(preset.Name);

            try
            {
                _fileSystem.EnsureDirectory(_folderPath);
                _fileSystem.WriteAllText(path, RoomPresetJson.Serialize(preset));
                return RoomPresetSaveResult.Succeeded;
            }
            catch (Exception ex) when (IsRecoverableIoException(ex))
            {
                return RoomPresetSaveResult.Failed($"プリセット \"{preset.Name}\" の保存に失敗しました: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// プリセットを読み込む。ファイルが無ければ <see cref="RoomPresetParseResult.Found"/> が false になる。
        /// 読み込み中に I/O 例外が起きた場合も例外を投げず、既定値ベースの結果＋警告を返す（#26 統括判断 H4）。
        /// </summary>
        public RoomPresetParseResult Load(string name)
        {
            var path = ResolvePath(name);

            try
            {
                if (!_fileSystem.FileExists(path))
                {
                    return RoomPresetParseResult.NotFound();
                }

                var json = _fileSystem.ReadAllText(path);
                return RoomPresetJson.Parse(json, name);
            }
            catch (Exception ex) when (IsRecoverableIoException(ex))
            {
                return RoomPresetParseResult.Success(
                    new RoomPreset(name, RoomSettings.Default),
                    new[] { $"プリセット \"{name}\" の読み込みに失敗しました。既定値を使用しました: {ex.GetType().Name}: {ex.Message}" });
            }
        }

        /// <summary>
        /// プリセットを削除する。
        /// </summary>
        /// <returns>
        /// 削除できたら true。<b>ファイルが存在しない場合</b>と<b>削除中に I/O 例外
        /// （<see cref="IOException"/> / <see cref="UnauthorizedAccessException"/>）が起きた場合</b>は、
        /// いずれも例外を投げず false を返す（#26 統括判断 H4/L5）。両者を区別する情報が必要な場合は
        /// 呼び出し前に <c>FileExists</c> 相当の確認を行うこと（本メソッドは区別しない）。
        /// </returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> が空白・ファイル名として使えない文字を含む・長さ超過・
        /// Windows の予約デバイス名・末尾のドット/空白のいずれかのとき（<see cref="ValidateName"/>、L5）。
        /// </exception>
        public bool Delete(string name)
        {
            var path = ResolvePath(name);

            try
            {
                if (!_fileSystem.FileExists(path))
                {
                    return false;
                }

                _fileSystem.DeleteFile(path);
                return true;
            }
            catch (Exception ex) when (IsRecoverableIoException(ex))
            {
                return false;
            }
        }

        private string ResolvePath(string name)
        {
            ValidateName(name);
            return Path.Combine(_folderPath, name + ".json");
        }

        /// <summary>
        /// プリセット名（ファイル名として使う）を検証する（#26 統括判断 L16/L4）。
        /// 空白・ファイル名として使えない文字・長さ超過・Windows の予約デバイス名・
        /// 末尾のドット/空白（Windows は末尾のドット・空白を無視するため、意図しないファイルを
        /// 指してしまう）を拒否する。
        /// </summary>
        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("プリセット名を指定してください。", nameof(name));
            }

            if (name.Length > MaxNameLength)
            {
                throw new ArgumentException(
                    $"プリセット名は {MaxNameLength} 文字以内にしてください（実際: {name.Length} 文字）。", nameof(name));
            }

            if (name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"プリセット名の末尾にドット・空白は使えません（Windows で無視されるため）: \"{name}\"", nameof(name));
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                if (name.IndexOf(c) >= 0)
                {
                    throw new ArgumentException(
                        $"プリセット名にファイル名として使えない文字が含まれています: \"{name}\"", nameof(name));
                }
            }

            foreach (var reserved in ReservedDeviceNames)
            {
                if (string.Equals(name, reserved, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"プリセット名に Windows の予約デバイス名は使えません: \"{name}\"", nameof(name));
                }
            }
        }

        private static bool IsRecoverableIoException(Exception ex) => ex is IOException || ex is UnauthorizedAccessException;
    }
}
