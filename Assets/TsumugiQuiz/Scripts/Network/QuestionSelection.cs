using System.Collections.Generic;
using System.Collections.ObjectModel;
using TsumugiQuiz.Questions;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="QuestionSelector"/> が確定させた出題列（#19、docs/room-settings.md §1「問題選択」）。
    /// 不変で、<see cref="Questions"/> の並びがそのまま出題順になる。
    /// </summary>
    /// <remarks>
    /// 除外の内訳（<see cref="MissingTypeCount"/> / <see cref="UnknownSetIds"/>）も持つ。
    /// 呼び出し側（ホストの UI・ログ）が「なぜこの問題数になったのか」を説明できるようにするため。
    /// 選択式の除外件数（<c>ExcludedChoiceCount</c>）は #17 で選択式の進行を実装したため削除した
    /// （レビュー L5）。
    /// </remarks>
    public sealed class QuestionSelection
    {
        private readonly ReadOnlyCollection<Question> _questions;
        private readonly ReadOnlyCollection<int> _sourceIndices;
        private readonly ReadOnlyCollection<string> _unknownSetIds;

        internal QuestionSelection(
            List<Question> questions,
            List<int> sourceIndices,
            int poolCount,
            int candidateCount,
            int missingTypeCount = 0,
            List<string> unknownSetIds = null)
        {
            _questions = new ReadOnlyCollection<Question>(questions);
            _sourceIndices = new ReadOnlyCollection<int>(sourceIndices);
            _unknownSetIds = new ReadOnlyCollection<string>(unknownSetIds ?? new List<string>());
            PoolCount = poolCount;
            CandidateCount = candidateCount;
            MissingTypeCount = missingTypeCount;
        }

        /// <summary>1 問も選ばれなかった結果（フィルタに合う問題が無い）。</summary>
        public static QuestionSelection Empty { get; } =
            new QuestionSelection(new List<Question>(), new List<int>(), 0, 0);

        /// <summary>出題順に並んだ問題。</summary>
        public IReadOnlyList<Question> Questions => _questions;

        /// <summary>
        /// 出題順に並んだ「候補プール上のインデックス」。
        /// <see cref="Questions"/> と同じ長さで、シャッフルの結果を確かめる用途に使う。
        /// </summary>
        public IReadOnlyList<int> SourceIndices => _sourceIndices;

        /// <summary>フィルタ前の候補プールの件数（問題セットを平坦化した総数）。</summary>
        public int PoolCount { get; }

        /// <summary>フィルタ後・出題数で切り詰める前の候補数。</summary>
        public int CandidateCount { get; }

        /// <summary>
        /// 出題形式（<c>type</c>）が設定されていないため除外した件数。
        /// 通常は <c>QuestionSetValidator</c> が読み込み時に弾くので 0 のはずで、
        /// 0 以外なら検証を通っていない問題が供給元に混ざっていることを意味する。
        /// </summary>
        public int MissingTypeCount { get; }

        /// <summary>
        /// <c>questions.setIds</c> のうち、候補プールのどのセットにも一致しなかった <c>setId</c>
        /// （docs/room-settings.md §5「存在しない setId は無視して警告する」）。
        /// </summary>
        public IReadOnlyList<string> UnknownSetIds => _unknownSetIds;

        /// <summary>出題する問題数。</summary>
        public int Count => _questions.Count;

        /// <summary>1 問も選ばれなかったか。</summary>
        public bool IsEmpty => _questions.Count == 0;
    }
}
