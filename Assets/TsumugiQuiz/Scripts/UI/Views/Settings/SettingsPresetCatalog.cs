using System;
using System.Collections.Generic;
using System.Linq;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// Settings View（issue #28）のプリセットタブが使う、組み込みプリセット（<see cref="RoomPreset.BuiltIns"/>）と
    /// ユーザー保存プリセット（<see cref="RoomPresetStore.ListUserPresetNames"/>）を 1 つの選択肢一覧にまとめる
    /// 純ロジック。ファイル I/O は行わない（一覧の文字列だけを受け取って並べる）。
    /// </summary>
    internal static class SettingsPresetCatalog
    {
        /// <summary>組み込みプリセットの名前（docs/room-settings.md §3、表示順）。</summary>
        public static readonly IReadOnlyList<string> BuiltInNames = new[]
        {
            RoomPreset.StandardName, RoomPreset.BuzzFocusedName, RoomPreset.RelaxedName,
        };

        /// <summary>指定した名前が組み込みプリセットか。</summary>
        public static bool IsBuiltIn(string name) => BuiltInNames.Contains(name, StringComparer.Ordinal);

        /// <summary>
        /// ドロップダウンに表示する選択肢一覧を組み立てる。
        /// 組み込み 3 種（<see cref="BuiltInNames"/> の順）を先頭に固定し、続けてユーザー保存プリセットを
        /// 名前順（序数比較）で並べる。空白・重複（組み込みと同名を含む）は取り除く。
        /// </summary>
        public static IReadOnlyList<string> BuildDisplayList(IReadOnlyList<string> userPresetNames)
        {
            var result = new List<string>(BuiltInNames);

            if (userPresetNames == null)
            {
                return result;
            }

            var additional = userPresetNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.Ordinal)
                .Where(name => !result.Contains(name, StringComparer.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal);

            result.AddRange(additional);
            return result;
        }

        /// <summary>組み込みプリセットの設定値を名前から引く。見つからなければ null。</summary>
        public static RoomSettings FindBuiltInSettings(string name)
            => RoomPreset.BuiltIns.FirstOrDefault(preset => preset.Name == name)?.Settings;
    }
}
