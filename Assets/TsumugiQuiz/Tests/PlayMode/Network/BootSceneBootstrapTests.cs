using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// Boot シーンに置いた <see cref="NetworkManager"/> が Main シーンへの遷移後も
    /// 破棄されずに残ることを確認する（docs/architecture.md §2、受け入れ条件 1）。
    /// </summary>
    public class BootSceneBootstrapTests
    {
        private const string BootSceneName = "Boot";
        private const string MainSceneName = "Main";
        private const float SceneLoadTimeoutSeconds = 10f;

        [TearDown]
        public void TearDown()
        {
            // DontDestroyOnLoad に残った常駐オブジェクトを片付けて、後続のテストへ持ち越さない。
            var bootstrap = NetworkBootstrap.Instance;
            if (bootstrap != null)
            {
                Object.DestroyImmediate(bootstrap.gameObject);
            }

            // Boot シーンには SePlayer（#35）も常駐配置されているため、同様に後片付けする。
            var sePlayer = SePlayer.Instance;
            if (sePlayer != null)
            {
                Object.DestroyImmediate(sePlayer.gameObject);
            }

            // TtsService（#22）も Boot 常駐（DontDestroyOnLoad）。残したままにすると
            // 後続のテストが自前で用意した TtsService が Awake の重複ガードで破棄されてしまう
            // （#23 の同期再生テスト・VoicevoxTtsServiceTests が TtsService.Instance を前提に動くため）。
            var ttsService = TtsService.Instance;
            if (ttsService != null)
            {
                Object.DestroyImmediate(ttsService.gameObject);
            }

            // AppPathsBootstrap（#71）も Boot 常駐。GameObject 自体は片付けるが、
            // AppPaths が保持する既定値（ConfigureDefault）は静的状態でありプロセス寿命の間有効なままでよい
            // （後続のテストにとっても Application.persistentDataPath 相当の妥当な既定値のため害はない）。
            var appPathsBootstrap = AppPathsBootstrap.Instance;
            if (appPathsBootstrap != null)
            {
                Object.DestroyImmediate(appPathsBootstrap.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator BootScene_KeepsNetworkManagerAcrossSceneTransition()
        {
            yield return SceneManager.LoadSceneAsync(BootSceneName, LoadSceneMode.Single);

            var bootstrap = NetworkBootstrap.Instance;
            Assert.IsNotNull(bootstrap, "Boot シーンに NetworkBootstrap が配置されているはず。");
            Assert.IsNotNull(NetworkManager.Singleton, "NetworkManager が常駐しているはず。");
            Assert.IsNotNull(bootstrap.Service, "NetworkService が生成されているはず。");

            // NetworkBootstrap.Start() が Main シーンを読み込む。
            var elapsed = 0f;
            while (SceneManager.GetActiveScene().name != MainSceneName && elapsed < SceneLoadTimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(MainSceneName, SceneManager.GetActiveScene().name, "Main シーンへ遷移しているはず。");

            // NetworkManager は自身の OnEnable で DontDestroyOnLoad される（NGO 2.13.2）。
            // 同じ GameObject に乗せた NetworkBootstrap も一緒に引き継がれる。
            Assert.IsNotNull(NetworkManager.Singleton, "遷移後も NetworkManager が残っているはず。");
            Assert.AreSame(bootstrap, NetworkBootstrap.Instance, "遷移後も同じインスタンスが残っているはず。");
            Assert.AreEqual("DontDestroyOnLoad", bootstrap.gameObject.scene.name);
            Assert.AreEqual(NetworkConstants.DefaultPort, bootstrap.RuntimeOptions.Port);
        }
    }
}
