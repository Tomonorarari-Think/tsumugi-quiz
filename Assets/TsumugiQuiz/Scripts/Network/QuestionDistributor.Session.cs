using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="QuestionDistributor"/> のうち、セッション（#19）に関わる部分。
    /// セッション開始時の配信状態の破棄と、途中参加・再接続したクライアント 1 人への再送を扱う。
    /// 通常の配信（全員 + Ack 待ち）は QuestionDistributor.cs / .Rpc.cs にある。
    /// </summary>
    /// <remarks>
    /// 再送は受信確認（Ack）を伴わない。進行はサーバー権威で既に動いており、
    /// 後から参加したクライアントを待つために全体を止めることはしないため
    /// （docs/network.md §8.4 と同じ「進行を止めない」方針）。
    /// </remarks>
    public sealed partial class QuestionDistributor
    {
        /// <summary>
        /// 進行中の配信（送信予定・Ack 待ち）を破棄する（サーバーのみ、新しいセッションを始めるとき）。
        /// <see cref="DistributionCompleted"/> は発火しない（前のセッションの出題を進めないため）。
        /// </summary>
        internal void AbortPendingDistribution()
        {
            CancelPrepared();
            _pendingAckClientIds.Clear();
            _ackedClientIds.Clear();
            _distributedIndices.Clear();
            _awaitingQuestionIndex = NoQuestionIndex;
            _ackDeadlineServerTime = 0.0;
            _completionSuppressionDepth = 0;

            // 画像（#16）の送信予定・Ack 待ちも同時に捨てる。前のセッションのチャンクを送り続けない。
            ResetImageDistribution();
        }

        /// <summary>
        /// 指定したクライアントへ現在問の DTO を送り直す（サーバーのみ）。
        /// </summary>
        /// <remarks>
        /// 画像（#16）も、送信準備済みであれば同じクライアントへ送り直す
        /// （<see cref="TryResendImageTo"/>）。こちらも Ack は待たない。
        /// <b>読み上げ（#23）</b>: 合流したクライアントは<b>その問題の読み上げには参加しない</b>
        /// （統括判断 #109。<see cref="GameSession.QuestionShown"/> に渡す
        /// <see cref="QuestionShownSource.Resync"/> を見て <see cref="TtsSyncCoordinator"/> が合成を始めない）。
        /// 合流時点で読み上げは既に進んでおり、いまから合成しても同期して鳴らせないため。
        /// 次の問題からは通常どおり読み上げに参加する（docs/tts.md §6.7、docs/network.md §2.4）。
        /// </remarks>
        /// <param name="clientId">送信先クライアント ID。</param>
        /// <param name="questionIndex">送り直す問題インデックス（出題列上の位置）。</param>
        /// <param name="error">失敗理由（ログ用）。成功時は null。</param>
        /// <returns>送信できたら true。</returns>
        internal bool TryResendTo(ulong clientId, int questionIndex, out string error)
        {
            if (!IsSpawned || !IsServer)
            {
                error = "問題の再送はサーバーでのみ行えます。";
                return false;
            }

            if (_questionSource == null)
            {
                error = "問題の供給元が設定されていません。";
                return false;
            }

            if (!_questionSource.TryGetQuestion(questionIndex, out var question))
            {
                error = $"問題インデックス {questionIndex} は範囲外です（問題数 {_questionSource.Count}）。";
                return false;
            }

            if (!question.Type.HasValue)
            {
                error = $"出題形式（type）が設定されていない問題は配信できません（id: {question.Id}）。";
                return false;
            }

            var dto = QuestionDto.From(question, out _);
            if (!dto.TryValidate(out var reason))
            {
                error = $"問題 {questionIndex}（id: {question.Id}）は配信できません: {reason}";
                return false;
            }

            QuestionResyncRpc(questionIndex, dto, RpcTarget.Single(clientId, RpcTargetUse.Temp));

            // 画像（#16）も送り直す。画像なしの問題では何も起きないのが正常なので、
            // 「画像がある問題なのに用意できていない」ときだけ警告を残す（PR #114 レビュー M-3）。
            // 重複排除・キュー上限で見送った場合（M-4）はそれぞれの経路で扱うのでここでは出さない。
            if (!TryResendImageTo(clientId, questionIndex)
                && !string.IsNullOrEmpty(question.ImagePath)
                && !HasPreparedImage(questionIndex))
            {
                Debug.LogWarning(
                    $"[QuestionDistributor] 問題 {questionIndex} には画像がありますが、送信準備済みでないため"
                    + $"クライアント {clientId} へ送り直せませんでした（このクライアントは画像なしで進みます）。");
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 現在問の再送（サーバー → 指定クライアント）。受信確認（Ack）は返さない。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="question">配信する問題データ（正解は含まない）。</param>
        /// <param name="rpcParams">送信先（NGO が埋める受信情報）。</param>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void QuestionResyncRpc(int questionIndex, QuestionDto question, RpcParams rpcParams = default)
        {
            // 通常の配信と同じく、サーバー発でも受信データは検証する（docs/network.md §9）。
            if (questionIndex < 0)
            {
                Debug.LogWarning($"[QuestionDistributor] 問題インデックスが不正な再送を受け取りました（{questionIndex}）。");
                return;
            }

            if (question == null)
            {
                Debug.LogWarning($"[QuestionDistributor] 問題 {questionIndex} の再送データが空でした。");
                return;
            }

            if (!question.TryValidate(out var reason))
            {
                Debug.LogWarning($"[QuestionDistributor] 問題 {questionIndex} の再送データを破棄しました: {reason}");
                return;
            }

            PruneOlderThan(questionIndex);
            _receivedQuestions[questionIndex] = question;
            TrimCache(questionIndex);
            QuestionDataReceived?.Invoke(questionIndex, question);
        }
    }
}
