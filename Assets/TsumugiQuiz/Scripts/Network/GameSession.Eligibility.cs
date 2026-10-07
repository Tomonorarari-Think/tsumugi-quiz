using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using Unity.Netcode;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、「まだ早押しを押せる参加者が居るか」の判定を
    /// <see cref="QuizStateMachine"/> へ渡す部分（#200、docs/network.md §6.6）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判定そのものは Core の純関数 <see cref="BuzzEligibility.HasEligibleBuzzer"/> に置き、ここでは材料を集めるだけにする:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// 接続中の参加者: NGO の接続中クライアント（<c>NetworkManager.ConnectedClientsIds</c>）。受付側
    /// （<see cref="BuzzRpc"/>）が名簿を見ずに接続中のクライアントからの押下を受理するので、それと条件をそろえる
    /// </description></item>
    /// <item><description>
    /// 切断中だが席を保持している参加者: 名簿（<see cref="LobbyState.ServerRoster"/>）の切断中エントリ。
    /// 一瞬の切断で問題が締まらないよう、戻ってくる可能性がある人として数える（統括判断、PR #203 レビュー H-1）。
    /// 切断ではペナルティを捨てない（<see cref="HandleClientDisconnected"/>）ので、切断時のクライアント ID のまま
    /// <see cref="QuizStateMachine.IsPenalized"/> で判定できる。名簿が無い（<see cref="LobbyState"/> 未スポーンの
    /// テストなど）ときは接続中の参加者だけで判定する
    /// </description></item>
    /// <item><description>司会専任のホスト（<see cref="IsHostModerator"/>。<see cref="BuzzRpc"/> と同じ判定）は除く</description></item>
    /// </list>
    /// <para>
    /// 席が名簿から消えた（保持期間切れ・ホストの手動削除 → <see cref="ForgetSeat"/>）人は数えなくなるので、
    /// その人が最後の「押せる人」だった場合は受付中でもそこで締まる。
    /// 再接続の直後、席の引き継ぎ（<see cref="TransferSeat"/>）が終わるまでの間は、新しいクライアント ID が
    /// ペナルティ無しの接続中参加者として数えられ、旧 ID の切断中エントリも残る。いずれも「押せる人が居る」側に
    /// 倒れる（締めるのが遅れるだけで、早く締めすぎることはない）ので安全側として許容する。
    /// </para>
    /// </remarks>
    public sealed partial class GameSession
    {
        /// <summary>
        /// 席を保持している切断中の参加者のクライアント ID を集める作業用バッファ（毎 tick 確保しない）。
        /// </summary>
        private readonly List<ulong> _retainedDisconnectedBuffer = new List<ulong>();

#if UNITY_INCLUDE_TESTS
        /// <summary>
        /// テスト専用: 押せる参加者の判定を差し替える（#200）。null なら本来の判定を使う。
        /// ホスト 1 人の画面テストで「ほかに押せる参加者が居る」状況を作り、次問休みの問題の受付中の表示を
        /// 確かめるための seam（1 人だと受付が 1 tick で締まり、受付中の状態を決定的に観測できないため）。
        /// デスポーンで null に戻す（次のスポーンへ持ち越さない）。
        /// </summary>
        internal Func<bool> EligibleBuzzersOverrideForTests { get; set; }

        /// <summary>
        /// テスト専用: 席を保持している切断中の参加者を、<see cref="IsHostModerator"/> を通さずに直接集める（#204 L-A）。
        /// 名簿の解決が呼び出し順に頼っていないことを確かめるためのもの。戻り値は複製（作業用バッファを渡さない）。
        /// </summary>
        internal List<ulong> CollectRetainedDisconnectedParticipantsForTests() =>
            new List<ulong>(CollectRetainedDisconnectedParticipants());

        /// <summary>
        /// テスト専用: メモ化した <see cref="LobbyState"/> を忘れる（#204 L-A）。<c>OnNetworkSpawn</c> の時点で
        /// まだ <see cref="LobbyState"/> が無かった（スポーン順が逆だった）状況を再現する。
        /// </summary>
        internal void ForgetResolvedLobbyStateForTests() => _lobbyState = null;
#endif

        /// <summary>
        /// 現在の制限時間・規則と、押せる参加者の判定を持った新しい状態機械を作る（サーバーの生成箇所すべてで使う）。
        /// </summary>
        private QuizStateMachine CreateQuizStateMachine() =>
            new QuizStateMachine(_limits, _rules, HasEligibleBuzzers);

        /// <summary>
        /// まだ押せる参加者が 1 人以上居るか（<see cref="QuizStateMachine"/> の判定シーム、サーバーのみ）。
        /// 判定の材料が無い（スポーン前・クライアント・状態機械が無い、参加者が 0 人）ときは「居る」とみなし、
        /// 従来どおり時間切れまで待たせる（誤って早く締めるより安全）。
        /// </summary>
        private bool HasEligibleBuzzers()
        {
#if UNITY_INCLUDE_TESTS
            if (EligibleBuzzersOverrideForTests != null)
            {
                return EligibleBuzzersOverrideForTests();
            }
#endif

            var machine = _machine;
            if (machine == null || NetworkManager == null || !IsSpawned || !IsServer)
            {
                return true;
            }

            ulong? moderatorClientId = IsHostModerator() ? NetworkManager.ServerClientId : (ulong?)null;
            return BuzzEligibility.HasEligibleBuzzer(
                NetworkManager.ConnectedClientsIds,
                CollectRetainedDisconnectedParticipants(),
                moderatorClientId,
                machine.IsPenalized);
        }

        /// <summary>
        /// 名簿で切断中・席を保持している参加者（司会専任のホストを除く）の、切断時のクライアント ID を集める。
        /// 名簿が無ければ空。戻り値は作業用バッファなので保持しないこと。
        /// </summary>
        private IReadOnlyList<ulong> CollectRetainedDisconnectedParticipants()
        {
            _retainedDisconnectedBuffer.Clear();

            // 名簿は自分で解決する（直前に IsHostModerator() が解決しているという呼び出し順に頼らない、#204 L-A）。
            var lobbyState = ResolveSpawnedLobbyState();
            var roster = lobbyState != null ? lobbyState.ServerRoster : null;
            if (roster == null)
            {
                return _retainedDisconnectedBuffer;
            }

            var players = roster.Players;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!player.IsConnected && player.OccupiesPlayerSlot)
                {
                    _retainedDisconnectedBuffer.Add(player.ClientId);
                }
            }

            return _retainedDisconnectedBuffer;
        }
    }
}
