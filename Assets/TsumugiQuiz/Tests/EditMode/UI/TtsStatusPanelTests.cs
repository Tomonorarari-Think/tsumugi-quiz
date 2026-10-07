using System;
using System.IO;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Tests.EditMode.Core;
using TsumugiQuiz.Tests.Shared.Core;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// <see cref="TtsStatusPanel"/>（#25、欠落時のフォールバック UI）の検証。
    /// 理由の優先順位（同意 → 設定 → <see cref="TtsService.Status"/>）と、
    /// 生成される <see cref="VisualElement"/>（<c>Resources/UI/tts-status-panel.uxml</c> から組み立て）の
    /// 文言・ボタン表示の切り替え、<see cref="TtsService.StatusChanged"/> の購読ライフサイクル（M-4）を確かめる。
    ///
    /// #97: 「再試行」ボタンの非同期継続をポンプするため、<see cref="SetUp"/> で Unity の
    /// <see cref="SynchronizationContext"/> を <see cref="QueuingSynchronizationContext"/>（#97 L-5、
    /// <c>Tests/Shared</c> 側の共通実装。<c>TtsServiceFakeEngineTests</c> と共通）に差し替える。
    /// </summary>
    public sealed class TtsStatusPanelTests
    {
        private const int WaitMs = 20000;
        private const string PanelSettingsPath = "Assets/TsumugiQuiz/Settings/panel-settings.asset";

        private FakeConsentStorage _consentStorage;
        private readonly System.Collections.Generic.List<GameObject> _gameObjects = new();
        private readonly System.Collections.Generic.List<string> _cacheRoots = new();
        private SynchronizationContext _originalContext;
        private QueuingSynchronizationContext _testContext;
        private TtsSynthesisEngineFactory _previousEngineFactoryOverride;

        [SetUp]
        public void SetUp()
        {
            // #97: クラス冒頭のコメント、および QueuingSynchronizationContext 自体のコメントを参照。
            _originalContext = SynchronizationContext.Current;
            _testContext = new QueuingSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_testContext);

            _consentStorage = new FakeConsentStorage();
            ConsentGate.SetStorageFactoryForTesting(() => _consentStorage);

            // #156: 「再試行」ボタン（TtsStatusPanel.OnRetryClicked）は engineFactory を渡さずに
            // TtsService.RetryInitializeAsync を呼ぶ（本番の意図どおり voicevox_core を使う）。
            // CreateService で作った TtsService はフェイクエンジンで初期化しているが、再試行はその
            // フェイクを引き継がないため、External/ の実 DLL が配置された環境では「再試行」のテストが
            // 本物の voicevox_core を読みに行ってしまう（docs/tts.md §11.1 違反）。ここで既定値を
            // フェイクへ差し替え、engineFactory 省略時も本番実装へフォールバックしないようにする。
            // #156 レビュー L-1: TearDown では（決め打ちの値ではなく）ここで退避した値へ戻す。
            _previousEngineFactoryOverride = TtsService.DefaultEngineFactoryOverrideForTesting;
            TtsService.DefaultEngineFactoryOverrideForTesting = (_, __) => new FakeTtsSynthesisEngine();
        }

        [TearDown]
        public void TearDown()
        {
            // #97: 前のテストの非同期継続が TearDown 後まで積み残らないよう、最後にもう一度 drain する
            // （通常は各テストの SpinWaitUntil で drain 済みだが、失敗時の保険として）。
            _testContext?.Drain();

            ConsentGate.SetStorageFactoryForTesting(null);
            // #156 レビュー L-1: SetUp で退避した値（通常は EditMode アセンブリ全体のガード
            // TtsEngineFactoryGuardSetUp.FailFactory）へ戻す。null に戻すと、このガードが
            // 以後実行される他のテストで効かなくなってしまう。
            TtsService.DefaultEngineFactoryOverrideForTesting = _previousEngineFactoryOverride;
            SynchronizationContext.SetSynchronizationContext(_originalContext);
            LogAssert.ignoreFailingMessages = false;

            foreach (var go in _gameObjects)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _gameObjects.Clear();

            foreach (var root in _cacheRoots)
            {
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
            _cacheRoots.Clear();
        }

        private void SetConsented(bool consented)
        {
            if (!consented) return;

            var store = new ConsentStore(_consentStorage);
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), "0.1.0", DateTime.UtcNow);
        }

        private TtsService CreateService(TtsSynthesisEngineFactory factory)
        {
            LogAssert.ignoreFailingMessages = true;

            var cacheRoot = Path.Combine(Path.GetTempPath(), "TsumugiQuizTtsStatusPanelTests", Guid.NewGuid().ToString("N"));
            _cacheRoots.Add(cacheRoot);

            var go = new GameObject(nameof(TtsStatusPanelTests));
            go.SetActive(false);
            _gameObjects.Add(go);

            var service = go.AddComponent<TtsService>();
            service.Initialize(new FixedTtsSettingsProvider(TtsSettings.Default), cacheRoot, null, factory);

            // #97 M-4: QueuingSynchronizationContext はポンプしないと継続が進まないため、
            // Task.Wait ではなく SpinWaitUntil（drain 付き）で待つ。
            var initTask = service.EnsureInitializedAsync();
            Assert.That(SpinWaitUntil(() => initTask.IsCompleted, WaitMs), Is.True, "初期化待ちが完了すること");
            Assert.That(initTask.IsFaulted, Is.False, $"初期化タスクが例外で終わらないこと: {initTask.Exception}");
            return service;
        }

        /// <summary>
        /// ボタンのクリック（<see cref="NavigationSubmitEvent"/>）や AttachToPanelEvent を実際に
        /// ディスパッチさせるにはパネルに attach されている必要があるため、テスト用の UIDocument に載せる
        /// （<c>MainSceneTestHelpers.SimulateClick</c> と同じ仕組み。EditMode でも UIDocument は動く）。
        /// </summary>
        private VisualElement AttachToPanel(VisualElement element)
        {
            var go = new GameObject(nameof(TtsStatusPanelTests) + "_Panel");
            _gameObjects.Add(go);

            var document = go.AddComponent<UIDocument>();
            var panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.That(panelSettings, Is.Not.Null, $"'{PanelSettingsPath}' の PanelSettings が見つかりません。");
            document.panelSettings = panelSettings;

            Assert.That(document.rootVisualElement, Is.Not.Null, "UIDocument.rootVisualElement が null です。");
            document.rootVisualElement.Add(element);
            return element;
        }

        private TtsService CreateReadyService() => CreateService((location, settings) => new FakeTtsSynthesisEngine());

        private TtsService CreateNotAvailableService(TtsUnavailableReason reason)
            => CreateService((location, settings) => throw new TtsSetupException($"テスト用の失敗（{reason}）。", reason));

        /// <summary>
        /// <see cref="TtsService.StatusChanged"/>（field-like event）の購読者数を、
        /// バッキングフィールドをリフレクションで覗いて数える（M-4 の検証専用）。
        /// </summary>
        private static int CountStatusChangedSubscribers(TtsService service)
        {
            var field = typeof(TtsService).GetField("StatusChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "StatusChanged のバッキングフィールドが見つかりません。");
            var del = field.GetValue(service) as Delegate;
            return del?.GetInvocationList().Length ?? 0;
        }

        [Test]
        public void 未同意ならサービスの状態に関わらずConsentNotGiven()
        {
            SetConsented(false);

            Assert.That(TtsStatusPanel.ResolveReason(null), Is.EqualTo(TtsUnavailableReason.ConsentNotGiven));
            Assert.That(TtsStatusPanel.DescribeCornerStatus(null), Is.EqualTo("読み上げ: 利用不可"));
        }

        [Test]
        public void 同意済みでサービスがnullなら未確認扱い()
        {
            SetConsented(true);

            // #25 H-5: Title では初期化しないため、サービス未接続はエラーではなく「未確認」。
            Assert.That(TtsStatusPanel.ResolveReason(null), Is.Null);
            Assert.That(TtsStatusPanel.DescribeCornerStatus(null), Is.EqualTo("読み上げ: 未確認"));
        }

        [Test]
        public void 同意済みでReadyなら理由なしコーナーはReady表示()
        {
            SetConsented(true);
            var service = CreateReadyService();

            Assert.That(TtsStatusPanel.ResolveReason(service), Is.Null);
            Assert.That(TtsStatusPanel.DescribeCornerStatus(service), Is.EqualTo("読み上げ: Ready"));
        }

        [Test]
        public void 読み上げ無効ならUserSuppressedになる()
        {
            SetConsented(true);
            var service = CreateReadyService();
            service.ReadingEnabled = false;

            Assert.That(TtsStatusPanel.ResolveReason(service), Is.EqualTo(TtsUnavailableReason.UserSuppressed));
            Assert.That(TtsStatusPanel.DescribeCornerStatus(service), Is.EqualTo("読み上げ: 利用不可"));
        }

        [Test]
        public void NotAvailableなら理由がそのまま反映される()
        {
            SetConsented(true);
            var service = CreateNotAvailableService(TtsUnavailableReason.MissingDictionary);

            Assert.That(TtsStatusPanel.ResolveReason(service), Is.EqualTo(TtsUnavailableReason.MissingDictionary));
            Assert.That(TtsStatusPanel.DescribeCornerStatus(service), Is.EqualTo("読み上げ: 利用不可"));
        }

        [Test]
        public void Createした要素にMissingCoreDllの文言が表示される()
        {
            SetConsented(true);
            var service = CreateNotAvailableService(TtsUnavailableReason.MissingCoreDll);

            var overlay = TtsStatusPanel.Create(service);
            var message = TtsStatusMessages.For(TtsUnavailableReason.MissingCoreDll);

            var headline = overlay.Q<Label>("tts-status-headline");
            var guidance = overlay.Q<Label>("tts-status-guidance");

            Assert.That(PhraseWrappedText.GetSourceText(headline), Is.EqualTo(message.Headline));
            Assert.That(PhraseWrappedText.GetSourceText(guidance), Is.EqualTo(message.Guidance));

            Assert.That(overlay.Q<Button>("tts-retry-button").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(overlay.Q<Button>("tts-setup-guide-button").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(overlay.Q<Button>("tts-terms-button").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        /// <summary>#25 H-1: UserSuppressed（設定で無効）でも「配置手順」「再試行」は残る。</summary>
        [Test]
        public void UserSuppressedでも配置手順と再試行ボタンは残る()
        {
            SetConsented(true);
            var service = CreateReadyService();
            service.ReadingEnabled = false;

            var overlay = TtsStatusPanel.Create(service);

            Assert.That(overlay.Q<Button>("tts-retry-button").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(overlay.Q<Button>("tts-setup-guide-button").style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void ConsentNotGivenのときは利用規約ボタンだけ表示される()
        {
            SetConsented(false);

            var overlay = TtsStatusPanel.Create(null, router: null);
            var headline = overlay.Q<Label>("tts-status-headline");
            var message = TtsStatusMessages.For(TtsUnavailableReason.ConsentNotGiven);

            Assert.That(PhraseWrappedText.GetSourceText(headline), Is.EqualTo(message.Headline));

            Assert.That(overlay.Q<Button>("tts-retry-button").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(overlay.Q<Button>("tts-setup-guide-button").style.display.value, Is.EqualTo(DisplayStyle.None));

            // router が null のときは利用規約ボタンも出さない（遷移できないため）。
            Assert.That(overlay.Q<Button>("tts-terms-button").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        /// <summary>#25 H-1: 「読み上げなしで続行」は ReadingEnabled を変更せず閉じるだけ。</summary>
        [Test]
        public void 続行ボタンはReadingEnabledを変更せず閉じるだけ()
        {
            SetConsented(true);
            var service = CreateNotAvailableService(TtsUnavailableReason.MissingModel);
            var readingEnabledBefore = service.ReadingEnabled;

            var closed = false;
            var overlay = TtsStatusPanel.Create(service, onClosed: () => closed = true);
            AttachToPanel(overlay);
            var continueButton = overlay.Q<Button>("tts-continue-button");

            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = continueButton;
                continueButton.SendEvent(evt);
            }

            Assert.That(service.ReadingEnabled, Is.EqualTo(readingEnabledBefore), "ReadingEnabled は変更されないこと");
            Assert.That(closed, Is.True);
        }

        /// <summary>#25 H-2: 「配置手順を表示」でアプリ内蔵テキストが展開される（外部 URL は使わない）。</summary>
        [Test]
        public void 配置手順ボタンでアプリ内蔵テキストが展開される()
        {
            SetConsented(true);
            var service = CreateNotAvailableService(TtsUnavailableReason.MissingCoreDll);

            var overlay = TtsStatusPanel.Create(service);
            AttachToPanel(overlay);

            var guideButton = overlay.Q<Button>("tts-setup-guide-button");
            var scroll = overlay.Q<ScrollView>("tts-setup-instructions-scroll");
            var text = overlay.Q<Label>("tts-setup-instructions-text");

            Assert.That(scroll.style.display.value, Is.EqualTo(DisplayStyle.None), "初期状態では折りたたまれていること");

            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = guideButton;
                guideButton.SendEvent(evt);
            }

            Assert.That(scroll.style.display.value, Is.EqualTo(DisplayStyle.Flex), "クリックで展開されること");
            Assert.That(text.text, Is.Not.Null.And.Not.Empty, "Resources/Docs/tts-setup.txt の内容が読み込まれること");
            Assert.That(text.text, Does.Contain("配置"), "配置手順の内容が入っていること");
        }

        /// <summary>#25 M-4: StatusChanged の購読は AttachToPanelEvent で開始し、DetachFromPanelEvent で解除する。</summary>
        [Test]
        public void 購読はAttachとDetachで開始終了する()
        {
            SetConsented(true);
            var service = CreateReadyService();

            var overlay = TtsStatusPanel.Create(service);
            Assert.That(CountStatusChangedSubscribers(service), Is.EqualTo(0), "未アタッチの間は購読していないこと");

            AttachToPanel(overlay);
            Assert.That(CountStatusChangedSubscribers(service), Is.EqualTo(1), "アタッチしたら購読すること");

            overlay.RemoveFromHierarchy();
            Assert.That(CountStatusChangedSubscribers(service), Is.EqualTo(0), "デタッチしたら購読解除すること");
        }

        /// <summary>#25 C-1: 再試行ボタンは実行中だけ無効化される。</summary>
        [Test]
        public void 再試行ボタンは実行完了後に再度有効になる()
        {
            SetConsented(true);
            var service = CreateNotAvailableService(TtsUnavailableReason.MissingModel);

            var overlay = TtsStatusPanel.Create(service);
            AttachToPanel(overlay);
            var retryButton = overlay.Q<Button>("tts-retry-button");
            var headline = overlay.Q<Label>("tts-status-headline");
            Assert.That(retryButton.enabledSelf, Is.True);

            using (var evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = retryButton;
                retryButton.SendEvent(evt);
            }

            // OnRetryClicked は async void。RetryInitializeAsync の完了を待つ（フェイクエンジンは同期的にすぐ終わる）。
            // #97 M-1: 「クリック直後は "確認しています…" のはず」というアサートは、await が
            // （フェイクエンジンの完了が早い場合等に）同期的に完了すると、この行に来る前に
            // finally の Refresh() までインラインで実行済みになりうる、新たなタイミング依存だったため削除した。
            // 待機条件は「finally（ボタン再有効化 + Refresh）が実際に完了したこと」まで含める。
            // Refresh() が呼ばれると headline.text はサービスの状態に応じた文言に変わり、
            // 「確認しています…」のままにはならない。ボタンの enabledSelf だけを見ると
            // （SetEnabled と Refresh は同じ Post 済みアクション内で順に実行されるため今回は起きないが）
            // 意図が伝わりにくいため、両方を固定する。
            Assert.That(SpinWaitUntil(() => retryButton.enabledSelf && PhraseWrappedText.GetSourceText(headline) != "確認しています…", WaitMs),
                Is.True, "再試行完了後にボタンが再び有効になり、Refresh() まで完了していること");
        }

        /// <summary>
        /// <paramref name="condition"/> が true になるまで、<see cref="_testContext"/> に貯まった
        /// 継続を吐き出しながら待つ（#97）。この drain がポンプの役割を果たすため、
        /// 「再試行」の非同期継続（<see cref="_testContext"/> へ Post される）が実行される。
        /// </summary>
        private bool SpinWaitUntil(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                _testContext.Drain();
                if (condition()) return true;
                System.Threading.Thread.Sleep(10);
            }

            _testContext.Drain();
            return condition();
        }
    }
}
