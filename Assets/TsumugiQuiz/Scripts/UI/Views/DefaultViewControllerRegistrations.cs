using TsumugiQuiz.Tts;
using TsumugiQuiz.UI.Views.HostSetup;
using TsumugiQuiz.UI.Views.Lobby;
using TsumugiQuiz.UI.Views.QuestionEditor;
using TsumugiQuiz.UI.Views.Settings;

namespace TsumugiQuiz.UI.Views
{
    /// <summary>
    /// この時点で用意されている View コントローラ（Terms / Title / HostSetup / Join / Lobby / Credits / Game /
    /// QuestionEditor / Settings / プレースホルダ群）を
    /// <see cref="ViewControllerRegistry"/> に登録する。ViewRouter からこの登録内容を分離することで、
    /// 「どの View にどのコントローラを割り当てるか」を差し替えやすくする。
    /// </summary>
    public static class DefaultViewControllerRegistrations
    {
        public static void RegisterDefaults(ViewControllerRegistry registry)
        {
            registry.Register(ViewNames.Terms, () => new TermsView());
            registry.Register(ViewNames.Title, () => new TitleView());
            registry.Register(ViewNames.HostSetup, () => new HostSetupView());
            registry.Register(ViewNames.Join, () => new JoinView());
            registry.Register(ViewNames.Lobby, () => new LobbyView());
            registry.Register(ViewNames.Result, () => new ResultView());
            registry.Register(ViewNames.Credits, () => new CreditsView());
            registry.Register(ViewNames.Game, () => new Game.GameView());
            registry.Register(ViewNames.QuestionEditor, () => new QuestionEditorView());
            registry.Register(ViewNames.Settings, () => new SettingsView());

            foreach (var viewName in ViewNames.PlaceholderViews)
            {
                // View ごとに新しい PlaceholderView インスタンスを生成する（複数 View 間でインスタンスを
                // 使い回すと、ある View で購読したイベントハンドラが別の View に残ってしまうため）。
                registry.Register(viewName, () => new PlaceholderView());
            }
        }

        /// <summary>
        /// ViewRouter の起動時の初期 View を決定する。利用規約への同意状況で Terms / それ以外に振り分け
        /// （requirements.md FR-71: 未同意時は Terms を表示、FR-74: 同意記録が無ければ先へ進めない）、
        /// 同意済みであれば <c>-tq-host</c> / <c>-tq-join</c>（issue #8、<see cref="LaunchOptionsRunner"/>）
        /// に応じて HostSetup / Join へ自動遷移させる。同意ゲートはこの自動化でも迂回しない
        /// （<see cref="LaunchOptionsRunner.DetermineInitialView"/> 参照）。
        /// あわせて <c>-tq-window</c> が指定されていればウィンドウ配置を適用する。
        /// </summary>
        public static void ConfigureInitialView(ViewRouter router)
        {
            router.InitialViewNameOverride = () => LaunchOptionsRunner.DetermineInitialView(ConsentGate.HasUserConsented());
            LaunchOptionsRunner.ApplyWindowPlacement();
        }

        /// <summary>
        /// アプリ起動時に、読み上げ（<see cref="TtsService"/>）へ UI 層でしか作れない注入物
        /// （同意確認と保存済みアプリ設定）を登録する（issue #127 レビュー H-1、FR-74 / FR-75 / NFR-08）。
        ///
        /// <b>なぜ起動時なのか</b>: 読み上げの初期化を最初に始めるのは、ロビーで <c>GameSession</c> が
        /// スポーンした時点の <c>TtsSyncCoordinator.OnNetworkSpawn</c> →
        /// <c>TtsSyncPlayer.InitializeAsync</c> であり、<c>GameView</c> がセッションを取得して
        /// <see cref="Game.GameView.WireTtsSyncPlayer"/> を呼ぶ<b>より前</b>になる。
        /// <c>Network</c> / <c>Tts</c> 層から <c>UI</c> 層は参照できないため、そこでは同意確認を渡せない。
        /// ここで先に登録しておけば、誰が最初の初期化者になっても同意ゲートが効く。
        ///
        /// <b>初期化（voicevox_core / ONNX Runtime のロード）は始めない</b>
        /// （<see cref="TtsService.ConfigureDefaults"/> の仕様。docs/tts.md §6.5、#25 H-5 と同じ方針）。
        ///
        /// <see cref="TtsService.Instance"/> が無い場合（Boot シーンを経由しないテスト等）は何もしない。
        /// </summary>
        public static void ConfigureTtsConsentGate()
        {
            var service = TtsService.Instance;
            if (service == null)
            {
                return;
            }

            service.ConfigureDefaults(
                settingsProvider: TtsSettingsProviderFactory.BuildOrNull(),
                consentCheck: TtsConsentCheckFactory.Build());
        }
    }
}
