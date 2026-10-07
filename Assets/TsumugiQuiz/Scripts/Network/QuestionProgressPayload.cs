using System;
using System.Collections.Generic;
using TsumugiQuiz.Core.Participants;
using Unity.Collections;
using Unity.Netcode;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 現在の問題の進行状態（押下順・回答順・回答権・選択式の回答済み、#194）をクライアントへ配るための
    /// 同期用データ（<c>NetworkVariable&lt;QuestionProgressPayload&gt;</c>、docs/network.md §1.2）。
    /// サーバーが書き、クライアントは読むだけ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>NetworkList</c> ではなく「unmanaged な構造体の <c>NetworkVariable</c>」にしてあるのは、
    /// NGO 2.13.2 がこの型の差分を<b>毎回値の全体</b>として送るため
    /// （<c>UnmanagedNetworkSerializableSerializer.WriteDelta</c> → <c>Write</c>、
    /// <c>Runtime/NetworkVariable/Serialization/TypedSerializerImplementations.cs</c>）。
    /// 受信側は何度適用しても同じ結果になり、再接続でスポーン時の同期と差分が重なっても食い違わない
    /// （<c>NetworkList</c> で起きた「削除 + 追加」の食い違いは <c>GameSession.Score.cs</c> の <c>MoveScoreRow</c> を参照）。
    /// 1 回の変更で <c>OnValueChanged</c> も 1 回しか発火しないので、途中の状態（行が空に見える等）も見えない。
    /// </para>
    /// <para>
    /// <b>選んだ番号・正誤は判定までここに入らない</b>（<see cref="ParticipantProgressFlags"/> のビットは
    /// 判定確定後にだけ立つ。組み立ては <c>QuizStateMachine.BuildProgress</c>）。
    /// </para>
    /// <para>
    /// <c>BufferSerializer</c> がフィールドを <c>ref</c> で受け取るため、フィールドは可変（public）にしている
    /// （<see cref="ScoreEntry"/> / <see cref="RoomSettingsPayload"/> と同じ例外）。値は <see cref="FromProgress"/> で作り直して使うこと。
    /// </para>
    /// </remarks>
    public struct QuestionProgressPayload : INetworkSerializable, IEquatable<QuestionProgressPayload>
    {
        /// <summary>
        /// 載せる行の上限。定員 12 人（<c>room.maxPlayers</c> の上限）に、保持期間中の切断席ぶんの余裕を足した値。
        /// <see cref="FixedList512Bytes{T}"/> の容量（1 行 16 バイトで 31 行）以内であること（EditMode テストで確認）。
        /// </summary>
        public const int MaxEntries = 24;

        /// <summary>対象の問題インデックス。問題が出ていなければ <see cref="QuestionProgress.NoQuestion"/>。</summary>
        public int QuestionIndex;

        /// <summary>状態を持つ参加者の行（クライアント ID の昇順）。</summary>
        public FixedList512Bytes<ParticipantProgressEntry> Entries;

        /// <summary>問題が出ていない状態（行なし）。<c>default</c> は問題インデックスが 0 になるので、初期値には必ずこれを使う。</summary>
        public static QuestionProgressPayload Empty => new QuestionProgressPayload { QuestionIndex = QuestionProgress.NoQuestion };

        /// <summary>
        /// 進行状態から作る。<see cref="MaxEntries"/> を超えた行は捨てる（件数は <paramref name="droppedCount"/> で返す）。
        /// </summary>
        /// <param name="progress">進行状態。null なら <see cref="Empty"/>。</param>
        /// <param name="droppedCount">上限を超えて捨てた行の数。</param>
        /// <returns>ペイロード。</returns>
        public static QuestionProgressPayload FromProgress(QuestionProgress progress, out int droppedCount)
        {
            droppedCount = 0;
            if (progress == null)
            {
                return Empty;
            }

            var payload = new QuestionProgressPayload { QuestionIndex = progress.QuestionIndex };
            foreach (var entry in progress.Entries)
            {
                if (payload.Entries.Length >= MaxEntries)
                {
                    droppedCount++;
                    continue;
                }

                payload.Entries.Add(ParticipantProgressEntry.From(entry));
            }

            return payload;
        }

        /// <summary>
        /// 受信した値を進行状態へ戻す（境界での入力検証: 未知のビットを落とし、重複した行は後の行を採用する）。
        /// </summary>
        /// <returns>進行状態。</returns>
        public QuestionProgress ToQuestionProgress()
        {
            var entries = new List<ParticipantProgress>(Entries.Length);
            for (var i = 0; i < Entries.Length; i++)
            {
                entries.Add(Entries[i].ToProgress());
            }

            return QuestionProgress.Create(QuestionIndex, entries);
        }

        /// <inheritdoc />
        public void NetworkSerialize<TSerializer>(BufferSerializer<TSerializer> serializer)
            where TSerializer : IReaderWriter
        {
            serializer.SerializeValue(ref QuestionIndex);

            var count = (byte)Math.Min(Entries.Length, MaxEntries);
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                Entries.Clear();
                for (var i = 0; i < count; i++)
                {
                    var entry = default(ParticipantProgressEntry);
                    entry.NetworkSerialize(serializer);

                    // 上限を超える件数（改造・バージョン違いの送信）はバイト列だけ読み進めて捨てる。
                    if (Entries.Length < MaxEntries)
                    {
                        Entries.Add(entry);
                    }
                }

                return;
            }

            for (var i = 0; i < count; i++)
            {
                var entry = Entries[i];
                entry.NetworkSerialize(serializer);
            }
        }

        /// <inheritdoc />
        public bool Equals(QuestionProgressPayload other)
        {
            if (QuestionIndex != other.QuestionIndex || Entries.Length != other.Entries.Length)
            {
                return false;
            }

            // FixedList の Equals はメモリ比較（パディングを含む）なので、要素ごとに比べる。
            for (var i = 0; i < Entries.Length; i++)
            {
                if (!Entries[i].Equals(other.Entries[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is QuestionProgressPayload other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = default(HashCode);
            hash.Add(QuestionIndex);
            for (var i = 0; i < Entries.Length; i++)
            {
                hash.Add(Entries[i]);
            }

            return hash.ToHashCode();
        }

        /// <inheritdoc />
        public override string ToString() => $"question={QuestionIndex} entries={Entries.Length}";
    }

    /// <summary><see cref="QuestionProgressPayload"/> の 1 行（#194）。</summary>
    public struct ParticipantProgressEntry : INetworkSerializable, IEquatable<ParticipantProgressEntry>
    {
        /// <summary>クライアント ID。</summary>
        public ulong ClientId;

        /// <summary>直近の早押しの押下順位（0 = なし）。</summary>
        public byte BuzzRank;

        /// <summary>この問題で回答権を得た順番（0 = なし）。</summary>
        public byte AnswerOrder;

        /// <summary><see cref="ParticipantProgressFlags"/> のバイト表現。</summary>
        public byte Flags;

        /// <summary>Core の行から作る。</summary>
        /// <param name="progress">行。</param>
        /// <returns>同期用の行。</returns>
        public static ParticipantProgressEntry From(ParticipantProgress progress) => new ParticipantProgressEntry
        {
            ClientId = progress.ClientId,
            BuzzRank = (byte)progress.BuzzRank,
            AnswerOrder = (byte)progress.AnswerOrder,
            Flags = (byte)progress.Flags,
        };

        /// <summary>Core の行へ戻す（未知のビットは <see cref="ParticipantProgress"/> が落とす）。</summary>
        /// <returns>行。</returns>
        public ParticipantProgress ToProgress() =>
            new ParticipantProgress(ClientId, BuzzRank, AnswerOrder, (ParticipantProgressFlags)Flags);

        /// <inheritdoc />
        public void NetworkSerialize<TSerializer>(BufferSerializer<TSerializer> serializer)
            where TSerializer : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref BuzzRank);
            serializer.SerializeValue(ref AnswerOrder);
            serializer.SerializeValue(ref Flags);
        }

        /// <inheritdoc />
        public bool Equals(ParticipantProgressEntry other) =>
            ClientId == other.ClientId
            && BuzzRank == other.BuzzRank
            && AnswerOrder == other.AnswerOrder
            && Flags == other.Flags;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is ParticipantProgressEntry other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(ClientId, BuzzRank, AnswerOrder, Flags);
    }
}
