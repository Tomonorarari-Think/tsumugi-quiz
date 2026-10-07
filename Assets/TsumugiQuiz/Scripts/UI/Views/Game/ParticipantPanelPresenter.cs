using System.Collections.Generic;
using TsumugiQuiz.Core.Participants;
using TsumugiQuiz.Questions;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// 参加者パネル（#194）の表示内容（<see cref="ParticipantPanelState"/>）を文言と USS クラスへ変換する。
    /// UI Toolkit に依存しない純関数なので EditMode でテストする。
    /// </summary>
    /// <remarks>
    /// 記号は「○」（U+25CB）と「×」（U+00D7）を使う。UI のフォント（Noto Sans JP、
    /// <c>UI/Fonts/NotoSansJP-Regular.otf</c>）は「✕」（U+2715）を持たず豆腐になるため（fontTools の cmap で確認）。
    /// </remarks>
    public static class ParticipantPanelPresenter
    {
        /// <summary>行の USS クラス。</summary>
        public const string RowClass = "participant-row";

        /// <summary>自分の行の USS クラス。</summary>
        public const string RowLocalClass = "participant-row--local";

        /// <summary>切断中の行の USS クラス。</summary>
        public const string RowDisconnectedClass = "participant-row--disconnected";

        /// <summary>押下順位の USS クラス。</summary>
        public const string RankClass = "participant-rank";

        /// <summary>行の上段（押下順位・名前・得点）の USS クラス。</summary>
        public const string HeaderClass = "participant-row-header";

        /// <summary>押下順位の列があるとき、状態をバッジの右から始めるための USS クラス。</summary>
        public const string StatusIndentedClass = "participant-status--indented";

        /// <summary>名前の USS クラス。</summary>
        public const string NameClass = "participant-name";

        /// <summary>状態の USS クラス。</summary>
        public const string StatusClass = "participant-status";

        /// <summary>得点の USS クラス。</summary>
        public const string ScoreClass = "participant-score";

        /// <summary>自分の行の名前に添える文言。</summary>
        public const string LocalSuffix = "（あなた）";

        /// <summary>
        /// 状態の文言。<see cref="ParticipantStatus.None"/> は空（まだ押せる人には何も付けない）。
        /// </summary>
        /// <param name="status">状態。</param>
        /// <returns>文言。</returns>
        public static string StatusLabel(ParticipantStatus status) => status switch
        {
            ParticipantStatus.Answering => "回答中",
            ParticipantStatus.Correct => "○ 正解",
            ParticipantStatus.Wrong => "× 不正解",
            ParticipantStatus.Suspended => "休み（お手つき）",
            ParticipantStatus.ChoiceSubmitted => "回答済み",
            ParticipantStatus.ChoiceUnanswered => "未回答",
            _ => string.Empty,
        };

        /// <summary>状態に付ける USS 修飾クラス（無ければ null）。</summary>
        /// <param name="status">状態。</param>
        /// <returns>USS クラス名。</returns>
        public static string StatusModifierClass(ParticipantStatus status) => status switch
        {
            ParticipantStatus.Answering => "participant-status--answering",
            ParticipantStatus.Correct => "participant-status--correct",
            ParticipantStatus.Wrong => "participant-status--wrong",
            ParticipantStatus.ChoiceSubmitted => "participant-status--submitted",
            _ => null,
        };

        /// <summary>
        /// 参加者パネルに得点の列を出すか。<c>display.showScores</c> が OFF でも司会には出す（統括判断 #194）。
        /// </summary>
        /// <param name="settingShowScores">ルーム設定の <c>display.showScores</c>。</param>
        /// <param name="isModerator">自分が司会専任のホストか。</param>
        /// <returns>出すなら true。</returns>
        public static bool ShouldShowScores(bool settingShowScores, bool isModerator) => settingShowScores || isModerator;

        /// <summary>
        /// 現在の問題が選択式か。手元に表示している問題が現在の問題インデックスと一致するときだけ、その形式を信じる
        /// （次の問題の提示が届く前に、前の問題の形式で現在の問題の状態を解釈しないため。PR #201 レビュー L-4 / 再レビュー L-1）。
        /// </summary>
        /// <param name="displayedType">表示している問題の形式（無ければ null）。</param>
        /// <param name="displayedQuestionIndex">表示している問題のインデックス（無ければ -1）。</param>
        /// <param name="currentQuestionIndex">同期された現在の問題インデックス。</param>
        /// <returns>選択式なら true。</returns>
        public static bool IsCurrentQuestionChoice(QuestionType? displayedType, int displayedQuestionIndex, int currentQuestionIndex)
            => displayedType == QuestionType.Choice
               && displayedQuestionIndex >= 0
               && displayedQuestionIndex == currentQuestionIndex;

        /// <summary>
        /// 押下順位のバッジの列を出すか（押下順位を持つ行が 1 つでもあれば出す。PR #201 再レビュー M-B (b)）。
        /// 選択式には押下順位が無いので常に出さない。
        /// </summary>
        /// <param name="state">表示内容。</param>
        /// <returns>出すなら true。</returns>
        public static bool ShowsRankColumn(ParticipantPanelState state)
        {
            if (state == null)
            {
                return false;
            }

            foreach (var row in state.Rows)
            {
                if (row.BuzzRank > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>切断中の行に添える文言（統括判断 #194 L-2）。</summary>
        public const string DisconnectedLabel = "切断中";

        /// <summary>
        /// 状態の行に出す文言（回答順・状態・同着・切断中を「・」でつなぐ）。何も無ければ空。
        /// </summary>
        /// <remarks>
        /// 統括判断 #194 M-1: 回答順（「回答n人目」）は誤答・正解が確定した行にだけ付ける。回答権を持っている行は
        /// 押下順位のバッジと「回答中」だけにする（「2着」と「2人目」が並んで紛らわしくならないように）。
        /// 回答者が 1 人だけのときは回答順を省く（<see cref="ParticipantPanelRow.AnswerOrder"/> が 0 になる）。
        /// </remarks>
        /// <param name="row">行。</param>
        /// <returns>文言。</returns>
        public static string StatusText(ParticipantPanelRow row)
        {
            var parts = new List<string>(4);
            if (row.AnswerOrder > 0
                && (row.Status == ParticipantStatus.Correct || row.Status == ParticipantStatus.Wrong))
            {
                parts.Add($"回答{row.AnswerOrder}人目");
            }

            var label = StatusLabel(row.Status);
            if (!string.IsNullOrEmpty(label))
            {
                parts.Add(label);
            }

            if (row.TiedWithWinner)
            {
                parts.Add("同着");
            }

            if (!row.IsConnected)
            {
                parts.Add(DisconnectedLabel);
            }

            return string.Join("・", parts);
        }

        /// <summary>押下順位の文言（「1着」「2着」…、順位が無ければ空。統括判断 #194 M-1）。</summary>
        /// <param name="row">行。</param>
        /// <returns>文言。</returns>
        public static string RankText(ParticipantPanelRow row) =>
            row.BuzzRank > 0
                ? row.BuzzRank.ToString(System.Globalization.CultureInfo.InvariantCulture) + "着"
                : string.Empty;

        /// <summary>名前の文言（自分の行には「（あなた）」を添える）。</summary>
        /// <param name="row">行。</param>
        /// <returns>文言。</returns>
        public static string NameText(ParticipantPanelRow row) => row.IsLocal ? row.Name + LocalSuffix : row.Name;

        /// <summary>得点の文言。</summary>
        /// <param name="score">累計得点。</param>
        /// <returns>文言（例: 「20点」「-5点」）。</returns>
        public static string ScoreText(int score) =>
            score.ToString(System.Globalization.CultureInfo.InvariantCulture) + "点";

        /// <summary>要約の文言（出さないときは空）。</summary>
        /// <param name="state">表示内容。</param>
        /// <returns>文言。</returns>
        public static string SummaryText(ParticipantPanelState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            return state.SummaryKind switch
            {
                ParticipantPanelSummaryKind.BuzzEligible => $"回答権あり {state.SummaryCount} / {state.SummaryTotal} 人",
                ParticipantPanelSummaryKind.ChoiceAnswered => $"回答済み {state.SummaryCount} / {state.SummaryTotal} 人",
                _ => string.Empty,
            };
        }
    }
}
