using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、司会専用モードの進行操作（#20、
    /// docs/tasks/setup-brief.md K18「次へ」「一時停止」「強制正解/不正解」）をまとめた部分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// このゲームは Host モードのみなので、「次へ」「一時停止」「再開」は送信元がホスト自身
    /// （<see cref="NetworkManager.ServerClientId"/>）であることだけを検証する（<see cref="IsFromHost"/>）。
    /// 通常モードのホストにも進行の巻き戻し手段として提供して差し支えないためで、
    /// <c>host.role == moderator</c> かどうかは問わない。
    /// </para>
    /// <para>
    /// 「強制正解」「強制不正解」（<see cref="ForceJudgeRpc"/>）はさらに強く、送信元がホストであることに加えて
    /// <c>host.role == "moderator"</c>（<see cref="IsHostModerator"/>、<see cref="LobbyState.Role"/>）でなければ
    /// 受理しない。通常モードのホストは自分自身が回答者になりうるため、自分の判定を自分で書き換えられて
    /// しまわないようにする（統括判断 M5）。
    /// </para>
    /// <para>
    /// 新しい判定種別は追加せず、既存の <see cref="QuizJudgement.Correct"/> / <see cref="QuizJudgement.Wrong"/> を
    /// そのまま使う（<see cref="QuizStateMachine.ForceJudge"/>）。結果の配信は通常の回答と同じ
    /// <c>QuestionResultRpc</c>（<c>HandleServerTick</c> 経由）を流用する。司会由来であることは
    /// サーバーのログにだけ残す（<see cref="ForceJudgeRpc"/>）。
    /// </para>
    /// <para>
    /// すべての RPC は先頭で <see cref="RpcRateGuard.Allow"/>（<see cref="BuzzRpc"/> と同形、#52）を通す。
    /// </para>
    /// </remarks>
    public sealed partial class GameSession
    {
        /// <summary>
        /// 「次へ」を要求する（クライアント側の司会 UI から呼ぶ）。
        /// </summary>
        /// <returns>送信したら true。</returns>
        public bool RequestNextQuestion()
        {
            if (!CanSendModeratorRequest())
            {
                return false;
            }

            NextQuestionRpc();
            return true;
        }

        /// <summary>「一時停止」を要求する（クライアント側の司会 UI から呼ぶ）。</summary>
        /// <returns>送信したら true。</returns>
        public bool RequestPause()
        {
            if (!CanSendModeratorRequest())
            {
                return false;
            }

            PauseRpc();
            return true;
        }

        /// <summary>「再開」を要求する（クライアント側の司会 UI から呼ぶ）。</summary>
        /// <returns>送信したら true。</returns>
        public bool RequestResume()
        {
            if (!CanSendModeratorRequest())
            {
                return false;
            }

            ResumeRpc();
            return true;
        }

        /// <summary>
        /// 「強制正解」「強制不正解」を要求する（クライアント側の司会 UI から呼ぶ）。
        /// </summary>
        /// <param name="judgement"><see cref="QuizJudgement.Correct"/> または <see cref="QuizJudgement.Wrong"/>。</param>
        /// <returns>送信したら true。</returns>
        public bool RequestForceJudge(QuizJudgement judgement)
        {
            if (judgement != QuizJudgement.Correct && judgement != QuizJudgement.Wrong)
            {
                return false;
            }

            if (!CanSendModeratorRequest())
            {
                return false;
            }

            ForceJudgeRpc(judgement);
            return true;
        }

        /// <summary>
        /// 「次へ」（クライアント → サーバー）。<see cref="NextQuestion"/> と同じ入口を、
        /// 自動進行だけでなく司会の操作からも呼べるようにする。
        /// </summary>
        /// <param name="rpcParams">NGO が埋める受信情報。</param>
        [Rpc(SendTo.Server)]
        public void NextQuestionRpc(RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time))
            {
                return;
            }

            if (!IsFromHost(senderId))
            {
                return;
            }

            if (!NextQuestion())
            {
                LogRejected(
                    senderId,
                    "[GameSession] 司会の「次へ」を受け付けられませんでした"
                    + $"（結果表示中でない、または一時停止中です。フェーズ: {_machine?.Phase}）。");
            }
        }

        /// <summary>「一時停止」（クライアント → サーバー）。</summary>
        /// <param name="rpcParams">NGO が埋める受信情報。</param>
        [Rpc(SendTo.Server)]
        public void PauseRpc(RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time))
            {
                return;
            }

            if (!IsFromHost(senderId))
            {
                return;
            }

            if (_machine == null)
            {
                LogRejected(senderId, $"[GameSession] 一時停止を受け取りましたが進行が始まっていません（送信元 {senderId}）。");
                return;
            }

            if (!_machine.Pause(NetworkManager.ServerTime.Time, out var reason))
            {
                LogRejected(senderId, $"[GameSession] 司会の一時停止を受け付けませんでした（理由: {reason}）。");
                return;
            }

            PublishState();
        }

        /// <summary>「再開」（クライアント → サーバー）。</summary>
        /// <param name="rpcParams">NGO が埋める受信情報。</param>
        [Rpc(SendTo.Server)]
        public void ResumeRpc(RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time))
            {
                return;
            }

            if (!IsFromHost(senderId))
            {
                return;
            }

            if (_machine == null)
            {
                LogRejected(senderId, $"[GameSession] 再開を受け取りましたが進行が始まっていません（送信元 {senderId}）。");
                return;
            }

            if (!_machine.Resume(NetworkManager.ServerTime.Time, out var reason))
            {
                LogRejected(senderId, $"[GameSession] 司会の再開を受け付けませんでした（理由: {reason}）。");
                return;
            }

            PublishState();
        }

        /// <summary>
        /// 「強制正解」「強制不正解」（クライアント → サーバー）。送信元がホストであることに加えて、
        /// <c>host.role == "moderator"</c> であることも要求する（統括判断 M5）。
        /// </summary>
        /// <param name="judgement"><see cref="QuizJudgement.Correct"/> または <see cref="QuizJudgement.Wrong"/>。</param>
        /// <param name="rpcParams">NGO が埋める受信情報。</param>
        [Rpc(SendTo.Server)]
        public void ForceJudgeRpc(QuizJudgement judgement, RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time))
            {
                return;
            }

            if (!IsFromHost(senderId))
            {
                return;
            }

            if (!IsHostModerator())
            {
                LogRejected(
                    senderId,
                    "[GameSession] 強制判定は司会専用モード（host.role == moderator）のホストのみ行えます"
                    + $"（送信元 {senderId}）。");
                return;
            }

            if (_machine == null)
            {
                LogRejected(senderId, $"[GameSession] 強制判定を受け取りましたが進行が始まっていません（送信元 {senderId}）。");
                return;
            }

            if (!_machine.ForceJudge(judgement, NetworkManager.ServerTime.Time, out var reason))
            {
                LogRejected(senderId, $"[GameSession] 司会の強制判定を受け付けませんでした（理由: {reason}）。");
                return;
            }

            // 司会由来であることはここ（サーバーのログ）にだけ残す（QuizStateMachine は判定種別を増やさない）。
            Debug.Log($"[GameSession] 司会（クライアント {senderId}）が判定を {judgement} に上書きしました。");
            PublishState();
        }

        /// <summary>クライアント側の事前判定（体感のためだけで、正しさはサーバーが決める、docs/network.md §9）。</summary>
        private bool CanSendModeratorRequest() =>
            IsSpawned && NetworkManager != null && NetworkManager.IsClient;

        /// <summary>
        /// 送信元がホスト自身かどうかを検証する。一致しなければ棄却ログだけ残す
        /// （クライアントへは理由を返さない、docs/network.md §9）。
        /// </summary>
        /// <param name="senderId">送信元クライアント ID（<c>rpcParams.Receive.SenderClientId</c> から取ったもの）。</param>
        /// <returns>ホスト自身からの呼び出しなら true。</returns>
        private bool IsFromHost(ulong senderId)
        {
            if (senderId == NetworkManager.ServerClientId)
            {
                return true;
            }

            LogRejected(senderId, $"[GameSession] 司会操作 RPC をホスト以外から受け取ったため拒否しました（送信元 {senderId}）。");
            return false;
        }

        /// <summary>
        /// 同じ <see cref="NetworkManager"/> にスポーンされている <see cref="LobbyState"/>（司会専用モードの
        /// 判定に使う）。<see cref="TryResolveLobbyState"/> でメモ化する（L-D）。
        /// </summary>
        private LobbyState _lobbyState;

        /// <summary>
        /// <see cref="_lobbyState"/> を解決してメモ化する（<c>OnNetworkSpawn</c> から呼ぶ、L-D）。
        /// <see cref="GameSession"/> と <see cref="LobbyState"/> はスポーン順序が保証されていないため、
        /// 見つからなかった場合は <see cref="IsHostModerator"/> 側で毎回引き直す（自己修復。
        /// 見つかった後は再 Find しない）。
        /// </summary>
        private void TryResolveLobbyState()
        {
            _lobbyState ??= LobbyState.Find(NetworkManager);
        }

        /// <summary>
        /// ホストが司会専用モード（<c>host.role == "moderator"</c>）かどうかを、メモ化した
        /// <see cref="LobbyState"/> から調べる。<see cref="LobbyState"/> が見つからない場合は
        /// false（既定の Player 扱い）とする。
        /// </summary>
        private bool IsHostModerator()
        {
            var lobbyState = ResolveSpawnedLobbyState();
            return lobbyState != null && lobbyState.Role.Value == HostRole.Moderator;
        }

        /// <summary>
        /// スポーン済みの <see cref="LobbyState"/> を返す（無ければ null）。メモ化した値がまだ無い・スポーンしていなければ
        /// その場で引き直す（OnNetworkSpawn 時点でまだ LobbyState が無かった場合の自己修復、L-D）。
        /// 見つかった後は引き直さない（次回以降メモ化済みの値をそのまま使う）。
        /// <see cref="_lobbyState"/> を読む箇所はすべてこれを通し、ほかの呼び出しが先に解決しているという順序に頼らない（#204 L-A）。
        /// </summary>
        private LobbyState ResolveSpawnedLobbyState()
        {
            if (_lobbyState == null || !_lobbyState.IsSpawned)
            {
                TryResolveLobbyState();
            }

            return _lobbyState != null && _lobbyState.IsSpawned ? _lobbyState : null;
        }
    }
}
