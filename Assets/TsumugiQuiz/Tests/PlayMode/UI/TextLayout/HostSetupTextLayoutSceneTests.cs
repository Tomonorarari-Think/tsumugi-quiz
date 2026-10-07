using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.PlayMode.UI.HostSetup;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.TextLayout
{
    /// <summary>
    /// issue #189: ホスト設定画面（ホスト開始後）の文字の崩れの回帰テスト。ホスト開始の手順と
    /// 実ネットワークに出ないフェイクの差し込みは <see cref="LobbyViewSceneTests"/> と共通。
    /// </summary>
    public sealed class HostSetupTextLayoutSceneTests
    {
        private ConsentFileScope _consentScope;
        private HostSetupPreferencesScope _preferencesScope;
        private AppSettingsFileScope _appSettingsScope;

        [SetUp]
        public void SetUp()
        {
            _consentScope = ConsentFileScope.Backup();
            _appSettingsScope = AppSettingsFileScope.Backup();
            _preferencesScope = HostSetupPreferencesScope.Backup();
            var store = ConsentGate.CreateDefaultStore();
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreFiles()
        {
            _consentScope?.Restore();
            _preferencesScope?.Restore();
            _appSettingsScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // LobbyViewSceneTests と同じ順序（ホスト停止 → シーンのアンロード → シングルトン破棄）。
            NetworkBootstrap.Instance?.Service?.Stop();
            yield return UnloadMainSceneRoutine();
            TearDownMainSceneAndBootstrapSingletons();
        }

        /// <summary>
        /// 修正前: 「インターネット用」（120px）が幅 120px の列で flex-shrink により 117px に縮められ、末尾が欠けていた。
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator 参加コード行の見出しは1行に収まる()
        {
            VisualElement hostSetupRoot = null;
            yield return StartHosting(r => hostSetupRoot = r);

            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(hostSetupRoot, viewport);

                var rows = hostSetupRoot.Query<VisualElement>(className: "join-code-row").ToList();
                Assert.That(rows.Count, Is.EqualTo(2), "参加コード行（インターネット用 / LAN 用）が見つかりません。");
                foreach (var row in rows)
                {
                    var caption = row.Q<Label>(className: "small-text");
                    Assert.That(TextLayoutProbe.SingleLineWidth(caption, caption.text),
                        Is.LessThanOrEqualTo(caption.contentRect.width + 0.5f),
                        $"{viewport}: 「{caption.text}」が幅 {caption.contentRect.width:0.0}px に収まっていません。");
                }
            }
        }

        /// <summary>
        /// 修正前: 「ホストを停止して戻りますか？」の確認パネルが縦に削られ、中の「はい、戻る」「キャンセル」が
        /// 下段の「ロビーへ」「戻る」と重なっていた（900x750 相当）。
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator 戻る確認パネルは下段のボタンと重ならない()
        {
            VisualElement hostSetupRoot = null;
            yield return StartHosting(r => hostSetupRoot = r);

            yield return SimulateClickRoutine(hostSetupRoot.Q<Button>("back-button"));
            var confirm = hostSetupRoot.Q<VisualElement>("back-confirm-container");
            yield return WaitUntil(
                () => confirm.resolvedStyle.display == DisplayStyle.Flex, "戻る確認パネルが表示されません。", dumpRoot: hostSetupRoot);

            var actions = hostSetupRoot.Q<VisualElement>("host-setup-actions");
            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(hostSetupRoot, viewport);

                Assert.That(confirm.worldBound.yMax, Is.LessThanOrEqualTo(actions.worldBound.yMin + 0.5f),
                    $"{viewport}: 確認パネル（下端 {confirm.worldBound.yMax:0}）が下段のボタン行（上端 {actions.worldBound.yMin:0}）に重なっています。");
                foreach (var name in new[] { "confirm-back-button", "cancel-back-button" })
                {
                    var button = confirm.Q<Button>(name);
                    Assert.That(button.worldBound.yMax, Is.LessThanOrEqualTo(confirm.worldBound.yMax + 0.5f),
                        $"{viewport}: {name} が確認パネルの外にはみ出しています。");
                    Assert.That(button.worldBound.yMax, Is.LessThanOrEqualTo(actions.worldBound.yMin + 0.5f),
                        $"{viewport}: {name} が下段のボタン行に重なっています。");
                }
            }

            yield return SimulateClickRoutine(hostSetupRoot.Q<Button>("cancel-back-button"));
        }

        /// <summary>
        /// #189 レビュー L-4: 明示改行を入れたホスト設定の文言（ポート番号のラベル、自動ポート開放に失敗したときの
        /// 案内文、Tailscale の注記）が、明示改行の位置以外では折り返さないこと。修正前は「…開放してく / ださい。」
        /// 「（README 参 / 照）」のように語の途中で折り返していた。
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator ホスト設定の案内文とラベルは明示改行の位置でだけ改行する()
        {
            VisualElement hostSetupRoot = null;
            yield return StartHosting(r => hostSetupRoot = r);

            var guide = hostSetupRoot.Q<VisualElement>("manual-guide-container");
            Assert.AreEqual(DisplayStyle.Flex, guide.resolvedStyle.display,
                "フェイクの UPnP は失敗するので、ポート開放の手動設定の案内が出ているはず。");

            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(hostSetupRoot, viewport);

                var labels = hostSetupRoot.Q<VisualElement>("setup-form").Query<Label>(className: "unity-base-field__label").ToList();
                labels.AddRange(guide.Children().OfType<Label>());
                Assert.That(labels.Count, Is.GreaterThanOrEqualTo(4), "確認対象のラベルが見つかりません。");

                var orphans = labels
                    .Where(label => TextLayoutProbe.IsDisplayed(label))
                    .SelectMany(label => TextLayoutProbe.FindImplicitlyWrappedLines(label))
                    .ToList();
                Assert.IsEmpty(orphans, $"{viewport}: 明示改行以外の位置で折り返す文言（語の途中・末尾だけの改行になりうる）: {string.Join(" / ", orphans)}");
            }
        }

        private static IEnumerator StartHosting(Action<VisualElement> onRoot)
        {
            VisualElement panelRoot = null;
            yield return LobbyViewSceneTests.LoadMainThroughBootAndInstallFakes(r => panelRoot = r);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", b => hostButton = b);
            yield return SimulateClickRoutine(hostButton);
            yield return LobbyViewSceneTests.StartHostingWithAutoPort(panelRoot);

            Button lobbyButton = null;
            yield return WaitForElement<Button>(panelRoot, "lobby-button", b => lobbyButton = b);
            yield return WaitUntil(() => lobbyButton.enabledSelf, "到達性の解決後は「ロビーへ」が有効になるはず。");

            VisualElement hostSetupRoot = null;
            yield return WaitForElement<VisualElement>(panelRoot, "host-setup-root", r => hostSetupRoot = r);
            onRoot(hostSetupRoot);
        }
    }
}
