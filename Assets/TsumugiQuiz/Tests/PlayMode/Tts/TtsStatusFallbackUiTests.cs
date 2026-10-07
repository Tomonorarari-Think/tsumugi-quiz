using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using TsumugiQuiz.Tests.PlayMode.UI;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// #25（欠落時のフォールバック UI）の通し検証。実 <see cref="TtsService"/> を使い、
    /// External が未配置のこの実行環境で <see cref="TtsStatusPanel"/> が
    /// クラッシュせず「利用不可（MissingCoreDll）」の案内を出すことを確認する（受け入れ条件）。
    ///
    /// External が配置済みの環境（開発機で scripts/setup-external.ps1 実行済みなど）では
    /// 素直に Ready になるはずなので、その場合はそちらの分岐で検証する
    /// （<see cref="VoicevoxTestFixture.IsAvailable"/> で分岐）。
    /// </summary>
    public sealed class TtsStatusFallbackUiTests
    {
        private GameObject _gameObject;
        private TtsService _service;
        private string _cacheRoot;
        private MainSceneTestHelpers.ConsentFileScope _consentScope;
        private Task _initTask;

        [SetUp]
        public void SetUp()
        {
            // 同意済み状態でないと ResolveReason が常に ConsentNotGiven になってしまうため、
            // MainSceneUiTests と同じ手順で実 consent.json を退避し、同意済みにする。
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();
            var store = ConsentGate.CreateDefaultStore();
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);

            // Boot シーンを経由する他のテストが DontDestroyOnLoad の TtsService を残していることがあるため、
            // VoicevoxTtsServiceTests と同様に先に片付ける。
            if (TtsService.Instance != null)
            {
                UnityEngine.Object.DestroyImmediate(TtsService.Instance.gameObject);
            }

            _cacheRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsStatusFallbackUiTests", Guid.NewGuid().ToString("N"));

            _gameObject = new GameObject(nameof(TtsStatusFallbackUiTests));
            _gameObject.SetActive(false);
            _service = _gameObject.AddComponent<TtsService>();
            _initTask = _service.EnsureInitializedAsync(new FixedTtsSettingsProvider(TtsSettings.Default), _cacheRoot);
            _gameObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null) UnityEngine.Object.DestroyImmediate(_gameObject);
            _gameObject = null;
            _service = null;

            if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true);

            _consentScope?.Restore();
        }

        /// <summary>
        /// 実 <see cref="TtsService"/> の初期化完了を待つ。実 voicevox_core + ONNX Runtime の
        /// モデルロードには数百 ms〜数秒かかることがあり、UI 要素探索用の
        /// <see cref="MainSceneTestHelpers.WaitUntil"/> の既定タイムアウトでは足りずに誤って
        /// 失敗しうるため、30 秒の実時間タイムアウトを明示指定する（issue #34。#87 で
        /// <see cref="MainSceneTestHelpers.WaitUntil"/> 自体が実時間ベースへ統一されたため、
        /// このクラス独自のフレーム/実時間待機ヘルパーは持たず、共通ヘルパーへ委譲する）。
        /// </summary>
        private IEnumerator WaitForInitialization()
        {
            yield return MainSceneTestHelpers.WaitUntil(
                () => _initTask.IsCompleted,
                "初期化が終わりませんでした。",
                timeoutSeconds: 30f);

            if (_initTask.IsFaulted)
            {
                Assert.Fail($"初期化タスクが失敗しました: {_initTask.Exception}");
            }

            if (_initTask.IsCanceled)
            {
                Assert.Fail("初期化タスクがキャンセルされました。");
            }
        }

        [UnityTest]
        public IEnumerator External未配置ならMissingCoreDllの案内が出て利用不可表示になる()
        {
            // #25 LOW: この検証は「External 未配置」専用。配置済みの開発機ではスキップする
            // （Ready を強制的にアサートする分岐は持たない）。
            // Assume.That ではなく Assert.Ignore を使う: Unity の CommandLineTest ランナーは
            // Inconclusive（Assume 由来）が 1 件でもあると全体の終了コードを非 0 にしてしまい、
            // scripts/verify.ps1 が「テストは全部成功なのに検証失敗」と誤検知するため
            // （Ignored/Skipped は終了コードに影響しない）。
            if (VoicevoxTestFixture.IsAvailable)
            {
                Assert.Ignore("この環境には External が配置済みのため、未配置専用のこの検証はスキップします。");
            }

            yield return WaitForInitialization();

            Assert.That(_service.Status.State, Is.EqualTo(TtsServiceState.NotAvailable),
                "External 未配置の環境では NotAvailable になること");

            var corner = TtsStatusPanel.DescribeCornerStatus(_service);
            var reason = TtsStatusPanel.ResolveReason(_service);

            Assert.That(reason, Is.EqualTo(TtsUnavailableReason.MissingCoreDll),
                "voicevox_core.dll が無いので MissingCoreDll と判定されること");
            Assert.That(corner, Is.EqualTo("読み上げ: 利用不可"), "Title 隅の表示が「利用不可」になること");

            // パネル本体にもクラッシュせず案内が出ること（受け入れ条件）。
            var overlay = TtsStatusPanel.Create(_service);
            var headline = overlay.Q<Label>("tts-status-headline");
            var guidance = overlay.Q<Label>("tts-status-guidance");
            var expected = TtsStatusMessages.For(TtsUnavailableReason.MissingCoreDll);

            Assert.That(PhraseWrappedText.GetSourceText(headline), Is.EqualTo(expected.Headline));
            Assert.That(PhraseWrappedText.GetSourceText(guidance), Is.EqualTo(expected.Guidance));

            // クイズは読み上げなしで続行できる（docs/tts.md §9）。
            Assert.That(_service.ReadingEnabled, Is.True, "利用不可でもクイズ側の設定自体は勝手に変えないこと");
        }

        /// <summary>
        /// External 配置済みの環境（開発機）専用。未配置環境ではスキップする
        /// （Assert.Ignore を使う理由は上のテストと同じ。Unity の終了コードに影響しない Ignored にするため）。
        /// </summary>
        [UnityTest]
        public IEnumerator External配置済みならReady表示になる()
        {
            if (!VoicevoxTestFixture.IsAvailable)
            {
                Assert.Ignore("この環境には External が配置されていないため、配置済み専用のこの検証はスキップします。");
            }

            yield return WaitForInitialization();

            Assert.That(_service.Status.State, Is.EqualTo(TtsServiceState.Ready));
            Assert.That(TtsStatusPanel.ResolveReason(_service), Is.Null, "配置済みなら案内すべき理由が無いこと");
            Assert.That(TtsStatusPanel.DescribeCornerStatus(_service), Is.EqualTo("読み上げ: Ready"));
        }
    }
}
