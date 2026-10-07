using System;
using TsumugiQuiz.Core.Network;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="NetworkService"/> のうち、クライアント側の再接続トークン（issue #69）を扱う部分クラス。
    ///
    /// 送信側: <c>StartClient</c> が接続先（<c>アドレス:ポート</c>）で保存済みトークンを引き、
    /// 承認ペイロード（<see cref="ConnectionPayload"/>）に載せる。
    /// 受信側: 接続完了後にホストから届くトークン（<c>LobbyState.SessionTokenRpc</c>）を受け取り、
    /// <c>session-token.json</c> へ保存する。
    /// </summary>
    /// <remarks>
    /// ファイル I/O をここに置いているのは、ホストの識別子（接続先アドレスとポート）を知っているのが
    /// このクラスだけだからで、<c>LobbyState</c>（<see cref="Unity.Netcode.NetworkBehaviour"/>）には
    /// 保存の責務を持たせない。テストでは <see cref="SessionTokenStore"/> を差し替えて
    /// 実ファイルに触らせないようにできる。
    /// </remarks>
    public sealed partial class NetworkService
    {
        private SessionTokenStore _sessionTokenStore;
        private LobbyState _sessionTokenSource;
        private string _sessionTokenHostKey = string.Empty;

        /// <summary>
        /// 再接続トークンの保管庫。既定は <c>AppPaths.DataRoot/session-token.json</c>
        /// （<see cref="JsonSessionTokenStorage"/>、#71）。テストからは差し替えられる。
        /// </summary>
        public SessionTokenStore SessionTokenStore
        {
            get => _sessionTokenStore ??= new SessionTokenStore(new JsonSessionTokenStorage());
            set => _sessionTokenStore = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// 現在の接続先に対する保存済みトークンを引く（無ければ <see cref="SessionToken.None"/>）。
        /// </summary>
        /// <param name="address">接続先アドレス。</param>
        /// <param name="port">接続先ポート。</param>
        private SessionToken LoadReconnectToken(string address, ushort port)
        {
            if (!SessionTokenHostKey.TryCreate(address, port, out var hostKey))
            {
                _sessionTokenHostKey = string.Empty;
                return SessionToken.None;
            }

            _sessionTokenHostKey = hostKey;

            try
            {
                return SessionTokenStore.TryGet(hostKey, DateTime.UtcNow, out var token)
                    ? token
                    : SessionToken.None;
            }
            catch (Exception ex)
            {
                // 読み出しに失敗しても接続そのものは続けられる（新規参加になるだけ）。
                Debug.LogWarning($"[NetworkService] 再接続トークンを読み出せませんでした: {ex.Message}");
                return SessionToken.None;
            }
        }

        /// <summary>
        /// 自分（クライアント）の接続が完了したときに、トークンの配布を受け取れるようにする。
        /// サーバーから届く RPC は <c>LobbyState</c> が受けるので、そこから購読する。
        /// </summary>
        private void AttachSessionTokenListener()
        {
            DetachSessionTokenListener();

            var lobby = LobbyState.Find(_networkManager);
            if (lobby == null)
            {
                // 通常の経路では、シーン同期が完了した時点で LobbyState はスポーン済み（NGO 2.13.2 の
                // NetworkSceneManager は同期完了後に OnClientConnected を発火する）。
                // ロビーを立てないホスト（テストの土台や将来の検証ツール）に繋いだ場合はここに来る。
                // 再接続トークンを保存できないだけで接続は続けられるので、警告ではなく記録に留める。
                Debug.Log("[NetworkService] ホストに LobbyState が無いため、再接続トークンは保存されません。");
                return;
            }

            _sessionTokenSource = lobby;
            lobby.SessionTokenReceived += HandleSessionTokenReceived;

            // 購読前に届いていた場合の取りこぼし防止。
            if (lobby.LastReceivedSessionToken.HasValue)
            {
                HandleSessionTokenReceived(lobby.LastReceivedSessionToken);
            }
        }

        /// <summary>トークン配布の購読を解除する。</summary>
        private void DetachSessionTokenListener()
        {
            if (_sessionTokenSource == null)
            {
                return;
            }

            _sessionTokenSource.SessionTokenReceived -= HandleSessionTokenReceived;
            _sessionTokenSource = null;
        }

        /// <summary>受け取ったトークンを保存する（失敗しても接続は続ける）。</summary>
        private void HandleSessionTokenReceived(SessionToken token)
        {
            if (!token.HasValue || !SessionTokenHostKey.IsValid(_sessionTokenHostKey))
            {
                return;
            }

            try
            {
                if (SessionTokenStore.Save(_sessionTokenHostKey, token, DateTime.UtcNow))
                {
                    Debug.Log("[NetworkService] 再接続トークンを保存しました（次回は同じ席へ復帰できます）。");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkService] 再接続トークンを保存できませんでした: {ex.Message}");
            }
        }
    }
}
