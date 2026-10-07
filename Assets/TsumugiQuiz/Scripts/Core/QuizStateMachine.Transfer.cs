using System;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> のうち、再接続で変わったクライアント ID の付け替えをまとめた部分
    /// （#84、docs/network.md §2.4 の「再接続で引き継ぐもの / 引き継がないもの」）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 進行中の状態（得点・ペナルティ・回答済み・ロック保持者・選択式の選択）は
    /// すべて NGO の <c>clientId</c> をキーにしている。NGO はクライアント ID を使い回さない
    /// （NGO 2.13.2 <c>Runtime/Connection/NetworkConnectionManager.cs</c> の <c>m_NextClientId++</c>）ため、
    /// 同じ人が切断 → 再接続すると別のキーになり、そのままでは得点 0 の別人として扱われる。
    /// </para>
    /// <para>
    /// 「同じ人か」を決めるのは名簿（<c>LobbyRoster</c> の席 = <c>LobbyPlayer.SeatId</c>）と
    /// 再接続トークン（#69）で、本クラスはその判定結果を受けて機械的に付け替えるだけ。
    /// 呼ぶのはサーバー（<c>TsumugiQuiz.Network.GameSession.TransferSeat</c>）のみ。
    /// </para>
    /// </remarks>
    public sealed partial class QuizStateMachine
    {
        /// <summary>
        /// 進行中の状態のクライアント ID を <paramref name="fromClientId"/> から
        /// <paramref name="toClientId"/> へ付け替える（#84）。
        /// </summary>
        /// <remarks>
        /// <para>付け替える対象（＝再接続しても引き継ぐもの）:</para>
        /// <list type="bullet">
        /// <item><description>得点表（<see cref="Scores"/>）と確定済みの最終得点（<see cref="FinalScores"/>）</description></item>
        /// <item><description>「次問休み」ペナルティ（<see cref="Penalties"/>、<c>Pending</c> と確定済みの両方）</description></item>
        /// <item><description>現在の問題で既に誤答したか（<see cref="WrongAnswerers"/>）</description></item>
        /// <item><description>早押しロック保持者（<see cref="LockedClientId"/>）と直近の得点通知先（<see cref="LastScoredClientId"/>）</description></item>
        /// <item><description>選択式の選択（受付中の 1 回だけの制約）と、受付中の押下候補・ペナルティ集合（<see cref="BuzzArbiter"/>）</description></item>
        /// <item><description>参加者パネルの押下順・回答順（#194、<see cref="BuildProgress"/>）</description></item>
        /// </list>
        /// <para>
        /// いずれも「切断・再接続で罰から逃れられない / 得点を失わない」ようにするための引き継ぎで、
        /// 席（名簿エントリ）が同一であることを呼び出し側が保証している前提に立つ。
        /// フェーズ・問題インデックス・時刻はクライアント ID に紐づかないので影響しない。
        /// </para>
        /// </remarks>
        /// <param name="fromClientId">切断時に使っていた古いクライアント ID。</param>
        /// <param name="toClientId">復帰して新しく割り当てられたクライアント ID。</param>
        /// <returns>実際に付け替えたものが 1 つでもあれば true。</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// いずれかが <see cref="NoClientId"/>（＝「居ない」を表す番兵）のとき。
        /// 呼び出し側の配線ミスなので握りつぶさず通知する。
        /// </exception>
        public bool TransferClient(ulong fromClientId, ulong toClientId)
        {
            if (fromClientId == NoClientId)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fromClientId), fromClientId, "付け替え元に NoClientId は指定できません。");
            }

            if (toClientId == NoClientId)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(toClientId), toClientId, "付け替え先に NoClientId は指定できません。");
            }

            if (fromClientId == toClientId)
            {
                return false;
            }

            var changed = false;

            var scores = Scores.WithClientIdChanged(fromClientId, toClientId);
            if (!ReferenceEquals(scores, Scores))
            {
                Scores = scores;
                changed = true;
            }

            if (FinalScores != null)
            {
                var finalScores = FinalScores.WithClientIdChanged(fromClientId, toClientId);
                if (!ReferenceEquals(finalScores, FinalScores))
                {
                    FinalScores = finalScores;
                    changed = true;
                }
            }

            var penalties = Penalties.WithClientIdChanged(fromClientId, toClientId);
            if (!ReferenceEquals(penalties, Penalties))
            {
                Penalties = penalties;
                changed = true;
            }

            changed |= TransferWrongAnswerer(fromClientId, toClientId);
            changed |= TransferChoiceSelection(fromClientId, toClientId);
            changed |= TransferProgress(fromClientId, toClientId); // #194: 押下順・回答順

            if (LockedClientId == fromClientId)
            {
                LockedClientId = toClientId;
                changed = true;
            }

            if (LastScoredClientId == fromClientId)
            {
                LastScoredClientId = toClientId;
                changed = true;
            }

            if (_arbiter != null && _arbiter.TransferClientId(fromClientId, toClientId))
            {
                changed = true;
            }

            return changed;
        }

        /// <summary>現在の問題の「誤答済み」を付け替える（重複させない）。</summary>
        private bool TransferWrongAnswerer(ulong fromClientId, ulong toClientId)
        {
            var index = _wrongAnswerers.IndexOf(fromClientId);
            if (index < 0)
            {
                return false;
            }

            if (_wrongAnswerers.Contains(toClientId))
            {
                _wrongAnswerers.RemoveAt(index);
            }
            else
            {
                _wrongAnswerers[index] = toClientId;
            }

            return true;
        }

        /// <summary>
        /// 選択式の選択（1 人 1 回）を付け替える。付け替え先が既に選択済みなら
        /// そちらを残す（新しい接続の選択のほうが後の意思表示だが、通常は起こらない）。
        /// </summary>
        private bool TransferChoiceSelection(ulong fromClientId, ulong toClientId)
        {
            if (!_choiceSelections.TryGetValue(fromClientId, out var choiceIndex))
            {
                return false;
            }

            _choiceSelections.Remove(fromClientId);
            if (!_choiceSelections.ContainsKey(toClientId))
            {
                _choiceSelections[toClientId] = choiceIndex;
            }

            return true;
        }
    }
}
