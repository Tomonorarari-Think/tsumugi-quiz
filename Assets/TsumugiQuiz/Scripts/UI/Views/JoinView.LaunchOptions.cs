using TsumugiQuiz.Core.Network;
using TsumugiQuiz.UI;
using UnityEngine;

namespace TsumugiQuiz.UI.Views
{
    /// <summary>
    /// <see cref="JoinView"/> のうち、<c>-tq-join &lt;code&gt;</c> 指定時の起動時自動参加
    /// (issue #8、<see cref="LaunchOptionsRunner"/>)を担う部分クラス。
    /// </summary>
    public sealed partial class JoinView
    {
        /// <summary>
        /// <c>-tq-join</c> が指定されていれば（プロセスにつき最初の 1 回だけ）、参加コード欄へ
        /// 反映し、<c>-tq-name</c> があればプレイヤー名欄も上書きしてから、通常の「接続」ボタンと
        /// 同じ処理を呼ぶ（既存の PlayerPrefs より CLI 引数を優先する）。
        /// </summary>
        private void TryAutoJoinFromCommandLine()
        {
            if (!LaunchOptionsRunner.TryConsumeAutoJoin(out var joinCode))
            {
                return;
            }

            Debug.Log("[JoinView] -tq-join 指定により自動で参加コードを入力して接続します。");

            // M-4: プレイヤー名の正規化は PlayerNameValidator.TryNormalize の 1 系統に統一する
            // （OnConnectClicked が呼ぶ検証と同じもの）。不正な値は入力欄へ反映せず理由をログする。
            if (LaunchOptionsRunner.TryGetPlayerName(out var rawName))
            {
                if (PlayerNameValidator.TryNormalize(rawName, out var normalizedName))
                {
                    _nameField.value = normalizedName;
                }
                else
                {
                    Debug.LogWarning(
                        $"[JoinView] -tq-name の値が不正なため無視します（{PlayerNameValidator.RuleSummary}）: '{rawName}'");
                }
            }

            // TextField.value の setter は登録済みハンドラ（OnCodeFieldChanged）を発火させ、
            // RefreshCodePreview / UpdateConnectButtonEnabled を呼ぶ。念のため明示的にも呼んでおく
            // （UI Toolkit のイベント発火タイミングに依存しすぎないため。冪等な呼び出し）。
            _codeField.value = joinCode;
            RefreshCodePreview();
            UpdateConnectButtonEnabled();

            // 不正な参加コードの場合、_decodedEndpoint は設定されず OnConnectClicked が理由をログして
            // 中止する（H-3、JoinView.cs 参照）。
            OnConnectClicked();
        }
    }
}
