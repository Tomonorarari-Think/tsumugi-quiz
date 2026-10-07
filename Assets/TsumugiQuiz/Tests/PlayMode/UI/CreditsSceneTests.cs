using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// Title → Credits → 戻る の画面遷移と、Credits View 内の主要要素の表示を検証する
    /// PlayMode テスト（issue #33）。<see cref="MainSceneUiTests"/> と同様、事前に「同意済み」の
    /// consent.json を用意して Title が表示される状態にしてから検証する。
    /// </summary>
    public class CreditsSceneTests
    {
        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        [SetUp]
        public void SeedConsentedState()
        {
            // 退避（バックアップの読み取り）が完了してから初めて consent.json を書き換える（M-8）。
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);
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
        public IEnumerator TitleView_CreditsButton_ShowsCreditsView_WithConsentStatusAndAppVersion()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button creditsButton = null;
            yield return WaitForElement<Button>(panelRoot, "credits-button", found => creditsButton = found);
            Assert.IsNotNull(creditsButton, "Title View の credits-button が見つかりません。");

            yield return SimulateClickRoutine(creditsButton);

            Label creditsHeading = null;
            yield return WaitForElement<Label>(panelRoot, "credits-heading", found => creditsHeading = found);
            Assert.IsNotNull(creditsHeading, "credits-button クリック後に Credits View（credits-heading）へ遷移していません。");

            var characterSection = panelRoot.Q<VisualElement>("credits-character-section");
            Assert.IsNotNull(characterSection, "credits-character-section が見つかりません。");
            Assert.IsTrue(characterSection.childCount > 0, "キャラクターセクションに項目が描画されていません。");

            // M-4: キャラクターセクションに確定クレジット文言「VOICEVOX:春日部つむぎ」が含まれること。
            var characterCreditLabel = panelRoot.Q<Label>("credits-credit-line-tsumugi-character-credit");
            Assert.IsNotNull(characterCreditLabel, "credits-credit-line-tsumugi-character-credit が見つかりません。");
            StringAssert.Contains("VOICEVOX:春日部つむぎ", characterCreditLabel.text);

            var voiceSection = panelRoot.Q<VisualElement>("credits-voice-section");
            Assert.IsNotNull(voiceSection, "credits-voice-section が見つかりません。");
            Assert.IsTrue(voiceSection.childCount > 0, "音声セクションに項目が描画されていません。");

            var ossSection = panelRoot.Q<VisualElement>("credits-oss-section");
            Assert.IsNotNull(ossSection, "credits-oss-section が見つかりません。");
            Assert.IsTrue(ossSection.childCount > 0, "OSS セクションに項目が描画されていません。");

            var consentStatusLabel = panelRoot.Q<Label>("credits-consent-status");
            Assert.IsNotNull(consentStatusLabel, "credits-consent-status が見つかりません。");
            StringAssert.Contains("既に同意済みです", consentStatusLabel.text,
                "テスト開始前に同意済みの consent.json を用意しているのに、同意済みとして表示されていません（TermsView と揃えた文言のはずです）。");

            var appVersionLabel = panelRoot.Q<Label>("credits-app-version");
            Assert.IsNotNull(appVersionLabel, "credits-app-version が見つかりません。");
            StringAssert.Contains(Application.version, appVersionLabel.text);
            StringAssert.Contains(
                $"ビルド番号 {LocalBuildIdentity.DisplayNumber}",
                appVersionLabel.text,
                "ビルドの違う相手を拒否したときの番号（#204）と突き合わせられるよう、自分のビルド番号を出すはず。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator CreditsView_OssSection_ShowsUnityMadeWithUnityAttribution()
        {
            // issue #100, docs/licenses.md §11: Unity Editor Software Terms Section 2.12 が要求する
            // 帰属定型文（Made with Unity / Copyright 行）と、Unity's Trademark Guidelines の
            // Trademark Notice and Attribution Statement が OSS セクションの unity-packages
            // クレジット行に表示されていることを検証する（レビュー M-1: Copyright 行も含めて検証）。
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button creditsButton = null;
            yield return WaitForElement<Button>(panelRoot, "credits-button", found => creditsButton = found);
            yield return SimulateClickRoutine(creditsButton);

            Label unityCreditLabel = null;
            yield return WaitForElement<Label>(
                panelRoot, "credits-credit-line-unity-packages", found => unityCreditLabel = found);
            Assert.IsNotNull(unityCreditLabel, "credits-credit-line-unity-packages が見つかりません。");
            StringAssert.Contains("was made with Unity®", unityCreditLabel.text);
            StringAssert.Contains(
                "Unity is a trademark or registered trademark of Unity Technologies", unityCreditLabel.text);
            StringAssert.Contains(
                $"Copyright © 2005-{DateTime.Now.Year} Unity Technologies. All rights reserved.",
                unityCreditLabel.text);
            StringAssert.Contains(
                "is not sponsored by or affiliated with Unity Technologies or its affiliates.", unityCreditLabel.text);
            StringAssert.Contains(
                "Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.",
                unityCreditLabel.text);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator CreditsView_BackButton_ReturnsToTitle()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button creditsButton = null;
            yield return WaitForElement<Button>(panelRoot, "credits-button", found => creditsButton = found);
            yield return SimulateClickRoutine(creditsButton);

            Button backButton = null;
            yield return WaitForElement<Button>(panelRoot, "back-button", found => backButton = found);
            Assert.IsNotNull(backButton, "Credits View の back-button が見つかりません。");
            Assert.IsTrue(backButton.enabledSelf, "Title から遷移した直後は履歴が2件になり、戻るボタンが有効になっているはずです。");

            yield return SimulateClickRoutine(backButton);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            Assert.IsNotNull(hostButton, "戻るボタンのクリック後に Title View（host-button）へ戻っていません。");
            Assert.IsNull(panelRoot.Q<Label>("credits-heading"), "戻った後も Credits View の要素が残っています。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator CreditsView_TermsButton_NavigatesToTermsView()
        {
            // H-1: Credits 側では同意を書き換えず、常に TermsView へ遷移して確認・撤回を行わせる。
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button creditsButton = null;
            yield return WaitForElement<Button>(panelRoot, "credits-button", found => creditsButton = found);
            yield return SimulateClickRoutine(creditsButton);

            Button termsButton = null;
            yield return WaitForElement<Button>(panelRoot, "terms-button", found => termsButton = found);
            Assert.IsNotNull(termsButton, "Credits View の terms-button（利用規約の確認・撤回）が見つかりません。");
            StringAssert.Contains("利用規約の確認・撤回", termsButton.text);

            yield return SimulateClickRoutine(termsButton);

            Toggle consentToggle = null;
            yield return WaitForElement<Toggle>(panelRoot, "consent-toggle", found => consentToggle = found);
            Assert.IsNotNull(consentToggle, "terms-button クリック後に Terms View へ遷移していません。");

            // 既に同意済みの状態で Terms へ来ているので、TermsView 側の撤回ボタンが表示されているはず。
            Button revokeButton = null;
            yield return WaitForElement<Button>(panelRoot, "revoke-button", found => revokeButton = found);
            Assert.IsNotNull(revokeButton, "Terms View の revoke-button（同意を撤回）が見つかりません。撤回は TermsView の責務のままのはずです。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator CreditsView_ExpandLicenseFoldout_LazilyGeneratesBodyLabel_WithResolvedHeight()
        {
            // H-3: Foldout の本文は展開するまで生成されない（遅延生成）。展開後は本文 Label が
            // 生成され、内側の ScrollView 経由でレイアウトされて高さが解決されることを確認する。
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button creditsButton = null;
            yield return WaitForElement<Button>(panelRoot, "credits-button", found => creditsButton = found);
            yield return SimulateClickRoutine(creditsButton);

            Foldout foldout = null;
            yield return WaitForElement<Foldout>(panelRoot, "credits-license-foldout-voicevox-core", found => foldout = found);
            Assert.IsNotNull(foldout, "voicevox-core のライセンス Foldout（credits-license-foldout-voicevox-core）が見つかりません。");

            Assert.IsNull(panelRoot.Q<Label>("credits-license-body-voicevox-core"),
                "Foldout を展開していないのに本文 Label が生成されています（遅延生成のはずです）。");

            foldout.value = true;

            Label bodyLabel = null;
            yield return WaitForElement<Label>(panelRoot, "credits-license-body-voicevox-core", found => bodyLabel = found);
            Assert.IsNotNull(bodyLabel, "Foldout を展開した後も本文 Label が生成されていません。");
            StringAssert.Contains("Hiroshiba Kazuyuki", bodyLabel.text);

            yield return WaitUntil(() => bodyLabel.resolvedStyle.height > 0f,
                "展開後の本文 Label の resolvedStyle.height が 0 より大きくなりませんでした。");
        }
    }
}
