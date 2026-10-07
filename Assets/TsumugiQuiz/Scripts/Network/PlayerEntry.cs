using System;
using TsumugiQuiz.Core.Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <c>NetworkList&lt;PlayerEntry&gt;</c>（<see cref="LobbyState"/>）で同期するロビー名簿の 1 エントリ
    /// （docs/network.md §1.2 / §2.4）。書き込み権限はサーバーのみで、クライアントは読むだけ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>NetworkList&lt;T&gt;</c> の制約が <c>where T : unmanaged, IEquatable&lt;T&gt;</c> なので、
    /// 名前は <see cref="string"/> ではなく <see cref="FixedString64Bytes"/>（UTF-8 で 61 バイトまで）で持つ。
    /// プレイヤー名は 16 コードポイントまで（<see cref="ProtocolConstants.MaxPlayerNameLength"/>）なので、
    /// 日本語（1 文字 3 バイト）なら最大 48 バイトで収まる。非 BMP 文字（絵文字など）だけで
    /// 16 文字を埋めた場合のみ 64 バイトになり収まらないため、
    /// <see cref="FromLobbyPlayer"/> は <c>CopyFromTruncated</c> でルーン境界を壊さずに切り詰める。
    /// </para>
    /// <para>
    /// 純 C# 側の表現は <see cref="LobbyPlayer"/>（<c>TsumugiQuiz.Core.Network</c>）で、
    /// 本構造体は NGO へ載せるための入れ物にすぎない。判定ロジックは
    /// <see cref="LobbyRoster"/> 側にあるので EditMode でテストできる。
    /// </para>
    /// </remarks>
    public struct PlayerEntry : INetworkSerializable, IEquatable<PlayerEntry>
    {
        /// <summary>NGO のクライアント ID。切断中は最後に使われた ID。</summary>
        public ulong ClientId;

        /// <summary>プレイヤー名（正規化済み・UTF-8 61 バイトまで）。</summary>
        public FixedString64Bytes Name;

        /// <summary>ホスト自身のエントリか。</summary>
        public bool IsHost;

        /// <summary>司会専任（<c>host.role = "moderator"</c>）か。ホスト以外は常に false。</summary>
        public bool IsModerator;

        /// <summary>現在接続中か。false なら一覧でグレー表示にする（docs/network.md §2.4）。</summary>
        public bool IsConnected;

        /// <summary>切断した時刻（サーバー時刻軸の秒）。接続中は 0。</summary>
        public double DisconnectedAtServerTime;

        /// <summary>
        /// 切断中のエントリをクライアントへ配るときに、実名の代わりに入れる伏せ字
        /// （レビュー H-2 の緩和策。docs/network.md §2.3 の K-N1 を参照）。
        /// </summary>
        public const string DisconnectedNameMask = "（切断中）";

        /// <summary>純 C# の <see cref="LobbyPlayer"/> から同期用エントリを作る。</summary>
        /// <param name="player">元になる名簿エントリ。</param>
        /// <param name="nameOverride">
        /// 名前を差し替える場合に指定する（切断中エントリの伏せ字など）。null なら実名を使う。
        /// </param>
        public static PlayerEntry FromLobbyPlayer(LobbyPlayer player, string nameOverride = null)
        {
            var entry = new PlayerEntry
            {
                ClientId = player.ClientId,
                IsHost = player.IsHost,
                IsModerator = player.IsModerator,
                IsConnected = player.IsConnected,
                DisconnectedAtServerTime = player.DisconnectedAtSec,
            };

            var name = nameOverride ?? player.Name ?? string.Empty;

            // CopyFromTruncated は容量を超えた分をルーン境界で切り捨てる（壊れた UTF-8 を作らない）。
            // 16 コードポイントすべてが非 BMP 文字（絵文字など）の場合だけ 61 バイトを超えうるので、
            // 黙って切り詰めずに警告を残す（レビュー LOW）。
            if (entry.Name.CopyFromTruncated(name) == CopyError.Truncation)
            {
                Debug.LogWarning(
                    $"[PlayerEntry] プレイヤー名が同期上限（UTF-8 {FixedString64Bytes.UTF8MaxLengthInBytes} バイト）を"
                    + $"超えたため切り詰めました clientId={player.ClientId}");
            }

            return entry;
        }

        /// <summary>文字列化した名前（名簿のデータのまま。照合・ログ用）。</summary>
        public string GetName() => Name.ToString();

        /// <summary>
        /// 画面に出す名前。名簿の名前を <see cref="PlayerDisplayNameSanitizer"/> で整えたもの（#206。
        /// 改変されたホストが載せた制御文字・長すぎる名前を表示しない）。整えた結果が空なら <c>プレイヤー{clientId}</c>。
        /// 名簿のデータ（<see cref="Name"/>）は変えない。
        /// </summary>
        public string GetDisplayName() => PlayerDisplayNameSanitizer.ForDisplay(GetName(), ClientId);

        /// <inheritdoc />
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref IsHost);
            serializer.SerializeValue(ref IsModerator);
            serializer.SerializeValue(ref IsConnected);
            serializer.SerializeValue(ref DisconnectedAtServerTime);
        }

        /// <inheritdoc />
        public bool Equals(PlayerEntry other)
            => ClientId == other.ClientId
               && Name.Equals(other.Name)
               && IsHost == other.IsHost
               && IsModerator == other.IsModerator
               && IsConnected == other.IsConnected
               && DisconnectedAtServerTime.Equals(other.DisconnectedAtServerTime);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is PlayerEntry other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(
            ClientId, Name, IsHost, IsModerator, IsConnected, DisconnectedAtServerTime);

        /// <inheritdoc />
        public override string ToString()
            => $"{GetName()} (clientId={ClientId}, host={IsHost}, moderator={IsModerator}, connected={IsConnected})";
    }
}
