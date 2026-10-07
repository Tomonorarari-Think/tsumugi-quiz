using System;
using System.Collections;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views
{
    /// <summary>
    /// 参加コードでホストへ接続する画面のコントローラ（docs/network.md §2.2、issue #6）。
    /// プレイヤー名・参加コードの入力検証はクライアント側の UX のためだけに行い、
    /// サーバー側の検証（<see cref="TsumugiQuiz.Network.ConnectionApprovalHandler"/>、#2/#7）を代替しない。
    /// 参加コード欄の正規化・整形・キャレット位置の再計算は <see cref="JoinCodeInputFormatter"/>
    /// （純 C#、Core）に切り出してあり、本クラスは UI Toolkit との配線・接続フローに専念する。
    /// UI 要素の取得・イベント購読の配線部分は <c>JoinView.UiBinding.cs</c>（同じ partial class）に分けている
    /// （M-9: 1 ファイル 400 行以内に収めるため）。
    /// </summary>
    public sealed partial class JoinView : IView
    {
        /// <summary>
        /// 前回入力したプレイヤー名を保存していた旧 PlayerPrefs キー。
        /// #28 で <see cref="PlayerNamePreferences"/>（<c>app-settings.json</c>）へ移行済みで、
        /// 本フィールドは移行元のキー名として <see cref="PlayerNamePreferences.LegacyPlayerPrefsKey"/> と
        /// 同じ値を指すためだけに残している（テストのキー名参照用）。
        /// </summary>
        public const string PlayerNamePrefsKey = PlayerNamePreferences.LegacyPlayerPrefsKey;

        /// <summary>
        /// 接続開始から結果（成功/拒否/失敗）が届くまでの UI 側タイムアウト（秒）。
        /// PlayMode テストから小さい値に差し替えられるよう非 const の <c>internal static</c> にしてある
        /// （M-8。<c>TsumugiQuiz.UI</c> アセンブリへの <c>InternalsVisibleTo</c> は
        /// <c>Scripts/UI/AssemblyInfo.cs</c> で宣言）。
        /// </summary>
        internal static float ConnectTimeoutSeconds = 10f;

        private ViewRouter _router;
        private NetworkService _networkService;

        private TextField _nameField;
        private TextField _codeField;
        private Label _nameErrorLabel;
        private Label _codeErrorLabel;
        private Label _statusLabel;
        private Button _connectButton;
        private Button _backButton;

        private EventCallback<ChangeEvent<string>> _nameChangedHandler;
        private EventCallback<ChangeEvent<string>> _codeChangedHandler;
        private Action _connectClickedHandler;
        private Action _backClickedHandler;

        private Action<ulong> _clientConnectedHandler;
        private Action<string> _disconnectedHandler;
        private Action _transportFailedHandler;

        private (int Version, string Ip, int Port)? _decodedEndpoint;
        private bool _isConnecting;
        private Coroutine _timeoutCoroutine;
        private Coroutine _connectCoroutine;

        public void OnShow(ViewContext context)
        {
            _router = context.Router;

            if (!BindElements(context.Root))
            {
                Debug.LogError("[JoinView] 必要な UI 要素が見つかりません。join-view.uxml を確認してください。");
                return;
            }

            _networkService = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;

            _nameField.value = PlayerNamePreferences.Load();
            _codeField.value = string.Empty;

            RegisterUiHandlers();
            SubscribeNetworkEvents();

            SetLabel(_nameErrorLabel, string.Empty);
            SetLabel(_codeErrorLabel, string.Empty);
            // L-4: NetworkService が使えない理由を画面に出す（Boot シーンを経由していないなど）。
            SetLabel(_statusLabel, _networkService == null ? JoinStatusMessages.NetworkServiceUnavailable : string.Empty);
            RefreshCodePreview();
            UpdateConnectButtonEnabled();

            // issue #8: -tq-join 指定時の起動時自動参加。
            TryAutoJoinFromCommandLine();
        }

        public void OnHide()
        {
            StopTimeoutCoroutine();
            StopConnectCoroutine();

            if (_isConnecting)
            {
                _isConnecting = false;
                _networkService?.Stop();
            }

            // M-6: 保存は接続成功時とここ（View を離れるとき）の 1 回に絞る。正規化後の値のみを保存する。
            SavePlayerNameIfValid();

            UnregisterUiHandlers();
            UnsubscribeNetworkEvents();
            ClearReferences();
        }

        private void OnNameFieldChanged(ChangeEvent<string> evt)
        {
            // M-5: 空欄はまだ入力前なのでエラーを出さない。
            var showError = !string.IsNullOrEmpty(evt.newValue) && !PlayerNameValidator.TryNormalize(evt.newValue, out _);
            SetLabel(_nameErrorLabel, showError ? ConnectionRejectionMessages.InvalidPlayerName : string.Empty);
            UpdateConnectButtonEnabled();
        }

        private void OnCodeFieldChanged(ChangeEvent<string> evt)
        {
            RefreshCodePreview();
            UpdateConnectButtonEnabled();
        }

        /// <summary>
        /// 入力中の参加コードを <see cref="JoinCodeInputFormatter"/> で整形・判定し、UI に反映する
        /// （docs/network.md §2.2 の 1〜2、H-2: キャレット位置の保持、M-4: 12 文字超過時の表示）。
        /// </summary>
        private void RefreshCodePreview()
        {
            var result = JoinCodeInputFormatter.Format(_codeField.value, _codeField.cursorIndex);

            if (_codeField.value != result.DisplayText)
            {
                _codeField.SetValueWithoutNotify(result.DisplayText);
                _codeField.SelectRange(result.CaretIndex, result.CaretIndex);
            }

            SetLabel(_codeErrorLabel, result.Error.HasValue ? JoinCodeErrorMessages.Create(result.Error.Value) : string.Empty);
            _decodedEndpoint = result.DecodedEndpoint;
        }

        private void UpdateConnectButtonEnabled()
        {
            if (_connectButton == null)
            {
                return;
            }

            var nameValid = PlayerNameValidator.TryNormalize(_nameField.value, out _);
            var canConnect = nameValid && _decodedEndpoint.HasValue && !_isConnecting && _networkService != null;
            _connectButton.SetEnabled(canConnect);
        }

        private void OnConnectClicked()
        {
            if (_isConnecting)
            {
                // 接続試行中の二重クリック等。失敗ではないためログしない。
                return;
            }

            // issue #8 レビュー H-3: 無言 return をやめ、接続を試みない理由をログする
            // （-tq-join の自動参加が失敗した場合に原因を追えるようにするため）。
            if (_networkService == null)
            {
                Debug.LogWarning("[JoinView] NetworkService が利用できないため接続を中止しました。");
                return;
            }

            if (!_decodedEndpoint.HasValue)
            {
                Debug.LogWarning($"[JoinView] 参加コードが不正なため接続を中止しました: '{_codeField?.value}'");
                return;
            }

            if (!PlayerNameValidator.TryNormalize(_nameField.value, out var normalizedName))
            {
                Debug.LogWarning($"[JoinView] プレイヤー名が不正なため接続を中止しました: '{_nameField?.value}'");
                return;
            }

            var endpoint = _decodedEndpoint.Value;

            _isConnecting = true;
            _connectButton.SetEnabled(false);
            SetLabel(_statusLabel, JoinStatusMessages.Connecting);

            _connectCoroutine = _router.StartCoroutine(_networkService.StartClientWhenReady(
                endpoint.Ip,
                (ushort)endpoint.Port,
                normalizedName,
                onCompleted: HandleStartClientResult));
        }

        private void HandleStartClientResult(NetworkStartResult result)
        {
            _connectCoroutine = null;

            if (!_isConnecting)
            {
                return;
            }

            if (!result.Success)
            {
                // StartClient 自体が同期的に失敗した時点ではネットワークは実際には開始していないことが
                // 多いが、Stop() は未開始なら何もしない安全な操作なので、他の失敗経路と同じく呼んでおく（H-1）。
                FinishConnecting(result.Message);
                return;
            }

            // ここから先は ClientConnected（成功）/ DisconnectedFromHost（拒否・切断）/
            // タイムアウトのいずれかで結果が届く（docs/network.md §2.2 の 4〜5）。
            _timeoutCoroutine = _router.StartCoroutine(ConnectTimeoutRoutine());
        }

        private IEnumerator ConnectTimeoutRoutine()
        {
            yield return new WaitForSecondsRealtime(ConnectTimeoutSeconds);

            _timeoutCoroutine = null;

            if (!_isConnecting)
            {
                yield break;
            }

            FinishConnecting(JoinStatusMessages.Timeout);
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!_isConnecting)
            {
                return;
            }

            StopTimeoutCoroutine();
            _isConnecting = false;

            // M-6: 接続成功時にも正規化後のプレイヤー名を保存する（保存タイミングは接続成功時と OnHide の 1 回ずつ）。
            SavePlayerNameIfValid();

            _router.ShowView(ViewNames.Lobby);
        }

        private void HandleDisconnectedFromHost(string reason)
        {
            if (!_isConnecting)
            {
                return;
            }

            StopTimeoutCoroutine();

            // 理由は NetworkService が日本語の文言に対応づけ済み（#208。自前の拒否理由はそのまま、NGO の英語・未知の理由は
            // 定型文）。空で来ることは無いが、念のため「拒否された」と決めつけない中立的な切断文言にする（L-3）。
            FinishConnecting(string.IsNullOrEmpty(reason) ? JoinStatusMessages.DisconnectedWithoutReason : reason);
        }

        private void HandleTransportFailed()
        {
            if (!_isConnecting)
            {
                return;
            }

            StopTimeoutCoroutine();
            FinishConnecting(JoinStatusMessages.TransportFailure);
        }

        /// <summary>
        /// 接続試行を終了し、結果メッセージを表示する。
        /// H-1: 拒否・タイムアウト・Transport 失敗のいずれの経路でも、既定で
        /// <see cref="NetworkService.Stop"/> を呼んでネットワークの状態をリセットする
        /// （未開始・未接続なら何もしない安全な操作）。
        /// </summary>
        private void FinishConnecting(string message, bool stopNetwork = true)
        {
            _isConnecting = false;

            if (stopNetwork)
            {
                _networkService?.Stop();
            }

            SetLabel(_statusLabel, message);
            UpdateConnectButtonEnabled();
        }

        private void OnBackClicked()
        {
            StopTimeoutCoroutine();
            StopConnectCoroutine();

            if (_isConnecting)
            {
                _isConnecting = false;
                _networkService?.Stop();
            }

            // M-10（統括判断）: Join の拒否時はこの画面に留まって再入力させる仕様のため、
            // 「戻る」は明示的に押したときだけ Title（または直前の履歴）へ戻る
            // （docs/network.md §2.2 手順 5、docs/architecture.md の画面遷移図を参照）。
            if (_router.CanGoBack)
            {
                _router.GoBack();
            }
            else
            {
                _router.ShowView(ViewNames.Title);
            }
        }

        private void StopTimeoutCoroutine()
        {
            if (_timeoutCoroutine != null && _router != null)
            {
                _router.StopCoroutine(_timeoutCoroutine);
            }

            _timeoutCoroutine = null;
        }

        private void StopConnectCoroutine()
        {
            if (_connectCoroutine != null && _router != null)
            {
                _router.StopCoroutine(_connectCoroutine);
            }

            _connectCoroutine = null;
        }

        /// <summary>
        /// プレイヤー名が妥当なときだけ、正規化後の値を <see cref="PlayerNamePreferences"/>
        /// （<c>app-settings.json</c>）に保存する（M-6）。
        /// </summary>
        private void SavePlayerNameIfValid()
        {
            if (_nameField == null)
            {
                return;
            }

            if (!PlayerNameValidator.TryNormalize(_nameField.value, out var normalizedName))
            {
                return;
            }

            PlayerNamePreferences.Save(normalizedName);
        }
    }
}
