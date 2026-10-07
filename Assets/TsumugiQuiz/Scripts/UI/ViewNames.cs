using System;
using System.Collections.Generic;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// ViewRouter に登録する View 名（ケバブケース）の定数一覧。
    /// architecture.md §2 の View 一覧に対応する。
    /// </summary>
    public static class ViewNames
    {
        /// <summary>利用規約同意画面（requirements.md FR-71〜FR-76）。<see cref="Views.TermsView"/>。</summary>
        public const string Terms = "terms";

        public const string Title = "title";
        public const string HostSetup = "host-setup";
        public const string Join = "join";
        public const string Lobby = "lobby";
        public const string Game = "game";
        public const string Result = "result";
        public const string QuestionEditor = "question-editor";
        public const string Settings = "settings";
        public const string Credits = "credits";

        /// <summary>まだ実体が用意されていない View（プレースホルダ表示のみ）。現時点では空。</summary>
        /// <remarks>
        /// Lobby は #7、Game は #14、Result は #20、QuestionEditor は #30、Settings は #28 で
        /// それぞれ実 View（<see cref="Views.Lobby.LobbyView"/> / <see cref="Views.Game.GameView"/> /
        /// <see cref="Views.ResultView"/> / <see cref="Views.QuestionEditor.QuestionEditorView"/> /
        /// <see cref="Views.Settings.SettingsView"/>）に差し替え済みで、
        /// <see cref="RegisteredViews"/> の全 View が実体を持つようになった。
        /// <see cref="Views.PlaceholderView"/>・<c>placeholder-view.uxml</c> は、今後 View を追加する際の
        /// 足場として残してある（この一覧に名前を足せばプレースホルダ表示に戻せる）。
        /// </remarks>
        public static readonly IReadOnlyList<string> PlaceholderViews = Array.Empty<string>();

        /// <summary>
        /// ViewRouter に登録される View 名すべて。ViewRouter はこの一覧を使い、
        /// 対応するテンプレートが揃っているかを起動時に検証する。
        /// </summary>
        public static readonly IReadOnlyList<string> RegisteredViews = new[]
        {
            Terms, Title, HostSetup, Join, Lobby, Game, Result, QuestionEditor, Settings, Credits,
        };
    }
}
