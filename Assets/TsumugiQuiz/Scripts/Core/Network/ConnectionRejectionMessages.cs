using System.Globalization;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 拒否理由から、クライアントに表示する定型文を作る。
    /// 内部情報（例外メッセージ・スタックトレース・IP）は含めない（docs/network.md §9）。
    /// </summary>
    public static class ConnectionRejectionMessages
    {
        /// <summary>接続情報そのものが壊れている場合の定型文。</summary>
        public const string InvalidPayload = "接続情報が正しくありません。";

        /// <summary>
        /// プレイヤー名が不正な場合の定型文。名前の規則の要約は <see cref="PlayerNameValidator.RuleSummary"/> を 1 か所の出所にする（#209）。
        /// Join 画面の入力チェックもこの文言を出す。
        /// </summary>
        /// <remarks>
        /// ホストが送る拒否理由でもあり、クライアントの <see cref="DisconnectReasonLocalizer"/> は自前の文言との完全一致でそのまま出す。
        /// 配布した後にこの文言を変えると、変える前の版のクライアントでは一覧に無い理由になり、汎用の文言
        /// （<see cref="JoinStatusMessages.DisconnectedWithoutReason"/>）が出る。#209 で変えた時点では、#208 を含む版はまだ配布していない（main にマージしておらず、タグも無い）。
        /// </remarks>
        public const string InvalidPlayerName = "プレイヤー名が正しくありません（" + PlayerNameValidator.RuleSummary + "）。";

        /// <summary>クライアントビルドハッシュが不正な場合の定型文。</summary>
        public const string InvalidClientBuildHash = "接続情報が正しくありません。";

        /// <summary>再接続トークンの形式が不正な場合の定型文（#69。内部の詳細は返さない）。</summary>
        public const string InvalidReconnectToken = "接続情報が正しくありません。";

        /// <summary>満室の場合の定型文。</summary>
        public const string RoomFull = "満室のため参加できません。";

        /// <summary>ゲーム進行中で途中参加が許可されていない場合の定型文（#7）。</summary>
        public const string GameInProgress = "ゲームが進行中のため参加できません。ホストが途中参加を許可するのを待ってください。";

        /// <summary>プレイヤー名が既に使われている場合の定型文（#7）。</summary>
        public const string DuplicatePlayerName = "同じ名前のプレイヤーがすでに参加しています。別の名前でお試しください。";

        /// <summary>空きが切断者の復帰用に確保された席だけだった場合の定型文（#7 Q3）。</summary>
        public const string SeatReserved = "切断したプレイヤーの席を確保中です（最大 60 秒）。しばらく待ってからもう一度お試しください。";

        /// <summary>ロビーの準備ができていない場合の定型文（#7）。</summary>
        public const string LobbyNotReady = "ホストのロビーがまだ準備中です。少し待ってからもう一度お試しください。";

        /// <summary>
        /// バージョンが一致しない場合の書式（{0} = ホスト、{1} = クライアント）。プロトコルバージョンの不一致
        /// （<see cref="ConnectionRejectionReason.ProtocolVersionMismatch"/>）とビルドの不一致
        /// （<see cref="ConnectionRejectionReason.ClientBuildMismatch"/>、#204。数字は <see cref="BuildDisplayNumber"/> の番号）の両方で使う。
        /// バージョンの違うホストから届くので、受信側（<see cref="DisconnectReasonLocalizer"/>）はこの書式との一致（数字は ASCII 1〜5 桁）で
        /// 自前の理由と判定する（#208）。書式を変えると、古い版のクライアントには汎用の文言で表示されるので変えないこと。
        /// #204 以降のホストはビルドの違う相手を承認の最初の方（名前の検査より前）で必ずこの文言で拒否するので、ほかの理由は
        /// 同じビルドの相手にしか届かない。#204 より前のホストは、プロトコルバージョンが同じならビルドの違う相手も承認するので、
        /// そのホストからはほかの理由も届きうる（相手の版で文言が違えば汎用の文言で表示される）。
        /// </summary>
        public const string ProtocolVersionMismatchFormat = "バージョンが異なります（ホスト: {0} / あなた: {1}）。";

        /// <summary>
        /// 拒否理由に対応する表示用メッセージを返す。
        /// </summary>
        /// <param name="reason">拒否理由。</param>
        /// <param name="hostVersion">
        /// ホストのバージョン。<see cref="ConnectionRejectionReason.ProtocolVersionMismatch"/> ならプロトコルバージョン、
        /// <see cref="ConnectionRejectionReason.ClientBuildMismatch"/> ならビルドの番号（<see cref="BuildDisplayNumber"/>）。
        /// ほかの理由では使わない。
        /// </param>
        /// <param name="clientVersion">クライアントのバージョン（意味は <paramref name="hostVersion"/> と同じ。不明なら 0）。</param>
        public static string Create(ConnectionRejectionReason reason, ushort hostVersion, ushort clientVersion)
        {
            switch (reason)
            {
                case ConnectionRejectionReason.None:
                    return string.Empty;
                case ConnectionRejectionReason.ProtocolVersionMismatch:
                case ConnectionRejectionReason.ClientBuildMismatch:
                    return FormatVersionMismatch(hostVersion, clientVersion);
                case ConnectionRejectionReason.InvalidPlayerName:
                    return InvalidPlayerName;
                case ConnectionRejectionReason.InvalidClientBuildHash:
                    return InvalidClientBuildHash;
                case ConnectionRejectionReason.InvalidReconnectToken:
                    return InvalidReconnectToken;
                case ConnectionRejectionReason.RoomFull:
                    return RoomFull;
                case ConnectionRejectionReason.GameInProgress:
                    return GameInProgress;
                case ConnectionRejectionReason.DuplicatePlayerName:
                    return DuplicatePlayerName;
                case ConnectionRejectionReason.SeatReserved:
                    return SeatReserved;
                case ConnectionRejectionReason.LobbyNotReady:
                    return LobbyNotReady;
                default:
                    return InvalidPayload;
            }
        }

        /// <summary>
        /// ビルドが一致しないとき（<see cref="ConnectionRejectionReason.ClientBuildMismatch"/>、#204）の表示用メッセージを返す。
        /// 識別子は <see cref="BuildDisplayNumber.ForMismatch"/> で番号に写してから書式に入れる（2 つの番号は必ず違う）。
        /// </summary>
        /// <param name="hostBuildHash">ホストのビルドの識別子。</param>
        /// <param name="clientBuildHash">クライアントのビルドの識別子。</param>
        public static string CreateBuildMismatch(string hostBuildHash, string clientBuildHash)
        {
            var numbers = BuildDisplayNumber.ForMismatch(hostBuildHash, clientBuildHash);
            return FormatVersionMismatch(numbers.Host, numbers.Client);
        }

        private static string FormatVersionMismatch(ushort hostVersion, ushort clientVersion)
            => string.Format(CultureInfo.InvariantCulture, ProtocolVersionMismatchFormat, hostVersion, clientVersion);
    }
}
