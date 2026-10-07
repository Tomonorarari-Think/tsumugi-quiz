using TsumugiQuiz.UI.TextLayout;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views
{
    /// <summary>
    /// <see cref="JoinView"/> のうち、UXML 要素の取得・イベント購読・解除だけを担う部分（M-9）。
    /// 接続フロー本体のロジックは <c>JoinView.cs</c> を参照。
    /// </summary>
    public sealed partial class JoinView
    {
        private bool BindElements(VisualElement root)
        {
            _nameField = root.Q<TextField>("player-name-field");
            _codeField = root.Q<TextField>("join-code-field");
            _nameErrorLabel = root.Q<Label>("player-name-error-label");
            _codeErrorLabel = root.Q<Label>("join-code-error-label");
            _statusLabel = root.Q<Label>("join-status-label");
            _connectButton = root.Q<Button>("connect-button");
            _backButton = root.Q<Button>("back-button");

            return _nameField != null && _codeField != null && _nameErrorLabel != null && _codeErrorLabel != null &&
                   _statusLabel != null && _connectButton != null && _backButton != null;
        }

        private void RegisterUiHandlers()
        {
            _nameChangedHandler = OnNameFieldChanged;
            _nameField.RegisterValueChangedCallback(_nameChangedHandler);

            _codeChangedHandler = OnCodeFieldChanged;
            _codeField.RegisterValueChangedCallback(_codeChangedHandler);

            _connectClickedHandler = OnConnectClicked;
            _connectButton.clicked += _connectClickedHandler;

            _backClickedHandler = OnBackClicked;
            _backButton.clicked += _backClickedHandler;
        }

        private void UnregisterUiHandlers()
        {
            if (_nameField != null && _nameChangedHandler != null)
            {
                _nameField.UnregisterValueChangedCallback(_nameChangedHandler);
            }

            if (_codeField != null && _codeChangedHandler != null)
            {
                _codeField.UnregisterValueChangedCallback(_codeChangedHandler);
            }

            if (_connectButton != null && _connectClickedHandler != null)
            {
                _connectButton.clicked -= _connectClickedHandler;
            }

            if (_backButton != null && _backClickedHandler != null)
            {
                _backButton.clicked -= _backClickedHandler;
            }
        }

        private void SubscribeNetworkEvents()
        {
            if (_networkService == null)
            {
                return;
            }

            _clientConnectedHandler = HandleClientConnected;
            _networkService.ClientConnected += _clientConnectedHandler;

            _disconnectedHandler = HandleDisconnectedFromHost;
            _networkService.DisconnectedFromHost += _disconnectedHandler;

            _transportFailedHandler = HandleTransportFailed;
            _networkService.TransportFailed += _transportFailedHandler;
        }

        private void UnsubscribeNetworkEvents()
        {
            if (_networkService == null)
            {
                return;
            }

            if (_clientConnectedHandler != null)
            {
                _networkService.ClientConnected -= _clientConnectedHandler;
            }

            if (_disconnectedHandler != null)
            {
                _networkService.DisconnectedFromHost -= _disconnectedHandler;
            }

            if (_transportFailedHandler != null)
            {
                _networkService.TransportFailed -= _transportFailedHandler;
            }
        }

        private void ClearReferences()
        {
            _router = null;
            _networkService = null;
            _nameField = null;
            _codeField = null;
            _nameErrorLabel = null;
            _codeErrorLabel = null;
            _statusLabel = null;
            _connectButton = null;
            _backButton = null;
            _nameChangedHandler = null;
            _codeChangedHandler = null;
            _connectClickedHandler = null;
            _backClickedHandler = null;
            _clientConnectedHandler = null;
            _disconnectedHandler = null;
            _transportFailedHandler = null;
            _decodedEndpoint = null;
        }

        private static void SetLabel(Label label, string message)
        {
            if (label == null)
            {
                return;
            }

            // #199: エラー・状態表示は列幅（360px）を超える文言があるため、語の途中ではなく区切りの位置で改行する。
            PhraseWrappedText.SetText(label, message);
            label.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
