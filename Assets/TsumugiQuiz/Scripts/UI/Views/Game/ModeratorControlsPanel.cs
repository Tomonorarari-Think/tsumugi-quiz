using System;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// 司会専用モードの進行操作パネル（#20、docs/tasks/setup-brief.md K18「次へ」「一時停止」
    /// 「強制正解/不正解」）。<see cref="GameView"/>（#14）内に組み込む
    /// 埋め込み用テンプレート（<c>Assets/TsumugiQuiz/UI/Views/moderator-controls.uxml</c>）のコントローラ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ホストかつ <c>HostRole.Moderator</c>（司会専用モード）のときだけ <see cref="GameView"/>
    /// （<c>GameView.Moderator.cs</c>）が組み込んで表示する
    /// （表示の可否は呼び出し側の責務。本クラスはパネルの組み立てと操作の送信だけを担う）。
    /// </para>
    /// <para>
    /// すべての操作は <see cref="GameSession"/> の <c>Request*</c> 経由でサーバーへ RPC を送るだけで、
    /// 実際の状態変更（一時停止・判定上書き）はサーバー権威で行われる
    /// （<c>GameSession.Moderator.cs</c>、docs/network.md §9）。
    /// </para>
    /// </remarks>
    public sealed class ModeratorControlsPanel
    {
        private const string PauseButtonText = "一時停止";
        private const string ResumeButtonText = "再開";
        private const string PausedStatusText = "一時停止中";

        private VisualElement _root;
        private GameSession _session;

        private Button _nextButton;
        private Button _pauseButton;
        private Button _forceCorrectButton;
        private Button _forceWrongButton;
        private Label _pauseStatusLabel;

        private bool _bound;

        /// <summary>
        /// テンプレートをインスタンス化し、指定した <see cref="GameSession"/> を操作するパネルを組み立てる。
        /// </summary>
        /// <param name="template"><c>moderator-controls.uxml</c>。GameView からシーン参照で渡す想定。</param>
        /// <param name="session">操作先の <see cref="GameSession"/>。</param>
        /// <returns>組み立てたパネル。<see cref="Root"/> を親の View に追加すること。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="template"/> / <paramref name="session"/> が null のとき。</exception>
        public static ModeratorControlsPanel Create(VisualTreeAsset template, GameSession session)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var panel = new ModeratorControlsPanel
            {
                _root = template.Instantiate(),
                _session = session,
            };
            panel.BindElements();
            panel.Subscribe();
            panel.Refresh();
            return panel;
        }

        /// <summary>親の View へ追加するルート要素。</summary>
        public VisualElement Root => _root;

        /// <summary>購読・イベントハンドラを解除する。View の <c>OnHide</c> から呼ぶこと。</summary>
        public void Dispose()
        {
            Unsubscribe();
            _root = null;
            _session = null;
        }

        private void BindElements()
        {
            _nextButton = _root.Q<Button>("moderator-next-button");
            _pauseButton = _root.Q<Button>("moderator-pause-button");
            _forceCorrectButton = _root.Q<Button>("moderator-force-correct-button");
            _forceWrongButton = _root.Q<Button>("moderator-force-wrong-button");
            _pauseStatusLabel = _root.Q<Label>("moderator-pause-status-label");

            if (_nextButton == null || _pauseButton == null || _forceCorrectButton == null
                || _forceWrongButton == null || _pauseStatusLabel == null)
            {
                Debug.LogError(
                    "[ModeratorControlsPanel] 必要な UI 要素が見つかりません。moderator-controls.uxml を確認してください。");
                return;
            }

            _nextButton.clicked += HandleNextClicked;
            _pauseButton.clicked += HandlePauseClicked;
            _forceCorrectButton.clicked += HandleForceCorrectClicked;
            _forceWrongButton.clicked += HandleForceWrongClicked;
            _bound = true;
        }

        private void Subscribe()
        {
            if (_session == null)
            {
                return;
            }

            _session.IsPaused.OnValueChanged += HandleIsPausedChanged;
            _session.Phase.OnValueChanged += HandlePhaseChanged;
        }

        private void Unsubscribe()
        {
            if (_session != null)
            {
                _session.IsPaused.OnValueChanged -= HandleIsPausedChanged;
                _session.Phase.OnValueChanged -= HandlePhaseChanged;
            }

            if (!_bound)
            {
                return;
            }

            _nextButton.clicked -= HandleNextClicked;
            _pauseButton.clicked -= HandlePauseClicked;
            _forceCorrectButton.clicked -= HandleForceCorrectClicked;
            _forceWrongButton.clicked -= HandleForceWrongClicked;
            _bound = false;
        }

        private void HandleIsPausedChanged(bool previous, bool current) => Refresh();

        private void HandlePhaseChanged(QuizPhase previous, QuizPhase current) => Refresh();

        private void Refresh()
        {
            if (_session == null || !_bound)
            {
                return;
            }

            var isPaused = _session.IsPaused.Value;
            _pauseButton.text = isPaused ? ResumeButtonText : PauseButtonText;

            // 統括判断 M-C: 一時停止/再開ボタンの活性条件は QuizStateMachine.CanPause を
            // 唯一の定義として、Pause 本体（QuizStateMachine.Pause）とこの ModeratorControlsPanel の
            // 2 箇所だけで参照する（受理できるのは BuzzOpen / Locked / Answering / ChoiceAnswering /
            // Judging / Result、または既に一時停止中〔＝「再開」として常に有効〕）。
            _pauseButton.SetEnabled(QuizStateMachine.CanPause(_session.Phase.Value, isPaused));

            _pauseStatusLabel.text = isPaused ? PausedStatusText : string.Empty;
            _pauseStatusLabel.style.display = isPaused ? DisplayStyle.Flex : DisplayStyle.None;

            // 「次へ」は CanPause を使わず、単純に一時停止中かどうか（!isPaused）だけで無効化する
            // （サーバー側も NextQuestion() / QuizStateMachine.StartQuestion が一時停止中は棄却する、H2）。
            // CanPause は「一時停止/再開を受理できるフェーズか」の判定であり、「次へ」の活性条件
            // （一時停止中でなければ常に有効）とは意味が異なるため、あえて共有しない。
            _nextButton.SetEnabled(!isPaused);

            // 強制正解/不正解は回答者が居る Answering 中のみ意味を持つ（サーバー側でも検証する、docs/network.md §9）。
            var canForceJudge = _session.Phase.Value == QuizPhase.Answering && !isPaused;
            _forceCorrectButton.SetEnabled(canForceJudge);
            _forceWrongButton.SetEnabled(canForceJudge);
        }

        private void HandleNextClicked() => _session?.RequestNextQuestion();

        private void HandlePauseClicked()
        {
            if (_session == null)
            {
                return;
            }

            if (_session.IsPaused.Value)
            {
                _session.RequestResume();
            }
            else
            {
                _session.RequestPause();
            }
        }

        private void HandleForceCorrectClicked() => _session?.RequestForceJudge(QuizJudgement.Correct);

        private void HandleForceWrongClicked() => _session?.RequestForceJudge(QuizJudgement.Wrong);
    }
}
