using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// Main シーン起動時の利用規約同意ゲート（requirements.md FR-71〜FR-74）を検証する PlayMode テスト。
    /// consent.json が存在しない状態から Terms View が表示され、本文の最後までスクロールしチェックを
    /// 入れるまで同意ボタンが無効であること（FR-72・M-3）、同意すると Title View に遷移し
    /// consent.json が生成されることを確認する。
    /// テスト前後で実際の consent.json（Application.persistentDataPath 配下）を退避・削除する。
    /// </summary>
    public class TermsConsentSceneTests
    {
        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        [SetUp]
        public void BackupAndClearConsentFile()
        {
            // 退避（バックアップの読み取り）が完了してから削除する（M-8）。
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();
            _consentScope.DeleteCurrentFile();
        }

        [TearDown]
        public void RestoreConsentFile()
        {
            _consentScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator UnloadScene()
        {
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator MainScene_NoConsentRecord_ShowsTermsView_AgreeNavigatesToTitle_AndRecordsConsent()
        {
            Assert.IsFalse(File.Exists(_consentScope.FilePath), "テスト開始前提として consent.json が存在しないはずです。");

            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Toggle consentToggle = null;
            yield return WaitForElement<Toggle>(panelRoot, "consent-toggle", found => consentToggle = found);
            Assert.IsNotNull(consentToggle, "未同意状態で Terms View の consent-toggle が見つかりません（Title が表示されている可能性があります）。");

            Button agreeButton = null;
            yield return WaitForElement<Button>(panelRoot, "agree-button", found => agreeButton = found);
            Assert.IsNotNull(agreeButton, "agree-button が見つかりません。");

            ScrollView scrollView = null;
            yield return WaitForElement<ScrollView>(panelRoot, "terms-scroll-view", found => scrollView = found);
            Assert.IsNotNull(scrollView, "terms-scroll-view が見つかりません。");

            Assert.IsFalse(consentToggle.enabledSelf, "本文の最後までスクロールする前は同意チェックが無効化されているはずです（M-3）。");
            Assert.IsFalse(agreeButton.enabledSelf, "チェック前は同意ボタンが無効化されているはずです（FR-72）。");

            // 本文の最後までスクロールする（M-3）。ScrollView のレイアウトが確定するまで数フレームかかることが
            // あるため、同意チェックが有効化されるまでポーリングしながらスクロール位置を最大まで進める。
            yield return WaitUntil(() =>
            {
                var scroller = scrollView.verticalScroller;
                if (scroller.highValue > 0f)
                {
                    scroller.value = scroller.highValue;
                }

                return consentToggle.enabledSelf;
            }, "本文の最後までスクロールしても consent-toggle が有効になりませんでした。");

            consentToggle.value = true;
            Assert.IsTrue(agreeButton.enabledSelf, "チェック後は同意ボタンが有効化されるはずです（FR-72）。");

            yield return SimulateClickRoutine(agreeButton);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            Assert.IsNotNull(hostButton, "同意後に Title View（host-button）へ遷移していません。");

            Assert.IsTrue(File.Exists(_consentScope.FilePath), "同意後に consent.json が生成されていません（FR-73）。");
        }
    }
}
