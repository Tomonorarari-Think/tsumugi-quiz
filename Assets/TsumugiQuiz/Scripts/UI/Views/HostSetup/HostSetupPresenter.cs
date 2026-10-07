using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Questions;

namespace TsumugiQuiz.UI.Views.HostSetup
{
    /// <summary>
    /// <c>HostAddressInfo</c>（#3 <c>HostConnectivityService</c> の結果）から HostSetup 画面の
    /// 表示状態（<see cref="HostSetupUiState"/>）を組み立てる。Unity API に依存しない純 C# のため、
    /// EditMode で UI 要素なしにテストできる（issue #5 の受け入れ条件）。
    ///
    /// 責務は次の 2 点に絞る。
    /// <list type="bullet">
    ///   <item><description>参加コード表示用の整形（インターネット/LAN の 2 種、公開 IP 未取得時の代替文言）</description></item>
    ///   <item><description>案内文の選択（どの警告・手動ポート開放案内・手入力欄を出すか）</description></item>
    /// </list>
    /// 警告文そのものの文面（CGNAT・二重 NAT 等）は <c>HostAddressInfo.Warnings</c>（Network 層）が
    /// 既に日本語で組み立てているため、ここでは「まだ解決していない場合は出さない」フィルタだけを掛ける。
    /// </summary>
    public static class HostSetupPresenter
    {
        private const string InternetCodeUnavailableNoPublicIp = "グローバル IP が未取得のため、インターネット用の参加コードを作成できません。下の欄に手入力するか、しばらく待って再試行してください。";
        private const string InternetCodeUnavailableCarrierGradeNat = "この回線は CGNAT のため、インターネット用の参加コードを作成できません。LAN 用コードを使うか、Tailscale の利用を検討してください。";
        private const string InternetCodeUnavailableGeneric = "インターネット用の参加コードを作成できません。";
        private const string LanCodeUnavailable = "LAN IP を取得できないため、LAN 用の参加コードを作成できません。";
        private const string CodeCreationFailed = "参加コードの生成に失敗しました。アドレスまたはポートの値を確認してください。";

        /// <summary>
        /// 表示状態を組み立てる。
        /// </summary>
        /// <param name="info">
        /// <c>HostConnectivityService.ResolveAsync</c> の結果、または <c>HostConnectivityService.Current</c>。
        /// <c>InternalPort == 0</c>（未解決）の場合は <see cref="HostSetupUiState.NotResolved"/> を返す。
        /// </param>
        public static HostSetupUiState Build(HostAddressInfo info)
        {
            if (info.InternalPort == 0)
            {
                return HostSetupUiState.NotResolved;
            }

            var (internetText, canCopyInternet) = BuildInternetCodeText(info);
            var (lanText, canCopyLan) = BuildLanCodeText(info);

            return new HostSetupUiState(
                isResolved: true,
                internetCodeText: internetText,
                canCopyInternetCode: canCopyInternet,
                lanCodeText: lanText,
                canCopyLanCode: canCopyLan,
                portMappingStatusText: BuildPortMappingStatusText(info.PortMapping),
                publicIpSourceText: BuildPublicIpSourceText(info),
                warnings: info.Warnings,
                showManualGuide: info.RequiresManualPortForwarding,
                manualGuideRows: info.RequiresManualPortForwarding ? info.ManualGuide.ToDisplayRows() : Array.Empty<KeyValuePair<string, string>>(),
                // H-1: グローバル IP 自体は取得できていても、自動ポート開放に失敗している場合は
                // （ルーターの設定次第で実際に届くかが変わるため）手入力欄も併せて出す。
                showManualIpInput: !info.CanCreateInternetCode || info.RequiresManualPortForwarding);
        }

        private static (string Text, bool CanCopy) BuildInternetCodeText(HostAddressInfo info)
        {
            if (!info.CanCreateInternetCode)
            {
                if (info.IsCarrierGradeNat)
                {
                    return (InternetCodeUnavailableCarrierGradeNat, false);
                }

                if (info.PublicIpAddress.Length == 0)
                {
                    return (InternetCodeUnavailableNoPublicIp, false);
                }

                return (InternetCodeUnavailableGeneric, false);
            }

            try
            {
                return (JoinCodeCodec.Encode(info.PublicIpAddress, info.ExternalPort), true);
            }
            catch (JoinCodeException)
            {
                return (CodeCreationFailed, false);
            }
        }

        private static (string Text, bool CanCopy) BuildLanCodeText(HostAddressInfo info)
        {
            if (!info.CanCreateLanCode)
            {
                return (LanCodeUnavailable, false);
            }

            try
            {
                return (JoinCodeCodec.Encode(info.LanIpAddress, info.InternalPort), true);
            }
            catch (JoinCodeException)
            {
                return (CodeCreationFailed, false);
            }
        }

        private static string BuildPortMappingStatusText(PortMappingResult portMapping)
        {
            switch (portMapping.Status)
            {
                case PortMappingStatus.NotAttempted:
                    return "未実行";
                case PortMappingStatus.Disabled:
                    return "無効化されています（設定で自動ポート開放が無効です）";
                case PortMappingStatus.Success:
                    return $"成功（{portMapping.DeviceProtocolName}、外部ポート {portMapping.ExternalPort}）";
                case PortMappingStatus.Timeout:
                    return "未検出（ルーターの探索がタイムアウトしました）";
                case PortMappingStatus.DeviceNotFound:
                    return "未検出（UPnP / NAT-PMP に対応した機器が見つかりませんでした）";
                case PortMappingStatus.Refused:
                    return "拒否されました（ルーターがポート開放を許可しませんでした）";
                case PortMappingStatus.Failed:
                default:
                    return "失敗しました";
            }
        }

        /// <summary>
        /// スキップされた問題セットとフォルダ単位のエラーを1行ずつの文字列にする（M-10）。
        /// 同一内容のフォルダエラーが重複している場合は1行にまとめる
        /// （統括メモ「FolderErrors 2件重複は1行にまとめてよい」）。
        /// Unity API に依存しない純 C# のため EditMode で直接テストできる。
        /// </summary>
        /// <param name="report"><c>QuestionLibrary</c> の読み込み結果。</param>
        public static IReadOnlyList<string> BuildQuestionIssueLines(QuestionLoadReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var lines = new List<string>();

            foreach (var skipped in report.SkippedSets)
            {
                var fileName = Path.GetFileName(skipped.FilePath);
                foreach (var message in skipped.Messages)
                {
                    lines.Add($"{fileName}: {message}");
                }
            }

            foreach (var folderError in report.FolderErrors)
            {
                lines.Add(folderError);
            }

            return lines.Distinct().ToArray();
        }

        private static string BuildPublicIpSourceText(HostAddressInfo info)
        {
            if (info.ManualPublicIpAddress.Length > 0)
            {
                return $"手入力（{info.ManualPublicIpAddress}）";
            }

            switch (info.PublicIp.Source)
            {
                case PublicIpSource.NatDevice:
                    return "ルーター（UPnP / NAT-PMP）から取得";
                case PublicIpSource.IpLookupService:
                    return "IP 確認サービスから取得";
                case PublicIpSource.None:
                default:
                    return "未取得";
            }
        }
    }
}
