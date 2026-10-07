using TsumugiQuiz.Tts;
using TsumugiQuiz.UI.Views.Settings;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、<see cref="TtsSyncPlayer"/> への注入
    /// （アプリ設定 <c>tts.*</c>: issue #28 M1 / PR #92 レビュー、利用規約の同意確認: issue #127）。
    /// </summary>
    /// <remarks>
    /// <c>TtsSyncPlayer.SetSettingsProvider</c> / <c>SetConsentCheck</c> は
    /// <see cref="TtsService.EnsureInitializedAsync"/> の初回呼び出し時にしか読まれないため
    /// （docs/tts.md §6.5）、実際に読み上げの初期化が走る前
    /// （<c>TtsSyncCoordinator</c> が <c>IReadingPlayback.InitializeAsync</c> を呼ぶ前）に
    /// 設定しておく必要がある。<c>TtsSyncPlayer</c> は <c>GameSession</c> と同じ GameObject に載っており
    /// （docs/tts.md §6.6）、<c>Network</c>/<c>Tts</c> 両asmdefは互いを参照できないため、
    /// 両方を参照できる本 View（<c>UI</c> 層）がセッション取得時（<c>TryAcquireSession</c>）に配線する。
    /// </remarks>
    public sealed partial class GameView
    {
        /// <summary>
        /// ゲームプレイ中の読み上げ（<paramref name="ttsSyncPlayer"/>）に、UI 層でしか作れない 2 つの注入物を渡す。
        /// <list type="number">
        ///   <item><description>
        ///     保存済みアプリ設定（<c>tts.*</c>）から作った <see cref="ITtsSettingsProvider"/>
        ///     （PR #103 再レビュー N1: 共通ファクトリ <see cref="TtsSettingsProviderFactory.BuildOrNull"/>）。
        ///     null が返った場合（<c>AppPaths</c> 未設定）は何もしない
        ///     （<see cref="TtsService"/> 既定の <see cref="DefaultTtsSettingsProvider"/> にフォールバックする）。
        ///   </description></item>
        ///   <item><description>
        ///     利用規約の同意確認（issue #127、requirements.md FR-74 / FR-75 / NFR-08）。
        ///     <see cref="TtsConsentCheckFactory.Build"/>（= <see cref="ConsentGate.HasUserConsented"/>）を渡す。
        ///     判定関数は出題のたび・再生開始のたびに評価されるため、撤回は<b>次の問題から</b>効く
        ///     （鳴っている音声は完了まで鳴らし、合成済み・未再生のものは鳴らさない。docs/tts.md §6.6）。
        ///   </description></item>
        /// </list>
        /// <see cref="TtsSyncPlayer.InitializeAsync"/> はこの同意確認を
        /// <see cref="TtsService.EnsureInitializedAsync"/> の <c>consentCheck</c> にも渡す
        /// （初期化済みでも取り込まれる。#127 レビュー M-5）。
        ///
        /// <b>ただし本メソッドは「最後の砦」ではなく「最初の砦」でもない</b>（#127 レビュー H-1）:
        /// 読み上げの初期化はロビーで <c>GameSession</c> がスポーンした時点
        /// （<c>TtsSyncCoordinator.OnNetworkSpawn</c>）に始まり、本メソッドより前に走る。
        /// そのため同意ゲートの本命は、アプリ起動時に
        /// <see cref="DefaultViewControllerRegistrations.ConfigureTtsConsentGate"/> が
        /// <see cref="TtsService.ConfigureDefaults"/> で登録するほうで、
        /// <c>TtsSyncPlayer</c> は明示指定が無ければそちらへフォールバックする。
        /// 本メソッドの配線は、その <c>TtsSyncPlayer</c> インスタンスに明示的な判定を持たせて
        /// （テスト・将来サービスを差し替える構成でも）確実に効かせるためのもの。
        /// </summary>
        /// <param name="ttsSyncPlayer">配線先。null（プレハブに未配置等）なら何もしない。</param>
        internal static void WireTtsSyncPlayer(TtsSyncPlayer ttsSyncPlayer)
        {
            if (ttsSyncPlayer == null)
            {
                return;
            }

            var settingsProvider = TtsSettingsProviderFactory.BuildOrNull();
            if (settingsProvider != null)
            {
                ttsSyncPlayer.SetSettingsProvider(settingsProvider);
            }

            ttsSyncPlayer.SetConsentCheck(TtsConsentCheckFactory.Build());
        }
    }
}
