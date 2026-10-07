using System;
using System.Collections;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.UI.TextLayout;
using TsumugiQuiz.UI.Views.HostSetup;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Lobby
{
    /// <summary>
    /// ロビー画面のコントローラ（docs/architecture.md §2、docs/network.md §2.3 / §2.4、issue #7）。
    /// 参加者一覧は <see cref="LobbyState"/> の <c>NetworkList</c> を購読して表示するだけで、
    /// 名簿を書き換えるのはサーバー（ホスト）だけ（クライアントは読み取り専用）。
    ///
    /// ホストのみ「ゲーム開始」「参加コードの再表示」「途中参加の許可」を操作できる。
    /// 一覧の描画と参加通知 SE は <c>LobbyView.Roster.cs</c>、「ゲーム開始」（問題データの読み込み →
    /// <see cref="GameSession.Configure"/> → <see cref="GameSession.StartSession"/> → Game View への遷移）は
    /// <c>LobbyView.GameStart.cs</c>（いずれも同じ partial class、#95）にある。
    /// </summary>
    /// <remarks>
    /// ホストから切断されたときは**自動で Title へ遷移させない**（統括判断 #7 Q1。
    /// Join View の拒否時の扱い（統括判断 M-10）と同じ方針）。自動で飛ばすと理由が読めないまま
    /// 画面が変わるため、この画面に留まって理由を表示し、「タイトルへ戻る」を押したときだけ Title へ遷移する。
    /// </remarks>
    public sealed partial class LobbyView : IView
    {
        /// <summary>
        /// <see cref="LobbyState"/> のスポーンを待つ上限（秒）。
        /// PlayMode テストから短くできるよう非 const の <c>internal static</c> にしてある
        /// （<c>InternalsVisibleTo</c> は <c>Scripts/UI/AssemblyInfo.cs</c> で宣言済み）。
        /// </summary>
        internal static float LobbyStateWaitSeconds = 10f;

        private const string LeaveButtonText = "退出";

        /// <summary>ホスト用の設定ボタン表示（編集できる）。issue #28 Phase 2（M9）。</summary>
        private const string SettingsButtonHostText = "ルーム設定";

        /// <summary>
        /// クライアント用の設定ボタン表示（閲覧のみ。統括判断の仮決め、docs/room-settings.md §7）。
        /// 画面自体は同じ Settings View で、ルーム設定の書き込みは <c>RoomSettingsSync</c> が
        /// ホスト以外を拒否する（アプリ設定・プリセットはクライアントでも編集できる）。
        /// </summary>
        private const string SettingsButtonClientText = "ルーム設定を見る";
        private const string BackToTitleButtonText = "タイトルへ戻る";
        private const string NetworkUnavailableMessage =
            "ネットワークサービスが利用できません（Boot シーンを経由して起動してください）。";
        private const string LobbyStateUnavailableMessage =
            "ロビーの情報を取得できませんでした。ホストとの接続を確認してください。";
        private const string GameSessionUnavailableMessage =
            "出題を開始できませんでした（ゲーム進行の準備ができていません）。";

        private ViewRouter _router;
        private NetworkService _networkService;
        private HostConnectivityService _hostConnectivity;
        private LobbyState _lobbyState;
        private Coroutine _lobbyCoroutine;

        private bool _isHost;
        private bool _hostConnectionLost;

        // 参加者一覧
        private VisualElement _playerList;
        private Label _playerCountLabel;
        private Label _emptyLabel;
        private Label _duplicateNameLabel;
        private Button _removeDisconnectedButton;

        // 参加コード（ホストのみ）
        private VisualElement _joinCodeSection;
        private Label _internetCodeLabel;
        private Button _copyInternetCodeButton;
        private Label _lanCodeLabel;
        private Button _copyLanCodeButton;
        private Label _joinCodeNoteLabel;

        // ルーム（ホストのみ）
        private VisualElement _roomSection;
        private Label _roleLabel;
        private Toggle _allowLateJoinToggle;

        // 操作
        private Label _statusLabel;
        private Button _startGameButton;
        private Button _settingsButton;
        private Button _leaveButton;

        private EventCallback<ChangeEvent<bool>> _allowLateJoinChangedHandler;
        private Action<string> _disconnectedHandler;
        private Action _transportFailedHandler;
        private Action<HostAddressInfo> _addressChangedHandler;
        private Action<string> _joinCodeInvalidatedHandler;

        /// <inheritdoc />
        public void OnShow(ViewContext context)
        {
            _router = context.Router;

            if (!BindElements(context.Root))
            {
                Debug.LogError("[LobbyView] 必要な UI 要素が見つかりません。lobby-view.uxml を確認してください。");
                return;
            }

            var bootstrap = NetworkBootstrap.Instance;
            _networkService = bootstrap != null ? bootstrap.Service : null;
            _isHost = _networkService != null && _networkService.IsHost;
            _hostConnectionLost = false;

            // レビュー M-6: View は Show のたびに作り直されるが、描画状態は明示的に初期化しておく
            // （前回の残りが参加通知 SE の判定に混ざらないようにする）。
            ResetRosterRenderState();

            _leaveButton.text = LeaveButtonText;
            _leaveButton.clicked += OnLeaveClicked;

            _startGameButton.style.display = _isHost ? DisplayStyle.Flex : DisplayStyle.None;
            _startGameButton.clicked += OnStartGameClicked;
            _startGameButton.SetEnabled(false);

            // M9（#28 Phase 2）: ロビーから設定画面へ。戻り先はこの画面（ViewRouter の履歴）。
            _settingsButton.text = _isHost ? SettingsButtonHostText : SettingsButtonClientText;
            _settingsButton.clicked += OnSettingsClicked;

            _joinCodeSection.style.display = _isHost ? DisplayStyle.Flex : DisplayStyle.None;
            _roomSection.style.display = _isHost ? DisplayStyle.Flex : DisplayStyle.None;
            _copyInternetCodeButton.clicked += OnCopyInternetCodeClicked;
            _copyLanCodeButton.clicked += OnCopyLanCodeClicked;

            // H-2 の緩和策: 切断中エントリの手動削除はホストだけが行える。
            _removeDisconnectedButton.style.display = DisplayStyle.None;
            _removeDisconnectedButton.clicked += OnRemoveDisconnectedClicked;

            _allowLateJoinChangedHandler = OnAllowLateJoinChanged;
            _allowLateJoinToggle.RegisterValueChangedCallback(_allowLateJoinChangedHandler);

            SubscribeNetworkEvents();

            if (_networkService == null)
            {
                // L-7: ネットワークが使えない（Boot を経由していない）状態では、ルーム設定を同期する
                // 相手も居ないので導線を隠す（切断時と同じ扱い）。
                _settingsButton.style.display = DisplayStyle.None;
                ShowStatus(NetworkUnavailableMessage);
                RenderRoster();
                return;
            }

            ShowStatus(string.Empty);

            if (_isHost)
            {
                SubscribeConnectivity(bootstrap);
                ApplyJoinCodes();
                _allowLateJoinToggle.SetValueWithoutNotify(false);
                _roleLabel.text = "あなたの役割: " + HostRoles.ToDisplayName(LoadHostRole());
            }
            else
            {
                _roleLabel.text = string.Empty;
            }

            RenderRoster();
            _lobbyCoroutine = _router.StartCoroutine(LobbyRoutine());

            // #95: 出題が始まったら（途中参加・再接続なら合流した時点で）Game View へ移れるよう、
            // GameSession が同期されしだい購読する。LobbyState 待ちとは独立させる（PR #104 M-4）。
            StartGameSessionRoutine();
        }

        /// <inheritdoc />
        public void OnHide()
        {
            StopLobbyCoroutine();
            StopGameSessionRoutine();
            UnbindLobbyState();
            UnbindGameSession();
            UnsubscribeNetworkEvents();

            if (_leaveButton != null)
            {
                _leaveButton.clicked -= OnLeaveClicked;
            }

            if (_startGameButton != null)
            {
                _startGameButton.clicked -= OnStartGameClicked;
            }

            if (_settingsButton != null)
            {
                _settingsButton.clicked -= OnSettingsClicked;
            }

            if (_copyInternetCodeButton != null)
            {
                _copyInternetCodeButton.clicked -= OnCopyInternetCodeClicked;
            }

            if (_copyLanCodeButton != null)
            {
                _copyLanCodeButton.clicked -= OnCopyLanCodeClicked;
            }

            if (_removeDisconnectedButton != null)
            {
                _removeDisconnectedButton.clicked -= OnRemoveDisconnectedClicked;
            }

            if (_allowLateJoinToggle != null && _allowLateJoinChangedHandler != null)
            {
                _allowLateJoinToggle.UnregisterValueChangedCallback(_allowLateJoinChangedHandler);
            }

            _allowLateJoinChangedHandler = null;

            // HostConnectivityService の所有は NetworkBootstrap（#5 C-1）。購読を外して参照を落とすだけ。
            UnsubscribeConnectivity();
            _networkService = null;
            _router = null;

            _playerList = null;
            _playerCountLabel = null;
            _emptyLabel = null;
            _duplicateNameLabel = null;
            _removeDisconnectedButton = null;
            _joinCodeSection = null;
            _internetCodeLabel = null;
            _copyInternetCodeButton = null;
            _lanCodeLabel = null;
            _copyLanCodeButton = null;
            _joinCodeNoteLabel = null;
            _roomSection = null;
            _roleLabel = null;
            _allowLateJoinToggle = null;
            _statusLabel = null;
            _startGameButton = null;
            _settingsButton = null;
            _leaveButton = null;
        }

        private bool BindElements(VisualElement root)
        {
            _playerList = root.Q<VisualElement>("lobby-player-list");
            _playerCountLabel = root.Q<Label>("lobby-player-count-label");
            _emptyLabel = root.Q<Label>("lobby-empty-label");
            // #206: 参加者の名前（ほかの参加者が決める文字列）を含むため、リッチテキストとして解釈させない。
            _duplicateNameLabel = PlainText.Apply(root.Q<Label>("lobby-duplicate-name-label"));
            _removeDisconnectedButton = root.Q<Button>("remove-disconnected-button");

            _joinCodeSection = root.Q<VisualElement>("lobby-join-code-section");
            _internetCodeLabel = root.Q<Label>("lobby-internet-code-label");
            _copyInternetCodeButton = root.Q<Button>("lobby-copy-internet-code-button");
            _lanCodeLabel = root.Q<Label>("lobby-lan-code-label");
            _copyLanCodeButton = root.Q<Button>("lobby-copy-lan-code-button");
            _joinCodeNoteLabel = root.Q<Label>("lobby-join-code-note-label");

            _roomSection = root.Q<VisualElement>("lobby-room-section");
            _roleLabel = root.Q<Label>("lobby-role-label");
            _allowLateJoinToggle = root.Q<Toggle>("lobby-allow-late-join-toggle");

            // #206: ホストから届く切断理由を表示するため、リッチテキストとして解釈させない。
            _statusLabel = PlainText.Apply(root.Q<Label>("lobby-status-label"));
            _startGameButton = root.Q<Button>("start-game-button");
            _settingsButton = root.Q<Button>("lobby-settings-button");
            _leaveButton = root.Q<Button>("leave-button");

            return _playerList != null && _playerCountLabel != null && _emptyLabel != null
                   && _duplicateNameLabel != null && _removeDisconnectedButton != null
                   && _joinCodeSection != null && _internetCodeLabel != null && _copyInternetCodeButton != null
                   && _lanCodeLabel != null && _copyLanCodeButton != null && _joinCodeNoteLabel != null
                   && _roomSection != null && _roleLabel != null && _allowLateJoinToggle != null
                   && _statusLabel != null && _startGameButton != null && _settingsButton != null
                   && _leaveButton != null;
        }

        /// <summary>
        /// <see cref="LobbyState"/> のスポーン（ホストなら自分が、クライアントならサーバーからの同期）を待ち、
        /// 見つかったら購読を始めて、以後は毎フレーム「保留中の再描画」を 1 回だけ流す。
        /// 接続直後は数フレーム遅れて届くため、見つかるまで毎フレーム探す。
        ///
        /// 再描画をこのコルーチンに集めているのは、<see cref="LobbyState.RosterChanged"/> が
        /// 1 回の変更で複数回発火し、その途中では名簿が空に見えるため（<c>LobbyView.Roster.cs</c> 参照）。
        /// </summary>
        private IEnumerator LobbyRoutine()
        {
            LobbyState lobby = null;
            var elapsed = 0f;
            while (elapsed < LobbyStateWaitSeconds)
            {
                var bootstrap = NetworkBootstrap.Instance;
                var candidate = bootstrap != null ? bootstrap.Lobby : null;
                if (candidate != null && candidate.IsSpawned)
                {
                    lobby = candidate;
                    break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (lobby == null)
            {
                _lobbyCoroutine = null;

                if (_statusLabel != null && !_hostConnectionLost)
                {
                    ShowStatus(LobbyStateUnavailableMessage);
                }

                yield break;
            }

            BindLobbyState(lobby);

            while (true)
            {
                yield return null;
                FlushPendingRosterRender();
            }
        }

        private void SubscribeNetworkEvents()
        {
            if (_networkService == null)
            {
                return;
            }

            _disconnectedHandler = HandleDisconnectedFromHost;
            _transportFailedHandler = HandleTransportFailed;
            _networkService.DisconnectedFromHost += _disconnectedHandler;
            _networkService.TransportFailed += _transportFailedHandler;
        }

        private void UnsubscribeNetworkEvents()
        {
            if (_networkService == null)
            {
                return;
            }

            if (_disconnectedHandler != null)
            {
                _networkService.DisconnectedFromHost -= _disconnectedHandler;
            }

            if (_transportFailedHandler != null)
            {
                _networkService.TransportFailed -= _transportFailedHandler;
            }

            _disconnectedHandler = null;
            _transportFailedHandler = null;
        }

        /// <summary>
        /// ホストから切断された（ホストの終了・ネットワーク断・キック）。
        /// 統括判断 #7 Q1 により、自動遷移せずこの画面で理由を表示し、
        /// 操作を「タイトルへ戻る」だけに絞る。理由は <c>NetworkService</c> が日本語の文言に対応づけ済み
        /// （#208。例: ホストの退出は「ホストがゲームを終了しました。」）。自分で退出したときは通知されない。
        /// </summary>
        private void HandleDisconnectedFromHost(string reason)
        {
            if (_statusLabel == null)
            {
                return;
            }

            _hostConnectionLost = true;
            StopLobbyCoroutine();
            UnbindLobbyState();

            ShowStatus(string.IsNullOrEmpty(reason)
                ? JoinStatusMessages.DisconnectedWithoutReason
                : reason);

            _startGameButton.style.display = DisplayStyle.None;
            _joinCodeSection.style.display = DisplayStyle.None;
            _roomSection.style.display = DisplayStyle.None;
            // M9（#28 Phase 2）: 切断後は操作を「タイトルへ戻る」だけに絞る方針（統括判断 #7 Q1）なので、
            // ルーム設定への導線も隠す（同期先が無い状態で開いても意味がないため）。
            _settingsButton.style.display = DisplayStyle.None;
            _leaveButton.text = BackToTitleButtonText;

            RenderRoster();
        }

        private void HandleTransportFailed()
        {
            if (_statusLabel == null)
            {
                return;
            }

            _hostConnectionLost = true;
            StopLobbyCoroutine();
            UnbindLobbyState();

            ShowStatus(JoinStatusMessages.TransportFailure);
            _startGameButton.style.display = DisplayStyle.None;
            _settingsButton.style.display = DisplayStyle.None;
            _leaveButton.text = BackToTitleButtonText;
            RenderRoster();
        }

        private void OnLeaveClicked()
        {
            // ホストなら Shutdown で全クライアントへ切断が伝播する（docs/network.md §2.4）。
            // クライアントなら自分だけが抜ける。どちらも Stop() 1 回で足りる。
            _networkService?.Stop();

            var router = _router;
            StopLobbyCoroutine();
            UnbindLobbyState();

            router?.ShowView(ViewNames.Title);
        }

        private void OnCopyInternetCodeClicked()
        {
            if (_copyInternetCodeButton.enabledSelf)
            {
                GUIUtility.systemCopyBuffer = _internetCodeLabel.text;
            }
        }

        private void OnCopyLanCodeClicked()
        {
            if (_copyLanCodeButton.enabledSelf)
            {
                GUIUtility.systemCopyBuffer = _lanCodeLabel.text;
            }
        }

        private void OnAllowLateJoinChanged(ChangeEvent<bool> evt)
        {
            _lobbyState?.SetAllowLateJoin(evt.newValue);
        }

        /// <summary>
        /// 到達性サービス（UPnP・グローバル IP）の更新を購読する。ロビー滞在中に
        /// 外部ポートが変わって参加コードが作り直されることがあるため（レビュー LOW）。
        /// サービスの所有は <see cref="NetworkBootstrap"/>（#5 C-1）なので Dispose はしない。
        /// </summary>
        private void SubscribeConnectivity(NetworkBootstrap bootstrap)
        {
            if (bootstrap == null)
            {
                return;
            }

            _hostConnectivity = bootstrap.HostConnectivity;
            if (_hostConnectivity == null)
            {
                return;
            }

            _addressChangedHandler = HandleAddressChanged;
            _joinCodeInvalidatedHandler = HandleJoinCodeInvalidated;
            _hostConnectivity.AddressChanged += _addressChangedHandler;
            _hostConnectivity.JoinCodeInvalidated += _joinCodeInvalidatedHandler;
        }

        private void UnsubscribeConnectivity()
        {
            if (_hostConnectivity != null)
            {
                if (_addressChangedHandler != null)
                {
                    _hostConnectivity.AddressChanged -= _addressChangedHandler;
                }

                if (_joinCodeInvalidatedHandler != null)
                {
                    _hostConnectivity.JoinCodeInvalidated -= _joinCodeInvalidatedHandler;
                }
            }

            _addressChangedHandler = null;
            _joinCodeInvalidatedHandler = null;
            _hostConnectivity = null;
        }

        private void HandleAddressChanged(HostAddressInfo info)
        {
            if (_internetCodeLabel == null)
            {
                // OnHide 済み（View 切替後）にコールバックが届いた場合は何もしない。
                return;
            }

            ApplyJoinCodes();
        }

        private void HandleJoinCodeInvalidated(string message)
        {
            if (_joinCodeNoteLabel == null)
            {
                return;
            }

            _joinCodeNoteLabel.text = message ?? string.Empty;
            _joinCodeNoteLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// 切断中のエントリを保持期間の満了を待たずに削除する（ホストのみ。レビュー H-2 の緩和策）。
        /// 「同名の別人が席を奪える」弱点（仮決め K-N1）に気づいたホストが、席を解放したり
        /// 怪しいエントリを片付けたりできるようにする。
        /// </summary>
        private void OnRemoveDisconnectedClicked()
        {
            if (!_isHost || _lobbyState == null)
            {
                return;
            }

            var removed = _lobbyState.RemoveDisconnectedEntries();
            ShowStatus(removed > 0 ? $"切断中のエントリを {removed} 件削除しました。" : string.Empty);
        }

        /// <summary>
        /// ホストの参加コードを再表示する。表示文言の組み立ては HostSetup 画面と同じ
        /// <see cref="HostSetupPresenter"/>（純 C#）を使い回す。
        /// </summary>
        private void ApplyJoinCodes()
        {
            if (_hostConnectivity == null)
            {
                return;
            }

            var state = HostSetupPresenter.Build(_hostConnectivity.Current);

            _internetCodeLabel.text = state.InternetCodeText;
            _copyInternetCodeButton.SetEnabled(state.CanCopyInternetCode);
            _lanCodeLabel.text = state.LanCodeText;
            _copyLanCodeButton.SetEnabled(state.CanCopyLanCode);

            var outdated = _hostConnectivity.IsJoinCodeOutdated;
            _joinCodeNoteLabel.text = outdated
                ? "外部ポートが変わったため、配布済みの参加コードが使えなくなっている可能性があります。ホスト設定画面で再確認してください。"
                : string.Empty;
            _joinCodeNoteLabel.style.display = outdated ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// 設定画面（#28）へ移動する。<see cref="ViewRouter"/> の履歴に積むので、
        /// Settings 画面の「戻る」でこのロビーへ戻ってくる（M9）。
        /// </summary>
        private void OnSettingsClicked() => _router?.ShowView(ViewNames.Settings);

        /// <summary>
        /// <c>host.role</c> のフォールバック読み出し（<see cref="HostSetupPreferences"/>、PlayerPrefs）。
        /// <see cref="LobbyState"/> がまだ見つかっていない間だけ使い、見つかったあとは
        /// <c>LobbyState.Role</c>（<c>RoomSettingsSync</c> → <c>RoomSettingsApplier</c> が書く値）を読む
        /// （<c>ApplyRoomLabels</c>）。
        /// </summary>
        private static HostRole LoadHostRole() => HostSetupPreferences.LoadHostRole();

        private void ShowStatus(string message)
        {
            if (_statusLabel == null)
            {
                return;
            }

            _statusLabel.text = message ?? string.Empty;
            _statusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void StopLobbyCoroutine()
        {
            if (_lobbyCoroutine != null && _router != null)
            {
                _router.StopCoroutine(_lobbyCoroutine);
            }

            _lobbyCoroutine = null;
        }
    }
}
