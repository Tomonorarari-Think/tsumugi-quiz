namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 接続承認の判定結果（不変）。
    /// </summary>
    public readonly struct ConnectionApprovalDecision
    {
        private ConnectionApprovalDecision(
            bool approved,
            ConnectionRejectionReason reason,
            string reasonMessage,
            ConnectionPayload payload)
        {
            Approved = approved;
            Reason = reason;
            ReasonMessage = reasonMessage;
            Payload = payload;
        }

        /// <summary>承認したか。</summary>
        public bool Approved { get; }

        /// <summary>拒否理由（承認時は <see cref="ConnectionRejectionReason.None"/>）。</summary>
        public ConnectionRejectionReason Reason { get; }

        /// <summary>クライアントに返す定型文（承認時は空文字）。</summary>
        public string ReasonMessage { get; }

        /// <summary>
        /// 検証済みのペイロード。承認時はプレイヤー名が正規化済み。
        /// 拒否時は復号できたところまでの内容（復号自体に失敗した場合は既定値）。
        /// </summary>
        public ConnectionPayload Payload { get; }

        /// <summary>承認結果を作る。</summary>
        public static ConnectionApprovalDecision Approve(ConnectionPayload payload)
            => new ConnectionApprovalDecision(true, ConnectionRejectionReason.None, string.Empty, payload);

        /// <summary>拒否結果を作る。</summary>
        public static ConnectionApprovalDecision Reject(
            ConnectionRejectionReason reason,
            string reasonMessage,
            ConnectionPayload payload)
            => new ConnectionApprovalDecision(false, reason, reasonMessage ?? string.Empty, payload);
    }
}
