using TsumugiQuiz.Core;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、UI 要素の取得・イベント購読の配線と、単純な表示更新
    /// （フェーズ文言・得点・各セクションの表示/非表示）をまとめた部分（issue #14）。
    /// <c>JoinView.UiBinding.cs</c> / <c>HostSetupView.Connectivity.cs</c> と同じ方針で
    /// 1 ファイル 400 行以内に収めるために分割している（M-9）。
    /// </summary>
    public sealed partial class GameView
    {
        private bool BindElements(VisualElement root)
        {
            _questionIndexLabel = root.Q<Label>("question-index-label");
            _phaseLabel = root.Q<Label>("phase-label");
            _pausedBadgeLabel = root.Q<Label>("paused-badge-label");
            // #206: 問題データは平文として表示する（リッチテキストのタグは解釈しない。docs/question-data.md）。
            _questionTextLabel = PlainText.Apply(root.Q<Label>("question-text-label"));
            _timeBarFill = root.Q<VisualElement>("time-bar-fill");
            _timeRemainingLabel = root.Q<Label>("time-remaining-label");

            // #132 レビュー H-1: buzz-section は司会専用モード（GameView.Moderator.cs）だけでなく
            // 選択式の問題でも隠すため、ここで常に取得しておく。
            _buzzSection = root.Q<VisualElement>("buzz-section");
            _buzzButton = root.Q<Button>("buzz-button");
            // #206: 押した参加者の名前（参加者が決める文字列）を含むため、リッチテキストとして解釈させない。
            _buzzResultLabel = PlainText.Apply(root.Q<Label>("buzz-result-label"));

            _answerSection = root.Q<VisualElement>("answer-section");
            _answerField = root.Q<TextField>("answer-field");
            _answerErrorLabel = root.Q<Label>("answer-error-label");
            _answerSubmitButton = root.Q<Button>("answer-submit-button");

            _choiceSection = root.Q<VisualElement>("choice-section");
            _choiceButtonsContainer = root.Q<VisualElement>("choice-buttons-container");

            _resultSection = root.Q<VisualElement>("result-section");
            // #206: 正解（問題データ）と回答者の名前（参加者が決める文字列）を含むため、平文として表示する。
            _resultLabel = PlainText.Apply(root.Q<Label>("result-label"));
            _scoreLabel = root.Q<Label>("score-label");

            _hostControls = root.Q<VisualElement>("host-controls");
            _nextButton = root.Q<Button>("next-button");
            _exitButton = root.Q<Button>("exit-button");

            _exitConfirmContainer = root.Q<VisualElement>("exit-confirm-container");
            _exitConfirmLabel = root.Q<Label>("exit-confirm-label");
            _confirmExitButton = root.Q<Button>("confirm-exit-button");
            _cancelExitButton = root.Q<Button>("cancel-exit-button");

            // issue #24: 立ち絵。見つからなくても GameView 本体の表示は続行する（必須要素の判定には含めない）。
            _characterRoot = root.Q<VisualElement>("character-root");

            return _questionIndexLabel != null && _phaseLabel != null && _pausedBadgeLabel != null
                && _questionTextLabel != null
                && _timeBarFill != null && _timeRemainingLabel != null
                && _buzzSection != null && _buzzButton != null && _buzzResultLabel != null
                && _answerSection != null && _answerField != null && _answerErrorLabel != null && _answerSubmitButton != null
                && _choiceSection != null && _choiceButtonsContainer != null
                && _resultSection != null && _resultLabel != null && _scoreLabel != null
                && _hostControls != null && _nextButton != null && _exitButton != null
                && _exitConfirmContainer != null && _exitConfirmLabel != null
                && _confirmExitButton != null && _cancelExitButton != null;
        }

        private void RegisterUiHandlers()
        {
            _buzzButton.clicked += OnBuzzButtonClicked;
            _answerSubmitButton.clicked += OnAnswerSubmitClicked;
            _answerField.RegisterCallback<KeyDownEvent>(OnAnswerFieldKeyDown);
            _nextButton.clicked += OnNextButtonClicked;
            _exitButton.clicked += OnExitButtonClicked;
            _confirmExitButton.clicked += OnConfirmExitClicked;
            _cancelExitButton.clicked += OnCancelExitClicked;
        }

        private void UnregisterUiHandlers()
        {
            if (_buzzButton != null)
            {
                _buzzButton.clicked -= OnBuzzButtonClicked;
            }

            if (_answerSubmitButton != null)
            {
                _answerSubmitButton.clicked -= OnAnswerSubmitClicked;
            }

            if (_answerField != null)
            {
                _answerField.UnregisterCallback<KeyDownEvent>(OnAnswerFieldKeyDown);
            }

            if (_nextButton != null)
            {
                _nextButton.clicked -= OnNextButtonClicked;
            }

            if (_exitButton != null)
            {
                _exitButton.clicked -= OnExitButtonClicked;
            }

            if (_confirmExitButton != null)
            {
                _confirmExitButton.clicked -= OnConfirmExitClicked;
            }

            if (_cancelExitButton != null)
            {
                _cancelExitButton.clicked -= OnCancelExitClicked;
            }
        }

        private void ResetUi()
        {
            _hasBuzzedLocally = false;
            _isExcludedFromBuzzing = false;

            _questionIndexLabel.text = string.Empty;
            _questionTextLabel.text = string.Empty;
            _buzzResultLabel.text = string.Empty;
            _resultLabel.text = string.Empty;
            _timeRemainingLabel.text = GameViewPresenter.EmptyTimeRemainingText;
            _timeBarFill.style.width = new StyleLength(Length.Percent(0));
            _pausedBadgeLabel.style.display = DisplayStyle.None;

            _answerSection.style.display = DisplayStyle.None;
            _buzzButton.style.display = DisplayStyle.Flex;
            ShowAnswerError(string.Empty);

            ClearChoiceButtons();
            _hasSelectedChoiceLocally = false;
            _choiceSection.style.display = DisplayStyle.None;
            _resultSection.style.display = DisplayStyle.None;

            _hostControls.style.display = DisplayStyle.None;
            SetExitConfirmVisible(false);

            _buzzButton.SetEnabled(false);
            UpdatePhaseText(GameViewPresenter.PhaseLabel(QuizPhase.Lobby));
            UpdateScoreLabel(0);
        }

        private void UpdatePhaseText(string text)
        {
            _phaseLabel.text = text;
        }

        private void UpdateScoreLabel(int total)
        {
            _scoreLabel.text = $"得点: {total}";
        }

        private void UpdateBuzzButtonEnabled()
        {
            var enabled = _session != null
                && GameViewPresenter.IsBuzzButtonEnabled(_session.Phase.Value, _hasBuzzedLocally, IsExcludedFromBuzzing);
            _buzzButton.SetEnabled(enabled);
        }

        private void UpdateAnswerSectionVisible()
        {
            var visible = _session != null
                && GameViewPresenter.IsAnswerFieldVisible(_session.Phase.Value, _session.LockedClientId.Value, LocalClientIdOrNull);

            _answerSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            // #187: 自分が回答している間は早押しボタンに役目が無い（押しても受け付けない）ので隠し、
            // 回答欄の分の縦幅を空ける（21:9 の論理縦幅（約 771px）でも回答欄がスクロールせずに見えるように。
            // docs/architecture.md §10.2）。回答権の案内（buzz-result-label）は残す。
            var buzzButtonVisible = _session == null
                || GameViewPresenter.IsBuzzButtonVisible(_session.Phase.Value, _session.LockedClientId.Value, LocalClientIdOrNull);
            _buzzButton.style.display = buzzButtonVisible ? DisplayStyle.Flex : DisplayStyle.None;

            if (visible)
            {
                _answerField.SetEnabled(true);
                _answerField.SetValueWithoutNotify(string.Empty);
                ShowAnswerError(string.Empty);
                _answerSubmitButton.SetEnabled(true);
                _answerField.Focus();
            }
        }

        /// <summary>
        /// 問題の形式・フェーズで出し分けるセクション（選択肢 / 早押し / 判定結果）をまとめて更新する
        /// （#132 レビュー H-1）。呼び出し漏れで片方だけ残らないよう、常にこの 1 本を呼ぶこと。
        /// </summary>
        private void UpdateQuestionSectionsVisible()
        {
            UpdateChoiceSectionVisible();
            UpdateBuzzSectionVisible();
            UpdateResultSectionVisible();
        }

        /// <summary>
        /// 早押しセクションの表示を更新する（#132 レビュー H-1）。
        /// 司会専用モード（#20）では常に隠す。それ以外は出題形式で決める（選択式なら隠す）。
        /// </summary>
        private void UpdateBuzzSectionVisible()
        {
            if (_buzzSection == null)
            {
                return;
            }

            var visible = !_isModerator && GameViewPresenter.IsBuzzSectionVisible(_currentQuestion?.Type);
            _buzzSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// 判定結果セクションの表示を更新する（#132 レビュー H-1）。
        /// フェーズに加えて「判定の文言が入っていること」も条件にする（#132 レビュー M3-3）。
        /// 出題の RPC がフェーズ変更より先に届くと、フェーズがまだ <c>Result</c> のまま
        /// <c>result-label</c> だけが空になり、中身の無いパネルが一瞬表示されるため。
        /// </summary>
        private void UpdateResultSectionVisible()
        {
            if (_resultSection == null || _resultLabel == null)
            {
                return;
            }

            var phase = _session?.Phase.Value ?? QuizPhase.Lobby;
            var visible = GameViewPresenter.IsResultSectionVisible(phase)
                && !string.IsNullOrEmpty(_resultLabel.text);
            _resultSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateHostControlsVisible()
        {
            // 司会専用モード（#20）は司会操作パネル側に「次へ」を持つため、汎用の host-controls とは
            // 重複させない（ModeratorControlsPanel を組み込んだときだけ true になる _isModerator）。
            var visible = _session != null && !_isModerator
                && GameViewPresenter.IsHostControlsVisible(_session.Phase.Value, IsLocalHost);
            _hostControls.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            // 統括判断 M-C: 一時停止中は通常モードのホストの「次へ」も無効化する
            // （サーバー側も一時停止中は NextQuestion() を棄却するため、押しても無反応にしない）。
            var isPaused = _session != null && _session.IsPaused.Value;
            _nextButton.SetEnabled(!isPaused);
        }

        private void ShowAnswerError(string message)
        {
            _answerErrorLabel.text = message ?? string.Empty;
            _answerErrorLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>「一時停止中」バッジの表示を更新する（M1、<see cref="GameSession.IsPaused"/>）。</summary>
        private void UpdatePausedBadge()
        {
            var isPaused = _session != null && _session.IsPaused.Value;
            _pausedBadgeLabel.style.display = isPaused ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateTimeDisplay()
        {
            if (_session == null || _session.NetworkManager == null)
            {
                return;
            }

            if (_session.IsPaused.Value)
            {
                // 一時停止中は残り時間の表示も止める（M1）。サーバー側も
                // QuizStateMachine.Tick を止めているため、実際の進行も止まっている。
                return;
            }

            var now = _session.NetworkManager.ServerTime.Time;
            var deadline = _session.CurrentDeadlineServerTime;
            var remaining = GameViewPresenter.RemainingSeconds(deadline, now);

            _timeRemainingLabel.text = GameViewPresenter.FormatTimeRemaining(deadline, remaining);

            var phaseStart = QuizDeadlines.IntervalStartServerTime(
                _session.Phase.Value, _session.PhaseStartServerTime.Value, _session.BuzzOpenServerTime.Value);
            var fraction = GameViewPresenter.ProgressFraction(phaseStart, deadline, now);
            _timeBarFill.style.width = new StyleLength(Length.Percent((float)(fraction * 100.0)));
        }

        private void SetExitConfirmVisible(bool visible)
        {
            _exitConfirmContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
