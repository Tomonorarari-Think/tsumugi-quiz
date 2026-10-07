using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="QuestionDistributor"/> のうち、RPC 定義（docs/network.md §1.3 / §8.6）と
    /// クライアント側の受信データ保持をまとめた部分。
    /// </summary>
    /// <remarks>
    /// サーバー → 全員の <see cref="QuestionDataRpc"/> は
    /// <c>InvokePermission = RpcInvokePermission.Server</c> かつ <c>private</c> で、クライアントから送れない
    /// （偽の問題を配れない）。クライアント → サーバーの <see cref="QuestionReceivedRpc"/> は
    /// 送信元を <c>rpcParams.Receive.SenderClientId</c> から取り、問題インデックスの範囲・重複を検証する
    /// （docs/network.md §1.5 / §9）。
    /// </remarks>
    public sealed partial class QuestionDistributor
    {
        /// <summary>RPC の頻度制限（docs/network.md §9、#52）。初回アクセス時に生成する。</summary>
        private RpcRateGuard _rpcRateGuard;

        /// <summary>このコンポーネント専用のレート制限ガード（<see cref="QuestionReceivedRpc"/> で使う）。</summary>
        private RpcRateGuard RpcRateGuard => _rpcRateGuard ??= new RpcRateGuard(NetworkManager, nameof(QuestionDistributor));

        /// <summary>
        /// 棄却ログの間引き（クライアント単位で 1 秒 1 回、docs/network.md §9）。
        /// 実体は <see cref="RpcRejectLogger"/>（#72、3 系統に分裂していた間引き実装の統合先）。
        /// 初回アクセス時に生成する（<see cref="RpcRateGuard"/> と同じ遅延生成の方針）。
        /// </summary>
        private RpcRejectLogger _rejectLogger;

        /// <summary>このコンポーネント専用の棄却ログロガー。</summary>
        private RpcRejectLogger RejectLogger => _rejectLogger ??= new RpcRejectLogger(NetworkManager);

        /// <summary>クライアントが保持している DTO の件数（テスト・診断用）。</summary>
        internal int CachedQuestionCount => _receivedQuestions.Count;

        /// <summary>
        /// 受信済みの問題データを取り出す（クライアント・ホスト双方で使える）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="question">見つかった DTO。無ければ null。</param>
        /// <returns>保持していたら true。</returns>
        public bool TryGetQuestion(int questionIndex, out QuestionDto question)
        {
            return _receivedQuestions.TryGetValue(questionIndex, out question);
        }

        /// <summary>
        /// 保持している問題データを全部捨てる。ロビーへ戻るときやセッションを作り直すときに呼ぶ
        /// （前のセッションの問題を持ち越さないため）。
        /// 現在問 0 の配信を受け取った時点でも自動で行う。
        /// </summary>
        public void ResetCache()
        {
            _receivedQuestions.Clear();

            // 画像（テクスチャ）も同時に捨てる（#16）。
            ResetImageCache();
        }

        /// <summary>
        /// 問題データの配信（サーバー → 全員）。正解は含まない（<see cref="QuestionDto"/>、仮決め K14）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="isCurrent">これから出題する問題か（false なら先読み分）。</param>
        /// <param name="question">配信する問題データ。</param>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void QuestionDataRpc(int questionIndex, bool isCurrent, QuestionDto question)
        {
            // 受信データは送信元がサーバーであっても検証する（docs/network.md §9）。
            if (questionIndex < 0)
            {
                Debug.LogWarning($"[QuestionDistributor] 問題インデックスが不正な配信を受け取りました（{questionIndex}）。");
                return;
            }

            if (question == null)
            {
                Debug.LogWarning($"[QuestionDistributor] 問題 {questionIndex} の配信データが空でした。");
                return;
            }

            if (!question.TryValidate(out var reason))
            {
                // 破棄した場合 Ack を返さないので、サーバー側はタイムアウトで進む（NAK は送らない。#16 で再送と併せて検討）。
                Debug.LogWarning($"[QuestionDistributor] 問題 {questionIndex} の配信データを破棄しました: {reason}");
                return;
            }

            if (isCurrent)
            {
                if (questionIndex == 0)
                {
                    // セッションの 1 問目。前のセッションの残りを持ち越さない。
                    ResetCache();
                }

                // 出題済みの問題は保持しない（メモリに過去の問題を溜めない、docs/network.md §8.1）。
                PruneOlderThan(questionIndex);
                PruneImagesOlderThan(questionIndex);
            }

            _receivedQuestions[questionIndex] = question;
            TrimCache(questionIndex);
            QuestionDataReceived?.Invoke(questionIndex, question);

            if (isCurrent)
            {
                // 現在問だけを Ack する。先読み分はサーバーが待っていない（docs/network.md §8.6）。
                QuestionReceivedRpc(questionIndex);
            }
        }

        /// <summary>
        /// 問題データの受信確認（クライアント → サーバー）。
        /// サーバーは全員分が揃うか <see cref="AckTimeoutSec"/> 秒経過するまで Reading に留まる。
        /// </summary>
        /// <param name="questionIndex">受信した問題インデックス。</param>
        /// <param name="rpcParams">NGO が埋める受信情報。送信元 ID はここから取る。</param>
        [Rpc(SendTo.Server)]
        internal void QuestionReceivedRpc(int questionIndex, RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time)) { return; }

            if (questionIndex < 0 || (_questionSource != null && questionIndex >= _questionSource.Count))
            {
                LogRejected(
                    senderId,
                    $"[QuestionDistributor] 範囲外の受信確認を棄却しました（送信元 {senderId}、問題 {questionIndex}）。");
                return;
            }

            if (!_distributedIndices.Contains(questionIndex))
            {
                LogRejected(
                    senderId,
                    $"[QuestionDistributor] 配信していない問題の受信確認を棄却しました（送信元 {senderId}、問題 {questionIndex}）。");
                return;
            }

            if (questionIndex != _awaitingQuestionIndex)
            {
                // 先読み分の Ack、または待ち終了後に遅れて届いた Ack。進行には影響しないので捨てる。
                return;
            }

            if (_ackedClientIds.Contains(senderId))
            {
                LogRejected(
                    senderId,
                    $"[QuestionDistributor] 重複した受信確認を棄却しました（送信元 {senderId}、問題 {questionIndex}）。");
                return;
            }

            if (!_pendingAckClientIds.Remove(senderId))
            {
                LogRejected(
                    senderId,
                    $"[QuestionDistributor] 待ち対象外のクライアントからの受信確認を棄却しました（送信元 {senderId}、問題 {questionIndex}）。");
                return;
            }

            _ackedClientIds.Add(senderId);

            // 画像付きの問題では画像 Ack も揃うまで待つ（docs/network.md §8.4、#16）。
            if (IsAckComplete && !IsCompletionSuppressed)
            {
                CompleteDistribution(timedOut: false);
            }
        }

        /// <summary>指定した問題インデックスより古い受信データを捨てる。</summary>
        private void PruneOlderThan(int questionIndex)
        {
            if (_receivedQuestions.Count == 0)
            {
                return;
            }

            var stale = new List<int>();
            foreach (var index in _receivedQuestions.Keys)
            {
                if (index < questionIndex)
                {
                    stale.Add(index);
                }
            }

            for (var i = 0; i < stale.Count; i++)
            {
                _receivedQuestions.Remove(stale[i]);
            }
        }

        /// <summary>
        /// 受信データの保持件数に上限をかける（壊れた・悪意ある配信で無制限に溜め込まないため）。
        /// 現在問から遠いものを先に捨てる。
        /// </summary>
        private void TrimCache(int keepQuestionIndex)
        {
            while (_receivedQuestions.Count > MaxCachedQuestions)
            {
                var farthestIndex = keepQuestionIndex;
                var farthestDistance = -1;
                foreach (var index in _receivedQuestions.Keys)
                {
                    var distance = Mathf.Abs(index - keepQuestionIndex);
                    if (distance > farthestDistance)
                    {
                        farthestDistance = distance;
                        farthestIndex = index;
                    }
                }

                if (farthestDistance <= 0)
                {
                    return;
                }

                _receivedQuestions.Remove(farthestIndex);
            }
        }

        /// <summary>
        /// 棄却をサーバーのログに残す（クライアントへは返さない、docs/network.md §9）。
        /// 同じクライアントからの連続した棄却は <see cref="RpcRejectLogger"/> により 1 秒 1 回へ間引く（#72）。
        /// サーバー時刻の取得は <see cref="RpcRejectLogger"/> 側に集約した（#83）。
        /// </summary>
        private void LogRejected(ulong clientId, string message) => RejectLogger.LogRejected(clientId, message);
    }
}
