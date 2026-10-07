using System;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// 保存済みアプリ設定（<c>tts.*</c>）から <see cref="ITtsSettingsProvider"/> を作る共通ファクトリ
    /// （PR #103 再レビュー N1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 呼び出し元は4か所ある: (1) <b>アプリ起動時</b>の
    /// <see cref="TsumugiQuiz.UI.Views.DefaultViewControllerRegistrations.ConfigureTtsConsentGate"/> が
    /// <c>TtsService.ConfigureDefaults(settingsProvider:)</c> へ登録する（#127 レビュー H-1）、
    /// (2) <see cref="TsumugiQuiz.UI.Views.Game.GameView"/>（<c>GameView.Tts.cs</c>）が
    /// セッション取得時（読み上げ初期化より前）に <c>TtsSyncPlayer.SetSettingsProvider</c> へ渡す、
    /// (3) <see cref="TsumugiQuiz.UI.Views.Settings.SettingsView"/>（<c>SettingsView.TtsTab.cs</c> の
    /// <c>TtsStatusPanel</c>「再試行」、<c>SettingsView.AppTab.cs</c> の保存 →
    /// <see cref="TtsAppSettingsReloader"/>）が <c>TtsService.RetryInitializeAsync(settingsProvider:)</c> へ渡す、
    /// (4) <see cref="TsumugiQuiz.UI.Views.QuestionEditor.QuestionEditorView"/>（<c>QuestionEditorView.Form.Tts.cs</c>、
    /// issue #32）が読み上げプレビューの <c>TtsService.EnsureInitializedAsync</c> / <c>TtsStatusPanel.Create</c> へ渡す。
    /// </para>
    /// <para>
    /// <see cref="TtsService.Initialize"/> は初回呼び出しでしか <c>settingsProvider</c> を読まないため
    /// （docs/tts.md §6.5）、どの呼び出し元が最初の初期化者になっても同じ設定（<c>AssetPathOverride</c>・
    /// <c>tts.*</c>）が使われるよう、すべての呼び出し元が本ファクトリを経由する必要がある
    /// （PR #103 再レビュー N1: 問題エディタの読み上げプレビューがこれを渡していなかったため、
    /// 先に開くと以後の本編読み上げで #28 の設定が無視される不具合があった）。
    /// </para>
    /// <para>
    /// <b>返すのは「起動時スナップショット」ではなく「都度読み」の provider</b>
    /// （<see cref="AppSettingsTtsSettingsProvider"/>、#138）。読み上げの初期化を最初に始めるのは
    /// ロビーでスポーンした <c>TtsSyncCoordinator</c> であり、アプリ起動時に登録した値をそのまま固定すると
    /// 「設定画面で変更 → 再起動せずホスト」で古い値が使われてしまうため。
    /// 初期化<b>後</b>の変更は provider だけでは反映できない（合成エンジンは生成時の設定で固定される）ので、
    /// <see cref="TtsAppSettingsReloader"/> が保存時に再初期化する。
    /// </para>
    /// </remarks>
    internal static class TtsSettingsProviderFactory
    {
        /// <summary>
        /// 保存済みアプリ設定を<b>読むたびに</b>読み直す <see cref="ITtsSettingsProvider"/> を作る（#138）。
        /// 保存先を解決できない場合（<c>AppPaths</c> 未設定。Boot を経由しないテスト等）は null
        /// （<see cref="TtsService"/> 既定の <see cref="DefaultTtsSettingsProvider"/> にフォールバックする）。
        /// </summary>
        public static ITtsSettingsProvider BuildOrNull()
        {
            try
            {
                // AppPaths 未設定なら InvalidOperationException。ここで一度だけ確かめておき、
                // 以後 provider.Load() のたびに null 判定を強いないようにする。
                AppSettingsStore.GetDefaultFilePath();
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            return new AppSettingsTtsSettingsProvider();
        }
    }
}
