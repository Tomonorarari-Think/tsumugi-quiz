using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 「次問休み」ペナルティ（<c>score.penaltyType = "skipNext"</c>、仮決め K19）の保持先。
    /// <b>不変</b>で、更新は新しいインスタンスを返す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// お手つきが起きた時点では「次の問題」の番号が確定していないため、いったん
    /// <see cref="Pending"/>（次の問題で休む集合）に積み、<see cref="WithQuestionStarted"/>
    /// （＝出題開始）でその問題番号の休み集合として確定させる。
    /// これにより、司会が問題番号を飛ばして出題してもペナルティが消えない。
    /// </para>
    /// <para>
    /// 保持するのは「現在確定している 1 問分」＋「次問用の <see cref="Pending"/>」だけで、
    /// 過去の問題の集合は <see cref="WithQuestionStarted"/> のたびに捨てる（無制限に伸ばさないため）。
    /// 早押し受付での棄却は <see cref="BuzzArbiter"/> のペナルティ集合が行う（docs/network.md §6.4）。
    /// </para>
    /// </remarks>
    public sealed class PenaltyTracker
    {
        /// <summary>休み集合が確定していないことを表す問題インデックス。</summary>
        public const int NoQuestionIndex = -1;

        private static readonly ReadOnlyCollection<ulong> EmptyIds =
            new ReadOnlyCollection<ulong>(new List<ulong>());

        private readonly ReadOnlyCollection<ulong> _pendingView;
        private readonly ReadOnlyCollection<ulong> _suspendedView;

        /// <summary>空のトラッカーを生成する。</summary>
        public PenaltyTracker()
            : this(new List<ulong>(), NoQuestionIndex, new List<ulong>())
        {
        }

        private PenaltyTracker(List<ulong> pending, int suspendedQuestionIndex, List<ulong> suspended)
        {
            _pendingView = new ReadOnlyCollection<ulong>(pending);
            _suspendedView = new ReadOnlyCollection<ulong>(suspended);
            SuspendedQuestionIndex = suspendedQuestionIndex;
        }

        /// <summary>空のトラッカー。</summary>
        public static PenaltyTracker Empty { get; } = new PenaltyTracker();

        /// <summary>次の問題で休むクライアント ID（まだ問題番号が確定していない分）。</summary>
        public IReadOnlyList<ulong> Pending => _pendingView;

        /// <summary>休み集合が確定している問題インデックス。未確定なら <see cref="NoQuestionIndex"/>。</summary>
        public int SuspendedQuestionIndex { get; }

        /// <summary>
        /// 指定した問題で休むクライアント ID。確定していない問題番号なら空。
        /// </summary>
        /// <param name="questionIndex">問題インデックス（0 以上）。</param>
        /// <returns>休むクライアント ID の一覧。</returns>
        public IReadOnlyList<ulong> SuspendedFor(int questionIndex) =>
            questionIndex >= 0 && questionIndex == SuspendedQuestionIndex ? _suspendedView : EmptyIds;

        /// <summary>指定クライアントが指定した問題で休みか。</summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>休みなら true。</returns>
        public bool IsSuspended(int questionIndex, ulong clientId) =>
            questionIndex >= 0 && questionIndex == SuspendedQuestionIndex && _suspendedView.Contains(clientId);

        /// <summary>指定クライアントが次の問題で休む予定か。</summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>予定に入っていたら true。</returns>
        public bool IsPending(ulong clientId) => _pendingView.Contains(clientId);

        /// <summary>
        /// 「次の問題を休む」ペナルティを課した新しいトラッカーを返す。
        /// 既に予定に入っているクライアントなら同じ内容の新しいインスタンスを返す。
        /// </summary>
        /// <param name="clientId">ペナルティを課すクライアント ID。</param>
        /// <returns>新しいインスタンス。</returns>
        public PenaltyTracker WithSkipNext(ulong clientId)
        {
            var pending = new List<ulong>(_pendingView);
            if (!pending.Contains(clientId))
            {
                pending.Add(clientId);
            }

            return new PenaltyTracker(pending, SuspendedQuestionIndex, new List<ulong>(_suspendedView));
        }

        /// <summary>
        /// 出題開始として <see cref="Pending"/> を指定した問題の休み集合に確定させた新しいトラッカーを返す。
        /// 過去の問題の休み集合は捨てる。
        /// </summary>
        /// <param name="questionIndex">これから出題する問題のインデックス（0 以上）。</param>
        /// <returns>新しいインスタンス。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="questionIndex"/> が負のとき。</exception>
        public PenaltyTracker WithQuestionStarted(int questionIndex)
        {
            if (questionIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(questionIndex), questionIndex, "questionIndex は 0 以上である必要があります。");
            }

            return new PenaltyTracker(new List<ulong>(), questionIndex, new List<ulong>(_pendingView));
        }

        /// <summary>
        /// 指定クライアントがペナルティ（<see cref="Pending"/> または確定済みの休み集合）に
        /// 載っているか（#84、付け替えが必要かの判定に使う）。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>どちらかに載っていたら true。</returns>
        public bool Contains(ulong clientId) =>
            _pendingView.Contains(clientId) || _suspendedView.Contains(clientId);

        /// <summary>
        /// ペナルティのキー（クライアント ID）を付け替えた新しいトラッカーを返す
        /// （#84、再接続の引き継ぎ）。切断・再接続でお手つきの罰から逃れられないようにする。
        /// </summary>
        /// <remarks>
        /// 付け替え先が既に載っている場合は重複させず、移動元だけを取り除く。
        /// 移動元がどちらの集合にも居なければ同じインスタンスを返す。
        /// </remarks>
        /// <param name="fromClientId">切断時に使っていた古いクライアント ID。</param>
        /// <param name="toClientId">復帰後の新しいクライアント ID。</param>
        /// <returns>新しいインスタンス（付け替えが不要なら同じインスタンス）。</returns>
        public PenaltyTracker WithClientIdChanged(ulong fromClientId, ulong toClientId)
        {
            if (fromClientId == toClientId || !Contains(fromClientId))
            {
                return this;
            }

            var pending = new List<ulong>(_pendingView);
            var suspended = new List<ulong>(_suspendedView);
            ReplaceClientId(pending, fromClientId, toClientId);
            ReplaceClientId(suspended, fromClientId, toClientId);

            return new PenaltyTracker(pending, SuspendedQuestionIndex, suspended);
        }

        /// <summary>
        /// 一覧の中のクライアント ID を置き換える（重複させない）。
        /// </summary>
        private static void ReplaceClientId(List<ulong> clientIds, ulong fromClientId, ulong toClientId)
        {
            var index = clientIds.IndexOf(fromClientId);
            if (index < 0)
            {
                return;
            }

            if (clientIds.Contains(toClientId))
            {
                clientIds.RemoveAt(index);
                return;
            }

            clientIds[index] = toClientId;
        }

        /// <summary>
        /// 指定クライアントをペナルティ対象から外した新しいトラッカーを返す（切断時の掃除用）。
        /// </summary>
        /// <param name="clientId">外すクライアント ID。</param>
        /// <returns>新しいインスタンス。</returns>
        public PenaltyTracker WithoutClient(ulong clientId)
        {
            var pending = new List<ulong>(_pendingView);
            pending.Remove(clientId);

            var suspended = new List<ulong>(_suspendedView);
            suspended.Remove(clientId);

            return new PenaltyTracker(pending, SuspendedQuestionIndex, suspended);
        }
    }
}
