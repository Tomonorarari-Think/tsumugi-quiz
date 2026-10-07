using System;
using System.Collections.Generic;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tts;
using UnityEngine;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// <b>呼ばれるたびに</b> <c>app-settings.json</c> を読み直す <see cref="ITtsSettingsProvider"/>（issue #138）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// #127（PR #133）で、アプリ起動時に <c>DefaultViewControllerRegistrations.ConfigureTtsConsentGate</c> が
    /// <see cref="TtsService.ConfigureDefaults"/> へ provider を登録するようになった。ただし登録するものが
    /// <see cref="FixedTtsSettingsProvider"/>（<b>起動時スナップショット</b>）だと、
    /// 「設定画面で <c>tts.speakerName</c> / <c>tts.assetPathOverride</c> を変更 → 再起動せずホスト」で
    /// 起動時の値が使われてしまう（#138）。<see cref="TtsService.Initialize"/> が
    /// <c>Settings = LoadSettings()</c> を実行するのは<b>初期化のその瞬間</b>なので、
    /// そこで読む provider を「都度読み」にすれば、誰が最初の初期化者
    /// （ロビーの <c>TtsSyncCoordinator.OnNetworkSpawn</c> であっても）でも保存済みの最新値が使われる。
    /// </para>
    /// <para>
    /// <b>例外の扱い（#138 レビュー L-1）</b>: <see cref="ITtsSettingsProvider.Load"/> の契約は
    /// 「読み込みに失敗しても例外を投げず既定値へフォールバックする」だが、<b>何でも握りつぶすわけではない</b>。
    /// <list type="bullet">
    ///   <item><description>
    ///     ファイル欠損・I/O 失敗・JSON 破損は <see cref="AppSettingsStore.Load"/> 自身が既定値へ丸め、
    ///     理由を <c>Warnings</c> で返す（例外にならない）。その <c>Warnings</c> はここでログに出す
    ///     （#138 レビュー M-1。握りつぶさない）
    ///   </description></item>
    ///   <item><description>
    ///     ここで捕捉するのは <see cref="AppSettingsStore"/> のコンストラクタが投げる
    ///     <see cref="InvalidOperationException"/>（<c>TsumugiQuiz.Core.AppPaths</c> 未設定。
    ///     Boot を経由しないテスト・ツール）だけ
    ///   </description></item>
    ///   <item><description>
    ///     それ以外の想定外の例外は<b>そのまま投げる</b>。呼び出し元の
    ///     <c>TtsService.LoadSettings</c> が捕まえて警告を出し、既定値で続行する
    ///   </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 生成は <see cref="TtsSettingsProviderFactory.BuildOrNull"/> 経由に限る（注入物の出所を 1 つに保つため）。
    /// </para>
    /// </remarks>
    internal sealed class AppSettingsTtsSettingsProvider : ITtsSettingsProvider
    {
        /// <inheritdoc/>
        public TtsSettings Load()
        {
            AppSettingsLoadResult result;
            try
            {
                result = new AppSettingsStore().Load();
            }
            catch (InvalidOperationException e)
            {
                // AppPaths 未設定（Boot を経由しないテスト・ツール）。読み上げは既定値で続行する。
                Debug.LogWarning(
                    $"[AppSettingsTtsSettingsProvider] アプリ設定の保存先を解決できませんでした。既定値を使います: {e.Message}");
                return TtsSettings.Default;
            }

            LogWarnings(result.Warnings);
            return AppSettingsAdapters.ToTtsSettings(result.Settings);
        }

        /// <summary>
        /// <see cref="AppSettingsStore.Load"/> が既定値へ丸めた理由をログに残す（#138 レビュー M-1）。
        /// 読み上げの設定が黙って既定値に戻ったように見えるのを防ぐため、握りつぶさない。
        /// </summary>
        private static void LogWarnings(IReadOnlyList<string> warnings)
        {
            if (warnings == null)
            {
                return;
            }

            foreach (var warning in warnings)
            {
                if (string.IsNullOrWhiteSpace(warning))
                {
                    continue;
                }

                Debug.LogWarning($"[AppSettingsTtsSettingsProvider] {warning}");
            }
        }
    }
}
