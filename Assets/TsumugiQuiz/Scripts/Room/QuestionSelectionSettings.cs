using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// ルーム設定のうち「問題選択」に関する部分（docs/room-settings.md §1 の <c>questions.*</c>）。
    /// 不変オブジェクトとして扱い、変更は新しいインスタンスの生成で表す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本 issue（#19）はフル <c>RoomSettings</c>（#26）より先に複数問の進行を実装するため、
    /// <see cref="ScoringSettings"/> と同じく必要な項目だけを持つ小さな不変レコードとして切り出している。
    /// #26 で <c>RoomSettings</c> を作るときは本クラスをその一部として内包し、
    /// プリセット JSON からの読み込みと範囲外値のクランプ（docs/room-settings.md §5）を追加する。
    /// </para>
    /// <para>
    /// 実際の絞り込み・シャッフル・出題数の適用は
    /// <c>TsumugiQuiz.Network.QuestionSelector</c> が行う（本クラスは値を持つだけ）。
    /// </para>
    /// </remarks>
    public sealed class QuestionSelectionSettings
    {
        /// <summary><c>questions.count</c> が「全問」を意味する値。</summary>
        public const int AllQuestions = 0;

        /// <summary><c>questions.typeFilter</c> の既定値。</summary>
        public const QuestionTypeFilter DefaultTypeFilter = QuestionTypeFilter.Both;

        /// <summary><c>questions.imageOnly</c> の既定値。</summary>
        public const bool DefaultImageOnly = false;

        /// <summary><c>questions.count</c> の既定値。</summary>
        public const int DefaultCount = 10;

        /// <summary><c>questions.shuffleOrder</c> の既定値。</summary>
        public const bool DefaultShuffleOrder = true;

        private static readonly QuestionSelectionSettings DefaultInstance = new QuestionSelectionSettings();

        private readonly ReadOnlyCollection<string> _setIds;
        private readonly ReadOnlyCollection<string> _tagFilter;

        /// <summary>
        /// 設定値を指定して生成する。
        /// </summary>
        /// <param name="setIds">
        /// <c>questions.setIds</c>。null / 空なら読み込み済みの全セットが対象。
        /// 空白だけの要素と重複は取り除く。
        /// </param>
        /// <param name="typeFilter"><c>questions.typeFilter</c>。</param>
        /// <param name="imageOnly"><c>questions.imageOnly</c>。</param>
        /// <param name="tagFilter">
        /// <c>questions.tagFilter</c>。null / 空ならタグで絞り込まない。
        /// 指定したタグの<b>いずれか</b>を持つ問題が対象になる。
        /// </param>
        /// <param name="count"><c>questions.count</c>（0 = 全問）。負の値は不可。</param>
        /// <param name="shuffleOrder"><c>questions.shuffleOrder</c>。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> が負のとき。</exception>
        /// <exception cref="ArgumentException"><paramref name="typeFilter"/> が未定義の値のとき。</exception>
        public QuestionSelectionSettings(
            IReadOnlyList<string> setIds = null,
            QuestionTypeFilter typeFilter = DefaultTypeFilter,
            bool imageOnly = DefaultImageOnly,
            IReadOnlyList<string> tagFilter = null,
            int count = DefaultCount,
            bool shuffleOrder = DefaultShuffleOrder)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count), count, "questions.count は 0（全問）以上である必要があります。");
            }

            if (typeFilter != QuestionTypeFilter.Both
                && typeFilter != QuestionTypeFilter.FreeText
                && typeFilter != QuestionTypeFilter.Choice)
            {
                throw new ArgumentException(
                    $"questions.typeFilter に未定義の値が指定されました: {typeFilter}", nameof(typeFilter));
            }

            _setIds = Normalize(setIds);
            _tagFilter = Normalize(tagFilter);
            TypeFilter = typeFilter;
            ImageOnly = imageOnly;
            Count = count;
            ShuffleOrder = shuffleOrder;
        }

        /// <summary>docs/room-settings.md の既定値（組み込みプリセット「標準」に相当）。</summary>
        public static QuestionSelectionSettings Default => DefaultInstance;

        /// <summary>出題に使う問題セットの <c>setId</c>。空なら全セット。</summary>
        public IReadOnlyList<string> SetIds => _setIds;

        /// <summary>出題形式フィルタ。</summary>
        public QuestionTypeFilter TypeFilter { get; }

        /// <summary>画像付きの問題だけを出題するか。</summary>
        public bool ImageOnly { get; }

        /// <summary>タグフィルタ（いずれかに一致）。空ならフィルタなし。</summary>
        public IReadOnlyList<string> TagFilter => _tagFilter;

        /// <summary>1 ゲームあたりの出題数。<see cref="AllQuestions"/>（0）なら候補すべて。</summary>
        public int Count { get; }

        /// <summary>出題順をシャッフルするか。</summary>
        public bool ShuffleOrder { get; }

        /// <summary>出題数の指定が「全問」か。</summary>
        public bool IsAllQuestions => Count == AllQuestions;

        /// <summary>問題セットの指定だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="setIds">問題セットの <c>setId</c>。</param>
        /// <returns>新しいインスタンス。</returns>
        public QuestionSelectionSettings WithSetIds(IReadOnlyList<string> setIds) =>
            new QuestionSelectionSettings(setIds, TypeFilter, ImageOnly, _tagFilter, Count, ShuffleOrder);

        /// <summary>出題形式フィルタだけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="typeFilter">出題形式フィルタ。</param>
        /// <returns>新しいインスタンス。</returns>
        public QuestionSelectionSettings WithTypeFilter(QuestionTypeFilter typeFilter) =>
            new QuestionSelectionSettings(_setIds, typeFilter, ImageOnly, _tagFilter, Count, ShuffleOrder);

        /// <summary>画像フィルタだけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="imageOnly">画像付きのみ出題するか。</param>
        /// <returns>新しいインスタンス。</returns>
        public QuestionSelectionSettings WithImageOnly(bool imageOnly) =>
            new QuestionSelectionSettings(_setIds, TypeFilter, imageOnly, _tagFilter, Count, ShuffleOrder);

        /// <summary>タグフィルタだけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="tagFilter">タグフィルタ。</param>
        /// <returns>新しいインスタンス。</returns>
        public QuestionSelectionSettings WithTagFilter(IReadOnlyList<string> tagFilter) =>
            new QuestionSelectionSettings(_setIds, TypeFilter, ImageOnly, tagFilter, Count, ShuffleOrder);

        /// <summary>出題数だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="count">出題数（0 = 全問）。</param>
        /// <returns>新しいインスタンス。</returns>
        public QuestionSelectionSettings WithCount(int count) =>
            new QuestionSelectionSettings(_setIds, TypeFilter, ImageOnly, _tagFilter, count, ShuffleOrder);

        /// <summary>出題順シャッフルの可否だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="shuffleOrder">シャッフルするか。</param>
        /// <returns>新しいインスタンス。</returns>
        public QuestionSelectionSettings WithShuffleOrder(bool shuffleOrder) =>
            new QuestionSelectionSettings(_setIds, TypeFilter, ImageOnly, _tagFilter, Count, shuffleOrder);

        /// <summary>
        /// 文字列リストを「空白要素・重複を除いた読み取り専用リスト」に整える。
        /// 前後の空白は落とす（ホストの手入力・プリセット JSON の両方を境界で正規化するため）。
        /// </summary>
        private static ReadOnlyCollection<string> Normalize(IReadOnlyList<string> values)
        {
            var collected = new List<string>();
            if (values != null)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    var value = values[i];
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    // List<string>.Contains は既定の文字列比較（序数）で判定する。
                    var trimmed = value.Trim();
                    if (!collected.Contains(trimmed))
                    {
                        collected.Add(trimmed);
                    }
                }
            }

            return new ReadOnlyCollection<string>(collected);
        }
    }
}
