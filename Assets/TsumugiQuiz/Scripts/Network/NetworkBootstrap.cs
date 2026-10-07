using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// Boot シーンに置く起動スクリプト（docs/architecture.md §2、docs/tasks/setup-brief.md K4）。
    /// <see cref="NetworkManager"/> と同じ GameObject に付けて使う。
    ///
    /// 役割:
    /// <list type="bullet">
    ///   <item><description><see cref="NetworkService"/> を生成して常駐させる</description></item>
    ///   <item><description>
    ///     <c>HostConnectivityService</c>（UPnP・グローバル IP・参加コード）を生成・保持する（#5 C-1）。
    ///     View（<c>HostSetupView</c>）が Show/Hide されるたびに破棄されると、Lobby へ遷移した後も
    ///     ポートマッピングの更新ループを生かし続けたい要件を満たせないため、Boot に常駐させる。
    ///     詳細は <c>NetworkBootstrap.HostConnectivity.cs</c>。
    ///   </description></item>
    ///   <item><description>
    ///     ロビーの共有状態（<see cref="LobbyState"/>、#7）をホスト開始と同時にスポーンし、保持する。
    ///     詳細は <c>NetworkBootstrap.Lobby.cs</c>。
    ///   </description></item>
    ///   <item><description>コマンドライン引数（<c>-tq-port</c> / <c>-port</c>）を読む</description></item>
    ///   <item><description><c>Main.unity</c> へ遷移する</description></item>
    /// </list>
    ///
    /// <c>DontDestroyOnLoad</c> は <see cref="NetworkManager"/> 自身が <c>OnEnable</c> で行うため
    /// （NGO 2.13.2 <c>Runtime/Core/NetworkManager.cs</c>、親を持たない場合のみ）、
    /// 本スクリプトは同じ GameObject に乗っていれば一緒に引き継がれる。
    /// そのため NetworkManager を子オブジェクトにしないこと。
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    [DisallowMultipleComponent]
    public sealed partial class NetworkBootstrap : MonoBehaviour
    {
        [Header("シーン遷移")]
        [SerializeField]
        [Tooltip("Boot の次に読み込むシーン名。Build Settings に登録されている必要がある。")]
        private string _mainSceneName = "Main";

        [SerializeField]
        [Tooltip("起動時に自動で次のシーンを読み込むか。テスト時は false にできる。")]
        private bool _loadMainSceneOnStart = true;

        private NetworkService _service;

        /// <summary>常駐している唯一のインスタンス。Boot シーンを通っていない場合は null。</summary>
        public static NetworkBootstrap Instance { get; private set; }

        /// <summary>常駐している <see cref="NetworkService"/>。</summary>
        public NetworkService Service => _service;

        /// <summary>コマンドライン引数を反映した起動時設定。</summary>
        public NetworkRuntimeOptions RuntimeOptions { get; private set; } = NetworkRuntimeOptions.Default;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Boot シーンを 2 度読み込んだ場合の保険。NetworkManager ごと重複するため丸ごと破棄する。
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // issue #161: テストが Boot シーンロード前に差し込んだ HostConnectivityFactory の上書きが
            // あれば、Start() を待たずこの時点で即反映する。ApplySavedAppSettings() が AppPaths 未設定
            // （InvalidOperationException）で早期 return すると ApplyAppSettings() 自体が呼ばれず、
            // そちら側での再上書き（NetworkBootstrap.AppSettings.cs）が発生しないため、その経路でも
            // 上書きが失われないようにするために必要。
            if (HostConnectivityFactoryOverrideForTesting != null)
            {
                HostConnectivityFactory = HostConnectivityFactoryOverrideForTesting;
            }

            RuntimeOptions = NetworkRuntimeOptions.FromCommandLine(Environment.GetCommandLineArgs());

            var networkManager = GetComponent<NetworkManager>();
            _service = new NetworkService(networkManager);
            _service.Stopped += HandleNetworkServiceStopped;

            // ロビーの共有状態（#7）はホスト開始と同時にスポーンする（NetworkBootstrap.Lobby.cs）。
            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnServerStopped += HandleNetworkStopped;
            networkManager.OnClientStopped += HandleNetworkStopped;

            // M-3（issue #8 レビュー）: これは起動引数から解析した「要求ポート」であり、実際にバインドされる
            // ポートとは限らない（-tq-port 0 は既定値へフォールバックする）。実際のポートは、UI 経由の
            // ホスト開始時に HostSetupView.OnHostStartCompleted が
            // 「[HostSetupView] ホストを開始しました activePort=...」でログする。
            // buildNumber / buildGuid は、ビルドの違う相手を拒否したとき（#204）の「バージョンが異なります（ホスト: x / あなた: y）」の
            // 番号と突き合わせるために残す（Editor では 0 / 空）。
            Debug.Log(
                $"[NetworkBootstrap] 起動しました requestedPort={RuntimeOptions.Port} protocolVersion={NetworkConstants.ProtocolVersion} "
                + $"buildNumber={LocalBuildIdentity.DisplayNumber} buildGuid={LocalBuildIdentity.BuildHash}");
        }

        private void Start()
        {
            // アプリ設定（app-settings.json）の反映は Awake ではなく Start で行う（issue #28 H5/M1）。
            // AppPaths.DataRoot を設定する AppPathsBootstrap は別 GameObject に載っており、
            // GameObject 同士の Awake 実行順は保証されないため、すべての Awake が終わった Start で読む。
            ApplySavedAppSettings();

            if (!_loadMainSceneOnStart || string.IsNullOrEmpty(_mainSceneName))
            {
                return;
            }

            if (SceneManager.GetActiveScene().name == _mainSceneName)
            {
                return;
            }

            // NGO のシーン管理（NetworkConfig.EnableSceneManagement）はホスト開始後にだけ働く。
            // ここはホスト開始前なので、通常の SceneManager で遷移してよい。
            SceneManager.LoadScene(_mainSceneName, LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_service != null)
            {
                _service.Stopped -= HandleNetworkServiceStopped;
            }

            var networkManager = GetComponent<NetworkManager>();
            if (networkManager != null)
            {
                networkManager.OnServerStarted -= HandleServerStarted;
                networkManager.OnServerStopped -= HandleNetworkStopped;
                networkManager.OnClientStopped -= HandleNetworkStopped;
            }

            _lobby = null;

            _service?.Dispose();
            _service = null;

            // アプリ終了時のポートマッピング解放は HostConnectivityService 自身が
            // Application.quitting を購読して行う（#3）。ここでは参照を破棄するだけでよい。
            _hostConnectivity?.Dispose();
            _hostConnectivity = null;
        }
    }
}
