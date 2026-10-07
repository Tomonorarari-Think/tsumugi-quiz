using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.Shared.Questions;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// issue #172 / #193: 3 列レイアウト（左 = 参加者パネル / 中央 / 右 = 立ち絵）で、立ち絵が非表示のときは
    /// 右列を隠し、中央列を最大幅（880px）まで広げて参加者パネルごと行の中央に寄せる（左寄りに見えない）こと、
    /// 立ち絵が表示されているときは右列が行の右端にあり中央列が残りを埋めること（16:9。21:9 では中央列が同じ最大幅で
    /// 頭打ちになり、3 列ごと中央に寄る。#213）を確かめる PlayMode テスト。
    /// </summary>
    /// <remarks>
    /// #172 の症状（立ち絵が非表示の間、右側に立ち絵の分の空きが残って本体が左寄りに見える）は、#193 の 3 列化後は
    /// 「右列を隠したうえで、中央列に最大幅を付けて行を中央寄せにする」ことで防ぐ（theme-views-game.uss）。
    /// バッチ実行の画面サイズは固定なので、<c>game-root</c> を 1600x900 基準の論理ビューポートに固定して測る。
    /// </remarks>
    public sealed class GameViewCharacterLayoutSceneTests
    {
        private const int TestImageWidth = 16;
        private const int TestImageHeight = 32;

        /// <summary>列の間隔（参加者パネルの margin-right / 右列の margin-left、var(--spacing-m)）。</summary>
        private const float ColumnGap = 16f;

        /// <summary>3 列のときの列の間隔の合計。</summary>
        private const float ColumnGaps = ColumnGap * 2f;

        private const float Tolerance = GameViewLayoutProbe.LayoutTolerance;

        /// <summary>
        /// 生成スクリプトの既定（バストアップ、--max-height 1280）が書き出す立ち絵の大きさ（#191）。
        /// 素材はテストに使えない（docs/licenses.md §3）ので、同じ大きさのノイズ画像で代用する。
        /// </summary>
        private const int BustUpImageWidth = 987;
        private const int BustUpImageHeight = 1280;

        /// <summary>全身（tsumugi_v2.png、2037x4084 ≒ 1:2）へのフォールバックを模した縦長の画像。</summary>
        private const int FullBodyImageWidth = 200;
        private const int FullBodyImageHeight = 401;

        private const string FrameLogTag = "#191 実測";

        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        [SetUp]
        public void SeedConsentedState()
        {
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
            var bootstrap = NetworkBootstrap.Instance;
            bootstrap?.Service?.Stop();
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
            if (bootstrap != null)
            {
                UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
            }
        }

        /// <summary>
        /// 既定のテスト環境（立ち絵素材は配置されていない）で本体パネルが広がることを確認する。
        /// </summary>
        /// <remarks>
        /// レビュー L4: 「既定環境では立ち絵未配置」という前提は、実ユーザーの環境で
        /// <c>scripts/verify.ps1</c> を介さず直接実行した場合には成り立たない可能性がある
        /// （実際に立ち絵を配置済みの開発機で実行すると、その配置が見えてしまう）。
        /// <see cref="AssertIsolatedDataRootOrIgnore"/> で隔離されたデータルートでの実行に限定した上で、
        /// <see cref="LegacyCharacterImageScope"/> で「未配置」であることを明示的に保証する。
        /// </remarks>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator NoCharacterPlaced_HidesRightColumn_CentersMainWithMaxWidth()
        {
            AssertIsolatedDataRootOrIgnore();

            var imageScope = LegacyCharacterImageScope.Backup();
            GameView.ResetCharacterTextureCacheForTesting();
            try
            {
                VisualElement panelRoot = null;
                yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

                yield return StartHostAndShowGame(panelRoot);

                yield return WaitForLayoutSettled();

                var row = panelRoot.Q<VisualElement>("game-content-row");
                var main = panelRoot.Q<VisualElement>("game-content-main");
                var slot = panelRoot.Q<VisualElement>("character-view-instance");
                var characterRoot = panelRoot.Q<VisualElement>("character-root");
                Assert.IsNotNull(row, "game-content-row が見つかりません。");
                Assert.IsNotNull(main, "game-content-main が見つかりません。");
                Assert.IsNotNull(slot, "character-view-instance が見つかりません。");
                Assert.IsNotNull(characterRoot, "character-root が見つかりません。");

                Assert.AreEqual(
                    DisplayStyle.None, characterRoot.resolvedStyle.display,
                    "立ち絵素材を配置していない既定環境では、立ち絵は非表示のはず。");
                Assert.AreEqual(
                    DisplayStyle.None, slot.resolvedStyle.display,
                    "立ち絵が非表示のときは外枠（character-view-instance）ごと非表示にして隙間を詰めるはず（issue #172）。");

                Assert.IsTrue(
                    main.ClassListContains(GameView.NoCharacterMainClassName),
                    "立ち絵が非表示のときは game-content-main に修飾クラスが付くはず。");

                AssertNoCharacterLayout(panelRoot);
            }
            finally
            {
                GameView.ResetCharacterTextureCacheForTesting();
                imageScope.Restore();
            }
        }

        /// <summary>
        /// <c>character.enabled</c>（アプリ設定）を明示的に false にした場合も同じ幅調整が働くことを確認する
        /// （<c>CharacterView</c> 自体が生成されない静的な非表示経路、GameView.Character.cs 参照）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator CharacterEnabledSettingFalse_AlsoCentersMainWithMaxWidth()
        {
            var appSettingsScope = MainSceneTestHelpers.AppSettingsFileScope.Backup();
            try
            {
                var store = new AppSettingsStore();
                var current = store.Load().Settings;
                store.Save(current.WithCharacterEnabled(false));
                GameView.InvalidateCharacterEnabledCache();

                VisualElement panelRoot = null;
                yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

                yield return StartHostAndShowGame(panelRoot);

                yield return WaitForLayoutSettled();

                var row = panelRoot.Q<VisualElement>("game-content-row");
                var main = panelRoot.Q<VisualElement>("game-content-main");
                var characterRoot = panelRoot.Q<VisualElement>("character-root");
                var slot = panelRoot.Q<VisualElement>("character-view-instance");
                Assert.IsNotNull(row);
                Assert.IsNotNull(main);
                Assert.IsNotNull(characterRoot);
                Assert.IsNotNull(slot);

                Assert.AreEqual(
                    DisplayStyle.None, characterRoot.resolvedStyle.display,
                    "character.enabled = false のときは character-root が非表示のはず。");
                Assert.AreEqual(
                    DisplayStyle.None, slot.resolvedStyle.display,
                    "character.enabled = false のときは外枠（character-view-instance）ごと非表示にするはず。");
                Assert.IsTrue(
                    main.ClassListContains(GameView.NoCharacterMainClassName),
                    "character.enabled = false でも game-content-main に修飾クラスが付くはず。");

                AssertNoCharacterLayout(panelRoot);
            }
            finally
            {
                appSettingsScope.Restore();
                GameView.InvalidateCharacterEnabledCache();
            }
        }

        /// <summary>
        /// 立ち絵が実際に表示されている場合は、修飾クラスが付かず、右列（立ち絵）が行の右端にあって
        /// 中央列が左右の列の残りを埋めることを確認する（#193）。このテストの 16:9 では残り（約 799px）が
        /// 最大幅 880px に届かないので頭打ちにならない。最大幅は #213 で立ち絵の有無にかかわらず付き、21:9 で
        /// 頭打ちになることは <c>BustUpImage_UltraWide_FrameFillsColumnWidthAtBottom</c> などで確かめる。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator CharacterPlaced_ShowsRightColumn_AndMainFillsRemainingWidth()
        {
            AssertIsolatedDataRootOrIgnore();

            var imageScope = LegacyCharacterImageScope.Backup();
            GameView.ResetCharacterTextureCacheForTesting();
            try
            {
                var path = AppPaths.Combine(CharacterImagePaths.DirectoryName, CharacterImagePaths.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, TestImageFactory.CreatePng(TestImageWidth, TestImageHeight, seed: TestImageWidth));

                VisualElement panelRoot = null;
                yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

                yield return StartHostAndShowGame(panelRoot);

                var characterRoot = panelRoot.Q<VisualElement>("character-root");
                Assert.IsNotNull(characterRoot, "character-root が見つかりません。");

                yield return WaitUntil(
                    () => characterRoot.resolvedStyle.display == DisplayStyle.Flex,
                    10f,
                    () => "立ち絵画像を配置したので表示されるはずですが、非表示のままでした。");

                yield return WaitForLayoutSettled();

                var row = panelRoot.Q<VisualElement>("game-content-row");
                var main = panelRoot.Q<VisualElement>("game-content-main");
                Assert.IsNotNull(row);
                Assert.IsNotNull(main);

                Assert.IsFalse(
                    main.ClassListContains(GameView.NoCharacterMainClassName),
                    "立ち絵が表示されているときは game-content-main に修飾クラスが付かないはず。");

                var participant = panelRoot.Q<VisualElement>("participant-panel");
                var slot = panelRoot.Q<VisualElement>("character-view-instance");
                Assert.IsNotNull(participant, "participant-panel が見つかりません。");
                Assert.IsNotNull(slot, "character-view-instance が見つかりません。");
                Assert.AreEqual(DisplayStyle.Flex, slot.resolvedStyle.display, "立ち絵が表示されているときは右列を出すはず。");
                Assert.That(
                    participant.worldBound.xMin, Is.EqualTo(row.worldBound.xMin).Within(Tolerance), "参加者パネルは行の左端のはず。");
                Assert.That(
                    slot.worldBound.xMax, Is.EqualTo(row.worldBound.xMax).Within(Tolerance), "右列（立ち絵）は行の右端のはず。");
                Assert.That(main.worldBound.xMin, Is.GreaterThan(participant.worldBound.xMax), "中央列は参加者パネルの右のはず。");
                Assert.That(slot.worldBound.xMin, Is.GreaterThan(main.worldBound.xMax), "右列は中央列の右のはず。");
                Assert.That(
                    main.layout.width,
                    Is.EqualTo(row.layout.width - participant.layout.width - slot.layout.width - ColumnGaps).Within(Tolerance),
                    "立ち絵が表示されているときは、中央列が左右の列の残りを埋めるはず。");
            }
            finally
            {
                GameView.ResetCharacterTextureCacheForTesting();
                imageScope.Restore();
            }
        }

        /// <summary>
        /// issue #172 レビュー M1: 立ち絵の可視性が表示中に動的に変わったとき（同意撤回・再同意）、
        /// ポーリング（旧実装の Tick）ではなく <see cref="CharacterView.VisibilityChanged"/> イベント経由で
        /// 本体パネルのレイアウトが追従することを確認する。
        /// </summary>
        /// <remarks>
        /// レビュー: <c>SetConsentCheckForTesting</c> 自体は設定と同時に即時再評価するため、
        /// 一度だけ呼んで delegate を差し替え、以降は delegate が参照するローカル変数
        /// （<c>consented</c>）の値だけを変える。実際の再評価は本番の実トリガー
        /// （<see cref="GameSession.QuestionShown"/>、出題ごと）にだけ行わせることで、
        /// <c>CharacterConsentRevocationTests</c> と同じ「撤回した瞬間には消えない」仕様も
        /// 併せて確認する。撤回 → 出題 → 再同意 → 出題、の両方向を確認する。
        /// </remarks>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator CharacterVisibilityChanges_Dynamically_UpdatesLayoutViaVisibilityChangedEvent()
        {
            AssertIsolatedDataRootOrIgnore();

            var imageScope = LegacyCharacterImageScope.Backup();
            GameView.ResetCharacterTextureCacheForTesting();
            try
            {
                var path = AppPaths.Combine(CharacterImagePaths.DirectoryName, CharacterImagePaths.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, TestImageFactory.CreatePng(TestImageWidth, TestImageHeight, seed: TestImageWidth));

                VisualElement panelRoot = null;
                yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);

                var hostService = NetworkBootstrap.Instance.Service;
                NetworkStartResult startResult = default;
                yield return hostService.StartHostWhenReady(startPort: 0, onCompleted: r => startResult = r);
                Assert.IsTrue(startResult.Success, startResult.Message);

                var hostSession = hostService.ActiveGameSession;
                Assert.IsNotNull(hostSession, "ホスト開始で GameSession がスポーンされるはず。");
                hostSession.Configure(new TestQuestionSource(
                    TestQuestionSource.FreeText("q-char-visibility-1", "問題1", "こたえ1"),
                    TestQuestionSource.FreeText("q-char-visibility-2", "問題2", "こたえ2"),
                    TestQuestionSource.FreeText("q-char-visibility-3", "問題3", "こたえ3")));

                var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
                Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
                router.ShowView(ViewNames.Game);

                yield return WaitForElement<VisualElement>(panelRoot, "game-content-row", _ => { });

                var gameView = router.CurrentController as GameView;
                Assert.IsNotNull(gameView, "表示中のコントローラは GameView のはず。");

                var characterRoot = panelRoot.Q<VisualElement>("character-root");
                var main = panelRoot.Q<VisualElement>("game-content-main");
                var slot = panelRoot.Q<VisualElement>("character-view-instance");
                Assert.IsNotNull(characterRoot);
                Assert.IsNotNull(main);
                Assert.IsNotNull(slot);

                Assert.IsTrue(hostSession.StartQuestion(0), "1 問目を開始できるはず。");

                yield return WaitUntil(
                    () => characterRoot.resolvedStyle.display == DisplayStyle.Flex,
                    10f,
                    () => "立ち絵画像を配置したので表示されるはずですが、非表示のままでした。");
                yield return WaitForLayoutSettled();

                Assert.IsFalse(
                    main.ClassListContains(GameView.NoCharacterMainClassName), "表示中は修飾クラスが付かないはず。");
                Assert.AreEqual(DisplayStyle.Flex, slot.resolvedStyle.display, "表示中は外枠も Flex のはず。");

                // --- 同意チェックをテスト用に差し替える（この時点では consented=true のまま、値は変えない） ---
                // レビュー: SetConsentCheckForTesting 自体は設定と同時に即時再評価するため、ここで
                // consented=true → true への差し替えは可視性を変えない（CharacterConsentRevocationTests と
                // 同じ作法で、以降は delegate が参照する consented 変数の値だけを変えて、実際の再評価は
                // 本番の実トリガー（QuestionShown）にだけ行わせる）。
                var consented = true;
                gameView.SetCharacterConsentCheckForTesting(() => consented);
                yield return null;

                Assert.AreEqual(
                    DisplayStyle.Flex, characterRoot.resolvedStyle.display, "差し替え直後（consented=true）は表示のまま。");

                // --- 撤回（delegate の参照先だけ変える。ここではまだ再評価されない） ---
                consented = false;
                yield return null;

                Assert.AreEqual(
                    DisplayStyle.Flex, characterRoot.resolvedStyle.display,
                    "撤回した瞬間に消えるのではなく、次の出題で評価し直す仕様のはず"
                    + "（CharacterConsentRevocationTests と同じ、毎フレーム consent を読まない）。");

                // --- 出題ごとの再評価（本番の実トリガー、QuestionShown）で撤回が反映されることを確認 ---
                // 1 問目を決着させて Result へ進め、NextQuestion() で 2 問目（QuestionShown）へ進む
                // （司会専用ではない通常プレイヤーの「次へ」＝ GameView.OnNextButtonClicked と同じ経路）。
                yield return WaitUntil(
                    () => hostSession.Phase.Value == QuizPhase.BuzzOpen,
                    10f,
                    () => $"早押し受付が開きませんでした（現在: {hostSession.Phase.Value}）。");
                Assert.IsTrue(hostSession.RequestBuzz(), "早押しできるはず。");
                yield return WaitUntil(
                    () => hostSession.Phase.Value == QuizPhase.Answering,
                    10f,
                    () => $"回答フェーズに進みませんでした（現在: {hostSession.Phase.Value}）。");
                Assert.IsTrue(hostSession.RequestAnswer("こたえ1"), "回答できるはず。");
                yield return WaitUntil(
                    () => hostSession.Phase.Value == QuizPhase.Result,
                    10f,
                    () => $"結果フェーズに進みませんでした（現在: {hostSession.Phase.Value}）。");

                // NextQuestion() は StartSession() が設定する _totalQuestions に依存するため
                // （本テストは単問経路の StartQuestion を使っている）、他の GameViewSceneTests と同じく
                // 2 問目も StartQuestion で直接進める（Result フェーズなら受理される）。
                Assert.IsTrue(hostSession.StartQuestion(1), "2 問目を開始できるはず。");
                yield return WaitUntil(
                    () => hostSession.QuestionIndex.Value == 1,
                    10f,
                    () => "2 問目（QuestionShown）へ進みませんでした。");
                yield return WaitForLayoutSettled();

                Assert.AreEqual(
                    DisplayStyle.None, characterRoot.resolvedStyle.display, "2 問目でも撤回状態が続くはず。");
                Assert.IsTrue(main.ClassListContains(GameView.NoCharacterMainClassName));
                Assert.AreEqual(DisplayStyle.None, slot.resolvedStyle.display);

                // --- 再同意（逆方向）。ここも delegate の参照先だけ変え、実際の再評価は
                //     出題ごとの再評価（本番の実トリガー、QuestionShown、3 問目）に行わせる ---
                consented = true;
                yield return null;

                Assert.AreEqual(
                    DisplayStyle.None, characterRoot.resolvedStyle.display,
                    "再同意した瞬間に表示されるのではなく、次の出題で評価し直す仕様のはず。");

                yield return WaitUntil(
                    () => hostSession.Phase.Value == QuizPhase.BuzzOpen,
                    10f,
                    () => $"早押し受付が開きませんでした（現在: {hostSession.Phase.Value}）。");
                Assert.IsTrue(hostSession.RequestBuzz(), "早押しできるはず。");
                yield return WaitUntil(
                    () => hostSession.Phase.Value == QuizPhase.Answering,
                    10f,
                    () => $"回答フェーズに進みませんでした（現在: {hostSession.Phase.Value}）。");
                Assert.IsTrue(hostSession.RequestAnswer("こたえ2"), "回答できるはず。");
                yield return WaitUntil(
                    () => hostSession.Phase.Value == QuizPhase.Result,
                    10f,
                    () => $"結果フェーズに進みませんでした（現在: {hostSession.Phase.Value}）。");

                Assert.IsTrue(hostSession.StartQuestion(2), "3 問目を開始できるはず。");
                yield return WaitUntil(
                    () => hostSession.QuestionIndex.Value == 2,
                    10f,
                    () => "3 問目（QuestionShown）へ進みませんでした。");
                yield return WaitForLayoutSettled();

                Assert.AreEqual(
                    DisplayStyle.Flex, characterRoot.resolvedStyle.display, "再同意後は立ち絵が表示されるはず。");
                Assert.IsFalse(
                    main.ClassListContains(GameView.NoCharacterMainClassName), "再同意後は修飾クラスが外れるはず。");
                Assert.AreEqual(DisplayStyle.Flex, slot.resolvedStyle.display, "再同意後は外枠も Flex に戻るはず。");
            }
            finally
            {
                GameView.ResetCharacterTextureCacheForTesting();
                imageScope.Restore();
            }
        }

        /// <summary>16:9（1600x900）+ バストアップ: カードが右列の幅いっぱい・下端揃えで、作業領域に収まる（#191）。</summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator BustUpImage_16By9_FrameFillsColumnWidthAtBottom()
            => RunFrameScenario("16:9・バストアップ", PanelScaleProbe.Viewport16By9, BustUpImageWidth, BustUpImageHeight);

        /// <summary>21:9（2560x1080）+ バストアップ: 右列が最大幅 420px になる、カードがいちばん大きい条件（#191）。</summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator BustUpImage_UltraWide_FrameFillsColumnWidthAtBottom()
            => RunFrameScenario("21:9・バストアップ", PanelScaleProbe.ViewportUltraWide, BustUpImageWidth, BustUpImageHeight);

        /// <summary>900x750 + バストアップ: 右列が最も狭い条件（#191）。</summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator BustUpImage_MinimumWindow_FrameFillsColumnWidthAtBottom()
            => RunFrameScenario("900x750・バストアップ", PanelScaleProbe.ViewportMinimumWindow, BustUpImageWidth, BustUpImageHeight);

        /// <summary>
        /// 21:9 + 全身（約 1:2）: 右列の幅に合わせると作業領域より高くなるので、カードは右列の高さで頭打ちになり、
        /// 作業領域からはみ出さない（#191。表情差分が未生成で全身の tsumugi_v2.png にフォールバックした場合）。
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator FullBodyImage_UltraWide_FrameIsClampedToColumnHeight()
            => RunFrameScenario("21:9・全身", PanelScaleProbe.ViewportUltraWide, FullBodyImageWidth, FullBodyImageHeight);

        /// <summary>
        /// 立ち絵のカード（<c>character-image-frame</c>、#191）の寸法を実シーンで測る。ダミーの画像
        /// （素材ではなく同じ大きさのノイズ画像）を従来の全身 PNG のパスに置き、論理ビューポートを固定して、
        /// カードが右列の下端に揃い、幅は右列いっぱい（収まらない場合は高さが右列で頭打ち）、縦横比は画像と同じで、
        /// 右列・中央列が作業領域の高さのまま（立ち絵を出しても縦幅が伸びない）ことを確かめる。
        /// 中央列は左右の列の残りを埋めて最大 880px で頭打ちになり、3 列ごと中央に寄ること（21:9 で頭打ち、#213）も確かめる。
        /// </summary>
        private static IEnumerator RunFrameScenario(string label, Vector2 viewport, int imageWidth, int imageHeight)
        {
            AssertIsolatedDataRootOrIgnore();

            var imageScope = LegacyCharacterImageScope.Backup();
            GameView.ResetCharacterTextureCacheForTesting();
            try
            {
                var path = AppPaths.Combine(CharacterImagePaths.DirectoryName, CharacterImagePaths.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, TestImageFactory.CreatePng(imageWidth, imageHeight, seed: imageWidth));

                VisualElement panelRoot = null;
                yield return LoadBootThenMainSceneAndGetRoot(root => panelRoot = root);
                yield return StartHostAndShowGame(panelRoot, viewport);

                var characterRoot = panelRoot.Q<VisualElement>("character-root");
                Assert.IsNotNull(characterRoot, "character-root が見つかりません。");
                yield return WaitUntil(
                    () => characterRoot.resolvedStyle.display == DisplayStyle.Flex,
                    10f,
                    () => "立ち絵画像を配置したので表示されるはずですが、非表示のままでした。");
                yield return WaitForLayoutSettled();

                AssertFrameLayout(panelRoot, label, (float)imageWidth / imageHeight);
            }
            finally
            {
                GameView.ResetCharacterTextureCacheForTesting();
                imageScope.Restore();
            }
        }

        private static void AssertFrameLayout(VisualElement panelRoot, string label, float imageAspect)
        {
            var gameRoot = panelRoot.Q<VisualElement>("game-root");
            var row = panelRoot.Q<VisualElement>("game-content-row");
            var participant = panelRoot.Q<VisualElement>("participant-panel");
            var main = panelRoot.Q<VisualElement>("game-content-main");
            var slot = panelRoot.Q<VisualElement>("character-view-instance");
            var frame = panelRoot.Q<VisualElement>("character-image-frame");
            var image = panelRoot.Q<Image>("character-image");
            Assert.IsNotNull(gameRoot, "game-root が見つかりません。");
            Assert.IsNotNull(row, "game-content-row が見つかりません。");
            Assert.IsNotNull(participant, "participant-panel が見つかりません。");
            Assert.IsNotNull(main, "game-content-main が見つかりません。");
            Assert.IsNotNull(slot, "character-view-instance が見つかりません。");
            Assert.IsNotNull(frame, "character-image-frame が見つかりません。");
            Assert.IsNotNull(image, "character-image が見つかりません。");

            var (top, bottom) = GameViewLayoutProbe.WorkingArea(gameRoot);
            var workingHeight = bottom - top;
            var frameBound = frame.worldBound;
            var slotBound = slot.worldBound;
            var border = frame.resolvedStyle;
            var contentWidth = frame.layout.width - border.borderLeftWidth - border.borderRightWidth;
            var contentHeight = frame.layout.height - border.borderTopWidth - border.borderBottomWidth;
            Debug.Log(
                $"[{FrameLogTag}] {label}: 論理ビューポート {gameRoot.layout.width:F1}x{gameRoot.layout.height:F1}、"
                + $"作業領域の縦 {workingHeight:F1}px、右列 {slot.layout.width:F1}x{slot.layout.height:F1}、"
                + $"カード {frame.layout.width:F1}x{frame.layout.height:F1}（内側 {contentWidth:F1}x{contentHeight:F1}、"
                + $"縦横比 {frame.resolvedStyle.aspectRatio.value:F4}）、画像の要素 {image.layout.width:F1}x{image.layout.height:F1}、"
                + $"中央列 {main.layout.width:F1}x{main.layout.height:F1}、参加者パネル {participant.layout.width:F1}、"
                + $"行の左右の余白 {participant.worldBound.xMin - row.worldBound.xMin:F1} / {row.worldBound.xMax - slot.worldBound.xMax:F1}");

            // 中央列は左右の列の残りを埋め、最大幅で頭打ちになれば 3 列ごと中央に寄る（#213）。
            GameViewLayoutProbe.AssertMainColumnWithCharacter(row, participant, main, slot, label);

            // 立ち絵を出しても 3 列の縦幅は作業領域のまま（#187 / #193 の縦幅の前提を崩さない）。
            Assert.That(slot.layout.height, Is.EqualTo(workingHeight).Within(Tolerance), $"{label}: 右列は作業領域の高さのはず。");
            Assert.That(main.layout.height, Is.EqualTo(workingHeight).Within(Tolerance), $"{label}: 中央列は作業領域の高さのはず。");

            // カードは右列の中・作業領域の中にあり、下端が右列の下端に揃う。
            Assert.That(frameBound.yMax, Is.EqualTo(slotBound.yMax).Within(Tolerance), $"{label}: カードは右列の下端に揃うはず。");
            Assert.That(frameBound.yMax, Is.LessThanOrEqualTo(bottom + Tolerance), $"{label}: カードが作業領域の下にはみ出している。");
            Assert.That(frameBound.yMin, Is.GreaterThanOrEqualTo(top - Tolerance), $"{label}: カードが作業領域の上にはみ出している。");
            Assert.That(frameBound.xMin, Is.GreaterThanOrEqualTo(slotBound.xMin - Tolerance), $"{label}: カードが右列の左にはみ出している。");
            Assert.That(frameBound.xMax, Is.LessThanOrEqualTo(slotBound.xMax + Tolerance), $"{label}: カードが右列の右にはみ出している。");

            // カードの縦横比は画像に合わせる（CharacterView.ApplyFrameAspectRatio）。
            Assert.That(
                frame.resolvedStyle.aspectRatio.value, Is.EqualTo(imageAspect).Within(0.0001f), $"{label}: カードの縦横比は画像と同じはず。");

            var naturalHeight = slot.layout.width / imageAspect;
            if (naturalHeight <= slot.layout.height)
            {
                // 幅は右列いっぱい、高さは縦横比から。カードの内側（画像の要素）も画像とほぼ同じ縦横比
                // （aspect-ratio は枠線込みの外寸に掛かるので、枠線 3px / 下 7px の分だけずれる。右列が最も狭い
                // 900x750 で 0.782（画像 0.771、+1.4%。左右の余白は合計約 4px）を実測したので 2% 以内とする）。
                Assert.That(frame.layout.width, Is.EqualTo(slot.layout.width).Within(Tolerance), $"{label}: カードの幅は右列いっぱいのはず。");
                // 高さは幅から導かれる値なので、物理ピクセルへの丸め（バッチ実行のパネル倍率が 1 より小さいと
                // 1 物理 px が論理 2〜3px になる。900x750 の実測で −2.6px）が効く。1% と LayoutTolerance の大きいほうで比べる。
                Assert.That(
                    frame.layout.height, Is.EqualTo(naturalHeight).Within(Math.Max(Tolerance, naturalHeight * 0.01f)),
                    $"{label}: カードの高さは幅 / 縦横比のはず。");
                Assert.That(
                    contentWidth / contentHeight, Is.EqualTo(imageAspect).Within(imageAspect * 0.02f),
                    $"{label}: カードの内側の縦横比が画像と大きくずれている。");
            }
            else
            {
                // 右列に収まらない縦長の画像: 高さを右列（作業領域）で頭打ちにする。
                Assert.That(frame.layout.height, Is.EqualTo(slot.layout.height).Within(Tolerance), $"{label}: カードの高さは右列で頭打ちのはず。");

                // 幅は頭打ちの高さから縦横比で決め直される（Yoga の aspect-ratio + max-height。実測: 21:9 の全身で
                // 353.6x707.1）ので、カードは画像に沿い、右列の中央に寄る。
                var clampedWidth = frame.layout.height * imageAspect;
                Assert.That(
                    frame.layout.width, Is.EqualTo(clampedWidth).Within(Math.Max(Tolerance, clampedWidth * 0.01f)),
                    $"{label}: 頭打ちのときの幅は高さ × 縦横比のはず。");
                Assert.That(
                    frameBound.center.x, Is.EqualTo(slotBound.center.x).Within(Tolerance), $"{label}: 頭打ちのときは右列の中央に寄るはず。");
            }

            Assert.That(image.layout.width, Is.EqualTo(contentWidth).Within(Tolerance), $"{label}: 画像の要素はカードの内側いっぱいのはず。");
            Assert.That(image.layout.height, Is.EqualTo(contentHeight).Within(Tolerance), $"{label}: 画像の要素はカードの内側いっぱいのはず。");
        }

        /// <summary>ホストとして開始し、Game View を表示する（司会専用ではない通常プレイヤー経路）。</summary>
        private static IEnumerator StartHostAndShowGame(VisualElement panelRoot)
            => StartHostAndShowGame(panelRoot, PanelScaleProbe.Viewport16By9);

        /// <summary>ホストとして開始し、Game View を表示して、<c>game-root</c> を指定の論理ビューポートに固定する。</summary>
        private static IEnumerator StartHostAndShowGame(VisualElement panelRoot, Vector2 viewport)
        {
            var hostService = NetworkBootstrap.Instance.Service;
            NetworkStartResult startResult = default;
            yield return hostService.StartHostWhenReady(startPort: 0, onCompleted: r => startResult = r);
            Assert.IsTrue(startResult.Success, startResult.Message);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Game);

            yield return WaitForElement<VisualElement>(panelRoot, "game-content-row", _ => { });

            // バッチ実行の画面サイズは固定なので、論理ビューポートに固定する（行の幅を決めるため。既定は 1600x900 基準）。
            GameViewLayoutProbe.SetLogicalViewport(panelRoot.Q<VisualElement>("game-root"), viewport);
        }

        /// <summary>
        /// 立ち絵が非表示のときの 3 列レイアウト（#172 / #193）: 右列を隠し、中央列は最大幅（880px）で頭打ちにし、
        /// 参加者パネル + 中央列を行の中央に寄せる（左右の余白が等しい）。
        /// </summary>
        private static void AssertNoCharacterLayout(VisualElement panelRoot)
        {
            var row = panelRoot.Q<VisualElement>("game-content-row");
            var participant = panelRoot.Q<VisualElement>("participant-panel");
            var main = panelRoot.Q<VisualElement>("game-content-main");
            Assert.IsNotNull(participant, "participant-panel が見つかりません。");

            // 前提: 行が「参加者パネル + 間隔 + 中央列の最大幅」より十分広いこと（狭いと中央寄せの検証にならない）。
            Assert.That(
                row.layout.width,
                Is.GreaterThan(participant.layout.width + ColumnGap + GameViewLayoutProbe.MainMaxWidth + 100f),
                $"本テストの前提（行が十分広い）が崩れています。実際: {row.layout.width}px。");

            Assert.That(
                main.layout.width,
                Is.EqualTo(GameViewLayoutProbe.MainMaxWidth).Within(Tolerance),
                "立ち絵が非表示のときは中央列を最大幅で頭打ちにするはず（行長を抑える）。");

            var leftSpace = participant.worldBound.xMin - row.worldBound.xMin;
            var rightSpace = row.worldBound.xMax - main.worldBound.xMax;
            Assert.That(leftSpace, Is.GreaterThan(50f), $"参加者パネル + 中央列が中央に寄り、左にも余白ができるはず（実際: {leftSpace}px）。");
            GameViewLayoutProbe.AssertCenteredInRow(
                row, leftSpace, rightSpace, expectSpace: true,
                $"立ち絵が非表示のときの参加者パネル + 中央列（row={row.worldBound}, participant={participant.worldBound}, main={main.worldBound}）");
        }

        /// <summary>レイアウト解決を待つ（#113 レビュー A-2 と同じ理由。1 フレームでは解決し切らないことがある）。</summary>
        private static IEnumerator WaitForLayoutSettled()
        {
            yield return null;
            yield return null;
            yield return null;
        }

        /// <summary>
        /// データルートが隔離されていない（<c>scripts/verify.ps1</c> を介さず、実ユーザーの環境で
        /// 直接実行している）場合はテストをスキップする（<c>CharacterExpressionSwitchTests</c> と同じ方針。
        /// 実ユーザーの立ち絵ファイルを書き換えないため）。
        /// </summary>
        private static void AssertIsolatedDataRootOrIgnore()
        {
            var environmentRoot = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(environmentRoot))
            {
                Assert.Ignore(
                    $"環境変数 {AppPaths.DataRootEnvironmentVariable} が未設定です。"
                    + "実ユーザーのデータルートにある立ち絵を書き換えないため、このテストはスキップします"
                    + "（scripts/verify.ps1 経由で実行してください）。");
            }

            if (!RootPathValidator.IsSameOrUnder(AppPaths.DataRoot, Path.GetFullPath(environmentRoot)))
            {
                Assert.Ignore(
                    $"AppPaths.DataRoot（{AppPaths.DataRoot}）が環境変数 "
                    + $"{AppPaths.DataRootEnvironmentVariable}（{environmentRoot}）由来ではありません。"
                    + "隔離されたデータルートでないため、このテストはスキップします。");
            }
        }

        /// <summary>
        /// <c>tsumugi_v2.png</c>（従来の全身 PNG、レガシー1枚のみ）を退避・復元するスコープ。
        /// <see cref="TsumugiQuiz.Tests.PlayMode.UI.CharacterImageFileScope"/>（#212 で
        /// <c>CharacterImageTestFiles.cs</c> へ切り出した、全表情の退避・復元）の縮小版
        /// （本テストは表情差分ではなく従来の全身 PNG 1 枚だけで足りる）。
        /// </summary>
        private sealed class LegacyCharacterImageScope
        {
            private const string BackupSuffix = ".testbak";

            private readonly string _path;
            private readonly bool _hadExistingFile;

            private LegacyCharacterImageScope(string path, bool hadExistingFile)
            {
                _path = path;
                _hadExistingFile = hadExistingFile;
            }

            public static LegacyCharacterImageScope Backup()
            {
                var path = AppPaths.Combine(CharacterImagePaths.DirectoryName, CharacterImagePaths.FileName);
                var hadExistingFile = File.Exists(path);
                if (hadExistingFile)
                {
                    var backupPath = path + BackupSuffix;
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    File.Move(path, backupPath);
                }

                return new LegacyCharacterImageScope(path, hadExistingFile);
            }

            public void Restore()
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                if (_hadExistingFile)
                {
                    var backupPath = _path + BackupSuffix;
                    if (File.Exists(backupPath))
                    {
                        File.Move(backupPath, _path);
                    }
                }
            }
        }
    }
}
