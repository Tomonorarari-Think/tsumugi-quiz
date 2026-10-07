namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 切断理由をどう扱ったか（<see cref="DisconnectReasonLocalizer"/>、issue #208）。ログの出し分けに使う。
    /// </summary>
    public enum DisconnectReasonCategory
    {
        /// <summary>理由が無い（空・空白だけ）。汎用の文言にした。</summary>
        None = 0,

        /// <summary>このアプリが送る日本語の理由（拒否理由・承認後の切断理由）。そのまま表示する。</summary>
        AppMessage = 1,

        /// <summary>NGO・旧版のこのアプリが送る既知の英語の理由。日本語の文言に対応づけた。</summary>
        KnownEnglish = 2,

        /// <summary>NGO がクライアント側で組み立てる診断文字列（<c>[Disconnect Event]…</c>）。イベントの種類で対応づけた。</summary>
        TransportEvent = 3,

        /// <summary>どれにも当てはまらない理由。そのまま出さず、汎用の文言にした。</summary>
        Unknown = 4,
    }
}
