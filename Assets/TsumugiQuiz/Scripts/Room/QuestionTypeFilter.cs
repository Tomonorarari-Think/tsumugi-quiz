namespace TsumugiQuiz.Room
{
    /// <summary>
    /// 出題形式フィルタ（docs/room-settings.md §1「問題選択」の <c>questions.typeFilter</c>）。
    /// JSON 上は <c>"freeText"</c> / <c>"choice"</c> / <c>"both"</c> の文字列で表す。
    /// </summary>
    /// <remarks>
    /// <c>TsumugiQuiz.Questions.QuestionType</c> と同じ意味の値を持つが、
    /// <c>TsumugiQuiz.Room</c> は <c>Questions</c> を参照しない（docs/architecture.md §3 の依存方向）ため
    /// 「両方」を含む独立の列挙として定義する。対応付けは
    /// <c>TsumugiQuiz.Network.QuestionSelector</c> が行う。
    /// </remarks>
    public enum QuestionTypeFilter
    {
        /// <summary>両方（既定）。</summary>
        Both = 0,

        /// <summary>自由入力のみ。</summary>
        FreeText = 1,

        /// <summary>選択式のみ。</summary>
        Choice = 2,
    }
}
