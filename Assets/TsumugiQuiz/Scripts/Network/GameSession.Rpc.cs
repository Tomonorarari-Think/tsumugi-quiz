using TsumugiQuiz.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、RPC 定義（docs/network.md §1.3 / §1.5）と
    /// クライアント側から呼ぶ送信 API をまとめた部分。
    /// 状態・フェーズ進行・サーバー側の公開 API は GameSession.cs 側にある。
    /// </summary>
    /// <remarks>
    /// RPC のメソッド名は必ず <c>Rpc</c> で終える（NGO 2.x の制約）。
    /// クライアント → サーバーの RPC では、送信元 ID を引数で受け取らず
    /// <c>rpcParams.Receive.SenderClientId</c> から取る（詐称を防ぐため、§1.5 / §9）。
    /// サーバー → 全員の RPC は <c>InvokePermission = RpcInvokePermission.Server</c> を付けて
    /// クライアントからの送信を NGO 側で禁止し（違反すると送信時に <c>RpcException</c>）、
    /// さらに <c>private</c> にして外部から呼べないようにする。
    /// </remarks>
    public sealed partial class GameSession
    {
        /// <summary>RPC の頻度制限（docs/network.md §9、#52）。初回アクセス時に生成する。</summary>
        private RpcRateGuard _rpcRateGuard;

        /// <summary>このコンポーネント専用のレート制限ガード（<see cref="BuzzRpc"/> / <see cref="SubmitAnswerRpc"/> で使う）。</summary>
        private RpcRateGuard RpcRateGuard => _rpcRateGuard ??= new RpcRateGuard(NetworkManager, nameof(GameSession));

        /// <summary>
        /// 棄却ログの間引き（クライアント単位で 1 秒 1 回、docs/network.md §9）。
        /// 実体は <see cref="RpcRejectLogger"/>（#72、3 系統に分裂していた間引き実装の統合先）。
        /// 初回アクセス時に生成する（<see cref="RpcRateGuard"/> と同じ遅延生成の方針）。
        /// </summary>
        private RpcRejectLogger _rejectLogger;

        /// <summary>このコンポーネント専用の棄却ログロガー。</summary>
        private RpcRejectLogger RejectLogger => _rejectLogger ??= new RpcRejectLogger(NetworkManager);

        /// <summary>
        /// 押下をサーバーへ送る（クライアント側から呼ぶ）。
        /// 送る時刻は <c>NetworkManager.LocalTime.Time</c>（docs/network.md §6.2）。
        /// </summary>
        /// <returns>送信したら true。フェーズ外・未接続なら false。</returns>
        public bool RequestBuzz()
        {
            if (!IsSpawned || NetworkManager == null || !NetworkManager.IsClient)
            {
                return false;
            }

            // ローカルの事前判定は体感（連打防止）のためだけで、正しさはサーバーが決める（§9）。
            if (_phase.Value != QuizPhase.BuzzOpen || _hasBuzzedInCurrentPhase)
            {
                return false;
            }

            _hasBuzzedInCurrentPhase = true;
            BuzzRpc(NetworkManager.LocalTime.Time);
            return true;
        }

        /// <summary>
        /// 回答をサーバーへ送る（クライアント側から呼ぶ）。
        /// </summary>
        /// <param name="text">回答文字列（<see cref="MaxAnswerLength"/> 文字以内）。</param>
        /// <returns>送信したら true。</returns>
        public bool RequestAnswer(string text)
        {
            if (!IsSpawned || NetworkManager == null || !NetworkManager.IsClient)
            {
                return false;
            }

            if (_phase.Value != QuizPhase.Answering || _lockedClientId.Value != NetworkManager.LocalClientId)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxAnswerLength)
            {
                return false;
            }

            SubmitAnswerRpc(ToFixedAnswer(text));
            return true;
        }

        /// <summary>
        /// 押下（クライアント → サーバー）。送信元は <c>rpcParams.Receive.SenderClientId</c> から取る
        /// （引数で受け取ると詐称できるため、docs/network.md §1.5 / §9）。
        /// </summary>
        /// <param name="buzzServerTime">クライアントが押下した時刻（<c>LocalTime.Time</c>）。</param>
        /// <param name="rpcParams">NGO が埋める受信情報。</param>
        [Rpc(SendTo.Server)]
        public void BuzzRpc(double buzzServerTime, RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time)) { return; }

            if (IsModeratorHostSender(senderId))
            {
                LogRejected(senderId, "[GameSession] 司会専用モードのホストは早押しできません（早押しは司会操作パネルの対象外）。");
                return;
            }

            if (_machine == null)
            {
                LogRejected(senderId, $"[GameSession] 押下を受け取りましたが進行が始まっていません（送信元 {senderId}）。");
                return;
            }

            if (!_machine.AcceptBuzz(senderId, buzzServerTime, NetworkManager.ServerTime.Time, out var reason))
            {
                // 棄却理由はサーバーのログにだけ残す（クライアントには返さない、§9）。
                LogRejected(senderId, $"[GameSession] 押下を受け付けませんでした（送信元 {senderId}、理由: {reason}、フェーズ: {_machine.Phase}）。");
            }
        }

        /// <summary>
        /// 回答（クライアント → サーバー）。ロック保持者本人からのものだけを受理する。
        /// </summary>
        /// <param name="text">回答文字列。</param>
        /// <param name="rpcParams">NGO が埋める受信情報。</param>
        [Rpc(SendTo.Server)]
        public void SubmitAnswerRpc(FixedString512Bytes text, RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time)) { return; }

            if (IsModeratorHostSender(senderId))
            {
                LogRejected(senderId, "[GameSession] 司会専用モードのホストは回答できません（回答は司会操作パネルの対象外）。");
                return;
            }

            if (_machine == null)
            {
                LogRejected(senderId, $"[GameSession] 回答を受け取りましたが進行が始まっていません（送信元 {senderId}）。");
                return;
            }

            var answer = text.ToString();
            if (!_machine.SubmitAnswer(senderId, answer, NetworkManager.ServerTime.Time, out var reason))
            {
                LogRejected(senderId, $"[GameSession] 回答を受け付けませんでした（送信元 {senderId}、理由: {reason}、フェーズ: {_machine.Phase}）。");
                return;
            }

            PublishState();
        }

        /// <summary>
        /// 問題の提示（サーバー → 全員）。問題データ自体は先に
        /// <see cref="QuestionDistributor"/> が配信済みなので、ここでは提示の合図だけを送る
        /// （docs/network.md §8.5。同じ内容を二重に送らないため）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void QuestionShownRpc(int questionIndex)
        {
            if (_distributor == null)
            {
                Debug.LogWarning($"[GameSession] QuestionDistributor が無いため問題 {questionIndex} を提示できません。");
                return;
            }

            if (!_distributor.TryGetQuestion(questionIndex, out var question))
            {
                // 受信確認がタイムアウトした場合など、配信が届いていないクライアントはここに来る
                // （進行そのものはサーバー権威なので続く。docs/network.md §8.4）。
                Debug.LogWarning($"[GameSession] 問題 {questionIndex} の配信データが届いていないため提示できません。");
                return;
            }

            RaiseQuestionShown(questionIndex, question, QuestionShownSource.Distribution);
        }

        /// <summary>
        /// 早押しの裁定結果（サーバー → 全員）。
        /// </summary>
        /// <param name="winnerClientId">勝者のクライアント ID。</param>
        /// <param name="lockedAtServerTime">勝者が確定したサーバー時刻。</param>
        /// <param name="wasTie">同着（差 &lt; 1ms）により抽選になったか（docs/network.md §6.3）。</param>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void BuzzResultRpc(ulong winnerClientId, double lockedAtServerTime, bool wasTie)
        {
            BuzzLocked?.Invoke(winnerClientId, lockedAtServerTime, wasTie);
        }

        /// <summary>
        /// 1 問の結果（サーバー → 全員）。正解はこのタイミングで初めてクライアントへ渡す（仮決め K14）。
        /// </summary>
        /// <param name="judgement">判定結果。</param>
        /// <param name="answererClientId">回答者。誰も押さなかった場合は <see cref="NoClientId"/>。</param>
        /// <param name="correctAnswer">正解（代表の 1 件）。</param>
        /// <param name="score">回答者の得点（累計）。累計の表示にはこの値を使う。</param>
        /// <param name="scoreDelta">
        /// この結果での得点の増減（お手つきの減点を含む、#18）。
        /// 誰も押さずにタイムアウトしたときと、再開放後にタイムアウトしたときは 0 になる。
        /// </param>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void QuestionResultRpc(
            QuizJudgement judgement,
            ulong answererClientId,
            FixedString512Bytes correctAnswer,
            int score,
            int scoreDelta)
        {
            QuestionResolved?.Invoke(judgement, answererClientId, correctAnswer.ToString(), score, scoreDelta);
        }

        /// <summary>
        /// 得点の増減（サーバー → 全員）。累計値そのものは得点表（<c>NetworkList</c>）で同期しており、
        /// 本 RPC は「何点動いたか」を演出に使うためのもの（#18）。
        /// </summary>
        /// <remarks>
        /// 増減が ±0 のとき（既定設定での誤答・お手つきなど）も送る。
        /// 「お手つきしたが得点は変わらない」ことも演出に必要なため。
        /// 累計の表示には <paramref name="total"/> を使うこと。
        /// <see cref="GetScore"/> は <c>NetworkList</c> の同期待ちで 1 tick 前の値を返すことがある。
        /// </remarks>
        /// <param name="clientId">得点が動いたクライアント。</param>
        /// <param name="delta">増減（±0 もありうる）。</param>
        /// <param name="total">増減後の累計得点。</param>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ScoreChangedRpc(ulong clientId, int delta, int total)
        {
            ScoreChanged?.Invoke(clientId, delta, total);
        }

        /// <summary>
        /// 誤答・お手つき後の早押し受付の再開放（サーバー → 全員、docs/network.md §6.6）。
        /// T0 は据え置きなので、クライアントは受け取った T0 から残り時間を計算し直せる。
        /// </summary>
        /// <param name="wrongClientId">誤答したクライアント（受付対象外になる）。</param>
        /// <param name="buzzOpenServerTime">据え置きの受付開始時刻 T0。</param>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void BuzzReopenedRpc(ulong wrongClientId, double buzzOpenServerTime)
        {
            BuzzReopened?.Invoke(wrongClientId, buzzOpenServerTime);
        }

        /// <summary>
        /// 送信元がホストかつ司会専用モード（<c>host.role == "moderator"</c>）かどうか（H3）。
        /// 司会は早押し・回答の代わりに司会操作パネルを使うため、ホスト自身が司会専用モードのときは
        /// <see cref="BuzzRpc"/> / <see cref="SubmitAnswerRpc"/> を受理しない。
        /// </summary>
        /// <param name="senderId">送信元クライアント ID。</param>
        private bool IsModeratorHostSender(ulong senderId) =>
            senderId == NetworkManager.ServerClientId && IsHostModerator();

        /// <summary>
        /// 棄却をサーバーのログに残す（クライアントへは返さない、docs/network.md §9）。
        /// 同じクライアントからの連続した棄却は <see cref="RpcRejectLogger"/> により 1 秒 1 回へ間引く（#72）。
        /// サーバー時刻の取得は <see cref="RpcRejectLogger"/> 側に集約した（#83）。
        /// </summary>
        /// <param name="clientId">棄却したクライアント ID。</param>
        /// <param name="message">ログに残す内容。</param>
        private void LogRejected(ulong clientId, string message) => RejectLogger.LogRejected(clientId, message);

        /// <summary>回答・正解を <see cref="FixedString512Bytes"/> に詰める（容量超過は切り詰め）。</summary>
        private static FixedString512Bytes ToFixedAnswer(string text)
        {
            var fixedText = new FixedString512Bytes();
            fixedText.CopyFromTruncated(text ?? string.Empty);
            return fixedText;
        }
    }
}
