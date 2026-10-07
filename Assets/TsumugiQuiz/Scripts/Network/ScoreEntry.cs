using System;
using Unity.Netcode;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 得点表 1 行分の同期用データ（<c>NetworkList&lt;ScoreEntry&gt;</c> の要素、docs/network.md §1.2）。
    /// サーバーが書き、クライアントは読むだけ。
    /// </summary>
    /// <remarks>
    /// <c>NetworkList&lt;T&gt;</c> の要素は <c>unmanaged</c> かつ <see cref="IEquatable{T}"/> である必要があり、
    /// さらに <c>BufferSerializer</c> が <c>ref</c> でフィールドを渡すため、
    /// 本プロジェクトの「不変データ優先」の方針の例外としてフィールドは可変（public）にしている。
    /// 値を書き換えるのではなく、行ごと差し替えて使うこと。
    /// </remarks>
    public struct ScoreEntry : INetworkSerializable, IEquatable<ScoreEntry>
    {
        /// <summary>クライアント ID。</summary>
        public ulong ClientId;

        /// <summary>累計得点（負になりうる）。</summary>
        public int Score;

        /// <summary>
        /// 値を指定して生成する。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <param name="score">累計得点。</param>
        public ScoreEntry(ulong clientId, int score)
        {
            ClientId = clientId;
            Score = score;
        }

        /// <inheritdoc />
        public void NetworkSerialize<TSerializer>(BufferSerializer<TSerializer> serializer)
            where TSerializer : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Score);
        }

        /// <inheritdoc />
        public bool Equals(ScoreEntry other) => ClientId == other.ClientId && Score == other.Score;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is ScoreEntry other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (ClientId, Score).GetHashCode();

        /// <inheritdoc />
        public override string ToString() => $"{ClientId}: {Score}";
    }
}
