using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.PlayMode.UI.HostSetup;
using TsumugiQuiz.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// <c>-tq-join</c> の負系（issue #8 レビュー M-7）。
    /// 不正な参加コードが指定された場合、Join 画面の自動参加は接続を試みず、理由をログすることを確認する。
    /// </summary>
    public class LaunchOptionsAutoJoinTests
    {
        private const string BootSceneName = "Boot";
        private const string MainSceneName = "Main";
        private const float SceneLoadTimeoutSeconds = 10f;

        private MainSceneTestHelpers.ConsentFileScope _consentScope;
        private HostSetupPreferencesScope _preferencesScope;

        [SetUp]
        public void SeedConsentedStateAndOptions()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);

            _preferencesScope = HostSetupPreferencesScope.Backup();

            // Unity.Netcode にも同名の CommandLineOptions が存在するため完全修飾する（CS0104 対策）。
            LaunchOptionsRunner.SetOptionsForTesting(TsumugiQuiz.Core.CommandLineOptions.Parse(new[]
            {
                "-tq-join", "not-a-valid-code",
                "-tq-name", "自動参加テスト",
            }));
        }

        [TearDown]
        public void RestoreConsentAndPreferencesAndOptions()
        {
            _consentScope?.Restore();
            _preferencesScope?.Restore();
            LaunchOptionsRunner.ResetForTesting();
        }

        [UnityTearDown]
        public IEnumerator TearDownScene()
        {
            MainSceneTestHelpers.TearDownMainSceneAndBootstrapSingletons();

            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TqJoin_WithInvalidCode_DoesNotConnect_AndLogsReason()
        {
            // H-3 で追加したログ（JoinView.OnConnectClicked の「参加コードが不正」分岐）を期待する。
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[JoinView\] 参加コードが不正なため接続を中止しました"));

            yield return SceneManager.LoadSceneAsync(BootSceneName, LoadSceneMode.Single);

            var elapsed = 0f;
            while (SceneManager.GetActiveScene().name != MainSceneName && elapsed < SceneLoadTimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(MainSceneName, SceneManager.GetActiveScene().name, "Main シーンへ遷移しているはず。");

            // JoinView.OnShow は同期的に自動参加を試みて即座に中止するが、念のため数フレーム待つ。
            yield return null;
            yield return null;
            yield return null;

            Assert.IsFalse(
                NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient,
                "不正な参加コードでは接続を試みない（クライアントとして開始しない）はず。");
        }
    }
}
