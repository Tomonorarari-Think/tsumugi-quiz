using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// Main シーンを対象にした PlayMode テスト（<see cref="MainSceneUiTests"/> /
    /// <see cref="TermsConsentSceneTests"/> / <c>JoinViewSceneTests</c> / <c>GameViewSceneTests</c>）で
    /// 共通に使う、シーンロード・要素待機・クリック操作・consent.json の退避/復元のヘルパー。
    /// </summary>
    internal static class MainSceneTestHelpers
    {
        public const string BootScenePath = "Assets/TsumugiQuiz/Scenes/Boot.unity";
        public const string MainScenePath = "Assets/TsumugiQuiz/Scenes/Main.unity";

        /// <summary>
        /// issue #73 のレビュー（H1/H2）を踏まえた要素待ちの設計メモ。
        ///
        /// 当初「重い View（Credits のライセンス一覧生成など）の直後は 1 フレームの実時間が伸び、
        /// 固定 120 フレームの合計待機時間が想定の約2秒に届かなくなる」という仮説を立てたが、これは
        /// 因果が逆（1 フレームが伸びるほど 120 フレーム分の合計待機時間はむしろ増える）で、待機不足を
        /// 直接には説明しない。逆に、batchmode や高フレームレートの環境では 1 フレームが極端に短くなり、
        /// 120 フレームの合計が想定していた約2秒に届いていなかった可能性がある（実測では再現できておらず、
        /// 断定はできない）。
        ///
        /// そのため、フレーム数の下限（<see cref="MinPollCount"/>、旧実装の 120 フレームを踏襲）は残しつつ、
        /// 実時間の上限（<see cref="DefaultTimeoutSeconds"/>）も課すハイブリッド方式にした
        /// （<c>while (!found &amp;&amp; (elapsed &lt; timeout || polls &lt; MinPollCount))</c>）。
        /// 高負荷時にポーリング回数がその分減る（＝要素の出現をチェックする頻度が下がる）トレードオフは
        /// 許容し、実時間の上限に達していなくても最低 <see cref="MinPollCount"/> 回のポーリングを保証する。
        ///
        /// 経過時間の計測は <c>Time.unscaledDeltaTime</c> の積算ではなく、<c>Time.maximumDeltaTime</c> による
        /// クランプの影響を受けない <see cref="Time.realtimeSinceStartupAsDouble"/> の差分（デッドライン方式）に
        /// 統一した。<c>GameViewSceneTests</c> / <c>JoinViewSceneTests</c> がそれぞれ持っていた同種の
        /// private ヘルパー（<c>WaitUntilRealtime</c>）もこの <see cref="WaitUntil"/> に統合し、
        /// タイムアウト値もこの共有定数に揃えた。
        /// </summary>
        public const float DefaultTimeoutSeconds = 20f;

        /// <summary>実時間の上限に達していなくても、最低これだけの回数はポーリングする（旧実装の固定フレーム数を踏襲した下限）。</summary>
        public const int MinPollCount = 120;

        /// <summary>
        /// <see cref="WaitForPanelAttachment"/> の既定タイムアウト（issue #102 レビュー M2）。
        /// <see cref="DefaultTimeoutSeconds"/>（20 秒）をそのまま使うと、1 テスト内で複数回クリックする
        /// ケース（<c>[Timeout(30000)]</c> のテストで 2 回クリックする等）で NUnit 側のテストタイムアウトが
        /// 先に発動してしまい、このヘルパー自身の診断メッセージが出ないまま打ち切られる。要素が
        /// アタッチされないのは通常 1 フレーム未満〜数秒の遅れであり、20 秒も待つ必要はないため、
        /// クリック送出前の待ちだけ短いタイムアウトに分ける。
        /// </summary>
        public const float PanelAttachTimeoutSeconds = 5f;

        /// <summary>
        /// UI Toolkit のライブリロードの監視間隔（<c>VisualTreeAssetChangeTrackerUpdater</c> の
        /// <c>kMinUpdateDelayMs</c>）より十分長く待つための基準値（issue #108 実測: 追跡対象 UXML を
        /// dirty にしてから要素が Panel から外れる／<c>UIDocument.rootVisualElement</c> が作り直される
        /// までが 0.972 秒）。その 4 倍を待つ。<c>UiToolkitLiveReloadGuardTests</c>（#108）と
        /// <c>ViewRouterLiveReloadRedrawTests</c>（issue #137 レビュー L-1）で共有する。
        /// </summary>
        internal const float LiveReloadObservationSeconds = 4f;

        /// <summary>
        /// 一時的な空シーンの名前（<see cref="UnloadMainSceneRoutine"/> 用）。
        /// </summary>
        private const string PlaceholderSceneName = "TsumugiQuizTestPlaceholder";

        /// <summary>
        /// 実際の consent.json（<see cref="TsumugiQuiz.Core.AppPaths.DataRoot"/> 配下。既定では
        /// Application.persistentDataPath 相当）をテスト用に退避・復元するスコープ。
        /// <see cref="Backup"/> は「読み取り→退避」を1メソッド内で完結させ、呼び出し側が退避完了前に
        /// ファイルを書き換えてしまう順序ミスを防ぐ（M-8）。<see cref="Restore"/>（または <see cref="Dispose"/>）で
        /// 元の状態（無かった場合は「無い」状態）に戻す。
        ///
        /// #71 で consent.json の保存先は worktree・プロセス単位（<c>AppPaths</c>）では分離されたが、
        /// 同一プロセス内の複数テスト間では依然として同じファイルを共有するため、
        /// このテスト間退避・復元のしくみ自体は引き続き必要（簡素化はしない）。
        /// </summary>
        public sealed class ConsentFileScope : IDisposable
        {
            private readonly bool _hadExistingFile;
            private readonly byte[] _backupBytes;

            private ConsentFileScope(string filePath, bool hadExistingFile, byte[] backupBytes)
            {
                FilePath = filePath;
                _hadExistingFile = hadExistingFile;
                _backupBytes = backupBytes;
            }

            /// <summary>退避対象の consent.json の実ファイルパス。</summary>
            public string FilePath { get; }

            /// <summary>
            /// 現在の consent.json を読み取って退避する。この呼び出しが完了するまでは、
            /// 呼び出し側は consent.json に一切触れないこと（退避完了前の上書きを防ぐため）。
            /// </summary>
            public static ConsentFileScope Backup()
            {
                var filePath = JsonConsentStorage.GetDefaultFilePath();
                var hadExistingFile = File.Exists(filePath);
                var backupBytes = hadExistingFile ? File.ReadAllBytes(filePath) : null;
                return new ConsentFileScope(filePath, hadExistingFile, backupBytes);
            }

            /// <summary>現在の consent.json を削除する（未同意状態を作るテスト用）。</summary>
            public void DeleteCurrentFile()
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }

            /// <summary>退避時点の状態（ファイルが無かったなら「無い」状態）に戻す。</summary>
            public void Restore()
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }

                if (_hadExistingFile)
                {
                    File.WriteAllBytes(FilePath, _backupBytes);
                }
            }

            public void Dispose()
            {
                Restore();
            }
        }

        /// <summary>
        /// 実際の <c>app-settings.json</c>（<see cref="AppSettingsStore.GetDefaultFilePath"/> 配下）を
        /// テスト用に退避・復元するスコープ（issue #28 H2。<see cref="ConsentFileScope"/> と同じ方式）。
        /// player.name / network.port を <c>PlayerPrefs</c> から <c>AppSettingsStore</c> へ移行したことで、
        /// HostSetup / Join の各 View が実際にこのファイルへ書き込むようになったため、
        /// 同一プロセス内の他テストへ影響しないよう退避・復元が必要になった。
        /// </summary>
        public sealed class AppSettingsFileScope : IDisposable
        {
            private readonly string _filePath;
            private readonly bool _hadExistingFile;
            private readonly byte[] _backupBytes;

            private AppSettingsFileScope(string filePath, bool hadExistingFile, byte[] backupBytes)
            {
                _filePath = filePath;
                _hadExistingFile = hadExistingFile;
                _backupBytes = backupBytes;
            }

            public static AppSettingsFileScope Backup()
            {
                var filePath = AppSettingsStore.GetDefaultFilePath();
                var hadExistingFile = File.Exists(filePath);
                var backupBytes = hadExistingFile ? File.ReadAllBytes(filePath) : null;
                return new AppSettingsFileScope(filePath, hadExistingFile, backupBytes);
            }

            public void Restore()
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }

                if (_hadExistingFile)
                {
                    File.WriteAllBytes(_filePath, _backupBytes);
                }
            }

            public void Dispose() => Restore();
        }

        public static IEnumerator LoadMainSceneAndGetRoot(Action<VisualElement> onRootFound)
        {
            var loadOperation = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
                MainScenePath, UnityEngine.SceneManagement.LoadSceneMode.Single);
            Assert.IsNotNull(loadOperation, $"'{MainScenePath}' の LoadSceneAsync が null を返しました。EditorBuildSettings への登録を確認してください。");

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            // UIDocument が rootVisualElement を構築するのを待つ。
            // issue #73 レビュー H3: 素の FindAnyObjectByType<UIDocument>() だと、シーン内に複数の
            // UIDocument が存在した場合にどれが返るか保証されない。実際に画面遷移を担う ViewRouter が
            // アタッチされている UIDocument（[RequireComponent(typeof(UIDocument))]）を名指しで取得する。
            var uiDocument = FindViewRouterUIDocument();
            var deadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            var polls = 0;
            while ((uiDocument == null || uiDocument.rootVisualElement == null)
                   && (Time.realtimeSinceStartupAsDouble < deadline || polls < MinPollCount))
            {
                polls++;
                yield return null;
                uiDocument = FindViewRouterUIDocument();
            }

            Assert.IsNotNull(uiDocument, "ViewRouter の UIDocument が Main シーンに見つかりません。");
            Assert.IsNotNull(uiDocument.rootVisualElement, "UIDocument.rootVisualElement が null のままです。PanelSettings の設定を確認してください。");

            yield return DisableLiveReloadWhenPanelReady(uiDocument);
            onRootFound(uiDocument.rootVisualElement);
        }

        /// <summary>
        /// Boot シーンをロードし、<see cref="NetworkBootstrap"/> による Main シーンへの自動遷移を待ってから
        /// UIDocument のルートを返す。Join View の接続処理は <see cref="NetworkBootstrap.Instance"/> の
        /// <see cref="NetworkService"/> を使うため、Main シーンを直接ロードする
        /// <see cref="LoadMainSceneAndGetRoot"/> ではネットワーク機能が使えない（issue #6）。
        /// 呼び出し側は必要に応じて <see cref="NetworkBootstrap.Instance"/> の GameObject を
        /// テスト後に破棄すること（<see cref="TsumugiQuiz.Tests.PlayMode.Network.BootSceneBootstrapTests"/> と同様）。
        /// </summary>
        public static IEnumerator LoadBootThenMainSceneAndGetRoot(Action<VisualElement> onRootFound)
        {
            yield return SceneManager.LoadSceneAsync(BootScenePath, LoadSceneMode.Single);

            var sceneDeadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            var scenePolls = 0;
            while (SceneManager.GetActiveScene().path != MainScenePath
                   && (Time.realtimeSinceStartupAsDouble < sceneDeadline || scenePolls < MinPollCount))
            {
                scenePolls++;
                yield return null;
            }

            Assert.AreEqual(MainScenePath, SceneManager.GetActiveScene().path, "Boot シーンから Main シーンへ自動遷移しなかった。");

            var uiDocument = FindViewRouterUIDocument();
            var documentDeadline = Time.realtimeSinceStartupAsDouble + DefaultTimeoutSeconds;
            var documentPolls = 0;
            while ((uiDocument == null || uiDocument.rootVisualElement == null)
                   && (Time.realtimeSinceStartupAsDouble < documentDeadline || documentPolls < MinPollCount))
            {
                documentPolls++;
                yield return null;
                uiDocument = FindViewRouterUIDocument();
            }

            Assert.IsNotNull(uiDocument, "ViewRouter の UIDocument が Main シーンに見つかりません。");
            Assert.IsNotNull(uiDocument.rootVisualElement, "UIDocument.rootVisualElement が null のままです。");

            yield return DisableLiveReloadWhenPanelReady(uiDocument);
            onRootFound(uiDocument.rootVisualElement);
        }

        /// <summary>
        /// UIDocument のルートがパネルにアタッチされるのを待ってから、そのパネルのライブリロードを
        /// 止める（issue #108、レビュー M-3）。テスト中にアセットの dirty カウントが変わると
        /// UIDocument がビジュアルツリーを作り直し、取得済みの要素が Panel から切り離されて
        /// クリックが届かなくなるため。<see cref="FindViewRouterUIDocument"/> でも同じことをしているが、
        /// そちらはパネル未生成なら黙って何もしないので、シーンを読み込むヘルパーは最後にこれを通して
        /// アタッチを待ってから確実に適用する。詳細は <see cref="UiToolkitLiveReloadGuard"/> を参照。
        ///
        /// <para>
        /// レビュー M-R1: 時間切れになったら黙って素通りせず Fail させる。ここで適用できていないと
        /// 「テストが掴んだ要素が途中で Panel から外れる」フレークが復活しうるため、
        /// 失敗として気づけるようにしておく必要がある。
        /// </para>
        /// <para>
        /// レビュー L-R1: 各テストクラスが持つ独自のシーンロードヘルパー
        /// （<c>LoadMainThroughBootAndInstallFakes</c>）の脱出条件は <c>rootVisualElement != null</c> までで、
        /// パネルへのアタッチまでは待っていない。それらの末尾からも呼べるよう <c>internal</c> にしている。
        /// </para>
        /// </summary>
        internal static IEnumerator DisableLiveReloadWhenPanelReady(UIDocument uiDocument)
        {
            Assert.IsNotNull(uiDocument, "DisableLiveReloadWhenPanelReady に渡された UIDocument が null です。");

            // クリック送出前の Panel アタッチ待ち（WaitForPanelAttachment）と同じタイムアウトに揃える。
            var deadline = Time.realtimeSinceStartupAsDouble + PanelAttachTimeoutSeconds;
            var polls = 0;
            while (uiDocument.rootVisualElement?.panel == null
                   && (Time.realtimeSinceStartupAsDouble < deadline || polls < MinPollCount))
            {
                polls++;
                yield return null;
            }

            Assert.IsNotNull(
                uiDocument.rootVisualElement?.panel,
                $"UIDocument.rootVisualElement が {PanelAttachTimeoutSeconds:0.#} 秒以内に Panel へアタッチされませんでした。"
                + "PanelSettings / UIDocument の設定を確認してください"
                + "（issue #108: この間は UI Toolkit のライブリロードを止められません）。");

            UiToolkitLiveReloadGuard.Disable(uiDocument.rootVisualElement.panel);
        }

        /// <summary>
        /// Main シーンをアンロードする（issue #108）。各テストクラスの <c>[UnityTearDown]</c> から呼ぶ。
        ///
        /// <para>
        /// これまで各クラスが直接 <c>SceneManager.UnloadSceneAsync(mainScene)</c> を呼んでいたが、
        /// Main シーンが唯一のロード済みシーンのとき Unity はこの呼び出しを拒否し
        /// （<c>Unloading the last loaded scene ... is not supported</c> の警告、1 回の PlayMode 実行で 56 件）、
        /// <b>アンロードは行われないまま</b>次のテストへ Main シーンが持ち越されていた。
        /// 空のシーンを 1 つ作ってアクティブにしてからアンロードすることで、意図どおり破棄する。
        /// </para>
        /// </summary>
        public static IEnumerator UnloadMainSceneRoutine()
        {
            var scene = SceneManager.GetSceneByPath(MainScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                yield break;
            }

            if (SceneManager.loadedSceneCount <= 1)
            {
                var placeholder = SceneManager.GetSceneByName(PlaceholderSceneName);
                if (!placeholder.IsValid() || !placeholder.isLoaded)
                {
                    placeholder = SceneManager.CreateScene(PlaceholderSceneName);
                }

                SceneManager.SetActiveScene(placeholder);
            }

            yield return SceneManager.UnloadSceneAsync(scene);
        }

        /// <summary>
        /// Main シーンで画面遷移を担う <see cref="ViewRouter"/> の UIDocument を取得する（issue #73 H3）。
        /// <c>HostSetupFlowTests</c> / <c>LobbyViewSceneTests</c> が個別に持っていた
        /// <c>FindAnyObjectByType&lt;UIDocument&gt;()</c> ループからも呼べるよう公開している。
        ///
        /// <para>
        /// issue #108 レビュー HIGH-1: <c>LobbyViewSceneTests</c> /
        /// <c>HostSetupFlowTests</c> / <c>GameViewModeratorSceneTests</c> / <c>ResultViewSceneTests</c> /
        /// <c>LobbyGameStartSceneTests</c> は <see cref="LoadMainSceneAndGetRoot"/> /
        /// <see cref="LoadBootThenMainSceneAndGetRoot"/> を通さない独自のロードヘルパー
        /// （<c>LoadMainThroughBootAndInstallFakes</c>）を持つが、いずれもルート取得にはこのメソッドを使う。
        /// そのため、ここでパネルが生成され次第ライブリロードを止めておくことで、全経路をカバーする。
        /// </para>
        /// <para>
        /// TODO(申し送り): <c>LoadMainThroughBootAndInstallFakes</c> の 4 実装は内容がほぼ同一なので、
        /// このクラスへ寄せて重複を解消したい（本 PR のスコープ外）。
        /// </para>
        /// </summary>
        public static UIDocument FindViewRouterUIDocument()
        {
            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            var document = router != null ? router.GetComponent<UIDocument>() : null;

            // パネル未生成（ルートがまだどのパネルにも属していない）の間は何もしない。
            // 呼び出し側がポーリングしていれば、生成された回の呼び出しで適用される。
            UiToolkitLiveReloadGuard.Disable(document != null ? document.rootVisualElement?.panel : null);

            return document;
        }

        /// <summary>
        /// 表示中の View の元になった <see cref="VisualTreeAsset"/>（＝ パネルのライブリロード追跡対象）を返す。
        /// <c>VisualTreeAsset.CloneTree</c> は生成した各要素に
        /// <see cref="VisualElement.visualTreeAssetSource"/> を設定するので、ツリーを
        /// <b>深さ優先の前順走査</b>（自分 → 子を先頭から再帰）で探して最初に見つかったものを使う
        /// （<c>Instantiate()</c> が返す <c>TemplateContainer</c> 自身には設定されないため、
        /// 直下の子だけを見ても見つからない。実測で確認済み）。
        /// issue #108 レビュー LOW-3: 以前は <c>Assets/TsumugiQuiz/UI</c> 配下の UXML を全部 dirty に
        /// していたが、実際に追跡されている 1 本だけに絞った。Main シーンの UIDocument 自体は
        /// <c>sourceAsset</c> 未設定なので <c>UIDocument.visualTreeAsset</c> からはたどれない。
        /// issue #137 レビュー L-2: <c>UiToolkitLiveReloadGuardTests</c> と
        /// <c>ViewRouterLiveReloadRedrawTests</c> の両方から使うため、元々 <c>UiToolkitLiveReloadGuardTests</c>
        /// にあった実装をここへ移設した。
        /// </summary>
        internal static VisualTreeAsset FindCurrentViewTemplateAsset(VisualElement root)
        {
            if (root.visualTreeAssetSource != null)
            {
                return root.visualTreeAssetSource;
            }

            foreach (var child in root.Children())
            {
                var found = FindCurrentViewTemplateAsset(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Main シーンの <see cref="ViewRouter"/> と、<see cref="LoadBootThenMainSceneAndGetRoot"/> で
        /// 常駐した <see cref="NetworkBootstrap"/> / <see cref="SePlayer"/> / <see cref="TtsService"/> を
        /// 後片付けする（<c>BootSceneBootstrapTests.TearDown</c> と同じ処理）。
        /// issue #73 調査: <see cref="TtsService"/> も <c>DontDestroyOnLoad</c> のシングルトンだが、
        /// これまでここで破棄されていなかった。Boot シーンを経由するテスト（<c>JoinViewSceneTests</c> /
        /// <c>GameViewSceneTests</c>）の後片付け漏れとして、TTS の初期化状態が同一プロセス内の後続テスト
        /// （<c>CreditsSceneTests</c> 等、Main シーンを直接ロードするテスト）に持ち越されていたため、
        /// ここで破棄するよう追加した。
        /// #101: さらに、シングルトンより先に <see cref="ViewRouter"/>（＝ Main シーンの UIDocument）を
        /// 破棄するようにした。破棄の順序を「View → NetworkService」に固定して、表示中の View の
        /// コールバックが破棄済みの <see cref="NetworkService"/> を触らないようにするため。
        /// </summary>
        public static void TearDownMainSceneAndBootstrapSingletons()
        {
            // #101: 先に View を畳む。ViewRouter の GameObject を壊すと OnDestroy → IView.OnHide が走り、
            // 表示中の View が握っているスケジュールコールバック（GameView の再探索 Tick 等）が止まる。
            // これを NetworkBootstrap（= NetworkService.Dispose）より後回しにすると、破棄済みサービスを
            // 触るコールバックが後続テストのセットアップ中まで生き残る。
            // Main シーンをアンロード済みのテストでは ViewRouter が見つからないので何もしない。
            DestroyViewRouterIfPresent();

            var bootstrap = NetworkBootstrap.Instance;
            if (bootstrap != null)
            {
                UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
            }

            var sePlayer = SePlayer.Instance;
            if (sePlayer != null)
            {
                UnityEngine.Object.DestroyImmediate(sePlayer.gameObject);
            }

            var appPathsBootstrap = AppPathsBootstrap.Instance;
            if (appPathsBootstrap != null)
            {
                UnityEngine.Object.DestroyImmediate(appPathsBootstrap.gameObject);
            }

            var ttsService = TtsService.Instance;
            if (ttsService != null)
            {
                UnityEngine.Object.DestroyImmediate(ttsService.gameObject);
            }
        }

        /// <summary>
        /// Main シーンの <see cref="ViewRouter"/>（＝ UIDocument）を破棄して、表示中の View に
        /// <c>OnHide</c> を届ける（#101）。<see cref="TearDownMainSceneAndBootstrapSingletons"/> の先頭から呼ぶ。
        /// </summary>
        private static void DestroyViewRouterIfPresent()
        {
            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            if (router != null)
            {
                UnityEngine.Object.DestroyImmediate(router.gameObject);
            }
        }

        /// <summary>
        /// 指定名・型の要素が現れるまで待つ。実時間で <paramref name="timeoutSeconds"/>（既定
        /// <see cref="DefaultTimeoutSeconds"/>）が経過し、かつ最低 <see cref="MinPollCount"/> 回の
        /// ポーリングを終えても見つからなければ、診断情報（画面階層ダンプ含む）を添えて Fail する
        /// （issue #73）。
        /// </summary>
        public static IEnumerator WaitForElement<T>(
            VisualElement root, string elementName, Action<T> onFound, float timeoutSeconds = DefaultTimeoutSeconds)
            where T : VisualElement
        {
            Assert.IsNotNull(root, "検索対象の root が null です。");

            var deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            var polls = 0;
            var element = root.Q<T>(elementName);
            while (element == null && (Time.realtimeSinceStartupAsDouble < deadline || polls < MinPollCount))
            {
                polls++;
                yield return null;
                element = root.Q<T>(elementName);
            }

            if (element == null)
            {
                var message =
                    $"'{elementName}' が {timeoutSeconds:0.#} 秒以内（最低 {MinPollCount} 回のポーリング）に見つかりませんでした。";
                var diagnostics = MainSceneDiagnostics.BuildFailureDiagnostics(root);
                var fullMessage = $"{message}\n{diagnostics}";
                Debug.LogError(fullMessage);
                Assert.Fail(fullMessage);
            }

            onFound(element);
        }

        /// <summary>
        /// 対象要素をクリックする（<see cref="NavigationSubmitEvent"/> を送る）。
        /// issue #73 レビュー H3: <paramref name="element"/> がまだどの Panel にもアタッチされていない
        /// （<see cref="VisualElement.panel"/> が null）状態で <c>SendEvent</c> しても、イベントは
        /// 静かに無視される（＝クリックしたつもりでも何も起きない）。issue #102: 要素を取得した直後でも
        /// アタッチが 1 フレーム遅れることがあるため、送信前に <see cref="WaitForPanelAttachment"/> で
        /// 実時間・最低ポーリング回数の両方を満たすまで待ってから送信する。呼び出し側は
        /// <c>yield return SimulateClickRoutine(element);</c> の形で使うこと。
        /// issue #102 レビュー H2: 待ちを追加するために <c>void</c> から <see cref="IEnumerator"/> に
        /// 変えたが、旧名 <c>SimulateClick</c> のままだと <c>yield return</c> の付け忘れが
        /// 「何もしないテスト」として無警告で通ってしまう。呼び出し側の変換漏れが
        /// コンパイルエラー（CS0103, "その名前は現在のコンテキストに存在しません"）で確実に検出されるよう、
        /// 旧名は残さず <c>SimulateClickRoutine</c> に改名した。
        /// </summary>
        public static IEnumerator SimulateClickRoutine(VisualElement element, VisualElement dumpRoot = null)
        {
            Assert.IsNotNull(element, "SimulateClickRoutine に渡された element が null です。");
            yield return WaitForPanelAttachment(element, dumpRoot: dumpRoot);

            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = element;
            element.SendEvent(evt);
        }

        /// <summary>
        /// 一覧行（<c>RegisterCallback&lt;ClickEvent&gt;</c> で結線されたカスタム行）をクリックする。
        /// <see cref="SimulateClickRoutine"/> は <see cref="NavigationSubmitEvent"/> を送るため
        /// <see cref="Button"/> 以外の任意の <see cref="ClickEvent"/> ハンドラには届かず、専用のヘルパーを使う。
        /// 元は <c>QuestionEditorFormSceneTests</c> にプライベートな重複ヘルパーとして存在していたが、
        /// issue #102 で <see cref="SimulateClickRoutine"/> と同じ Panel アタッチ待ちを共有するためここへ移した
        /// （改名の経緯は <see cref="SimulateClickRoutine"/> のコメントを参照）。
        /// </summary>
        public static IEnumerator SimulateRowClickRoutine(VisualElement element, VisualElement dumpRoot = null)
        {
            Assert.IsNotNull(element, "SimulateRowClickRoutine に渡された element が null です。");
            yield return WaitForPanelAttachment(element, dumpRoot: dumpRoot);

            using var evt = ClickEvent.GetPooled();
            evt.target = element;
            element.SendEvent(evt);
        }

        /// <summary>
        /// 要素が Panel にアタッチされる（<see cref="VisualElement.panel"/> が非 null になる）まで、
        /// 実時間で <paramref name="timeoutSeconds"/>（既定 <see cref="PanelAttachTimeoutSeconds"/>）が経過し、
        /// かつ最低 <see cref="MinPollCount"/> 回のポーリングを終えるまで待つ（<see cref="WaitUntil"/> と
        /// 同じハイブリッド方式）。<see cref="SimulateClickRoutine"/> / <see cref="SimulateRowClickRoutine"/> が
        /// クリック送出の直前に必ず通す（issue #102）。失敗時は要素名（<c>name</c> が未設定なら型名・クラス・
        /// <see cref="VisualElement.userData"/>）を含めて報告する。
        /// </summary>
        public static IEnumerator WaitForPanelAttachment(
            VisualElement element, float timeoutSeconds = PanelAttachTimeoutSeconds, VisualElement dumpRoot = null)
        {
            Assert.IsNotNull(element, "WaitForPanelAttachment に渡された element が null です。");

            var deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            var polls = 0;
            while (element.panel == null && (Time.realtimeSinceStartupAsDouble < deadline || polls < MinPollCount))
            {
                polls++;
                yield return null;
            }

            if (element.panel == null)
            {
                var message =
                    $"{MainSceneDiagnostics.DescribeElementForDiagnostics(element)} が {timeoutSeconds:0.#} 秒以内（最低 {MinPollCount} 回のポーリング）に" +
                    "Panel にアタッチされませんでした（画面遷移中で参照が古くなっている可能性があります）。" +
                    "この状態で SendEvent してもクリックは無視されるため、送信前に検出しました。" +
                    Environment.NewLine + MainSceneDiagnostics.DescribeDetachedRoot(element);
                if (dumpRoot != null)
                {
                    message = $"{message}\n{MainSceneDiagnostics.BuildFailureDiagnostics(dumpRoot)}";
                }

                Debug.LogError(message);
                Assert.Fail(message);
            }
        }

        /// <summary>
        /// 条件が満たされるまで待つ。実時間で <paramref name="timeoutSeconds"/>（既定
        /// <see cref="DefaultTimeoutSeconds"/>）が経過し、かつ最低 <see cref="MinPollCount"/> 回の
        /// ポーリングを終えても満たされなければ Fail する。<paramref name="dumpRoot"/> を渡すと、
        /// 失敗時に画面階層ダンプも添える。
        /// </summary>
        public static IEnumerator WaitUntil(
            Func<bool> condition, string failureMessage, float timeoutSeconds = DefaultTimeoutSeconds, VisualElement dumpRoot = null)
            => WaitUntil(condition, timeoutSeconds, () => failureMessage, dumpRoot);

        /// <summary>
        /// <see cref="WaitUntil(Func{bool}, string, float, VisualElement)"/> のオーバーロード。
        /// <c>GameViewSceneTests</c> / <c>JoinViewSceneTests</c> が個別に持っていた
        /// <c>WaitUntilRealtime(condition, timeoutSeconds, message)</c> と同じ引数順で呼べるようにしたもの
        /// （issue #73 レビュー H2 で統合）。
        /// </summary>
        public static IEnumerator WaitUntil(
            Func<bool> condition, float timeoutSeconds, string failureMessage, VisualElement dumpRoot = null)
            => WaitUntil(condition, timeoutSeconds, () => failureMessage, dumpRoot);

        /// <summary>
        /// <see cref="WaitUntil(Func{bool}, string, float, VisualElement)"/> のオーバーロード。
        /// 失敗メッセージを遅延評価（<see cref="Func{TResult}"/>）したい場合に使う。
        /// </summary>
        public static IEnumerator WaitUntil(
            Func<bool> condition, float timeoutSeconds, Func<string> failureMessage, VisualElement dumpRoot = null)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
            var polls = 0;
            var conditionMet = condition();
            while (!conditionMet && (Time.realtimeSinceStartupAsDouble < deadline || polls < MinPollCount))
            {
                polls++;
                yield return null;
                conditionMet = condition();
            }

            if (!conditionMet)
            {
                var message = failureMessage();
                if (dumpRoot != null)
                {
                    message = $"{message}\n{MainSceneDiagnostics.BuildFailureDiagnostics(dumpRoot)}";
                }

                Debug.LogError(message);
                Assert.Fail(message);
            }
        }
    }
}
