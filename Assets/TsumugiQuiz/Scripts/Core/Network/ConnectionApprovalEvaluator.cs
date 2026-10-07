using System;
using System.Globalization;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 接続承認の判定（純 C#）。Unity / NGO に依存しないので EditMode でそのままテストできる。
    /// NGO 側の <c>ConnectionApprovalCallback</c> からはバイト列と接続済み人数だけを渡す
    /// （<c>TsumugiQuiz.Network.ConnectionApprovalHandler</c>）。
    /// </summary>
    public static class ConnectionApprovalEvaluator
    {
        /// <summary>
        /// 承認ペイロードを検証する。
        /// 判定順序は「バージョン（先頭 2 バイト）→ 形式・サイズ → ビルドハッシュの形式 → ビルドの一致 → 名前 → 人数」
        /// （docs/network.md §2.3）。ビルドの一致を見るようになった #204 で、名前の検査をその後ろへ移した。
        ///
        /// バージョンを最初に見るのは、ペイロード形式そのものを変えた将来版のクライアントが
        /// 接続してきたときに「形式が不正」ではなく「バージョンが異なります」と正しく伝えるため。
        /// 先頭 2 バイトのレイアウトは今後も変えない前提とする。
        /// </summary>
        /// <param name="rawPayload">クライアントから届いたバイト列（信用しない）。</param>
        /// <param name="connectedPlayerCount">すでに参加しているプレイヤー数。</param>
        /// <param name="policy">判定条件。</param>
        /// <returns>判定結果。</returns>
        public static ConnectionApprovalDecision Evaluate(
            byte[] rawPayload,
            int connectedPlayerCount,
            ConnectionApprovalPolicy policy)
        {
            if (rawPayload == null || rawPayload.Length == 0)
            {
                return ConnectionApprovalDecision.Reject(
                    ConnectionRejectionReason.PayloadMissing,
                    ConnectionRejectionMessages.InvalidPayload,
                    default);
            }

            if (!ConnectionPayloadCodec.TryReadProtocolVersion(rawPayload, out var clientProtocolVersion))
            {
                return ConnectionApprovalDecision.Reject(
                    ConnectionRejectionReason.PayloadMalformed,
                    ConnectionRejectionMessages.InvalidPayload,
                    default);
            }

            if (clientProtocolVersion != policy.ExpectedProtocolVersion)
            {
                return ConnectionApprovalDecision.Reject(
                    ConnectionRejectionReason.ProtocolVersionMismatch,
                    ConnectionRejectionMessages.Create(
                        ConnectionRejectionReason.ProtocolVersionMismatch,
                        policy.ExpectedProtocolVersion,
                        clientProtocolVersion),
                    default);
            }

            if (!ConnectionPayloadCodec.TryDeserialize(rawPayload, out var payload, out var decodeReason))
            {
                return ConnectionApprovalDecision.Reject(
                    decodeReason,
                    ConnectionRejectionMessages.Create(decodeReason, policy.ExpectedProtocolVersion, clientProtocolVersion),
                    default);
            }

            if (!IsValidClientBuildHash(payload.ClientBuildHash))
            {
                return ConnectionApprovalDecision.Reject(
                    ConnectionRejectionReason.InvalidClientBuildHash,
                    ConnectionRejectionMessages.InvalidClientBuildHash,
                    payload);
            }

            // ビルドの一致（#204）。名前より先に見るのは、ビルドの違う相手には（名前の規則や文言が版で違っていても）
            // 常に「バージョンが異なります」を返すため。
            if (!string.Equals(payload.ClientBuildHash, policy.ExpectedClientBuildHash, StringComparison.Ordinal))
            {
                return ConnectionApprovalDecision.Reject(
                    ConnectionRejectionReason.ClientBuildMismatch,
                    ConnectionRejectionMessages.CreateBuildMismatch(policy.ExpectedClientBuildHash, payload.ClientBuildHash),
                    payload);
            }

            if (!PlayerNameValidator.TryNormalize(payload.PlayerName, out var normalizedName))
            {
                return ConnectionApprovalDecision.Reject(
                    ConnectionRejectionReason.InvalidPlayerName,
                    ConnectionRejectionMessages.InvalidPlayerName,
                    payload);
            }

            if (policy.MaxPlayers.HasValue && connectedPlayerCount >= policy.MaxPlayers.Value)
            {
                return ConnectionApprovalDecision.Reject(
                    ConnectionRejectionReason.RoomFull,
                    ConnectionRejectionMessages.RoomFull,
                    payload);
            }

            return ConnectionApprovalDecision.Approve(payload.WithPlayerName(normalizedName));
        }

        /// <summary>
        /// クライアントビルドハッシュの文字種の検査。制御文字は許可しない。長さは復号時にすでに検査済み。
        /// 空文字は形式としては許可するが、続くビルドの一致（#204）で空と一致するのは識別子が空のホスト、
        /// つまり Editor のホストだけ（<c>Application.buildGUID</c> は Editor では空）。ビルドした exe のホストは
        /// 空の識別子のクライアントを <see cref="ConnectionRejectionReason.ClientBuildMismatch"/> で拒否する。
        /// </summary>
        private static bool IsValidClientBuildHash(string clientBuildHash)
        {
            if (string.IsNullOrEmpty(clientBuildHash))
            {
                return true;
            }

            foreach (var c in clientBuildHash)
            {
                if (c <= '\u001F' || c == '\u007F')
                {
                    return false;
                }

                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Control)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
