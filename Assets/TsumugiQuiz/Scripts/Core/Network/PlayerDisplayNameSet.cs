using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// <see cref="PlayerDisplayNames.Resolve"/> の結果（不変値、issue #85）。
    /// 入力した名前の並び順と 1 対 1 で対応する表示名（<see cref="Labels"/>）と、
    /// 重複していた名前（<see cref="DuplicatedNames"/>）を持つ。
    /// </summary>
    public sealed class PlayerDisplayNameSet
    {
        private static readonly string[] NoStrings = Array.Empty<string>();

        private readonly string[] _labels;
        private readonly string[] _duplicatedNames;
        private readonly string[] _numberedLabels;

        /// <summary>
        /// 結果を作る（生成は <see cref="PlayerDisplayNames.Resolve"/> が行う）。
        /// </summary>
        /// <param name="labels">表示名（入力と同じ並び順・同じ件数）。</param>
        /// <param name="duplicatedNames">2 件以上あった名前（初出順）。</param>
        /// <param name="numberedLabels">実際に連番を付けた表示名（付けた順）。</param>
        internal PlayerDisplayNameSet(string[] labels, string[] duplicatedNames, string[] numberedLabels)
        {
            _labels = labels ?? NoStrings;
            _duplicatedNames = duplicatedNames ?? NoStrings;
            _numberedLabels = numberedLabels ?? NoStrings;
        }

        /// <summary>空の結果（表示対象が居ない場合）。</summary>
        public static PlayerDisplayNameSet Empty { get; } =
            new PlayerDisplayNameSet(NoStrings, NoStrings, NoStrings);

        /// <summary>表示名（入力と同じ並び順）。</summary>
        public IReadOnlyList<string> Labels => _labels;

        /// <summary>2 件以上あった名前（初出順）。重複が無ければ空。</summary>
        public IReadOnlyList<string> DuplicatedNames => _duplicatedNames;

        /// <summary>
        /// 実際に連番を付けた表示名（付けた順）。注意文（<see cref="DuplicateNameNotice"/>）で
        /// 「実際に画面へ出ている表示名」をそのまま例示するために使う。
        /// 既存の名前と衝突する番号は飛ばすので、必ずしも <c>#1</c> から始まらない。
        /// </summary>
        public IReadOnlyList<string> NumberedLabels => _numberedLabels;

        /// <summary>同名が 2 件以上あったか。</summary>
        public bool HasDuplicates => _duplicatedNames.Length > 0;

        /// <summary>
        /// 指定した位置の表示名。範囲外なら空文字（呼び出し側で元の名前へフォールバックする）。
        /// </summary>
        public string GetLabel(int index)
            => index >= 0 && index < _labels.Length ? _labels[index] : string.Empty;
    }
}
