using System.Collections.Generic;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// <see cref="RoomSettingsValidator"/> の検証結果（docs/room-settings.md §5）。
    /// 範囲外の値をクランプした最終的な <see cref="RoomSettings"/> と、クランプ内容の警告一覧を持つ。
    /// </summary>
    public sealed class RoomSettingsValidationResult
    {
        public RoomSettingsValidationResult(RoomSettings settings, IReadOnlyList<string> warnings)
        {
            Settings = settings;
            Warnings = warnings ?? System.Array.Empty<string>();
        }

        /// <summary>クランプ後の設定。</summary>
        public RoomSettings Settings { get; }

        /// <summary>クランプ・未定義値の置き換えが発生した項目の警告メッセージ一覧（日本語）。</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>1件以上の警告があるか。</summary>
        public bool HasWarnings => Warnings.Count > 0;
    }
}
