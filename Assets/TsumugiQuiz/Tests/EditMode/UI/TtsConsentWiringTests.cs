using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Tests.EditMode.Core;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using TsumugiQuiz.UI.Views.Settings;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// 利用規約の同意確認（<see cref="ConsentGate.HasUserConsented"/>）を読み上げへ配線する仕組みの検証
    /// （issue #127、requirements.md FR-74 / FR-75 / NFR-08）。
    ///
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="TtsConsentCheckFactory"/> が「判定結果」ではなく「判定関数」を返すこと
    ///     （撤回が後から効くための前提）
    ///   </description></item>
    ///   <item><description>
    ///     <c>GameView.WireTtsSyncPlayer</c>（<c>GameView.Tts.cs</c>）が
    ///     <see cref="TtsSyncPlayer"/> にその判定関数を差し込むこと。差し込まれていないと
    ///     <see cref="TtsSyncPlayer.IsReadingPossible"/> は撤回後も true のままになる（#127 の症状）
    ///   </description></item>
    /// </list>
    ///
    /// ゲーム進行（出題 → 合成）まで通した検証は PlayMode の
    /// <c>TsumugiQuiz.Tests.PlayMode.Tts.TtsConsentRevocationTests</c> が行う。
    /// </summary>
    public sealed class TtsConsentWiringTests
    {
        private const int WaitMs = 20000;

        private FakeConsentStorage _consentStorage;
        private readonly List<GameObject> _gameObjects = new();
        private readonly List<string> _cacheRoots = new();

        [SetUp]
        public void SetUp()
        {
            _consentStorage = new FakeConsentStorage();
            ConsentGate.SetStorageFactoryForTesting(() => _consentStorage);
        }

        [TearDown]
        public void TearDown()
        {
            ConsentGate.SetStorageFactoryForTesting(null);

            foreach (var go in _gameObjects)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _gameObjects.Clear();

            foreach (var root in _cacheRoots)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // 一時ディレクトリなので、消せなくてもテスト結果には影響しない。
                }
            }

            _cacheRoots.Clear();
        }

        [Test]
        public void ファクトリは常に判定関数を返す()
        {
            var consentCheck = TtsConsentCheckFactory.Build();

            Assert.That(
                consentCheck, Is.Not.Null,
                "null を返すと受け取り側（TtsService / TtsSyncPlayer）は「制限しない」と解釈してしまう。");
        }

        [Test]
        public void ファクトリの判定関数は呼ぶたびに最新の同意状態を返す()
        {
            RecordConsent();
            var consentCheck = TtsConsentCheckFactory.Build();
            Assert.That(consentCheck(), Is.True, "同意済みなら true。");

            RevokeConsent();

            Assert.That(
                consentCheck(), Is.False,
                "同じインスタンスでも撤回後は false になること（＝判定結果ではなく判定関数を渡している）。");

            RecordConsent();
            Assert.That(consentCheck(), Is.True, "同意し直せば true に戻ること（FR-75）。");
        }

        /// <summary>
        /// #127 の本体。<c>GameView</c> が配線する前後で、撤回が
        /// <see cref="TtsSyncPlayer.IsReadingPossible"/>（出題ごとに評価される）に効くかどうかが変わる。
        /// </summary>
        [Test]
        public void GameViewの配線で撤回が読み上げ可否に反映される()
        {
            RecordConsent();

            var service = CreateReadyService();
            var player = CreatePlayer();
            player.SetService(service);

            Assert.That(player.IsReadingPossible, Is.True, "同意済み・Ready なら読み上げられる。");

            // --- 配線前（#127 の症状の再現）---
            // ここで true を期待するのは「この状態が正しい」からではなく、
            // 配線が無ければ撤回が効かないこと（＝このテストが配線の有無を実際に見分けられること）を
            // 記録するため。直後の配線後アサートが本命で、そちらが #127 の受け入れ条件にあたる。
            // なお TtsService 側のゲート（レビュー H-1 のフォールバック）はこのサービスに
            // consentCheck を渡していないので働かない。フォールバックの検証は
            // サービス側ゲートに従うで行う。
            RevokeConsent();
            Assert.That(
                player.IsReadingPossible, Is.True,
                "SetConsentCheck も TtsService 側の登録も無ければ既定（制限しない）のまま。");

            // --- 配線後: 撤回が次の出題から効く ---
            GameView.WireTtsSyncPlayer(player);
            Assert.That(
                player.IsReadingPossible, Is.False,
                "GameView.WireTtsSyncPlayer が ConsentGate.HasUserConsented を差し込むこと（FR-74 / FR-75）。");

            RecordConsent();
            Assert.That(
                player.IsReadingPossible, Is.True,
                "同意し直せば読み上げに戻る（撤回時にルーム設定 tts.enabled を潰していないこと）。");
        }

        /// <summary>
        /// レビュー H-1: ロビーでの初期化（<c>TtsSyncCoordinator.OnNetworkSpawn</c> →
        /// <c>TtsSyncPlayer.InitializeAsync</c>）は <c>GameView</c> の配線より前に走る。
        /// そのとき <see cref="TtsSyncPlayer"/> に明示的な同意確認が無くても、UI 層がアプリ起動時に
        /// <see cref="TtsService.ConfigureDefaults"/> で登録した判定に従うこと。
        /// </summary>
        [Test]
        public void 配線前でもサービス側に登録された同意ゲートに従う()
        {
            RecordConsent();

            var service = CreateReadyService();
            // アプリ起動時の登録（DefaultViewControllerRegistrations.ConfigureTtsConsentGate 相当）。
            service.ConfigureDefaults(consentCheck: TtsConsentCheckFactory.Build());

            var player = CreatePlayer();
            player.SetService(service);

            Assert.That(player.IsReadingPossible, Is.True, "同意済みなら読み上げられる。");

            RevokeConsent();

            Assert.That(
                player.IsReadingPossible, Is.False,
                "GameView の配線が無くても、TtsService に登録された同意確認で止まること（H-1）。");
        }

        /// <summary>
        /// レビュー H-1 / M-5: 初期化を始めるのが「同意を知らない呼び出し元」でも、
        /// 後から渡した非 null の同意確認が効くこと（<c>_initialized</c> で固定されない）。
        /// </summary>
        [Test]
        public void 初期化済みでも後から渡した同意確認が効く()
        {
            RecordConsent();

            // 同意を知らない呼び出し元が先に初期化した状態（ロビーの TtsSyncPlayer 相当）。
            var service = CreateReadyService();
            Assert.That(service.IsConsentSatisfied, Is.True, "consentCheck 未設定なら制限しない。");

            RevokeConsent();
            Assert.That(service.IsConsentSatisfied, Is.True, "まだ同意確認を渡していないので制限されない。");

            service.Initialize(consentCheck: TtsConsentCheckFactory.Build());

            Assert.That(
                service.IsConsentSatisfied, Is.False,
                "2 回目以降の Initialize でも非 null の consentCheck は取り込むこと（M-5）。");
        }

        /// <summary>
        /// レビュー H-1: <see cref="TtsService.ConfigureDefaults"/> は登録するだけで、
        /// voicevox_core（ONNX Runtime）のロードを始めないこと（docs/tts.md §6.5、#25 H-5）。
        /// </summary>
        [Test]
        public void ConfigureDefaultsは初期化を始めない()
        {
            RecordConsent();

            var go = new GameObject(nameof(TtsConsentWiringTests) + "-NotInitialized");
            go.SetActive(false);
            _gameObjects.Add(go);
            var service = go.AddComponent<TtsService>();

            service.ConfigureDefaults(
                new FixedTtsSettingsProvider(TtsSettings.Default), TtsConsentCheckFactory.Build());

            Assert.That(
                service.Status.State, Is.EqualTo(TtsServiceState.NotInitialized),
                "登録だけでは初期化を始めないこと（起動時に呼ぶため）。");
            Assert.That(service.IsConsentSatisfied, Is.True, "同意ゲートは登録済みで、いまは同意済み。");

            RevokeConsent();
            Assert.That(service.IsConsentSatisfied, Is.False, "撤回すると初期化前でもゲートが閉じる。");
        }

        [Test]
        public void 配線済みプレイヤーはnullを渡しても落ちない()
        {
            // プレハブに TtsSyncPlayer が載っていない構成（TTS 無効ビルド等）でも例外にしない。
            Assert.DoesNotThrow(() => GameView.WireTtsSyncPlayer(null));
        }

        private void RecordConsent()
        {
            var store = new ConsentStore(_consentStorage);
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), "0.1.0", DateTime.UtcNow);
        }

        private void RevokeConsent()
        {
            var store = new ConsentStore(_consentStorage);
            store.Revoke();
        }

        private TtsSyncPlayer CreatePlayer()
        {
            var go = new GameObject(nameof(TtsConsentWiringTests));
            go.SetActive(false);
            _gameObjects.Add(go);
            return go.AddComponent<TtsSyncPlayer>();
        }

        /// <summary>
        /// フェイクエンジンで <see cref="TtsServiceState.Ready"/> まで進めた <see cref="TtsService"/>。
        /// <see cref="TtsStatusPanelTests"/> と同じく、非アクティブな GameObject に載せて
        /// <c>Awake</c>（<c>Instance</c> / <c>DontDestroyOnLoad</c>）を走らせない。
        /// </summary>
        private TtsService CreateReadyService()
        {
            var cacheRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsConsentWiringTests", Guid.NewGuid().ToString("N"));
            _cacheRoots.Add(cacheRoot);

            var go = new GameObject(nameof(TtsConsentWiringTests) + "-Service");
            go.SetActive(false);
            _gameObjects.Add(go);

            var service = go.AddComponent<TtsService>();
            service.Initialize(
                new FixedTtsSettingsProvider(TtsSettings.Default),
                cacheRoot,
                consentCheck: null,
                engineFactory: (location, settings) => new FakeTtsSynthesisEngine());

            // 初期化はワーカースレッド（Task.Run）で完結するため、継続のポンプは不要。
            var initTask = service.EnsureInitializedAsync();
            Assert.That(SpinWaitUntil(() => initTask.IsCompleted), Is.True, "初期化待ちが完了すること。");
            Assert.That(initTask.IsFaulted, Is.False, $"初期化が例外で終わらないこと: {initTask.Exception}");
            Assert.That(service.Status.IsReady, Is.True, $"フェイクエンジンなら Ready になること（{service.Status}）。");
            return service;
        }

        private static bool SpinWaitUntil(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < WaitMs)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(1);
            }

            return condition();
        }
    }
}
