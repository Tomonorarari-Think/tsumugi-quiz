using System;
using System.Security.Cryptography;
using TsumugiQuiz.Core.Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="LobbyState"/> のうち、再接続トークンの発行・配布・受信を担当する部分クラス
    /// （issue #69、docs/network.md §2.3 の K-N1）。
    ///
    /// サーバーは承認のたびに 128bit の乱数トークンを発行し、名簿に新しいエントリを作った場合だけ
    /// （＝新規参加として席を得た場合だけ）接続完了後にその 1 人へ送る。名簿（<see cref="LobbyRoster"/>）には
    /// ハッシュだけを残し、再接続は「プレイヤー名の一致 + トークンの一致」で判定する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// NGO 2.13.2 の <c>NetworkManager.ConnectionApprovalResponse</c>（<c>Runtime/Core/NetworkManager.cs</c> L803）には
    /// <c>Approved</c> / <c>CreatePlayerObject</c> / <c>PlayerPrefabHash</c> / <c>Position</c> / <c>Rotation</c> /
    /// <c>Pending</c> / <c>Reason</c> しか無く、承認応答に任意のペイロードを載せる口が無い
    /// （<c>Reason</c> は <c>NetworkConnectionManager.HandleConnectionDisconnect</c> でしか使われず、
    /// 承認時にクライアントへ届く <c>ConnectionApprovedMessage</c> にもアプリ独自の領域は無い）。
    /// そのため issue の代替案どおり、承認直後（接続完了時）に
    /// <c>SendTo.SpecifiedInParams</c> の RPC で 1 人へ送る方式にした。
    /// </para>
    /// <para>
    /// 送信はサーバー側の <c>OnClientConnectedCallback</c>（= クライアントのシーン同期完了後）で行うので、
    /// 受け手側では既に <see cref="LobbyState"/> がスポーン済みになっている。
    /// </para>
    /// <para>
    /// 受け取ったトークンをファイルへ保存するのはクライアント側の <see cref="NetworkService"/> の責務
    /// （<c>NetworkService.SessionToken.cs</c>）。<see cref="LobbyState"/> はファイル I/O を持たない。
    /// </para>
    /// </remarks>
    public sealed partial class LobbyState
    {
        /// <summary>トークン発行用の乱数源（サーバーのみ。遅延生成して <c>ShutdownServer</c> で破棄する）。</summary>
        private RandomNumberGenerator _tokenGenerator;

        /// <summary>
        /// クライアントが最後に受け取った再接続トークン（#69）。サーバーでは常に
        /// <see cref="SessionToken.None"/>。保存済みトークンの取り出しは <see cref="NetworkService"/> が行う。
        /// </summary>
        public SessionToken LastReceivedSessionToken { get; private set; }

        /// <summary>
        /// クライアントがホストから再接続トークンを受け取ったとき（クライアントのみ）。
        /// 購読が遅れた場合に備えて <see cref="LastReceivedSessionToken"/> にも残してある。
        /// </summary>
        public event Action<SessionToken> SessionTokenReceived;

        /// <summary>
        /// 新しい再接続トークンを発行する（サーバーのみ）。
        /// </summary>
        private SessionToken IssueSessionToken()
        {
            _tokenGenerator ??= RandomNumberGenerator.Create();
            return SessionToken.CreateRandom(_tokenGenerator);
        }

        /// <summary>乱数源を破棄する（<c>ShutdownServer</c> から呼ぶ）。</summary>
        private void DisposeTokenGenerator()
        {
            _tokenGenerator?.Dispose();
            _tokenGenerator = null;
        }

        /// <summary>
        /// 発行したトークンを対象のクライアント 1 人だけへ送る（サーバーのみ）。
        /// トークン無しのときは何もしない。
        /// </summary>
        /// <param name="clientId">送信先のクライアント ID。</param>
        /// <param name="token">発行したトークン。</param>
        private void SendSessionTokenTo(ulong clientId, SessionToken token)
        {
            if (!IsServer || !IsSpawned || !token.HasValue)
            {
                return;
            }

            if (clientId == NetworkManager.ServerClientId)
            {
                // ホスト自身は再接続しない（ホストが落ちればセッションごと終わる）。
                return;
            }

            SessionTokenRpc(new FixedString64Bytes(token.ToHex()), RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        /// <summary>
        /// 再接続トークンの配布（サーバー → 指定クライアント 1 人）。
        /// 16 進 32 文字で送り、受け側でも長さ・文字種を検証する（docs/network.md §9: 受信データは信用しない）。
        /// </summary>
        /// <param name="tokenHex">トークンの 16 進表記（32 文字）。</param>
        /// <param name="rpcParams">送信先（NGO が埋める受信情報）。</param>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void SessionTokenRpc(FixedString64Bytes tokenHex, RpcParams rpcParams = default)
        {
            if (IsServer)
            {
                // ホスト自身には送っていないので通常は来ない。来ても名簿側が権威なので無視する。
                return;
            }

            if (!SessionToken.TryParseHex(tokenHex.ToString(), out var token))
            {
                Debug.LogWarning("[LobbyState] 形式が不正な再接続トークンを破棄しました。");
                return;
            }

            LastReceivedSessionToken = token;
            SessionTokenReceived?.Invoke(token);
        }
    }
}
