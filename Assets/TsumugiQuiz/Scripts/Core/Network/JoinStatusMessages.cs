namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// Join View（参加コード入力画面）の接続進捗表示に使う定型文（issue #6 レビュー M-7）。
    /// 文言をここに集約することで、実装とテストが同じ定数を参照できるようにする（L-11）。
    /// サーバーから届く拒否理由（<see cref="ConnectionRejectionMessages"/>）はそのまま表示するため、
    /// ここには含めない。
    /// </summary>
    public static class JoinStatusMessages
    {
        /// <summary>接続開始直後に表示する進捗文言。</summary>
        public const string Connecting = "接続中…";

        /// <summary>UI 側のタイムアウト（既定 10 秒）に達したときの文言。</summary>
        public const string Timeout = "接続がタイムアウトしました。参加コードや接続先を確認してください。";

        /// <summary>Transport 層の失敗（NGO の <c>OnTransportFailure</c>）を受けたときの文言。</summary>
        public const string TransportFailure = "接続に失敗しました（ネットワークの問題が発生しました）。";

        /// <summary>
        /// 拒否理由（Reason）が付かずに切断されたときの文言（L-3）。
        /// 承認拒否は <see cref="TsumugiQuiz.Network.ConnectionApprovalHandler"/> が必ず Reason を
        /// 添えて拒否する（<see cref="ConnectionRejectionMessages"/>）ため、Reason が空のままここに
        /// 来るのは「承認より前の切断」など拒否以外のケースであり、「拒否されました」と表示すると
        /// 誤解を招く。中立的に「切断されました」とだけ伝える。
        /// </summary>
        public const string DisconnectedWithoutReason = "ホストとの接続が切断されました。";

        /// <summary>NetworkService が利用できない（Boot シーンを経由していない）ときの文言（L-4）。</summary>
        public const string NetworkServiceUnavailable = "ネットワーク機能を初期化できませんでした。アプリを起動し直してください。";
    }
}
