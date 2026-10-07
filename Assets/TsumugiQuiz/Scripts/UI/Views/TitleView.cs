using TsumugiQuiz.Tts;
using TsumugiQuiz.UI.Views.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views
{
    /// <summary>
    /// Title View のコントローラ。「ホストとして開始」「参加コードで参加」「問題エディタ」「設定」「クレジット」の
    /// 各ボタンから遷移先 View への切り替えを行う。遷移先が未実装の View はプレースホルダ View が表示される。
    ///
    /// #25: 隅に読み上げ（TTS）の状態（Ready / 準備中 / 未確認 / 利用不可）を 1 行表示し、クリックで
    /// <see cref="TtsStatusPanel"/>（欠落時のフォールバック UI）を開く。
    ///
    /// <b>Title では読み上げの初期化を行わない（#25 H-5）</b>。隅の表示は受動的で、
    /// <see cref="TtsService.Status"/> の現状を映すだけ。初期化はパネルの「再試行（確認する）」
    /// （<see cref="TtsService.RetryInitializeAsync"/>）、または後続のロビー/ゲーム入場（#23）からのみ行われる。
    /// </summary>
    public sealed class TitleView : IView
    {
        private Button _hostButton;
        private Button _joinButton;
        private Button _questionEditorButton;
        private Button _settingsButton;
        private Button _creditsButton;
        private Button _ttsStatusButton;
        private ViewRouter _router;
        private VisualElement _root;
        private VisualElement _ttsStatusOverlay;

        /// <summary>
        /// <see cref="TtsService.StatusChanged"/> を購読した相手（#25 M-5）。<see cref="TtsService.Instance"/> を
        /// OnHide 時点で再取得すると null・別インスタンスになっている可能性があるため、購読した参照そのものを持つ。
        /// </summary>
        private TtsService _subscribedTtsService;

        public void OnShow(ViewContext context)
        {
            _router = context.Router;
            _root = context.Root;
            var root = context.Root;

            _hostButton = root.Q<Button>("host-button");
            _joinButton = root.Q<Button>("join-button");
            _questionEditorButton = root.Q<Button>("question-editor-button");
            _settingsButton = root.Q<Button>("settings-button");
            _creditsButton = root.Q<Button>("credits-button");
            _ttsStatusButton = root.Q<Button>("tts-status-button");

            RegisterClick(_hostButton, ViewNames.HostSetup);
            RegisterClick(_joinButton, ViewNames.Join);
            RegisterClick(_questionEditorButton, ViewNames.QuestionEditor);
            RegisterClick(_settingsButton, ViewNames.Settings);
            RegisterClick(_creditsButton, ViewNames.Credits);

            SetUpTtsStatus();
        }

        public void OnHide()
        {
            if (_subscribedTtsService != null)
            {
                _subscribedTtsService.StatusChanged -= OnTtsStatusChanged;
                _subscribedTtsService = null;
            }

            CloseTtsStatusOverlay();

            // ShowView() は毎回 UXML から新しいインスタンスを生成するため、このインスタンスが保持していた
            // Button 要素（とそこに登録したハンドラ）は参照が切れて GC 対象になる。明示的な解除は不要。
            _hostButton = null;
            _joinButton = null;
            _questionEditorButton = null;
            _settingsButton = null;
            _creditsButton = null;
            _ttsStatusButton = null;
            _root = null;
            _router = null;
        }

        private void RegisterClick(Button button, string destinationViewName)
        {
            if (button == null)
            {
                Debug.LogError($"[TitleView] ボタンが見つかりません（遷移先: {destinationViewName}）。UXML の name を確認してください。");
                return;
            }

            button.clicked += () => OnButtonClicked(destinationViewName);
        }

        private void OnButtonClicked(string destinationViewName)
        {
            // HostSetup / Join / QuestionEditor / Settings / Credits は本 issue の時点では未実装のため、
            // ViewRouter 側でプレースホルダ View に解決される（TODO: 各 issue で実 View に差し替える）。
            _router.ShowView(destinationViewName);
        }

        private void SetUpTtsStatus()
        {
            if (_ttsStatusButton == null)
            {
                Debug.LogError("[TitleView] tts-status-button が見つかりません。title-view.uxml を確認してください。");
                return;
            }

            // #25 H-5: ここでは EnsureInitializedAsync / RetryInitializeAsync を呼ばない。
            // 表示は現状の Status を映すだけの受動的なもの（TtsService.Instance が無ければ「未確認」）。
            var service = TtsService.Instance;
            if (service != null)
            {
                _subscribedTtsService = service;
                service.StatusChanged += OnTtsStatusChanged;
            }

            RefreshTtsStatusLabel();
            _ttsStatusButton.clicked += OnTtsStatusButtonClicked;
        }

        private void OnTtsStatusChanged(TtsServiceStatus status) => RefreshTtsStatusLabel();

        private void RefreshTtsStatusLabel()
        {
            if (_ttsStatusButton == null) return;
            _ttsStatusButton.text = TtsStatusPanel.DescribeCornerStatus(TtsService.Instance);
        }

        private void OnTtsStatusButtonClicked()
        {
            if (_root == null || _ttsStatusOverlay != null) return;

            // PR #103 再レビュー N1: SettingsView / QuestionEditorView と同じ共通ファクトリで
            // 保存済みアプリ設定（tts.*）を渡し、「再試行」でも #28 の設定が反映されるようにする。
            _ttsStatusOverlay = TtsStatusPanel.Create(
                TtsService.Instance, _router, onClosed: CloseTtsStatusOverlay,
                settingsProvider: TtsSettingsProviderFactory.BuildOrNull());

            // #25 M-8: title-root の flex レイアウト（screen-root）に間借りすると全画面を覆えないことがあるため、
            // Document.rootVisualElement 直下（= このインスタンス instance の親）に直接追加する。
            // _root は ViewRouter が Document.rootVisualElement へ Add した instance そのものなので、
            // _root.parent が Document.rootVisualElement になる。
            var attachRoot = _root.parent ?? _root;
            attachRoot.Add(_ttsStatusOverlay);
        }

        private void CloseTtsStatusOverlay()
        {
            if (_ttsStatusOverlay == null) return;

            _ttsStatusOverlay.RemoveFromHierarchy();
            _ttsStatusOverlay = null;
            RefreshTtsStatusLabel();
        }
    }
}
