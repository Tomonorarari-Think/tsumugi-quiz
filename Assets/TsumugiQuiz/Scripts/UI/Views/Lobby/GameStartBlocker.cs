namespace TsumugiQuiz.UI.Views.Lobby
{
    /// <summary>
    /// ロビーの「ゲーム開始」を進められない理由（issue #95）。
    /// ユーザーへ出す文言は <see cref="GameStartPlan.FailureMessage"/> が持ち、
    /// 本列挙は「どの種類の失敗か」をテスト・ログから判別するために使う。
    /// </summary>
    public enum GameStartBlocker
    {
        /// <summary>問題なく開始できる。</summary>
        None = 0,

        /// <summary>問題フォルダに出題できる問題セットが 1 件も無い。</summary>
        NoQuestionSets = 1,

        /// <summary>
        /// 問題フォルダの読み込み自体に失敗した（フォルダが無い・列挙に失敗した・
        /// 全セットが検証エラーでスキップされた）。
        /// </summary>
        LoadFailed = 2,

        /// <summary>問題セットはあるが、ルーム設定の絞り込み条件に合う問題が 1 件も無い。</summary>
        NoMatchingQuestions = 3,
    }
}
