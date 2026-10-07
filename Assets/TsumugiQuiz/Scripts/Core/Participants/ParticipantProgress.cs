using System;

namespace TsumugiQuiz.Core.Participants
{
    /// <summary>
    /// 現在の問題での参加者 1 人の進行状態（#194、参加者パネル）。不変。
    /// </summary>
    public readonly struct ParticipantProgress : IEquatable<ParticipantProgress>
    {
        /// <summary>順位・回答順が無いことを表す値。</summary>
        public const int NoRank = 0;

        /// <summary>順位・回答順の上限（同期用に 1 バイトで持つため）。</summary>
        public const int MaxRank = byte.MaxValue;

        /// <summary>クライアント ID。</summary>
        public ulong ClientId { get; }

        /// <summary>
        /// 直近の早押し（集計窓）での押下順位。1 が勝者。押していない・受付を開き直した後は <see cref="NoRank"/>。
        /// </summary>
        public int BuzzRank { get; }

        /// <summary>
        /// この問題で回答権を得た順番（1 人目, 2 人目, …）。受付を開き直しても消えない。無ければ <see cref="NoRank"/>。
        /// </summary>
        public int AnswerOrder { get; }

        /// <summary>状態のビット。</summary>
        public ParticipantProgressFlags Flags { get; }

        /// <summary>値を指定して生成する。</summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <param name="buzzRank">押下順位（0〜255）。</param>
        /// <param name="answerOrder">回答順（0〜255）。</param>
        /// <param name="flags">状態のビット（未知のビットは落とす）。</param>
        /// <exception cref="ArgumentOutOfRangeException">順位・回答順が範囲外のとき。</exception>
        public ParticipantProgress(ulong clientId, int buzzRank, int answerOrder, ParticipantProgressFlags flags)
        {
            if (buzzRank < NoRank || buzzRank > MaxRank)
            {
                throw new ArgumentOutOfRangeException(nameof(buzzRank), buzzRank, $"0〜{MaxRank} の範囲である必要があります。");
            }

            if (answerOrder < NoRank || answerOrder > MaxRank)
            {
                throw new ArgumentOutOfRangeException(nameof(answerOrder), answerOrder, $"0〜{MaxRank} の範囲である必要があります。");
            }

            ClientId = clientId;
            BuzzRank = buzzRank;
            AnswerOrder = answerOrder;
            Flags = flags & ParticipantProgressFlagsExtensions.All;
        }

        /// <summary>状態を何も持たないか（行を作る必要が無いか）。</summary>
        public bool IsEmpty => BuzzRank == NoRank && AnswerOrder == NoRank && Flags == ParticipantProgressFlags.None;

        /// <inheritdoc />
        public bool Equals(ParticipantProgress other) =>
            ClientId == other.ClientId
            && BuzzRank == other.BuzzRank
            && AnswerOrder == other.AnswerOrder
            && Flags == other.Flags;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is ParticipantProgress other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(ClientId, BuzzRank, AnswerOrder, Flags);

        /// <inheritdoc />
        public override string ToString() =>
            $"{ClientId}: rank={BuzzRank}, order={AnswerOrder}, flags={Flags}";
    }
}
