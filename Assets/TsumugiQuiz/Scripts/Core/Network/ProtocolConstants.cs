namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// ホストとクライアントの間で一致していなければならないプロトコル上の定数。
    /// Unity API に依存しないため <c>TsumugiQuiz.Core</c> に置き、Network 層から参照する
    /// （docs/network.md §2.3、docs/architecture.md §3）。
    /// </summary>
    public static class ProtocolConstants
    {
        /// <summary>
        /// アプリ独自のプロトコルバージョン。承認ペイロード（<see cref="ConnectionPayload"/>）の先頭 2 バイトに載せ、
        /// ホストと完全一致しないクライアントは、ペイロードの残りを読む前に拒否する（docs/network.md §2.3 の 1 と「バージョンとビルドの一致」）。
        /// NGO の <c>NetworkConfig.ProtocolVersion</c> とは別物（あちらは ushort 1 個しか持てず、
        /// 問題フォーマットのバージョンまで表現できないため独自に持つ）。
        ///
        /// <para>
        /// <b>上げる場面（#204 で統一）</b>: 承認ペイロードの形式（先頭 2 バイトより後ろの並び・長さの規則）か、
        /// 承認で照合する項目の意味を変えたときだけ上げる。ホストはペイロードを読めないと他の項目を照合できないため。
        /// ゲーム中の RPC・<c>NetworkVariable</c>・enum の値・問題配信の形式の変更では上げない。それらの食い違いは、
        /// ビルドの一致の照合（<see cref="ConnectionApprovalPolicy.ExpectedClientBuildHash"/>、#204）が
        /// ビルドの違う相手をすべて拒否することで防ぐ。先頭 2 バイトのレイアウトと、拒否の文言の書式
        /// （<see cref="ConnectionRejectionMessages.ProtocolVersionMismatchFormat"/>）は変えない。
        /// </para>
        ///
        /// 履歴:
        /// <list type="bullet">
        ///   <item><description>1 — 初版（#2）。protocolVersion / playerName / clientBuildHash</description></item>
        ///   <item><description>2 — 再接続トークンの区画を追加（#69）</description></item>
        ///   <item><description>
        ///   3 — clientBuildHash の一致を必須にした（#204）。形式は 2 と同じ。照合する項目の意味が変わったので上げた。
        ///   ビルドの一致を見ない #204 より前のホスト（2）に、#204 以降のクライアントが入れないようにするため
        ///   （逆向きの #204 より前のクライアントは、上げなくてもビルドの不一致で拒否される）
        ///   </description></item>
        /// </list>
        /// </summary>
        public const ushort Version = 3;

        /// <summary>承認ペイロード全体の上限バイト数（docs/network.md §9）。</summary>
        public const int MaxApprovalPayloadBytes = 256;

        /// <summary>プレイヤー名の最小文字数（コードポイント単位）。</summary>
        public const int MinPlayerNameLength = 1;

        /// <summary>プレイヤー名の最大文字数（コードポイント単位、docs/network.md §9）。</summary>
        public const int MaxPlayerNameLength = 16;

        /// <summary>クライアントビルドハッシュの上限バイト数（UTF-8 エンコード後）。</summary>
        public const int MaxClientBuildHashBytes = 64;

        /// <summary>
        /// 再接続トークンのバイト数（#69）。承認ペイロードに載せられるのはこの長さちょうどか 0（無し）だけ。
        /// </summary>
        public const int ReconnectTokenBytes = SessionToken.ByteCount;
    }
}
