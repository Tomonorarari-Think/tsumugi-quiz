using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 「いま早押しを押せる参加者が 1 人以上居るか」を判定する純関数（#200、docs/network.md §6.6）。
    /// Unity API に依存しないので EditMode でテストする。
    /// </summary>
    /// <remarks>
    /// <para>参加者（押せる人の候補）は次の 2 種類で、どちらも司会専任のホストは含めない:</para>
    /// <list type="bullet">
    /// <item><description>
    /// 接続中のクライアント。受付側（<c>GameSession.BuzzRpc</c> → <see cref="QuizStateMachine.AcceptBuzz"/>）が
    /// 名簿を見ずに押下を受理する相手と同じ（NGO の接続中クライアント）
    /// </description></item>
    /// <item><description>
    /// 切断中だが席を保持している参加者（名簿で切断中、まだ保持期間内で <c>ForgetSeat</c> されていない人）。
    /// 一瞬の切断で問題が締まらないよう、戻ってくる可能性がある人として数える
    /// </description></item>
    /// </list>
    /// <para>
    /// そのうち <see cref="QuizStateMachine.IsPenalized"/> が false（現在の問題で誤答済みでも、次問休みでもない）の人が
    /// 1 人でも居れば「居る」。切断中の人のペナルティは、切断時のクライアント ID のまま状態機械に残っている
    /// （切断では捨てない。#84）ので、同じ <paramref name="isPenalized"/> で判定できる。
    /// </para>
    /// <para>
    /// <b>参加者が 0 人のとき</b>（司会専任のホストだけ、名簿が空など）は判定の材料が無いものとして true を返す。
    /// 呼び出し側（<see cref="QuizStateMachine"/>）は従来どおり時間切れまで待つ。参加者の居ない部屋で
    /// 問題が次々に締まって流れてしまうのを防ぐため。
    /// </para>
    /// </remarks>
    public static class BuzzEligibility
    {
        /// <summary>押せる参加者が 1 人以上居るか（参加者が 0 人なら true）。</summary>
        /// <param name="connectedClientIds">いま接続しているクライアント ID（ホスト自身を含む）。</param>
        /// <param name="retainedDisconnectedClientIds">
        /// 切断中だが席を保持している参加者の、切断時のクライアント ID。名簿が無ければ空でよい。
        /// </param>
        /// <param name="moderatorClientId">
        /// 早押しの対象外にするクライアント ID（司会専任のホスト）。ホストも参加者なら null。
        /// </param>
        /// <param name="isPenalized">
        /// 現在の早押し受付で棄却されるか（<see cref="QuizStateMachine.IsPenalized"/> を渡す）。
        /// </param>
        /// <returns>押せる参加者が 1 人以上居る、または参加者が 0 人なら true。</returns>
        /// <exception cref="ArgumentNullException">引数のいずれかが null のとき。</exception>
        public static bool HasEligibleBuzzer(
            IReadOnlyList<ulong> connectedClientIds,
            IReadOnlyList<ulong> retainedDisconnectedClientIds,
            ulong? moderatorClientId,
            Func<ulong, bool> isPenalized)
        {
            if (connectedClientIds == null)
            {
                throw new ArgumentNullException(nameof(connectedClientIds));
            }

            if (retainedDisconnectedClientIds == null)
            {
                throw new ArgumentNullException(nameof(retainedDisconnectedClientIds));
            }

            if (isPenalized == null)
            {
                throw new ArgumentNullException(nameof(isPenalized));
            }

            var participantCount = 0;
            if (ContainsEligible(connectedClientIds, moderatorClientId, isPenalized, ref participantCount)
                || ContainsEligible(retainedDisconnectedClientIds, moderatorClientId, isPenalized, ref participantCount))
            {
                return true;
            }

            // 参加者が 1 人も居ない（判定の材料が無い）ときは締めない。
            return participantCount == 0;
        }

        private static bool ContainsEligible(
            IReadOnlyList<ulong> clientIds,
            ulong? moderatorClientId,
            Func<ulong, bool> isPenalized,
            ref int participantCount)
        {
            for (var i = 0; i < clientIds.Count; i++)
            {
                var clientId = clientIds[i];
                if (moderatorClientId.HasValue && clientId == moderatorClientId.Value)
                {
                    continue;
                }

                participantCount++;
                if (!isPenalized(clientId))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
