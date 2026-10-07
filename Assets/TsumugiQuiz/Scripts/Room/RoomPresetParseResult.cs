using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// プリセット JSON の読み込み結果（<see cref="RoomPresetJson.Parse"/> / <see cref="RoomPresetStore.Load"/>）。
    /// ファイルが見つからない場合は <see cref="Found"/> が false になる。
    /// JSON が壊れている場合は <see cref="Found"/> は true のまま、既定値ベースの <see cref="Preset"/> と警告を返す
    /// （docs/room-settings.md §5 の方針に倣い、壊れた設定でホストを起動不能にしない）。
    /// </summary>
    public sealed class RoomPresetParseResult
    {
        private RoomPresetParseResult(bool found, RoomPreset preset, IReadOnlyList<string> warnings)
        {
            Found = found;
            Preset = preset;
            Warnings = warnings ?? Array.Empty<string>();
        }

        /// <summary>ファイル・JSON が見つかったか（内容が壊れていても、ファイル自体があれば true）。</summary>
        public bool Found { get; }

        /// <summary>読み込んだプリセット。<see cref="Found"/> が false のときは null。</summary>
        public RoomPreset Preset { get; }

        /// <summary>クランプ・解析失敗などの警告メッセージ一覧。</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>1件以上の警告があるか。</summary>
        public bool HasWarnings => Warnings.Count > 0;

        internal static RoomPresetParseResult Success(RoomPreset preset, IReadOnlyList<string> warnings) =>
            new RoomPresetParseResult(true, preset, warnings);

        internal static RoomPresetParseResult NotFound() =>
            new RoomPresetParseResult(false, null, Array.Empty<string>());
    }
}
