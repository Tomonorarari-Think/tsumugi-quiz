using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// <see cref="AppSettingsValidator"/> の検証結果。<see cref="RoomSettingsValidationResult"/> と同じ構造
    /// （#26 統括判断 H2）。クランプ・破棄が発生した項目はすべて <see cref="Warnings"/> に記録する。
    /// </summary>
    public sealed class AppSettingsValidationResult
    {
        public AppSettingsValidationResult(AppSettings settings, IReadOnlyList<string> warnings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Warnings = warnings ?? Array.Empty<string>();
        }

        /// <summary>クランプ後の設定。</summary>
        public AppSettings Settings { get; }

        /// <summary>クランプ・破棄が発生した項目の警告メッセージ一覧（日本語）。</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>1件以上の警告があるか。</summary>
        public bool HasWarnings => Warnings.Count > 0;
    }
}
