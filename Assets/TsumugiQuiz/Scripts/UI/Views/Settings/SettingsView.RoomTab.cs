using System;
using System.Collections.Generic;
using System.Linq;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// <see cref="SettingsView"/> のうち「ルーム設定」タブ（docs/room-settings.md §1 の全 26 キー。
    /// <c>buzz.collectWindowMs</c> は「詳細設定」の <see cref="Foldout"/> に隠す）。
    /// </summary>
    public sealed partial class SettingsView
    {
        // ホスト・ルーム
        private DropdownField _roomHostRoleField;
        private IntegerField _roomMaxPlayersField;

        // 問題選択
        private TextField _roomQuestionsSetIdsField;
        private DropdownField _roomQuestionsTypeFilterField;
        private Toggle _roomQuestionsImageOnlyField;
        private TextField _roomQuestionsTagFilterField;
        private IntegerField _roomQuestionsCountField;
        private Toggle _roomQuestionsShuffleOrderField;

        // 選択式
        private Toggle _roomChoicesShuffleDisplayField;

        // 制限時間（docs/room-settings.md §1 の型は integer）
        private IntegerField _roomBuzzTimeLimitField;
        private IntegerField _roomAnswerFreeTextTimeLimitField;
        private IntegerField _roomAnswerChoiceTimeLimitField;

        // 得点
        private IntegerField _roomScoreCorrectPointsField;
        private IntegerField _roomScoreIncorrectPointsField;
        private DropdownField _roomScorePenaltyTypeField;
        private IntegerField _roomScorePenaltyMinusPointsField;

        // 読み上げ
        private Toggle _roomTtsEnabledField;
        private FloatField _roomTtsSpeedField;
        private IntegerField _roomTtsReadyTimeoutField;
        private FloatField _roomTtsLeadTimeField;
        private IntegerField _roomQuestionRevealMsPerCharField;

        // 早押し詳細
        private Toggle _roomBuzzAllowDuringReadingField;
        private Toggle _roomBuzzReopenAfterWrongAnswerField;

        // 結果・進行
        private FloatField _roomResultAutoAdvanceField;
        private Toggle _roomNetworkAllowLateJoinField;

        // 表示（#194）
        private Toggle _roomDisplayShowScoresField;

        // 詳細設定（隠す項目）
        private IntegerField _roomBuzzCollectWindowField;

        /// <summary>
        /// 閲覧モードで無効化する入力群の器（L-2）。警告・状態ラベル・「適用」はこの外にある。
        /// </summary>
        private VisualElement _roomFieldsContainer;

        /// <summary>「詳細設定」<see cref="Foldout"/> 内の入力群の器（L-2。Foldout 自体は開閉できるままにする）。</summary>
        private VisualElement _roomAdvancedFieldsContainer;

        private VisualElement _roomWarningsContainer;
        private Label _roomStatusLabel;
        private Button _roomApplyButton;

        /// <summary>現在編集中のルーム設定。プリセットタブからも参照・更新する。</summary>
        private RoomSettings _currentRoomSettings;

        /// <summary>
        /// 下書き（<see cref="RoomSettingsDraft"/>）へ書き込んでよいか（PR #92 再レビュー M-1）。
        /// クライアントとして接続中は false（ホストの設定で自分の下書きを潰さない）。
        /// </summary>
        private bool _roomDraftWritable = true;

        private void OnShowRoomTab(VisualElement root)
        {
            var allFound = true;

            _roomHostRoleField = SettingsFieldBinder.Require<DropdownField>(root, "room-host-role-field", ref allFound);
            _roomMaxPlayersField = SettingsFieldBinder.Require<IntegerField>(root, "room-max-players-field", ref allFound);

            _roomQuestionsSetIdsField = SettingsFieldBinder.Require<TextField>(root, "room-questions-set-ids-field", ref allFound);
            _roomQuestionsTypeFilterField = SettingsFieldBinder.Require<DropdownField>(root, "room-questions-type-filter-field", ref allFound);
            _roomQuestionsImageOnlyField = SettingsFieldBinder.Require<Toggle>(root, "room-questions-image-only-field", ref allFound);
            _roomQuestionsTagFilterField = SettingsFieldBinder.Require<TextField>(root, "room-questions-tag-filter-field", ref allFound);
            _roomQuestionsCountField = SettingsFieldBinder.Require<IntegerField>(root, "room-questions-count-field", ref allFound);
            _roomQuestionsShuffleOrderField = SettingsFieldBinder.Require<Toggle>(root, "room-questions-shuffle-order-field", ref allFound);

            _roomChoicesShuffleDisplayField = SettingsFieldBinder.Require<Toggle>(root, "room-choices-shuffle-display-field", ref allFound);

            _roomBuzzTimeLimitField = SettingsFieldBinder.Require<IntegerField>(root, "room-buzz-time-limit-field", ref allFound);
            _roomAnswerFreeTextTimeLimitField = SettingsFieldBinder.Require<IntegerField>(root, "room-answer-free-text-time-limit-field", ref allFound);
            _roomAnswerChoiceTimeLimitField = SettingsFieldBinder.Require<IntegerField>(root, "room-answer-choice-time-limit-field", ref allFound);

            _roomScoreCorrectPointsField = SettingsFieldBinder.Require<IntegerField>(root, "room-score-correct-points-field", ref allFound);
            _roomScoreIncorrectPointsField = SettingsFieldBinder.Require<IntegerField>(root, "room-score-incorrect-points-field", ref allFound);
            _roomScorePenaltyTypeField = SettingsFieldBinder.Require<DropdownField>(root, "room-score-penalty-type-field", ref allFound);
            _roomScorePenaltyMinusPointsField = SettingsFieldBinder.Require<IntegerField>(root, "room-score-penalty-minus-points-field", ref allFound);

            _roomTtsEnabledField = SettingsFieldBinder.Require<Toggle>(root, "room-tts-enabled-field", ref allFound);
            _roomTtsSpeedField = SettingsFieldBinder.Require<FloatField>(root, "room-tts-speed-field", ref allFound);
            _roomTtsReadyTimeoutField = SettingsFieldBinder.Require<IntegerField>(root, "room-tts-ready-timeout-field", ref allFound);
            _roomTtsLeadTimeField = SettingsFieldBinder.Require<FloatField>(root, "room-tts-lead-time-field", ref allFound);
            _roomQuestionRevealMsPerCharField = SettingsFieldBinder.Require<IntegerField>(
                root, "room-question-reveal-ms-per-char-field", ref allFound);

            _roomBuzzAllowDuringReadingField = SettingsFieldBinder.Require<Toggle>(root, "room-buzz-allow-during-reading-field", ref allFound);
            _roomBuzzReopenAfterWrongAnswerField = SettingsFieldBinder.Require<Toggle>(root, "room-buzz-reopen-after-wrong-answer-field", ref allFound);

            _roomResultAutoAdvanceField = SettingsFieldBinder.Require<FloatField>(root, "room-result-auto-advance-field", ref allFound);
            _roomNetworkAllowLateJoinField = SettingsFieldBinder.Require<Toggle>(root, "room-network-allow-late-join-field", ref allFound);

            _roomDisplayShowScoresField = SettingsFieldBinder.Require<Toggle>(root, "room-display-show-scores-field", ref allFound);

            _roomBuzzCollectWindowField = SettingsFieldBinder.Require<IntegerField>(root, "room-buzz-collect-window-field", ref allFound);

            _roomFieldsContainer = SettingsFieldBinder.Require<VisualElement>(root, "room-settings-fields", ref allFound);
            _roomAdvancedFieldsContainer =
                SettingsFieldBinder.Require<VisualElement>(root, "room-settings-advanced-fields", ref allFound);

            _roomWarningsContainer = SettingsFieldBinder.Require<VisualElement>(root, "room-settings-warnings", ref allFound);
            _roomStatusLabel = SettingsFieldBinder.Require<Label>(root, "room-settings-status-label", ref allFound);
            _roomApplyButton = SettingsFieldBinder.Require<Button>(root, "room-settings-apply-button", ref allFound);

            if (!allFound)
            {
                // L6: 一部でも見つからなければ、見つかった分も含めて null に戻す
                // （見つかった要素だけに部分的にイベントを配線した中途半端な状態を残さない）。
                OnHideRoomTab();
                return;
            }

            _roomHostRoleField.choices = RoomSettingsDropdownOptions.LabelsOf(RoomSettingsDropdownOptions.HostRole);
            _roomQuestionsTypeFilterField.choices =
                RoomSettingsDropdownOptions.LabelsOf(RoomSettingsDropdownOptions.QuestionsTypeFilter);
            _roomScorePenaltyTypeField.choices =
                RoomSettingsDropdownOptions.LabelsOf(RoomSettingsDropdownOptions.ScorePenaltyType);

            // 初期値の優先順位（#28 Phase 2）:
            // 1. 稼働中の RoomSettingsSync.Current（ホスト・クライアントの双方。いま実際に有効な値）
            // 2. RoomSettingsDraft.Current（前回この PC で編集・適用した内容。PR #92 レビュー H4/H-2）
            // 2 は、何も編集したことが無ければ「標準」プリセット。
            var sync = RoomSettingsSyncLocator();
            IReadOnlyList<string> draftWarnings = Array.Empty<string>();
            _currentRoomSettings = sync != null ? sync.Current : RoomSettingsDraft.Load(out draftWarnings);
            PopulateRoomFields(_currentRoomSettings);
            ShowRoomWarnings(draftWarnings.Count > 0 ? draftWarnings : null);

            ApplyRoomTabPermission(sync);

            _roomApplyButton.clicked += OnRoomApplyClicked;
        }

        /// <summary>
        /// ルーム設定タブの編集可否を、いまの <c>RoomSettingsSync</c> の状態から決める
        /// （PR #92 再レビュー M-1）。
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        ///   <item><description>クライアント（ホストでない）: 閲覧のみ。下書きにも書かない
        ///     （「適用」を押しても自分の下書きがホストの設定で上書きされないようにする）</description></item>
        ///   <item><description>ロック中（ゲーム進行中）: 閲覧のみ。下書きへの書き込みは許す
        ///     （ロビーへ戻ったあとに使えるようにするため）だが、入力自体を止めるので実際には起きない</description></item>
        /// </list>
        /// タブの入力要素は 26 個あるため、個別ではなく入力群の器
        /// （<c>room-settings-fields</c> と、詳細設定 <see cref="Foldout"/> 内の
        /// <c>room-settings-advanced-fields</c>）ごと <c>SetEnabled(false)</c> する
        /// （UI Toolkit は子要素へ伝播する）。L-2: 警告・状態ラベル・「適用」はこの器の外にあるので、
        /// 閲覧モードでも理由の文言がグレーにならず、詳細設定の開閉もできる。
        /// </remarks>
        private void ApplyRoomTabPermission(RoomSettingsSync sync)
        {
            var isClient = sync != null && !sync.IsServer;
            var isLocked = sync != null && sync.IsLocked;

            _roomDraftWritable = !isClient;

            var editable = !isClient && !isLocked;
            _roomFieldsContainer.SetEnabled(editable);
            _roomAdvancedFieldsContainer.SetEnabled(editable);
            _roomApplyButton.SetEnabled(editable);

            if (isClient)
            {
                ShowRoomStatus("ホストのみ変更できます（閲覧）。");
            }
            else if (isLocked)
            {
                ShowRoomStatus("ゲーム進行中は変更できません（閲覧）。ロビーへ戻ると変更できます。");
            }
            else
            {
                ShowRoomStatus(string.Empty);
            }
        }

        /// <summary>
        /// 下書き（<see cref="RoomSettingsDraft"/>）へ保存する。クライアント接続中は書かない（M-1）。
        /// 永続化に失敗したら理由を <paramref name="warnings"/> へ足す。
        /// </summary>
        private void SaveRoomDraft(RoomSettings settings, List<string> warnings)
        {
            if (!_roomDraftWritable)
            {
                return;
            }

            if (!RoomSettingsDraft.Set(settings, out var draftWarnings))
            {
                warnings?.AddRange(draftWarnings);
            }
        }

        private void OnHideRoomTab()
        {
            if (_roomApplyButton != null)
            {
                _roomApplyButton.clicked -= OnRoomApplyClicked;
            }

            _roomHostRoleField = null;
            _roomMaxPlayersField = null;
            _roomQuestionsSetIdsField = null;
            _roomQuestionsTypeFilterField = null;
            _roomQuestionsImageOnlyField = null;
            _roomQuestionsTagFilterField = null;
            _roomQuestionsCountField = null;
            _roomQuestionsShuffleOrderField = null;
            _roomChoicesShuffleDisplayField = null;
            _roomBuzzTimeLimitField = null;
            _roomAnswerFreeTextTimeLimitField = null;
            _roomAnswerChoiceTimeLimitField = null;
            _roomScoreCorrectPointsField = null;
            _roomScoreIncorrectPointsField = null;
            _roomScorePenaltyTypeField = null;
            _roomScorePenaltyMinusPointsField = null;
            _roomTtsEnabledField = null;
            _roomTtsSpeedField = null;
            _roomTtsReadyTimeoutField = null;
            _roomTtsLeadTimeField = null;
            _roomQuestionRevealMsPerCharField = null;
            _roomBuzzAllowDuringReadingField = null;
            _roomBuzzReopenAfterWrongAnswerField = null;
            _roomResultAutoAdvanceField = null;
            _roomNetworkAllowLateJoinField = null;
            _roomDisplayShowScoresField = null;
            _roomBuzzCollectWindowField = null;
            _roomFieldsContainer = null;
            _roomAdvancedFieldsContainer = null;
            _roomWarningsContainer = null;
            _roomStatusLabel = null;
            _roomApplyButton = null;
            _roomDraftWritable = true;
        }

        /// <summary>各フィールドに <paramref name="settings"/> の値を反映する（読み込み・クランプ後の再表示に使う）。</summary>
        private void PopulateRoomFields(RoomSettings settings)
        {
            _roomHostRoleField.value = RoomSettingsDropdownOptions.LabelFor(RoomSettingsDropdownOptions.HostRole, HostRoles.ToKey(settings.HostRole));
            _roomMaxPlayersField.value = settings.MaxPlayers;

            _roomQuestionsSetIdsField.value = string.Join(", ", settings.Questions.SetIds);
            _roomQuestionsTypeFilterField.value = RoomSettingsDropdownOptions.LabelFor(
                RoomSettingsDropdownOptions.QuestionsTypeFilter, TypeFilterKey(settings.Questions.TypeFilter));
            _roomQuestionsImageOnlyField.value = settings.Questions.ImageOnly;
            _roomQuestionsTagFilterField.value = string.Join(", ", settings.Questions.TagFilter);
            _roomQuestionsCountField.value = settings.Questions.Count;
            _roomQuestionsShuffleOrderField.value = settings.Questions.ShuffleOrder;

            _roomChoicesShuffleDisplayField.value = settings.ShuffleChoiceDisplay;

            _roomBuzzTimeLimitField.value = (int)Math.Round(settings.TimeLimits.BuzzTimeLimitSec);
            _roomAnswerFreeTextTimeLimitField.value = (int)Math.Round(settings.TimeLimits.AnswerTimeLimitSec);
            _roomAnswerChoiceTimeLimitField.value = (int)Math.Round(settings.ChoiceTimeLimitSec);

            _roomScoreCorrectPointsField.value = settings.Scoring.CorrectPoints;
            _roomScoreIncorrectPointsField.value = settings.Scoring.IncorrectPoints;
            _roomScorePenaltyTypeField.value = RoomSettingsDropdownOptions.LabelFor(
                RoomSettingsDropdownOptions.ScorePenaltyType, PenaltyTypeKey(settings.Scoring.PenaltyType));
            _roomScorePenaltyMinusPointsField.value = settings.Scoring.PenaltyMinusPoints;

            _roomTtsEnabledField.value = settings.TtsEnabled;
            _roomTtsSpeedField.value = (float)settings.TtsSpeed;
            _roomTtsReadyTimeoutField.value = settings.TtsReadyTimeoutMs;
            _roomTtsLeadTimeField.value = (float)settings.TtsLeadTimeSec;
            _roomQuestionRevealMsPerCharField.value = settings.QuestionRevealMsPerChar;

            _roomBuzzAllowDuringReadingField.value = settings.AllowDuringReading;
            _roomBuzzReopenAfterWrongAnswerField.value = settings.Scoring.ReopenAfterWrongAnswer;

            _roomResultAutoAdvanceField.value = (float)settings.ResultAutoAdvanceSec;
            _roomNetworkAllowLateJoinField.value = settings.AllowLateJoin;

            _roomDisplayShowScoresField.value = settings.ShowScores;

            _roomBuzzCollectWindowField.value = (int)Math.Round(settings.TimeLimits.CollectWindowSec * 1000.0);
        }

        /// <summary>現在のフィールド値から <see cref="RoomSettingsInput"/> を組み立てる。</summary>
        private RoomSettingsInput CollectRoomInput() => new RoomSettingsInput
        {
            HostRole = RoomSettingsDropdownOptions.ValueFor(RoomSettingsDropdownOptions.HostRole, _roomHostRoleField.value),
            MaxPlayers = _roomMaxPlayersField.value,

            QuestionsSetIds = SplitCommaSeparated(_roomQuestionsSetIdsField.value),
            QuestionsTypeFilter = RoomSettingsDropdownOptions.ValueFor(
                RoomSettingsDropdownOptions.QuestionsTypeFilter, _roomQuestionsTypeFilterField.value),
            QuestionsImageOnly = _roomQuestionsImageOnlyField.value,
            QuestionsTagFilter = SplitCommaSeparated(_roomQuestionsTagFilterField.value),
            QuestionsCount = _roomQuestionsCountField.value,
            QuestionsShuffleOrder = _roomQuestionsShuffleOrderField.value,

            ChoicesShuffleDisplay = _roomChoicesShuffleDisplayField.value,

            BuzzTimeLimitSec = _roomBuzzTimeLimitField.value,
            AnswerFreeTextTimeLimitSec = _roomAnswerFreeTextTimeLimitField.value,
            AnswerChoiceTimeLimitSec = _roomAnswerChoiceTimeLimitField.value,

            ScoreCorrectPoints = _roomScoreCorrectPointsField.value,
            ScoreIncorrectPoints = _roomScoreIncorrectPointsField.value,
            ScorePenaltyType = RoomSettingsDropdownOptions.ValueFor(
                RoomSettingsDropdownOptions.ScorePenaltyType, _roomScorePenaltyTypeField.value),
            ScorePenaltyMinusPoints = _roomScorePenaltyMinusPointsField.value,

            TtsEnabled = _roomTtsEnabledField.value,
            TtsSpeed = _roomTtsSpeedField.value,
            TtsReadyTimeoutMs = _roomTtsReadyTimeoutField.value,
            TtsLeadTimeSec = _roomTtsLeadTimeField.value,
            QuestionRevealMsPerChar = _roomQuestionRevealMsPerCharField.value,

            BuzzAllowDuringReading = _roomBuzzAllowDuringReadingField.value,
            BuzzReopenAfterWrongAnswer = _roomBuzzReopenAfterWrongAnswerField.value,
            // #221: 画面から外した項目。読み込んだ値（プリセット等）を適用・保存で失わないよう引き継ぐ。
            AnswerSingleAttemptOnly = _currentRoomSettings?.Scoring.SingleAttemptOnly,

            ResultAutoAdvanceSec = _roomResultAutoAdvanceField.value,
            NetworkAllowLateJoin = _roomNetworkAllowLateJoinField.value,

            DisplayShowScores = _roomDisplayShowScoresField.value,

            BuzzCollectWindowMs = _roomBuzzCollectWindowField.value,
        };

        /// <summary>
        /// 「適用」。入力値を <see cref="RoomSettingsValidator"/> で検証し、
        /// (1) ローカルの下書き（<see cref="RoomSettingsDraft"/>）へ保存し、
        /// (2) ホストかつ未ロックなら <see cref="TsumugiQuiz.Network.RoomSettingsSync"/> へ渡して
        /// 参加者全員へ同期する（issue #28 Phase 2、<c>SettingsView.RoomSync.cs</c>）。
        ///
        /// 下書きの保存はロック中・クライアント・未接続でも必ず行う（次にホストを始めたときに使える）。
        /// </summary>
        private void OnRoomApplyClicked()
        {
            var input = CollectRoomInput();
            var result = RoomSettingsValidator.Validate(input);
            _currentRoomSettings = result.Settings;

            PopulateRoomFields(_currentRoomSettings);

            var warnings = new List<string>(result.Warnings);
            SaveRoomDraft(_currentRoomSettings, warnings);
            var status = PushToRoomSettingsSync(_currentRoomSettings, warnings);

            ShowRoomWarnings(warnings);
            ShowRoomStatus(warnings.Count == 0
                ? status
                : $"{status}（{warnings.Count} 件の警告があります）");
        }

        private void ShowRoomWarnings(IReadOnlyList<string> warnings)
        {
            _roomWarningsContainer.Clear();
            if (warnings == null)
            {
                return;
            }

            foreach (var warning in warnings)
            {
                var label = new Label(warning);
                label.AddToClassList("body-text");
                label.AddToClassList("host-setup-warning");
                _roomWarningsContainer.Add(label);
            }
        }

        private void ShowRoomStatus(string message)
        {
            _roomStatusLabel.text = message ?? string.Empty;
            _roomStatusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static List<string> SplitCommaSeparated(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();

        private static string TypeFilterKey(QuestionTypeFilter filter) => filter switch
        {
            QuestionTypeFilter.FreeText => "freeText",
            QuestionTypeFilter.Choice => "choice",
            _ => "both",
        };

        private static string PenaltyTypeKey(PenaltyKind kind) => kind switch
        {
            PenaltyKind.MinusPoints => "minusPoints",
            PenaltyKind.None => "none",
            _ => "skipNext",
        };
    }
}
