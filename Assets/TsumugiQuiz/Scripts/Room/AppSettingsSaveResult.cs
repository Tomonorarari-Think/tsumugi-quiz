using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// <see cref="AppSettingsStore.Save"/> の結果（#26 統括判断 H4）。
    /// ファイル I/O 例外（<see cref="System.IO.IOException"/> / <see cref="UnauthorizedAccessException"/>）は
    /// 呼び出し元に投げず、<see cref="Success"/> = false ＋ <see cref="Warnings"/> として返す。
    /// </summary>
    public sealed class AppSettingsSaveResult
    {
        public AppSettingsSaveResult(bool success, IReadOnlyList<string> warnings)
        {
            Success = success;
            Warnings = warnings ?? Array.Empty<string>();
        }

        /// <summary>保存に成功したか。</summary>
        public bool Success { get; }

        /// <summary>失敗時の警告メッセージ一覧。</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>成功結果（警告なし）。</summary>
        public static AppSettingsSaveResult Succeeded { get; } = new AppSettingsSaveResult(true, Array.Empty<string>());

        /// <summary>失敗結果を作る。</summary>
        public static AppSettingsSaveResult Failed(string warning) =>
            new AppSettingsSaveResult(false, new[] { warning });
    }
}
