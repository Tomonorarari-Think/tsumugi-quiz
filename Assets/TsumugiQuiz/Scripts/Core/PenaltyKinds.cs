namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <c>score.penaltyType</c>（docs/room-settings.md §1「得点」）の文字列キーと
    /// <see cref="PenaltyKind"/> の相互変換（#27）。
    /// </summary>
    /// <remarks>
    /// プリセット JSON（<c>TsumugiQuiz.Room.RoomSettingsValidator</c>）と
    /// ルーム設定の同期ペイロード（<c>TsumugiQuiz.Network.RoomSettingsPayload</c>）の両方が
    /// 同じ文字列を使うため、キーの定義をここ 1 か所に集約する（#26 の <see cref="Network.HostRoles"/> と同じ方針）。
    /// </remarks>
    public static class PenaltyKinds
    {
        /// <summary>ルーム設定のキー名。</summary>
        public const string SettingsKey = "score.penaltyType";

        /// <summary><see cref="PenaltyKind.SkipNext"/> の文字列キー。</summary>
        public const string SkipNextKey = "skipNext";

        /// <summary><see cref="PenaltyKind.MinusPoints"/> の文字列キー。</summary>
        public const string MinusPointsKey = "minusPoints";

        /// <summary><see cref="PenaltyKind.None"/> の文字列キー。</summary>
        public const string NoneKey = "none";

        /// <summary>
        /// 文字列キーを <see cref="PenaltyKind"/> へ変換する。
        /// </summary>
        /// <param name="value">文字列キー。</param>
        /// <param name="kind">変換結果。失敗時は <see cref="ScoreRules.DefaultPenaltyKind"/>。</param>
        /// <returns>既知のキーなら true。</returns>
        public static bool TryParse(string value, out PenaltyKind kind)
        {
            switch (value)
            {
                case SkipNextKey:
                    kind = PenaltyKind.SkipNext;
                    return true;
                case MinusPointsKey:
                    kind = PenaltyKind.MinusPoints;
                    return true;
                case NoneKey:
                    kind = PenaltyKind.None;
                    return true;
                default:
                    kind = ScoreRules.DefaultPenaltyKind;
                    return false;
            }
        }

        /// <summary>
        /// <see cref="PenaltyKind"/> を文字列キーへ変換する。未定義値は既定値のキーにする。
        /// </summary>
        /// <param name="kind">ペナルティ種別。</param>
        /// <returns>文字列キー。</returns>
        public static string ToKey(PenaltyKind kind)
        {
            switch (kind)
            {
                case PenaltyKind.MinusPoints:
                    return MinusPointsKey;
                case PenaltyKind.None:
                    return NoneKey;
                case PenaltyKind.SkipNext:
                    return SkipNextKey;
                default:
                    // 既定値（ScoreRules.DefaultPenaltyKind）のキー。再帰させない。
                    return SkipNextKey;
            }
        }
    }
}
