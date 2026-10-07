using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.UI;
using UnityEngine;

namespace TsumugiQuiz.UI.Views.HostSetup
{
    /// <summary>
    /// <see cref="HostSetupView"/> のうち、<c>-tq-host</c> 指定時の起動時自動ホスト開始(issue #8、
    /// <see cref="LaunchOptionsRunner"/>)を担う部分クラス。
    /// </summary>
    public sealed partial class HostSetupView
    {
        /// <summary>
        /// 自動開始の要求・参加コード書き出しの保留を追跡する状態機械（issue #15 レビュー M-3）。
        /// 完了コールバックが呼び出し元の呼び出し中に同期発火するケースの詳細は
        /// <see cref="AutoHostStartCoordinator"/> の remarks / <c>docs/tasks/m2-verification.md</c> を参照。
        /// </summary>
        private readonly AutoHostStartCoordinator _autoHostStartCoordinator = new AutoHostStartCoordinator();

        /// <summary>
        /// <c>-tq-host</c> が指定されていれば（プロセスにつき最初の 1 回だけ）、<c>-tq-name</c> /
        /// <c>-tq-port</c> を入力欄へ反映してから、通常の「ホストを開始」ボタンと同じ処理を呼ぶ
        /// （既存の PlayerPrefs より CLI 引数を優先する）。
        /// </summary>
        private void TryAutoStartFromCommandLine()
        {
            if (!LaunchOptionsRunner.TryConsumeAutoHost())
            {
                return;
            }

            Debug.Log("[HostSetupView] -tq-host 指定により自動でホストを開始します。");

            // M-4: プレイヤー名の正規化は PlayerNameValidator.TryNormalize の 1 系統に統一する
            // （OnStartHostClicked が呼ぶ検証と同じもの）。不正な値は入力欄へ反映せず理由をログする。
            if (LaunchOptionsRunner.TryGetPlayerName(out var rawName))
            {
                if (PlayerNameValidator.TryNormalize(rawName, out var normalizedName))
                {
                    _playerNameField.value = normalizedName;
                }
                else
                {
                    Debug.LogWarning(
                        $"[HostSetupView] -tq-name の値が不正なため無視します（{PlayerNameValidator.RuleSummary}）: '{rawName}'");
                }
            }

            if (LaunchOptionsRunner.TryGetPort(out var port))
            {
                _portField.value = port;
            }

            _autoHostStartCoordinator.BeginAutoStart(OnStartHostClicked);
        }

        /// <summary>
        /// <c>-tq-host</c> による自動開始の結果を受け取る。<c>HostSetupView.cs</c> の
        /// <c>OnHostStartCompleted</c> から、手動開始かどうかを問わず毎回呼ばれるが、
        /// 手動開始の場合は <see cref="AutoHostStartCoordinator.HandleResult"/> が false を返し無視される。
        /// </summary>
        private void HandleAutoHostStartResult(bool success)
        {
            if (!_autoHostStartCoordinator.HandleResult(success) || success)
            {
                return;
            }

            Debug.LogWarning("[HostSetupView] -tq-host によるホスト開始に失敗したため join-code.txt は書き出しません。");
        }

        /// <summary>
        /// -tq-host による自動開始の最初の到達性解決が完了した直後に、
        /// <see cref="HostSetupView.Connectivity"/> 側の <c>ResolveConnectivity</c> から呼ばれる。
        /// LAN 参加コードが作れなければ書き出さず、理由をログする（H-3）。
        /// </summary>
        private void WriteJoinCodeFileIfPending(HostAddressInfo info)
        {
            if (!_autoHostStartCoordinator.ConsumePendingJoinCodeWrite())
            {
                return;
            }

            if (!info.CanCreateLanCode)
            {
                Debug.LogWarning(
                    "[HostSetupView] LAN 参加コードを作成できなかったため join-code.txt を書き出しません。");
                return;
            }

            try
            {
                var joinCode = JoinCodeCodec.Encode(info.LanIpAddress, info.InternalPort);
                LaunchOptionsRunner.WriteJoinCodeFile(joinCode);
            }
            catch (JoinCodeException ex)
            {
                Debug.LogWarning($"[HostSetupView] 参加コードの生成に失敗したため join-code.txt を書き出しません: {ex}");
            }
        }
    }
}
