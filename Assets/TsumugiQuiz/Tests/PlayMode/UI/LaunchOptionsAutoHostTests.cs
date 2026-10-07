using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Tests.PlayMode.UI.HostSetup;
using TsumugiQuiz.Tests.Shared.Network.Nat;
using TsumugiQuiz.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// <c>-tq-host</c> 指定時、Boot → Main 遷移後に HostSetup 画面が自動でホストを開始し、
    /// <see cref="AppPaths.DataRoot"/> 配下に <c>join-code.txt</c> を書き出すこと（issue #8）を確認する
    /// PlayMode テスト。実プロセス（exe）は起動しない。<c>scripts/run-multi.ps1</c> による
    /// 実プロセスでの実測は別途手動で行う（docs/dev-workflow.md §3.3）。
    ///
    /// データルートは <c>-tq-data-root</c> 経由ではなく（issue #71 統合後は
    /// <c>TsumugiQuiz.Network.AppPathsBootstrap</c> が Boot で実引数から解決するため、テストからは
    /// 直接制御できない）、<see cref="AppPaths.Configure"/> で明示的にテスト用の一時フォルダへ差し替える
    /// （優先順位最上位。<see cref="AppPathsBootstrap"/> の既定値登録より先に SetUp で設定しておく）。
    ///
    /// UPnP 探索・IP 確認サービスは実ネットワークに依存させず、<c>HostSetupFlowTests</c>（#5）と
    /// 同様に <c>Tests/Shared/Network/Nat</c>（#67/#80）のフェイクへ差し替える。<c>-tq-host</c> 指定時は
    /// HostSetup 画面が Main シーンの初期表示として自動的に開始されるため、<c>HostSetupFlowTests</c>
    /// のように Main 表示後にフェイクを差し替えると間に合わない（実ネットワークへ出てしまう）。
    ///
    /// issue #161: Boot シーンロード後の「隙間」を狙って
    /// <see cref="NetworkBootstrap.HostConnectivityFactory"/> に直接差し込む方式は、
    /// <c>NetworkBootstrap.Start()</c> 内の Main シーンへの連鎖的な遷移がエンジン内部で
    /// 何フレームで完了するかに依存するタイミングレースがあり（実測からの推測。公式文書での確認なし）、
    /// 時々実ネットワークへ出てしまうことが実測で判明した。現在は
    /// <see cref="NetworkBootstrap.HostConnectivityFactoryOverrideForTesting"/>
    /// （Boot シーンをロードする**前**に静的に設定する）を使い、フレーム内の実行順に一切依存しない
    /// 形でフェイクを保証する。
    /// </summary>
    public class LaunchOptionsAutoHostTests
    {
        private const string BootSceneName = "Boot";
        private const string MainSceneName = "Main";
        private const float SceneLoadTimeoutSeconds = 10f;
        private static readonly Regex JoinCodePattern = new Regex(@"^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$");

        private MainSceneTestHelpers.ConsentFileScope _consentScope;
        private HostSetupPreferencesScope _preferencesScope;
        private string _dataRootDir;

        [SetUp]
        public void SeedConsentedStateAndOptions()
        {
            // issue #161: 前のテストが異常終了して [TearDown] がスキップされた場合の防御として、
            // ここでも null に戻しておく（本来の後始末は [TearDown] 側）。
            NetworkBootstrap.HostConnectivityFactoryOverrideForTesting = null;

            // 重要: AppPaths.Configure は consent.json の読み書き（ConsentFileScope.Backup /
            // RecordConsent）より先に行うこと。JsonConsentStorage の既定パスは
            // AppPaths.DataRoot を都度解決するため、順序を誤ると「同意を記録した場所」と
            // 「Main シーンが同意を確認する場所（= このテスト用データルート）」がずれ、
            // ConsentGate.HasUserConsented() が false になって Terms 画面に落ちてしまう
            // （-tq-host が無視されたように見える。実際に踏んだ回帰）。
            _dataRootDir = Path.Combine(Path.GetTempPath(), "tq-launch-options-test-" + Guid.NewGuid().ToString("N"));
            AppPaths.Configure(_dataRootDir);

            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);

            _preferencesScope = HostSetupPreferencesScope.Backup();

            // Unity.Netcode にも同名の CommandLineOptions が存在するため完全修飾する（CS0104 対策）。
            LaunchOptionsRunner.SetOptionsForTesting(TsumugiQuiz.Core.CommandLineOptions.Parse(new[]
            {
                "-tq-host",
                "-tq-name", "自動ホスト",
                "-tq-port", "0",
            }));
        }

        [TearDown]
        public void RestoreConsentAndPreferencesAndOptions()
        {
            _consentScope?.Restore();
            _preferencesScope?.Restore();
            LaunchOptionsRunner.ResetForTesting();
            AppPaths.Reset();

            // issue #161: 次のテストへ持ち越さないよう必ず null に戻す。
            NetworkBootstrap.HostConnectivityFactoryOverrideForTesting = null;

            if (_dataRootDir != null && Directory.Exists(_dataRootDir))
            {
                Directory.Delete(_dataRootDir, recursive: true);
            }
        }

        [UnityTearDown]
        public IEnumerator TearDownScene()
        {
            MainSceneTestHelpers.TearDownMainSceneAndBootstrapSingletons();

            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TqHost_AutoStartsHostAndWritesJoinCodeFile()
        {
            // issue #161: 以前は Boot シーンのロード後・Main シーン表示前の「隙間」を狙って
            // NetworkBootstrap.Instance.HostConnectivityFactory に直接フェイクを差し込んでいたが、
            // NetworkBootstrap.Start() 内の SceneManager.LoadScene(Main) 呼び出しが、呼び出し元の
            // LoadSceneAsync(Boot) の完了報告より前に（同一エンジンフレームの処理中に連鎖的に）
            // Main シーンの表示・HostSetupView.OnShow（→ HostConnectivity への最初のアクセス）まで
            // 完了させてしまうことがあり（実測からの推測。公式文書での確認なし）、この「隙間」を狙う
            // 差し替えが手遅れになる（実 UPnP・実 IP Lookup へ実際に出てしまう）レースが起きていた
            // （実測: PlayMode 通し実行で時々失敗し、失敗時のログに実グローバル IP・実 LAN IP が
            // 記録されていた）。
            //
            // NetworkBootstrap.HostConnectivityFactoryOverrideForTesting（シーンロードより前に
            // 静的に設定できる）へ差し込むことで、Awake/Start がフレーム内のどのタイミングで
            // 実行されても（Main シーンへの連鎖的な遷移が何フレームで完了しても）、常にこのフェイクが
            // 使われることを保証する。
            NetworkBootstrap.HostConnectivityFactoryOverrideForTesting = () => new HostConnectivityService(
                discovery: new FakeNatDiscovery(device: null),
                lookupClient: new FakeIpLookupClient { DefaultResponse = IpLookupResponse.Ok(200, "203.0.113.5") },
                lanIpProvider: () => "192.168.1.23");

            yield return SceneManager.LoadSceneAsync(BootSceneName, LoadSceneMode.Single);

            var bootstrap = NetworkBootstrap.Instance;
            Assert.IsNotNull(bootstrap, "NetworkBootstrap が見つかりません。");

            var elapsed = 0f;
            while (SceneManager.GetActiveScene().name != MainSceneName && elapsed < SceneLoadTimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(MainSceneName, SceneManager.GetActiveScene().name, "Main シーンへ遷移しているはず。");

            yield return WaitUntil(
                () => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost,
                "-tq-host 指定時は HostSetup 画面が自動でホストを開始するはず。");

            var joinCodePath = Path.Combine(_dataRootDir, "join-code.txt");
            yield return WaitUntil(
                () => File.Exists(joinCodePath),
                $"join-code.txt が AppPaths.DataRoot 配下に書き出されていません: {joinCodePath}");

            var joinCode = File.ReadAllText(joinCodePath);
            Assert.IsTrue(
                JoinCodePattern.IsMatch(joinCode),
                $"join-code.txt の内容が参加コード形式（例: 60N0-0HE7-K12R）ではありません: '{joinCode}'");

            Assert.IsFalse(
                File.Exists(joinCodePath + ".tmp"),
                "H-2: 一時ファイル（.tmp）が残っていないこと（アトミックな書き込みの確認）。");
        }
    }
}
