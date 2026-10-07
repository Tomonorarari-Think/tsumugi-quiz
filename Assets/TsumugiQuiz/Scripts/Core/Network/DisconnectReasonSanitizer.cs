namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// ホストから届く切断理由（NGO の <c>NetworkManager.DisconnectReason</c>。承認拒否の Reason もここに入る）を、
    /// 画面・ログに出せる形に整える（issue #206、docs/network.md §9）。#208 からは、画面に出すのは
    /// <see cref="DisconnectReasonLocalizer"/> が対応づけた文言だけで、元の理由は <see cref="SanitizeForLog"/> で整えて詳細ログに残す。
    /// 切断理由はホストが自由に決められる文字列で、改変されたホストなら長さも中身も任意になる。
    /// 規則は <see cref="DisplayTextSanitizer"/>（改行・タブは空白 1 つ、制御文字・単独のサロゲート・見えない文字（双方向の制御・幅のない区切りなど、#209）は除去、
    /// 結合記号は基底文字 1 つあたり 4 個まで、前後の空白を除く、<see cref="MaxLength"/> コードポイントを超えたら「…」で切り詰め）。
    /// 純粋関数で、Unity API に依存しない。
    /// </summary>
    public static class DisconnectReasonSanitizer
    {
        /// <summary>
        /// 切断理由の上限（コードポイントの数。<see cref="Sanitize"/> が使う）。このアプリが送る理由は最長で 50 文字
        /// （<see cref="ConnectionRejectionMessages.InvalidPlayerName"/>。#209 で名前の規則の要約を入れて長くなった。それまでは
        /// <see cref="ConnectionRejectionMessages.SeatReserved"/> の 48 文字）で、その 2 倍の余裕を取った（自前の文言は上限の半分以下に
        /// 収める。<c>DisconnectReasonSanitizerTests.Sanitize_AllReasonsThisAppSends_AreUnchanged</c>）。
        /// Join 画面の表示欄（幅 351px）で 6 行になる（#206 の実測）。
        /// </summary>
        public const int MaxLength = 100;

        /// <summary>
        /// 元の理由を詳細ログに残すときの上限（コードポイントの数、#208）。NGO がクライアント側で組み立てる診断文字列
        /// （<c>[Disconnect Event][Client-1][TransportClientId-…][TransportShutdown] …</c>）は #208 の実測で 144 文字あり、
        /// ID が最大桁でも 200 文字に届かない。その全文を残せるよう、表示の上限（<see cref="MaxLength"/>）より大きくした。
        /// </summary>
        public const int MaxLogLength = 300;

        /// <summary>
        /// ログに書くとき「&lt;」の代わりに置く文字（U+2039 SINGLE LEFT-POINTING ANGLE QUOTATION MARK、#208）。
        /// Unity Editor のコンソールはログのリッチテキストのタグを解釈するので、ホストが送った文字列でコンソールの表示を崩させない。
        /// </summary>
        public const char LogTagOpenerReplacement = '‹';

        /// <summary>切り詰めたときに末尾へ付ける記号。</summary>
        public const string Ellipsis = DisplayTextSanitizer.Ellipsis;

        /// <summary>
        /// 切断理由を画面に出せる形に整える（#206）。
        /// #208 から、本体の表示経路（<c>NetworkService.DisconnectedFromHost</c>）では使っていない（画面に出すのは
        /// <see cref="DisconnectReasonLocalizer"/> が対応づけた自前の文言だけ）。それでも残しているのは、ホストから届いた文字列を
        /// 表示したくなった場合に通すための後方互換と多層防御のためで、自前の文言がこれで変わらないこともテストで確かめている。
        /// </summary>
        /// <param name="reason">受け取った切断理由。null 可。</param>
        /// <returns>整えた切断理由。何も残らなければ空文字。</returns>
        public static string Sanitize(string reason) => DisplayTextSanitizer.Sanitize(reason, MaxLength);

        /// <summary>
        /// 元の理由を詳細ログに残せる形に整える（#208）。規則は <see cref="Sanitize"/> と同じで、上限だけが
        /// <see cref="MaxLogLength"/>。改行・制御文字を除くので、ログの行を偽装されない。
        /// さらに「&lt;」を <see cref="LogTagOpenerReplacement"/> に置き換え、Editor のコンソールにタグとして解釈させない
        /// （1 文字を 1 文字に置き換えるので長さは変わらない）。
        /// </summary>
        /// <param name="reason">受け取った切断理由。null 可。</param>
        /// <returns>整えた切断理由。何も残らなければ空文字。</returns>
        public static string SanitizeForLog(string reason)
            => DisplayTextSanitizer.Sanitize(reason, MaxLogLength).Replace('<', LogTagOpenerReplacement);
    }
}
