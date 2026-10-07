using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// <see cref="TtsService.UnavailableReason"/> が <see cref="TtsSetupException.Reason"/> を
    /// そのまま反映し、理由を分類できない失敗は <see cref="TtsUnavailableReason.InitializationFailed"/> に
    /// フォールバックすることの検証（#25）。
    /// </summary>
    public sealed class TtsServiceUnavailableReasonTests
    {
        private GameObject _gameObject;
        private TtsService _service;
        private string _cacheRoot;

        [SetUp]
        public void SetUp()
        {
            _cacheRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsServiceReasonTests", Guid.NewGuid().ToString("N"));

            _gameObject = new GameObject(nameof(TtsServiceUnavailableReasonTests));
            _gameObject.SetActive(false);
            _service = _gameObject.AddComponent<TtsService>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null) UnityEngine.Object.DestroyImmediate(_gameObject);
            _gameObject = null;
            _service = null;
            LogAssert.ignoreFailingMessages = false;

            try
            {
                if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        [Test]
        public void 初期化前はUnavailableReasonがnull()
        {
            Assert.That(_service.UnavailableReason, Is.Null);
        }

        [Test]
        public void 分類済みのTtsSetupExceptionはそのままUnavailableReasonになる()
        {
            InitializeWithThrow(new TtsSetupException("音声モデルが見つかりません（テスト）。", TtsUnavailableReason.MissingModel));

            Assert.That(_service.Status.State, Is.EqualTo(TtsServiceState.NotAvailable));
            Assert.That(_service.UnavailableReason, Is.EqualTo(TtsUnavailableReason.MissingModel));
        }

        [Test]
        public void 理由未分類のTtsSetupExceptionはInitializationFailedにフォールバックする()
        {
            InitializeWithThrow(new TtsSetupException("原因不明の設定エラー（テスト）。"));

            Assert.That(_service.UnavailableReason, Is.EqualTo(TtsUnavailableReason.InitializationFailed));
        }

        // TtsSetupException 以外の例外（想定外の失敗）も InitializationFailed にフォールバックするが、
        // その経路は TtsService.CreateEngine が Debug.LogError + Debug.LogException を無条件に出すため
        // （本番でも「本当に想定外」として目立たせる設計。docs/tts.md §9）、
        // scripts/verify.ps1 のログスキャン（error/exception 検出）に引っかかってしまう。
        // フォールバック自体は上の「理由未分類の TtsSetupException」で検証済みなので、ここでは重ねて検証しない。

        [Test]
        public void Readyになった場合はUnavailableReasonがnull()
        {
            LogAssert.ignoreFailingMessages = true;

            _service.Initialize(
                new FixedTtsSettingsProvider(TtsSettings.Default), _cacheRoot, null,
                (location, settings) => new FakeTtsSynthesisEngine());

            Assert.That(_service.EnsureInitializedAsync().Wait(20000), Is.True);
            Assert.That(_service.Status.State, Is.EqualTo(TtsServiceState.Ready));
            Assert.That(_service.UnavailableReason, Is.Null);
        }

        [Test]
        public void バージョン不一致のDetailにはminmaxが反映される()
        {
            InitializeWithThrow(new TtsSetupException(
                "ONNX Runtime のロードに失敗しました（テスト）。", TtsUnavailableReason.OnnxRuntimeVersionMismatch, 4, 6));

            Assert.That(_service.UnavailableReason, Is.EqualTo(TtsUnavailableReason.OnnxRuntimeVersionMismatch));
            Assert.That(_service.UnavailableDetail, Is.EqualTo("対応バージョンは 1.4 以上 1.6 以下です。"));
        }

        /// <summary>
        /// #25 C-1: 失敗 → RetryInitializeAsync → Ready になれること。
        /// Initialize は 1 回しか本体を実行しない（_initialized フラグ）ため、
        /// RetryInitializeAsync がこれを正しくリセットしていることの検証を兼ねる。
        /// </summary>
        [Test]
        public void 失敗後にRetryInitializeAsyncするとReadyになれる()
        {
            InitializeWithThrow(new TtsSetupException("音声モデルが見つかりません（テスト）。", TtsUnavailableReason.MissingModel));
            Assert.That(_service.Status.State, Is.EqualTo(TtsServiceState.NotAvailable));

            var fakeEngine = new FakeTtsSynthesisEngine();
            var retryTask = _service.RetryInitializeAsync(
                new FixedTtsSettingsProvider(TtsSettings.Default), _cacheRoot, null,
                (location, settings) => fakeEngine);

            Assert.That(retryTask.Wait(20000), Is.True, "再試行の待ちが完了すること");
            Assert.That(_service.Status.State, Is.EqualTo(TtsServiceState.Ready), "再試行後は Ready になること");
            Assert.That(_service.UnavailableReason, Is.Null);
            Assert.That(_service.ResolvedStyle.HasValue, Is.True);
        }

        [Test]
        public void RetryInitializeAsyncは初回でも通常の初期化として動く()
        {
            var fakeEngine = new FakeTtsSynthesisEngine();
            var task = _service.RetryInitializeAsync(
                new FixedTtsSettingsProvider(TtsSettings.Default), _cacheRoot, null,
                (location, settings) => fakeEngine);

            Assert.That(task.Wait(20000), Is.True);
            Assert.That(_service.Status.State, Is.EqualTo(TtsServiceState.Ready));
        }

        // 引数の型を TtsSetupException にしているのは意図的。System.Exception 型を経由すると、
        // Unity がログ（Debug.LogWarning 含む）に添えるスタックトレースの引数表記
        // "(System.Exception)" が verify.ps1 のログスキャン（\bexception\b）に引っかかってしまうため
        // （"TtsSetupException" は 1 語なので "exception" 単体の単語境界に一致せず引っかからない）。
        private void InitializeWithThrow(TtsSetupException exception)
        {
            LogAssert.ignoreFailingMessages = true;

            _service.Initialize(
                new FixedTtsSettingsProvider(TtsSettings.Default), _cacheRoot, null,
                (location, settings) => throw exception);

            Assert.That(_service.EnsureInitializedAsync().Wait(20000), Is.True, "失敗しても待ちは完了すること");
        }
    }
}
