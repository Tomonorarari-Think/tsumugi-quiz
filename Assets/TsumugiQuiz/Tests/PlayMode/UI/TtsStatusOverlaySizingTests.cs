using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// Title 隅から開く <c>TtsStatusPanel</c> のオーバーレイが、画面全体（<c>Document.rootVisualElement</c>）を
    /// 覆うサイズになることの検証（#25 M-8）。<c>title-root</c>（<c>screen-root</c>、中央寄せの flex 列）に
    /// 間借りすると全画面を覆えない回帰を防ぐ。
    /// </summary>
    public sealed class TtsStatusOverlaySizingTests
    {
        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        [SetUp]
        public void SeedConsentedState()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();

            var store = TsumugiQuiz.UI.ConsentGate.CreateDefaultStore();
            var requiredTerms = TsumugiQuiz.UI.TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, System.DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreConsentFile()
        {
            _consentScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator オーバーレイはDocumentRootVisualElement全体を覆う()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button statusButton = null;
            yield return WaitForElement<Button>(panelRoot, "tts-status-button", found => statusButton = found);
            Assert.That(statusButton, Is.Not.Null, "tts-status-button が見つかりません。");

            yield return SimulateClickRoutine(statusButton);

            VisualElement overlay = null;
            yield return WaitForElement<VisualElement>(panelRoot, "tts-status-overlay", found => overlay = found);
            Assert.That(overlay, Is.Not.Null, "tts-status-overlay が見つかりません。パネルが開いていない可能性があります。");

            // レイアウト解決を待つ。
            yield return null;
            yield return null;

            // #105 レビュー M-2: レイアウト未解決のまま両辺が 0 == 0 で一致してしまう空振りを防ぐ。
            Assert.That(panelRoot.resolvedStyle.height, Is.GreaterThan(0f),
                "panelRoot.resolvedStyle.height が 0 のままです（レイアウトが未解決の可能性）。");

            Assert.That(overlay.parent, Is.SameAs(panelRoot),
                "オーバーレイは Document.rootVisualElement の直下に追加されていること（M-8）。");
            Assert.That(overlay.resolvedStyle.width, Is.EqualTo(panelRoot.resolvedStyle.width).Within(1f),
                "オーバーレイの幅が画面全体と一致すること。");
            Assert.That(overlay.resolvedStyle.height, Is.EqualTo(panelRoot.resolvedStyle.height).Within(1f),
                "オーバーレイの高さが画面全体と一致すること。");
        }
    }
}
