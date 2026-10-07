using System;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// <see cref="AppSettings"/>（<c>TsumugiQuiz.Room</c>、issue #26）から、実際に読み上げ・NAT 越え処理が
    /// 参照する設定型（<see cref="TtsSettings"/> / <see cref="NatOptions"/>）へ変換するアダプタ（issue #28）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// docs/room-settings.md §6 のとおり、<c>TsumugiQuiz.Tts</c> asmdef は <c>TsumugiQuiz.Room</c> を
    /// 参照していない（依存方向 <c>Core ← Questions/Room ← Network/Tts ← UI</c>、docs/architecture.md §3）。
    /// そのため変換ロジックは両方を参照できる <c>TsumugiQuiz.UI</c> 層に置く。
    /// </para>
    /// <para>
    /// 実際の配線（PR #92 レビュー H5/M1 で完了）:
    /// <see cref="ToNatOptions"/> は既定の <c>NetworkBootstrap.HostConnectivityFactory</c> 自体が
    /// （<c>NatOptionsAppSettingsAdapter</c> 経由で）起動時から使うほか、<c>SettingsView</c> が
    /// アプリ設定保存時に <c>NetworkBootstrap.RefreshHostConnectivityFactory</c> で即時反映する
    /// （次回のホスト開始から有効）。
    /// <see cref="ToTtsSettings"/> の呼び出し元は <see cref="AppSettingsTtsSettingsProvider"/>
    /// （<c>tts.*</c> を読むたびに変換する都度読みの <c>ITtsSettingsProvider</c>、#138）に一本化してある。
    /// その provider を作るのは <see cref="TtsSettingsProviderFactory.BuildOrNull"/> で、
    /// アプリ起動時の <c>TtsService.ConfigureDefaults</c>、<c>GameView</c> の
    /// <c>TtsSyncPlayer.SetSettingsProvider</c>、<c>TtsStatusPanel</c> の「再試行」、
    /// <c>QuestionEditorView</c> の読み上げプレビューがいずれもそこを経由する。
    /// </para>
    /// </remarks>
    internal static class AppSettingsAdapters
    {
        /// <summary><paramref name="settings"/> の <c>tts.*</c> を <see cref="TtsSettings"/> に変換する。</summary>
        public static TtsSettings ToTtsSettings(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return TtsSettings.Create(
                settings.TtsSpeakerName,
                settings.TtsStyleName,
                settings.TtsCacheMaxBytes,
                settings.TtsCacheMaxEntries,
                settings.TtsAssetPathOverride);
        }

        /// <summary>
        /// <paramref name="settings"/> の <c>upnp.*</c> / <c>network.ipLookupUrls</c> を
        /// <see cref="NatOptions"/> に変換する。実体は <see cref="NatOptionsAppSettingsAdapter"/>
        /// （<c>TsumugiQuiz.Network</c>、issue #28 H5）に委譲し、ロジックを二重に持たない。
        /// </summary>
        public static NatOptions ToNatOptions(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return NatOptionsAppSettingsAdapter.FromAppSettings(settings);
        }
    }
}
