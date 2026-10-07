using System;
using TsumugiQuiz.Network;
using TsumugiQuiz.Network.Nat;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.HostSetup
{
    /// <summary>
    /// <see cref="HostSetupView"/> のうち、<see cref="HostConnectivityService"/>（UPnP・グローバル IP・
    /// 参加コード）まわりの処理をまとめた部分クラス。表示テキストの組み立ては
    /// <see cref="HostSetupPresenter"/>（純 C#）に委ね、ここでは UI 要素への反映だけを行う。
    ///
    /// レビュー #5 C-1: サービスの生成・破棄は <see cref="NetworkBootstrap"/> の責務。
    /// 本クラスは <see cref="NetworkBootstrap.HostConnectivity"/> への参照を保持して
    /// イベントを購読するだけで、<see cref="UnsubscribeFromConnectivity"/> で Dispose は呼ばない。
    /// </summary>
    public sealed partial class HostSetupView
    {
        private HostConnectivityService _hostConnectivityService;
        private bool _isResolving;

        private void SubscribeToConnectivity()
        {
            var bootstrap = NetworkBootstrap.Instance;
            if (bootstrap == null)
            {
                return;
            }

            _hostConnectivityService = bootstrap.HostConnectivity;
            _hostConnectivityService.AddressChanged += OnAddressChanged;
            _hostConnectivityService.JoinCodeInvalidated += OnJoinCodeInvalidated;

            // Lobby 等から戻ってきた場合など、既にホストが動いていて到達性が解決済みのことがある。
            // その場合は再解決を待たずに現在値を即座に反映する（H-4 / C-1 の効果）。
            if (_hostConnectivityService.Current.InternalPort != 0)
            {
                ShowConnectivitySection(true);
                ApplyState(HostSetupPresenter.Build(_hostConnectivityService.Current));
                _lobbyButton.SetEnabled(true);
            }
        }

        private void UnsubscribeFromConnectivity()
        {
            if (_hostConnectivityService == null)
            {
                return;
            }

            _hostConnectivityService.AddressChanged -= OnAddressChanged;
            _hostConnectivityService.JoinCodeInvalidated -= OnJoinCodeInvalidated;
            _hostConnectivityService = null;
        }

        /// <summary>
        /// 到達性を解決する。H-4: 生成し直すのではなく、<see cref="NetworkBootstrap"/> が保持する
        /// 同一インスタンスに対して <c>ResolveAsync</c> を呼び直すだけ（初回開始・再試行の両方で使う）。
        /// M-2: 解決中の多重実行は禁止する（再試行ボタンを無効化しておく）。
        /// </summary>
        private async void ResolveConnectivity(ushort port)
        {
            // issue #161: OnHide（View 切替）が await 中に発生して _hostConnectivityService が
            // null に戻されても、以下の join-code.txt 書き出しを継続できるようローカル変数へ退避する。
            var service = _hostConnectivityService;
            if (service == null || _isResolving)
            {
                return;
            }

            _isResolving = true;
            SetRetryEnabled(false);

            // M-3: 再解決を試みる時点で、参加コード変更の警告は一旦クリアする。
            service.AcknowledgeJoinCodeChange();
            ShowJoinCodeInvalidated(string.Empty);

            var succeeded = true;
            try
            {
                await service.ResolveAsync(port);
            }
            catch (Exception ex)
            {
                succeeded = false;
                Debug.LogError($"[HostSetupView] 到達性の確認に失敗しました: {ex}");
            }

            _isResolving = false;

            var currentInfo = service.Current;

            // issue #8 レビュー M-2: 参加コードの書き出しは ApplyState（再解決・アドレス変更のたびに
            // 呼ばれる）から切り離し、-tq-host 自動開始時の最初の解決完了でだけ 1 回行う。
            //
            // issue #161: 以前はこの書き出しを「_hostStatusLabel == null（OnHide 済み＝View 切替後）
            // なら何もしない」という早期 return の後に置いていたため、到達性の解決待ち（実 UPnP 探索の
            // タイムアウト等）が長引いている間に View が作り直され OnHide が呼ばれると
            // （例: #137 のライブリロードによる再表示）、-tq-host 自動開始で 1 回だけのはずの
            // join-code.txt 書き出しがそのまま失われていた。書き出しは UI 表示の生死と無関係に
            // 行うべきものなので、UI 更新（この下のブロック）より先に、常に実行する。
            WriteJoinCodeFileIfPending(currentInfo);

            if (_hostStatusLabel == null)
            {
                // OnHide 済み（View 切替後）にコールバックが届いた場合、UI 更新はできないためここで終える。
                return;
            }

            SetRetryEnabled(true);

            // M-4: 成功・失敗にかかわらず、現在値で欄を更新する（未解決のまま放置しない）。
            ApplyState(HostSetupPresenter.Build(currentInfo));

            ShowStatus(succeeded ? string.Empty : "到達性の確認に失敗しました。ネットワーク環境を確認してください。");
            _lobbyButton.SetEnabled(succeeded);
        }

        private void OnRetryPortMappingClicked()
        {
            if (_activePort == 0 || _isResolving)
            {
                return;
            }

            ShowStatus("到達性を再確認しています…");
            ResolveConnectivity(_activePort);
        }

        private void OnApplyManualIpClicked()
        {
            if (_hostConnectivityService == null)
            {
                return;
            }

            var address = _manualIpField.value ?? string.Empty;

            // M-1: 空文字（手入力の取り消し）以外は、受け付けられるアドレスかを事前に検証する。
            if (address.Trim().Length > 0 && !HostAddressInfo.IsAcceptableManualAddress(address.Trim()))
            {
                ShowManualIpError("このアドレスは使用できません。グローバル IP、または Tailscale の 100.x アドレスを入力してください。");
                return;
            }

            ShowManualIpError(string.Empty);
            _hostConnectivityService.ApplyManualPublicIpAddress(address);

            // M-3: 手入力を適用したら参加コードが直った可能性があるので警告をクリアする。
            _hostConnectivityService.AcknowledgeJoinCodeChange();
            ShowJoinCodeInvalidated(string.Empty);
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

        /// <summary>LOW: 手動ポート開放案内の中からも LAN IP だけを単独でコピーできるようにする。</summary>
        private void OnCopyLanIpInGuideClicked()
        {
            var lanIp = _hostConnectivityService?.Current.LanIpAddress ?? string.Empty;
            if (lanIp.Length > 0)
            {
                GUIUtility.systemCopyBuffer = lanIp;
            }
        }

        private void OnAddressChanged(HostAddressInfo info)
        {
            if (_internetCodeLabel == null)
            {
                // OnHide 済み（View 切替後）にコールバックが届いた場合は何もしない。
                return;
            }

            ShowConnectivitySection(true);
            ApplyState(HostSetupPresenter.Build(info));
        }

        private void OnJoinCodeInvalidated(string message)
        {
            ShowJoinCodeInvalidated(message);
        }

        private void ApplyState(HostSetupUiState state)
        {
            _internetCodeLabel.text = state.InternetCodeText;
            _copyInternetCodeButton.SetEnabled(state.CanCopyInternetCode);

            _lanCodeLabel.text = state.LanCodeText;
            _copyLanCodeButton.SetEnabled(state.CanCopyLanCode);

            _portMappingStatusLabel.text = "自動ポート開放: " + state.PortMappingStatusText;
            _publicIpSourceLabel.text = "グローバル IP 取得元: " + state.PublicIpSourceText;

            _warningsContainer.Clear();
            foreach (var warning in state.Warnings)
            {
                var label = new Label(warning);
                label.AddToClassList("body-text");
                label.AddToClassList("host-setup-warning");
                _warningsContainer.Add(label);
            }

            _manualGuideContainer.style.display = state.ShowManualGuide ? DisplayStyle.Flex : DisplayStyle.None;
            _manualGuideRowsContainer.Clear();
            foreach (var row in state.ManualGuideRows)
            {
                var label = new Label($"{row.Key}: {row.Value}");
                label.AddToClassList("small-text");
                label.AddToClassList("manual-guide-row");
                _manualGuideRowsContainer.Add(label);
            }

            _manualIpSection.style.display = state.ShowManualIpInput ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ShowJoinCodeInvalidated(string message)
        {
            if (_joinCodeInvalidatedLabel == null)
            {
                return;
            }

            _joinCodeInvalidatedLabel.text = message ?? string.Empty;
            _joinCodeInvalidatedLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void ShowManualIpError(string message)
        {
            if (_manualIpErrorLabel == null)
            {
                return;
            }

            _manualIpErrorLabel.text = message ?? string.Empty;
            _manualIpErrorLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void SetRetryEnabled(bool enabled)
        {
            if (_retryPortMappingButton != null)
            {
                _retryPortMappingButton.SetEnabled(enabled);
            }
        }
    }
}
