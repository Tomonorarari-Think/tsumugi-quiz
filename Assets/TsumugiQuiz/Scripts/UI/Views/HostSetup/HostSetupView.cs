using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Network.Nat;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.HostSetup
{
    /// <summary>
    /// HostSetup 画面のコントローラ(issue #5)。プレイヤー名・役割・ポートを受け取って
    /// <see cref="NetworkService.StartHostWhenReady"/> でホストを開始し、
    /// <see cref="NetworkBootstrap"/> が保持する <see cref="HostConnectivityService"/>
    /// （UPnP 状態・グローバル IP・参加コード）を購読する。
    /// 問題フォルダの読み込み状況表示・再読込・フォルダを開くは <see cref="Questions.QuestionLibrary"/> を
    /// 本 View が生成して扱う（統括メモ、#29 からの引き継ぎ）。「ロビーへ」は
    /// <see cref="ViewNames.Lobby"/>（<c>LobbyView</c>、#7）へ遷移する。
    ///
    /// レビュー #5 C-1: <see cref="HostConnectivityService"/> の生成・破棄は本 View の責務ではない
    /// （<see cref="NetworkBootstrap"/> が Boot シーンに常駐して行う）。本 View は
    /// <see cref="NetworkBootstrap.HostConnectivity"/> のイベント購読と <c>ResolveAsync</c> の呼び出しだけを
    /// 行い、<see cref="OnHide"/> では購読解除のみを行う（Dispose しない）。これにより、ホストを開始した
    /// まま Lobby 等へ進んでもポートマッピングの更新ループが生き続ける。
    /// </summary>
    public sealed partial class HostSetupView : IView
    {
        private ViewRouter _router;
        private NetworkService _networkService;

        // 入力
        private TextField _playerNameField;
        private Toggle _hostOnlyToggle;
        private IntegerField _portField;
        private Button _startHostButton;
        private Label _hostStatusLabel;

        // 到達性
        private VisualElement _connectivitySection;
        private Label _internetCodeLabel;
        private Button _copyInternetCodeButton;
        private Label _lanCodeLabel;
        private Button _copyLanCodeButton;
        private Label _portMappingStatusLabel;
        private Label _publicIpSourceLabel;
        private VisualElement _warningsContainer;
        private VisualElement _manualGuideContainer;
        private VisualElement _manualGuideRowsContainer;
        private Button _copyLanIpInGuideButton;
        private Button _retryPortMappingButton;
        private VisualElement _manualIpSection;
        private TextField _manualIpField;
        private Label _manualIpErrorLabel;
        private Button _applyManualIpButton;
        private Label _joinCodeInvalidatedLabel;

        // 問題データ
        private Label _questionSummaryLabel;
        private VisualElement _questionIssuesContainer;
        private Label _questionStatusLabel;
        private Button _reloadQuestionsButton;
        private Button _openQuestionFolderButton;

        // 遷移
        private Button _lobbyButton;
        private Button _backButton;
        private VisualElement _backConfirmContainer;
        private Button _confirmBackButton;
        private Button _cancelBackButton;

        private ushort _activePort;

        /// <summary>
        /// issue #155 M-a: <c>host.role</c> の保存に失敗した旨の警告メッセージ（成功なら null）。
        /// <see cref="OnStartHostClicked"/> で確定し、直後の「開始しています…」表示と、
        /// 続いて届く <see cref="OnHostStartCompleted"/> の成功メッセージの両方に付ける
        /// （成功メッセージが警告を即座に上書きしてしまわないようにするため）。
        /// </summary>
        private string _pendingHostRoleSaveWarning;

        public void OnShow(ViewContext context)
        {
            _router = context.Router;
            var root = context.Root;

            _playerNameField = root.Q<TextField>("player-name-field");
            _hostOnlyToggle = root.Q<Toggle>("host-only-toggle");
            _portField = root.Q<IntegerField>("port-field");
            _startHostButton = root.Q<Button>("start-host-button");
            _hostStatusLabel = root.Q<Label>("host-status-label");

            _connectivitySection = root.Q<VisualElement>("connectivity-section");
            _internetCodeLabel = root.Q<Label>("internet-code-label");
            _copyInternetCodeButton = root.Q<Button>("copy-internet-code-button");
            _lanCodeLabel = root.Q<Label>("lan-code-label");
            _copyLanCodeButton = root.Q<Button>("copy-lan-code-button");
            _portMappingStatusLabel = root.Q<Label>("port-mapping-status-label");
            _publicIpSourceLabel = root.Q<Label>("public-ip-source-label");
            _warningsContainer = root.Q<VisualElement>("warnings-container");
            _manualGuideContainer = root.Q<VisualElement>("manual-guide-container");
            _manualGuideRowsContainer = root.Q<VisualElement>("manual-guide-rows-container");
            _copyLanIpInGuideButton = root.Q<Button>("copy-lan-ip-in-guide-button");
            _retryPortMappingButton = root.Q<Button>("retry-port-mapping-button");
            _manualIpSection = root.Q<VisualElement>("manual-ip-section");
            _manualIpField = root.Q<TextField>("manual-ip-field");
            _manualIpErrorLabel = root.Q<Label>("manual-ip-error-label");
            _applyManualIpButton = root.Q<Button>("apply-manual-ip-button");
            _joinCodeInvalidatedLabel = root.Q<Label>("join-code-invalidated-label");

            _questionSummaryLabel = root.Q<Label>("question-summary-label");
            _questionIssuesContainer = root.Q<VisualElement>("question-issues-container");
            _questionStatusLabel = root.Q<Label>("question-status-label");
            _reloadQuestionsButton = root.Q<Button>("reload-questions-button");
            _openQuestionFolderButton = root.Q<Button>("open-question-folder-button");

            _lobbyButton = root.Q<Button>("lobby-button");
            _backButton = root.Q<Button>("back-button");
            _backConfirmContainer = root.Q<VisualElement>("back-confirm-container");
            _confirmBackButton = root.Q<Button>("confirm-back-button");
            _cancelBackButton = root.Q<Button>("cancel-back-button");

            if (_playerNameField == null || _portField == null || _startHostButton == null || _hostStatusLabel == null
                || _connectivitySection == null || _internetCodeLabel == null || _copyInternetCodeButton == null
                || _lanCodeLabel == null || _copyLanCodeButton == null || _portMappingStatusLabel == null
                || _publicIpSourceLabel == null || _warningsContainer == null || _manualGuideContainer == null
                || _manualGuideRowsContainer == null || _copyLanIpInGuideButton == null || _retryPortMappingButton == null
                || _manualIpSection == null || _manualIpField == null || _manualIpErrorLabel == null || _applyManualIpButton == null
                || _questionSummaryLabel == null || _questionIssuesContainer == null || _questionStatusLabel == null
                || _reloadQuestionsButton == null || _openQuestionFolderButton == null
                || _lobbyButton == null || _backButton == null || _backConfirmContainer == null
                || _confirmBackButton == null || _cancelBackButton == null)
            {
                Debug.LogError("[HostSetupView] 必要な UI 要素が見つかりません。host-setup-view.uxml を確認してください。");
                return;
            }

            _networkService = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;
            _pendingHostRoleSaveWarning = null;

            LoadPreferences();
            ShowConnectivitySection(false);
            SetBackConfirmVisible(false);
            ShowStatus(string.Empty);
            ShowManualIpError(string.Empty);
            ShowQuestionStatus(string.Empty);
            _lobbyButton.SetEnabled(false);

            _startHostButton.clicked += OnStartHostButtonClicked;
            _retryPortMappingButton.clicked += OnRetryPortMappingClicked;
            _applyManualIpButton.clicked += OnApplyManualIpClicked;
            _copyInternetCodeButton.clicked += OnCopyInternetCodeClicked;
            _copyLanCodeButton.clicked += OnCopyLanCodeClicked;
            _copyLanIpInGuideButton.clicked += OnCopyLanIpInGuideClicked;
            _reloadQuestionsButton.clicked += OnReloadQuestionsClicked;
            _openQuestionFolderButton.clicked += OnOpenQuestionFolderClicked;
            _lobbyButton.clicked += OnLobbyClicked;
            _backButton.clicked += OnBackClicked;
            _confirmBackButton.clicked += OnConfirmBackClicked;
            _cancelBackButton.clicked += OnCancelBackClicked;

            SubscribeToConnectivity();
            InitializeQuestionLibrary();

            // issue #8: -tq-host 指定時の起動時自動ホスト開始。上記の購読・読み込みが終わった後に行う。
            TryAutoStartFromCommandLine();
        }

        public void OnHide()
        {
            if (_startHostButton != null)
            {
                _startHostButton.clicked -= OnStartHostButtonClicked;
            }

            if (_retryPortMappingButton != null)
            {
                _retryPortMappingButton.clicked -= OnRetryPortMappingClicked;
            }

            if (_applyManualIpButton != null)
            {
                _applyManualIpButton.clicked -= OnApplyManualIpClicked;
            }

            if (_copyInternetCodeButton != null)
            {
                _copyInternetCodeButton.clicked -= OnCopyInternetCodeClicked;
            }

            if (_copyLanCodeButton != null)
            {
                _copyLanCodeButton.clicked -= OnCopyLanCodeClicked;
            }

            if (_copyLanIpInGuideButton != null)
            {
                _copyLanIpInGuideButton.clicked -= OnCopyLanIpInGuideClicked;
            }

            if (_reloadQuestionsButton != null)
            {
                _reloadQuestionsButton.clicked -= OnReloadQuestionsClicked;
            }

            if (_openQuestionFolderButton != null)
            {
                _openQuestionFolderButton.clicked -= OnOpenQuestionFolderClicked;
            }

            if (_lobbyButton != null)
            {
                _lobbyButton.clicked -= OnLobbyClicked;
            }

            if (_backButton != null)
            {
                _backButton.clicked -= OnBackClicked;
            }

            if (_confirmBackButton != null)
            {
                _confirmBackButton.clicked -= OnConfirmBackClicked;
            }

            if (_cancelBackButton != null)
            {
                _cancelBackButton.clicked -= OnCancelBackClicked;
            }

            // C-1: HostConnectivityService の所有は NetworkBootstrap にあるため、ここでは
            // イベント購読の解除だけを行う（Dispose しない。Lobby 等へ進んだ後もマッピングの
            // 更新ループを生かし続けるため）。
            UnsubscribeFromConnectivity();
            DisposeQuestionLibrary();

            _router = null;
            _networkService = null;

            _playerNameField = null;
            _hostOnlyToggle = null;
            _portField = null;
            _startHostButton = null;
            _hostStatusLabel = null;

            _connectivitySection = null;
            _internetCodeLabel = null;
            _copyInternetCodeButton = null;
            _lanCodeLabel = null;
            _copyLanCodeButton = null;
            _portMappingStatusLabel = null;
            _publicIpSourceLabel = null;
            _warningsContainer = null;
            _manualGuideContainer = null;
            _manualGuideRowsContainer = null;
            _copyLanIpInGuideButton = null;
            _retryPortMappingButton = null;
            _manualIpSection = null;
            _manualIpField = null;
            _manualIpErrorLabel = null;
            _applyManualIpButton = null;
            _joinCodeInvalidatedLabel = null;

            _questionSummaryLabel = null;
            _questionIssuesContainer = null;
            _questionStatusLabel = null;
            _reloadQuestionsButton = null;
            _openQuestionFolderButton = null;

            _lobbyButton = null;
            _backButton = null;
            _backConfirmContainer = null;
            _confirmBackButton = null;
            _cancelBackButton = null;
        }

        private void LoadPreferences()
        {
            _playerNameField.value = PlayerNamePreferences.Load();
            _portField.value = HostSetupPreferences.LoadPort();
            if (_hostOnlyToggle != null)
            {
                _hostOnlyToggle.value = HostSetupPreferences.LoadHostRole() == HostRole.Moderator;
            }
        }

        // issue #15 レビュー LOW: Button.clicked（Action）へメソッドグループとして登録するための
        // 戻り値なしラッパー（OnStartHostClicked は bool を返すため直接登録できない）。
        private void OnStartHostButtonClicked() => OnStartHostClicked();

        // issue #15 レビュー L: 早期 return（バリデーション失敗・NetworkService 未接続）を
        // 呼び出し側（自動ホスト開始 TryAutoStartFromCommandLine）が判別できるよう bool を返す。
        // true = ホスト開始のコルーチンを実際に開始した、false = 何も開始せず終了した。
        private bool OnStartHostClicked()
        {
            if (_networkService == null)
            {
                ShowStatus("ネットワークサービスが利用できません（Boot シーンを経由して起動してください）。");
                return false;
            }

            if (!PlayerNameValidator.TryNormalize(_playerNameField.value, out var normalizedName))
            {
                // #209: 規則の要約は PlayerNameValidator.RuleSummary を 1 か所の出所にする。
                ShowStatus("プレイヤー名を入力してください（" + PlayerNameValidator.RuleSummary + "）。");
                return false;
            }

            // M-6: ポートは 0（OS に自動選択させる）か 1024〜65535 のみ受け付ける。
            // 1〜1023 はよく知られたポート（well-known ports）で、権限やアプリ間の衝突の
            // 問題が起きやすいため対象外にする。
            var rawPort = _portField.value;
            if (rawPort != 0 && (rawPort < 1024 || rawPort > 65535))
            {
                ShowStatus("ポート番号は 0（自動）または 1024〜65535 の範囲で指定してください。");
                return false;
            }

            var port = (ushort)rawPort;
            var hostOnly = _hostOnlyToggle != null && _hostOnlyToggle.value;

            // M-7: 保存するのは正規化後の名前（前後の空白を除いたもの）。
            PlayerNamePreferences.Save(normalizedName);
            // port が 0（OS 自動選択）のときは「次回以降の既定値」としては保存しない（H2、AppSettings.NetworkPort
            // の範囲外のため）。その回だけ OS に選ばせたい、という単発の指定として扱う。
            if (port != 0)
            {
                HostSetupPreferences.SavePort(port);
            }

            // issue #155 M-2/H-1（再レビュー）: HostRolePreference.Save はファイル書き込み前に
            // プロセス内キャッシュを更新するため、app-settings.json への永続化に失敗しても
            // 今回のセッションは _hostOnlyToggle の値どおりに開始される（HostRolePreference.Load が
            // 直後にこのキャッシュを読む）。失敗するのは「次回起動時の初期値としての保存」だけ。
            var hostRoleSaveResult = HostSetupPreferences.SaveHostRole(hostOnly ? HostRole.Moderator : HostRole.Player);

            // M-a: 生の例外・警告文字列はユーザーへ見せず、詳細は HostRolePreference.Save 内の
            // Debug.LogWarning に任せる。ここでは短い定型文だけを保持し、直後の「開始しています…」表示と
            // OnHostStartCompleted の成功メッセージの両方へ付ける（成功メッセージによる即時上書きを防ぐ）。
            _pendingHostRoleSaveWarning = hostRoleSaveResult.Success
                ? null
                : "設定を保存できませんでした（詳細はログ）。今回はこの設定で開始します";

            // H-3: 開始が完了するまでは、途中で操作されると状態管理が複雑になる「戻る」「ロビーへ」も
            // 無効化しておく（ロビーへは既定で無効だが、念のため明示する）。
            SetFormEnabled(false);
            _backButton.SetEnabled(false);
            _lobbyButton.SetEnabled(false);

            var startingMessage = "ホストを開始しています…";
            if (_pendingHostRoleSaveWarning != null)
            {
                startingMessage += $"（{_pendingHostRoleSaveWarning}）";
            }

            ShowStatus(startingMessage);

            // H-3: OnHostStartCompleted が届く前に View が破棄されている可能性があるため、
            // どの NetworkService に対して開始したかをクロージャで捕まえておく
            // （その時点で孤立してしまったホストを停止できるようにするため）。
            var networkService = _networkService;
            _router.StartCoroutine(networkService.StartHostWhenReady(
                port,
                // 定員の最終的な権威は名簿を持つ LobbyState / LobbyRoster（#7）だが、
                // ホスト開始から LobbyState がスポーンするまでの数フレームは名簿が無い。
                // その隙間の保険として既定の上限を渡しておく（レビュー H-3）。
                // 名簿が立った時点で LobbyState.AttachServer が MaxPlayers = null に戻し、
                // 判定を名簿へ一本化する。TODO(#27): RoomSettings（room.maxPlayers）と接続する。
                maxPlayers: LobbyRoster.DefaultMaxPlayers,
                hostPlayerName: normalizedName,
                onCompleted: result => OnHostStartCompleted(result, networkService)));

            return true;
        }

        private void OnHostStartCompleted(NetworkStartResult result, NetworkService networkService)
        {
            if (_hostStatusLabel == null)
            {
                // H-3: OnHide が既に呼ばれた後（View 切替後）にコールバックが届いた場合、
                // 誰も参照を持たないまま起動してしまったホストを孤立させないよう、ここで停止する。
                if (result.Success)
                {
                    networkService.Stop();
                }

                return;
            }

            _backButton.SetEnabled(true);

            // issue #8 レビュー L-1: -tq-host 自動開始の成否をここで確定させる
            // （成功時のみ HostSetupView.LaunchOptions.cs 側で参加コード書き出し待ちのフラグを立てる）。
            HandleAutoHostStartResult(result.Success);

            if (!result.Success)
            {
                SetFormEnabled(true);
                ShowStatus(result.Message);
                _pendingHostRoleSaveWarning = null;
                return;
            }

            _activePort = result.Port;

            // issue #8 レビュー M-3: 実際にバインドされたポートをログする（-tq-port 0 は要求値に
            // すぎず、実ポートはここで初めて確定するため）。NetworkService.StartHost 自体に
            // ログを足すと、UI を経由しない各種テストフィクスチャ（GameSessionTestFixture 等）の
            // 厳密な LogAssert.Expect 順序を崩すことが実測で判明したため、UI 経由の開始
            // （手動クリック・-tq-host 自動開始の両方）に限定してここでログする。
            Debug.Log($"[HostSetupView] ホストを開始しました activePort={result.Port}");

            // M-a: OnStartHostClicked で保持した host.role 保存失敗の警告を、成功メッセージが
            // 即座に上書きしてしまわないよう、ここで引き継いで付ける。
            var completedMessage = $"ホストを開始しました（ポート {result.Port}）。到達性を確認しています…";
            if (_pendingHostRoleSaveWarning != null)
            {
                completedMessage += $"（{_pendingHostRoleSaveWarning}）";
                _pendingHostRoleSaveWarning = null;
            }

            ShowStatus(completedMessage);
            ShowConnectivitySection(true);
            ResolveConnectivity(_activePort);
        }

        private void OnLobbyClicked()
        {
            _router.ShowView(ViewNames.Lobby);
        }

        private void OnBackClicked()
        {
            if (_networkService != null && (_networkService.IsListening || _networkService.IsClient))
            {
                SetBackConfirmVisible(true);
                return;
            }

            NavigateBack();
        }

        private void OnConfirmBackClicked()
        {
            SetBackConfirmVisible(false);
            StopHostAndNavigateBack();
        }

        private void OnCancelBackClicked()
        {
            SetBackConfirmVisible(false);
        }

        private void StopHostAndNavigateBack()
        {
            // C-1: ポートマッピングの解放は NetworkService.Stopped を購読している
            // NetworkBootstrap の責務になったため、ここでは Stop() を呼ぶだけでよい。
            _networkService?.Stop();

            // M-8: 履歴が無い場合は Title へフォールバックする NavigateBack() に統一する。
            NavigateBack();
        }

        private void NavigateBack()
        {
            if (_router == null)
            {
                // Stop() 呼び出し後、View が既に破棄されている（OnHide 済み）場合。
                return;
            }

            if (_router.CanGoBack)
            {
                _router.GoBack();
            }
            else
            {
                _router.ShowView(ViewNames.Title);
            }
        }

        private void SetFormEnabled(bool enabled)
        {
            _playerNameField.SetEnabled(enabled);
            _portField.SetEnabled(enabled);
            _hostOnlyToggle?.SetEnabled(enabled);
            _startHostButton.SetEnabled(enabled);
        }

        private void ShowStatus(string message)
        {
            _hostStatusLabel.text = message ?? string.Empty;
            _hostStatusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void ShowConnectivitySection(bool visible)
        {
            _connectivitySection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetBackConfirmVisible(bool visible)
        {
            _backConfirmContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
