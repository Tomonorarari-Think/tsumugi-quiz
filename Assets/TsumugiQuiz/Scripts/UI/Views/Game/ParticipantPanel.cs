using TsumugiQuiz.Core.Participants;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// Game 画面の左列「参加者」パネル（#194）の VisualElement を組み立てる。
    /// 表示内容は <see cref="ParticipantPanelModel"/>（Core）が作り、文言は <see cref="ParticipantPanelPresenter"/> が決める。
    /// 本クラスは要素の生成と差し替えだけを行う。
    /// </summary>
    /// <remarks>
    /// 行は最大でも定員（12 人）+ 切断中の席なので、差分更新はせず描き直すたびに組み立て直す
    /// （LobbyView の名簿と同じ方針）。描き直しは <c>GameView</c> が変更のあった Tick（100ms 間隔）でだけ行う。
    /// </remarks>
    public sealed class ParticipantPanel
    {
        /// <summary>要約ラベルの要素名。</summary>
        public const string SummaryLabelName = "participant-summary-label";

        /// <summary>行の一覧（ScrollView）の要素名。</summary>
        public const string ListName = "participant-list";

        private readonly Label _summaryLabel;
        private readonly ScrollView _list;

        private ParticipantPanel(Label summaryLabel, ScrollView list)
        {
            _summaryLabel = summaryLabel;
            _list = list;
        }

        /// <summary>直近に描いた表示内容（テスト・診断用）。</summary>
        public ParticipantPanelState LastState { get; private set; }

        /// <summary>
        /// game-view.uxml から要素を探して作る。見つからなければ null（パネルなしで動く）。
        /// </summary>
        /// <param name="root">View のルート。</param>
        /// <returns>パネル。要素が足りなければ null。</returns>
        public static ParticipantPanel Create(VisualElement root)
        {
            var summary = root?.Q<Label>(SummaryLabelName);
            var list = root?.Q<ScrollView>(ListName);
            if (summary == null || list == null)
            {
                return null;
            }

            // Space（早押し）・Tab の移動先にならないよう、スクロール領域とスクロールバーからフォーカスを外す
            // （#148 の M-6 と同じ理由。フォーカスを持つと既定の操作がキー入力を奪いうる）。
            list.focusable = false;
            list.contentContainer.focusable = false;
            DisableFocus(list.verticalScroller);
            DisableFocus(list.horizontalScroller);
            return new ParticipantPanel(summary, list);
        }

        /// <summary>表示内容を描き直す。</summary>
        /// <param name="state">表示内容。null なら空にする。</param>
        public void Render(ParticipantPanelState state)
        {
            LastState = state;

            var summary = ParticipantPanelPresenter.SummaryText(state);
            _summaryLabel.text = summary;
            _summaryLabel.style.display = string.IsNullOrEmpty(summary) ? DisplayStyle.None : DisplayStyle.Flex;

            _list.Clear();
            if (state == null)
            {
                return;
            }

            // PR #201 再レビュー M-B (b): 押下順位を持つ行が 1 つも無い（選択式・押す前・開き直した直後）なら、
            // バッジの列ごと出さずに名前と状態へ幅を回す。
            var showRankColumn = ParticipantPanelPresenter.ShowsRankColumn(state);
            foreach (var row in state.Rows)
            {
                _list.Add(CreateRow(row, state.ShowScores, showRankColumn));
            }
        }

        /// <summary>
        /// 1 行を組み立てる。上段 = [押下順位] [名前（折り返す）] [得点]、下段 = 状態（バッジの右から行の右端まで）。
        /// </summary>
        /// <remarks>
        /// PR #201 再レビュー M-B (a): 状態は名前と同じ列に入れると得点の列の分だけ幅が削られ、900x750 で
        /// 「回答1人目・× / 不正解」のように不自然な位置で折り返していた。下段に出して得点の列の下まで使う。
        /// </remarks>
        private static VisualElement CreateRow(ParticipantPanelRow row, bool showScores, bool showRankColumn)
        {
            var element = new VisualElement { name = $"participant-row-{row.ClientId}" };
            element.AddToClassList(ParticipantPanelPresenter.RowClass);
            element.EnableInClassList(ParticipantPanelPresenter.RowLocalClass, row.IsLocal);
            element.EnableInClassList(ParticipantPanelPresenter.RowDisconnectedClass, !row.IsConnected);

            var header = new VisualElement { name = "participant-row-header" };
            header.AddToClassList(ParticipantPanelPresenter.HeaderClass);
            element.Add(header);

            if (showRankColumn)
            {
                var rank = new Label(ParticipantPanelPresenter.RankText(row)) { name = "participant-rank" };
                rank.AddToClassList(ParticipantPanelPresenter.RankClass);
                header.Add(rank);
            }

            // #206: 名前は参加者が決める文字列なので、リッチテキストとして解釈させない。
            var name = PlainText.Apply(new Label(ParticipantPanelPresenter.NameText(row)) { name = "participant-name" });
            name.AddToClassList(ParticipantPanelPresenter.NameClass);
            header.Add(name);

            if (showScores)
            {
                var score = new Label(ParticipantPanelPresenter.ScoreText(row.Score)) { name = "participant-score" };
                score.AddToClassList(ParticipantPanelPresenter.ScoreClass);
                header.Add(score);
            }

            var statusText = ParticipantPanelPresenter.StatusText(row);
            if (!string.IsNullOrEmpty(statusText))
            {
                var status = new Label(statusText) { name = "participant-status" };
                status.AddToClassList("small-text");
                status.AddToClassList(ParticipantPanelPresenter.StatusClass);
                status.EnableInClassList(ParticipantPanelPresenter.StatusIndentedClass, showRankColumn);
                var modifier = ParticipantPanelPresenter.StatusModifierClass(row.Status);
                if (modifier != null)
                {
                    status.AddToClassList(modifier);
                }

                element.Add(status);
            }

            return element;
        }

        private static void DisableFocus(Scroller scroller)
        {
            if (scroller == null)
            {
                return;
            }

            scroller.focusable = false;
            scroller.Query<VisualElement>().ForEach(child => child.focusable = false);
        }
    }
}
