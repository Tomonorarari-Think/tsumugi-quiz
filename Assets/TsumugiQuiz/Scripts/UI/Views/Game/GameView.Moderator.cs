using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、司会専用モード（<c>host.role == "moderator"</c>、#20、
    /// docs/tasks/setup-brief.md K18）の表示切り替えをまとめた部分。
    /// ホストかつ <see cref="HostRole.Moderator"/> のときだけ、早押し・回答・選択 UI
    /// （<c>buzz-section</c> / <c>answer-section</c> / <c>choice-section</c>）を隠し、
    /// 代わりに司会操作パネル（<c>moderator-controls.uxml</c> + <see cref="ModeratorControlsPanel"/>）を表示する。
    /// </summary>
    /// <remarks>
    /// 司会（ホスト）は自分自身が回答者になることはない（<see cref="_buzzSection"/> を隠すため）ので、
    /// <see cref="GameView.UiBinding"/> 側の回答欄の表示判定（ロック保持者が自分か）は
    /// 変更なしで自然に非表示のままになる。選択式は <see cref="UpdateChoiceSectionVisible"/>
    /// 自体が <c>_isModerator</c> を見て隠す（M-A、K18 取りこぼしの修正）。「次へ」の重複表示だけは
    /// <see cref="GameView.UiBinding.UpdateHostControlsVisible"/> 側で <c>_isModerator</c> を見て防ぐ。
    /// </remarks>
    public sealed partial class GameView
    {
        private VisualElement _moderatorControlsContainer;
        private ModeratorControlsPanel _moderatorPanel;
        private bool _isModerator;

        /// <summary>
        /// 司会専用モードかどうかを判定し、該当すれば早押し・回答 UI を隠して司会操作パネルを組み込む。
        /// <see cref="TryAcquireSession"/>（<see cref="GameView.OnShow"/> と <see cref="GameView.Tick"/> の双方）から呼ぶ。
        /// </summary>
        private void SetupModeratorMode()
        {
            if (_moderatorPanel != null || _session == null || _root == null)
            {
                // 既に組み込み済み、またはまだセッションが見つかっていない。
                return;
            }

            var lobby = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Lobby : null;
            _isModerator = IsLocalHost && lobby != null && lobby.IsSpawned && lobby.Role.Value == HostRole.Moderator;

            if (!_isModerator)
            {
                return;
            }

            // 司会は早押し・回答をしない（H3）。キー入力もクリックも両方止める
            // （サーバー側も BuzzRpc / SubmitAnswerRpc を拒否するが、UI 側でも二重に防ぐ）。
            DisableBuzzInput();

            // issue #144 仕様 5: 司会画面は文字送りせず全文を出す（進行中の問題があれば全文へ切り替える）。
            CompleteQuestionReveal();

            // #132 レビュー H-1: _buzzSection は BindElements（GameView.UiBinding.cs）が取得済み。
            // 司会かどうかは UpdateBuzzSectionVisible() が _isModerator を見て判断する。
            UpdateHostControlsVisible();
            UpdateQuestionSectionsVisible();

            _moderatorControlsContainer = _root.Q<VisualElement>("moderator-controls-container");
            var template = _router?.ModeratorControlsTemplate;
            if (_moderatorControlsContainer == null || template == null)
            {
                Debug.LogWarning(
                    "[GameView] 司会操作パネルを組み込めません（moderator-controls-container またはテンプレートが見つかりません）。");
                return;
            }

            _moderatorPanel = ModeratorControlsPanel.Create(template, _session);
            _moderatorControlsContainer.Clear();
            _moderatorControlsContainer.Add(_moderatorPanel.Root);
            _moderatorControlsContainer.style.display = DisplayStyle.Flex;
        }

        /// <summary>司会操作パネルの購読を解除する。<see cref="GameView.OnHide"/> から呼ぶこと。</summary>
        private void TeardownModeratorMode()
        {
            _moderatorPanel?.Dispose();
            _moderatorPanel = null;
            _moderatorControlsContainer = null;
            _isModerator = false;
        }
    }
}
