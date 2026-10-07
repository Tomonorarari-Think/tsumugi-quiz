namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// <see cref="HostRole"/> と docs/room-settings.md §1 の <c>host.role</c> の文字列表現
    /// （<c>"player"</c> / <c>"moderator"</c>）の相互変換。
    /// 設定ファイル・<c>PlayerPrefs</c>・プリセット JSON で使うキー名をここに集約する。
    /// </summary>
    public static class HostRoles
    {
        /// <summary>
        /// 設定キー名（docs/room-settings.md §1 の <c>host.role</c>）。
        /// <c>RoomSettings</c>（#26 / #27）へ移行するまでの暫定保存先である
        /// <c>PlayerPrefs</c> のキーとしても同じ文字列を使う。
        /// </summary>
        public const string SettingsKey = "host.role";

        /// <summary><c>host.role</c> の既定値。</summary>
        public const string PlayerKey = "player";

        /// <summary><c>host.role</c> の司会専任。</summary>
        public const string ModeratorKey = "moderator";

        /// <summary>
        /// 設定値の文字列を <see cref="HostRole"/> に変換する。
        /// 未知の値・null は既定値（<see cref="HostRole.Player"/>）として扱う
        /// （docs/room-settings.md §5: 範囲外の値は既定値にクランプする）。
        /// </summary>
        public static HostRole Parse(string value)
            => TryParse(value, out var role) ? role : HostRole.Player;

        /// <summary>
        /// 設定値の文字列を <see cref="HostRole"/> に変換する。<c>"player"</c> / <c>"moderator"</c>
        /// のいずれとも一致しない場合（null・空・未知の値）は false を返し、
        /// <paramref name="role"/> には <see cref="HostRole.Player"/> を設定する
        /// （呼び出し側がクランプの警告を出すかどうかを判断できるようにするため、#26 統括判断 M13）。
        /// </summary>
        public static bool TryParse(string value, out HostRole role)
        {
            if (string.Equals(value, PlayerKey, System.StringComparison.Ordinal))
            {
                role = HostRole.Player;
                return true;
            }

            if (string.Equals(value, ModeratorKey, System.StringComparison.Ordinal))
            {
                role = HostRole.Moderator;
                return true;
            }

            role = HostRole.Player;
            return false;
        }

        /// <summary>設定値の文字列に変換する。</summary>
        public static string ToKey(HostRole role)
            => role == HostRole.Moderator ? ModeratorKey : PlayerKey;

        /// <summary>画面表示用の日本語表記。</summary>
        public static string ToDisplayName(HostRole role)
            => role == HostRole.Moderator ? "司会専任" : "ホスト（プレイヤー兼任）";
    }
}
