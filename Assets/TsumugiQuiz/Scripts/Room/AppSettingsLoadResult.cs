using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Room
{
    /// <summary><see cref="AppSettingsStore.Load"/> の結果。読み込み・解析に失敗しても既定値を返し、例外は投げない。</summary>
    public sealed class AppSettingsLoadResult
    {
        public AppSettingsLoadResult(AppSettings settings, IReadOnlyList<string> warnings)
        {
            Settings = settings ?? AppSettings.Default;
            Warnings = warnings ?? Array.Empty<string>();
        }

        /// <summary>読み込んだ設定（失敗時は既定値）。</summary>
        public AppSettings Settings { get; }

        /// <summary>解析失敗などの警告メッセージ一覧。</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>1件以上の警告があるか。</summary>
        public bool HasWarnings => Warnings.Count > 0;
    }
}
