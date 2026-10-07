namespace TsumugiQuiz.Room
{
    /// <summary>
    /// <c>questions.typeFilter</c>（docs/room-settings.md §1「問題選択」）の文字列キーと
    /// <see cref="QuestionTypeFilter"/> の相互変換（#27）。
    /// </summary>
    /// <remarks>
    /// プリセット JSON（<see cref="RoomSettingsValidator"/>）とルーム設定の同期ペイロード
    /// （<c>TsumugiQuiz.Network.RoomSettingsPayload</c>）の両方が同じ文字列を使うため、
    /// キーの定義をここ 1 か所に集約する。
    /// </remarks>
    public static class QuestionTypeFilters
    {
        /// <summary>ルーム設定のキー名。</summary>
        public const string SettingsKey = "questions.typeFilter";

        /// <summary><see cref="QuestionTypeFilter.Both"/> の文字列キー。</summary>
        public const string BothKey = "both";

        /// <summary><see cref="QuestionTypeFilter.FreeText"/> の文字列キー。</summary>
        public const string FreeTextKey = "freeText";

        /// <summary><see cref="QuestionTypeFilter.Choice"/> の文字列キー。</summary>
        public const string ChoiceKey = "choice";

        /// <summary>
        /// 文字列キーを <see cref="QuestionTypeFilter"/> へ変換する。
        /// </summary>
        /// <param name="value">文字列キー。</param>
        /// <param name="filter">変換結果。失敗時は <see cref="QuestionSelectionSettings.DefaultTypeFilter"/>。</param>
        /// <returns>既知のキーなら true。</returns>
        public static bool TryParse(string value, out QuestionTypeFilter filter)
        {
            switch (value)
            {
                case BothKey:
                    filter = QuestionTypeFilter.Both;
                    return true;
                case FreeTextKey:
                    filter = QuestionTypeFilter.FreeText;
                    return true;
                case ChoiceKey:
                    filter = QuestionTypeFilter.Choice;
                    return true;
                default:
                    filter = QuestionSelectionSettings.DefaultTypeFilter;
                    return false;
            }
        }

        /// <summary>
        /// <see cref="QuestionTypeFilter"/> を文字列キーへ変換する。未定義値は既定値のキーにする。
        /// </summary>
        /// <param name="filter">出題形式フィルタ。</param>
        /// <returns>文字列キー。</returns>
        public static string ToKey(QuestionTypeFilter filter)
        {
            switch (filter)
            {
                case QuestionTypeFilter.FreeText:
                    return FreeTextKey;
                case QuestionTypeFilter.Choice:
                    return ChoiceKey;
                case QuestionTypeFilter.Both:
                    return BothKey;
                default:
                    return BothKey;
            }
        }
    }
}
