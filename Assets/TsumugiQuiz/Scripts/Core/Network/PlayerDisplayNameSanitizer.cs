namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 名簿（ホストから同期される <c>PlayerEntry</c>）の名前を、表示の直前に整える（issue #206、docs/network.md §9）。
    /// 名前は承認時にホストが検証する（<see cref="PlayerNameValidator"/>）が、改変されたホストは検証していない名前を
    /// 名簿に載せられる。クライアントは名前を捨てたり伏せたりせず、表示用に整えるだけにする（名簿のデータは変えない）。
    /// 規則は <see cref="DisplayTextSanitizer"/> と同じ（改行・行区切り類と半角空白の連続は空白 1 つにまとめる、制御文字・
    /// 単独のサロゲート・見えない文字（双方向の制御・幅のない区切りなど）は除く、結合記号は基底文字 1 つあたり 4 個まで、
    /// 前後の空白を除く）。
    /// 長さは承認時と同じくコードポイントで数え、上限 <see cref="MaxLength"/> を超えたら「…」で切り詰める。
    /// 承認を通った名前（16 コードポイント以内）は、整えても長さで切り詰められることはない。ただし、連続した空白や
    /// 行区切り類（U+2028 / U+2029）は承認を通るが、表示では空白 1 つにまとめる。見えない文字と 5 個目以降の結合記号は、
    /// 承認時にも同じ規則（<see cref="TextRules.RemoveHiddenCharacters(string)"/>）で拒否する（#209）ので、承認を通った名前が
    /// 表示で変わるのは空白の扱いだけである。純粋関数で、Unity API に依存しない。
    /// </summary>
    public static class PlayerDisplayNameSanitizer
    {
        /// <summary>表示する名前の上限（コードポイントの数）。承認時の上限と同じ。</summary>
        public const int MaxLength = ProtocolConstants.MaxPlayerNameLength;

        /// <summary>
        /// 名前を表示用に整える。
        /// </summary>
        /// <param name="name">名簿の名前。null 可。</param>
        /// <returns>整えた名前。何も残らなければ空文字。</returns>
        public static string Sanitize(string name) => DisplayTextSanitizer.Sanitize(name, MaxLength);

        /// <summary>
        /// 画面に出す名前。整えた名前が空になったとき（名前が制御文字だけだった場合など）は
        /// <see cref="FallbackName"/> を返す。名簿・参加者パネル・回答者の表示・Result で同じ表示にするため。
        /// </summary>
        /// <param name="name">名簿の名前。null 可。</param>
        /// <param name="clientId">そのプレイヤーのクライアント ID。</param>
        public static string ForDisplay(string name, ulong clientId)
        {
            var sanitized = Sanitize(name);
            return sanitized.Length == 0 ? FallbackName(clientId) : sanitized;
        }

        /// <summary>名前が分からない・空のときの表示（<c>プレイヤー{clientId}</c>）。</summary>
        /// <param name="clientId">クライアント ID。</param>
        public static string FallbackName(ulong clientId)
            => "プレイヤー" + clientId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
