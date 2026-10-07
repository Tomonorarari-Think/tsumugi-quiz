using TsumugiQuiz.Tts;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// <see cref="SettingsView"/> のうち「音声合成」（issue #25 で新設した <see cref="TtsStatusPanel"/> を
    /// Settings View に統合する）。voicevox_core/辞書/モデルの状態確認・再試行・配置手順の表示を行う。
    /// </summary>
    /// <remarks>
    /// <see cref="TtsStatusPanel"/> は <c>position: absolute; inset: 0</c> で全画面を覆う設計のため、
    /// タブ内容として入れ子にすると <see cref="TsumugiQuiz.UI.Views.TitleView"/> の M-8 と同じ理由
    /// （flex レイアウトの中では全画面を覆えないことがある）で正しく表示できない。そのため「音声合成」は
    /// タブ切り替えではなく、<see cref="TsumugiQuiz.UI.Views.TitleView"/> と同じくボタンでオーバーレイを
    /// 開閉するアクションとして扱う。
    /// </remarks>
    public sealed partial class SettingsView
    {
        private VisualElement _ttsStatusOverlay;

        private void OnHideTtsTab()
        {
            CloseTtsStatusOverlay();
        }

        private void OnTtsStatusButtonClicked()
        {
            if (_root == null || _ttsStatusOverlay != null)
            {
                return;
            }

            // issue #28 M1: 保存済みアプリ設定（tts.*）から作った provider を渡し、「再試行」のたびに
            // 最新の設定で再初期化できるようにする（PR #103 再レビュー N1: 共通ファクトリに切り出し済み）。
            _ttsStatusOverlay = TtsStatusPanel.Create(
                TtsService.Instance, _router, onClosed: CloseTtsStatusOverlay,
                settingsProvider: TtsSettingsProviderFactory.BuildOrNull());

            // #25 M-8 と同じ理由: settings-root の flex レイアウトに間借りすると全画面を覆えないため、
            // Document.rootVisualElement 直下（= _root の親）に直接追加する。
            var attachRoot = _root.parent ?? _root;
            attachRoot.Add(_ttsStatusOverlay);
        }

        private void CloseTtsStatusOverlay()
        {
            if (_ttsStatusOverlay == null)
            {
                return;
            }

            _ttsStatusOverlay.RemoveFromHierarchy();
            _ttsStatusOverlay = null;
        }
    }
}
