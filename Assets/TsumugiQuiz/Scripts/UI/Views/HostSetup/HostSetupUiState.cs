using System.Collections.Generic;

namespace TsumugiQuiz.UI.Views.HostSetup
{
    /// <summary>
    /// HostSetup 画面の表示状態（不変）。<see cref="HostSetupPresenter.Build"/> が
    /// <c>TsumugiQuiz.Network.Nat.HostAddressInfo</c> から組み立てる。
    /// <see cref="HostSetupView"/> はこの値をそのまま UI 要素へ反映するだけにし、
    /// 「何を表示するか」の判断はここに閉じることで、UI 要素なしに EditMode でテストできるようにする。
    /// </summary>
    public sealed class HostSetupUiState
    {
        public HostSetupUiState(
            bool isResolved,
            string internetCodeText,
            bool canCopyInternetCode,
            string lanCodeText,
            bool canCopyLanCode,
            string portMappingStatusText,
            string publicIpSourceText,
            IReadOnlyList<string> warnings,
            bool showManualGuide,
            IReadOnlyList<KeyValuePair<string, string>> manualGuideRows,
            bool showManualIpInput)
        {
            IsResolved = isResolved;
            InternetCodeText = internetCodeText ?? string.Empty;
            CanCopyInternetCode = canCopyInternetCode;
            LanCodeText = lanCodeText ?? string.Empty;
            CanCopyLanCode = canCopyLanCode;
            PortMappingStatusText = portMappingStatusText ?? string.Empty;
            PublicIpSourceText = publicIpSourceText ?? string.Empty;
            Warnings = warnings ?? System.Array.Empty<string>();
            ShowManualGuide = showManualGuide;
            ManualGuideRows = manualGuideRows ?? System.Array.Empty<KeyValuePair<string, string>>();
            ShowManualIpInput = showManualIpInput;
        }

        /// <summary>到達性の解決（<c>HostConnectivityService.ResolveAsync</c>）が完了しているか。</summary>
        public bool IsResolved { get; }

        /// <summary>インターネット用参加コード欄に出すテキスト（コード本体、または未作成の理由）。</summary>
        public string InternetCodeText { get; }

        /// <summary>インターネット用コードの「コピー」ボタンを有効化できるか。</summary>
        public bool CanCopyInternetCode { get; }

        /// <summary>LAN 用参加コード欄に出すテキスト（コード本体、または未作成の理由）。</summary>
        public string LanCodeText { get; }

        /// <summary>LAN 用コードの「コピー」ボタンを有効化できるか。</summary>
        public bool CanCopyLanCode { get; }

        /// <summary>自動ポート開放の状態を表す日本語文言。</summary>
        public string PortMappingStatusText { get; }

        /// <summary>グローバル IP の取得元を表す日本語文言。</summary>
        public string PublicIpSourceText { get; }

        /// <summary>画面に並べる警告文（CGNAT・二重 NAT・グローバル IP 未取得など）。</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>手動ポート開放案内（プロトコル／外部ポート／内部ポート／LAN IP）を表示するか。</summary>
        public bool ShowManualGuide { get; }

        /// <summary>手動ポート開放案内の行（「項目名」「値」の組）。</summary>
        public IReadOnlyList<KeyValuePair<string, string>> ManualGuideRows { get; }

        /// <summary>グローバル IP の手入力欄を表示するか（自動取得・自動開放のいずれかが失敗している場合）。</summary>
        public bool ShowManualIpInput { get; }

        /// <summary>まだ解決を行っていない状態（ホスト未開始）。</summary>
        public static HostSetupUiState NotResolved { get; } = new HostSetupUiState(
            isResolved: false,
            internetCodeText: "ホストを開始すると表示されます。",
            canCopyInternetCode: false,
            lanCodeText: "ホストを開始すると表示されます。",
            canCopyLanCode: false,
            portMappingStatusText: "未実行",
            publicIpSourceText: "未取得",
            warnings: System.Array.Empty<string>(),
            showManualGuide: false,
            manualGuideRows: System.Array.Empty<KeyValuePair<string, string>>(),
            showManualIpInput: false);
    }
}
