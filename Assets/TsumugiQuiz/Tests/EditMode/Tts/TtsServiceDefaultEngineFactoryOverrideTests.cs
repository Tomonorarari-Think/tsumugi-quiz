using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// <see cref="TtsService.DefaultEngineFactoryOverrideForTesting"/> の優先順位を固定する（#156 レビュー L-1）。
    /// <see cref="TtsService.Initialize"/> の engineFactory 解決順は
    /// 「明示的な <c>engineFactory</c> 引数」→「<see cref="TtsService.DefaultEngineFactoryOverrideForTesting"/>」
    /// →「本番実装（voicevox_core）」であること。<c>TtsEngineFactoryGuardSetUp</c>（EditMode アセンブリ全体の
    /// ガード）が正しく機能する前提を、このテスト自身が壊さないよう、SetUp/TearDown で既存の値を
    /// 退避・復元する。
    /// </summary>
    public sealed class TtsServiceDefaultEngineFactoryOverrideTests
    {
        private const int WaitMs = 20000;

        private GameObject _gameObject;
        private TtsService _service;
        private string _cacheRoot;
        private TtsSynthesisEngineFactory _previousOverride;

        [SetUp]
        public void SetUp()
        {
            // TtsEngineFactoryGuardSetUp が設定したガード（あるいは他のテストが残した値）を退避する。
            _previousOverride = TtsService.DefaultEngineFactoryOverrideForTesting;

            _cacheRoot = Path.Combine(
                Path.GetTempPath(),
                "TsumugiQuizTtsServiceDefaultEngineFactoryOverrideTests",
                Guid.NewGuid().ToString("N"));

            // 非アクティブなので Awake は走らない（他の TtsService 系テストと同じ作法）。
            _gameObject = new GameObject(nameof(TtsServiceDefaultEngineFactoryOverrideTests));
            _gameObject.SetActive(false);
            _service = _gameObject.AddComponent<TtsService>();
        }

        [TearDown]
        public void TearDown()
        {
            TtsService.DefaultEngineFactoryOverrideForTesting = _previousOverride;

            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
            }

            _gameObject = null;
            _service = null;

            try
            {
                if (Directory.Exists(_cacheRoot))
                {
                    Directory.Delete(_cacheRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        [Test]
        public void 明示的なengineFactoryはDefaultEngineFactoryOverrideForTestingより優先される()
        {
            var explicitCalled = false;
            var overrideCalled = false;

            TtsService.DefaultEngineFactoryOverrideForTesting = (_, __) =>
            {
                overrideCalled = true;
                return new FakeTtsSynthesisEngine();
            };

            _service.Initialize(
                new FixedTtsSettingsProvider(TtsSettings.Default),
                _cacheRoot,
                consentCheck: null,
                engineFactory: (_, __) =>
                {
                    explicitCalled = true;
                    return new FakeTtsSynthesisEngine();
                });

            Assert.That(WaitForReady(_service), Is.True, "フェイクエンジンなら Ready になること。");
            Assert.That(explicitCalled, Is.True, "明示的な engineFactory が使われること。");
            Assert.That(
                overrideCalled, Is.False,
                "明示指定があるときは DefaultEngineFactoryOverrideForTesting を使わないこと。");
        }

        [Test]
        public void engineFactory省略時はDefaultEngineFactoryOverrideForTestingが使われる()
        {
            var overrideCalled = false;

            TtsService.DefaultEngineFactoryOverrideForTesting = (_, __) =>
            {
                overrideCalled = true;
                return new FakeTtsSynthesisEngine();
            };

            _service.Initialize(
                new FixedTtsSettingsProvider(TtsSettings.Default),
                _cacheRoot,
                consentCheck: null,
                engineFactory: null);

            Assert.That(WaitForReady(_service), Is.True, "フェイクエンジンなら Ready になること。");
            Assert.That(
                overrideCalled, Is.True,
                "engineFactory 省略時は DefaultEngineFactoryOverrideForTesting が使われること。");
        }

        private static bool WaitForReady(TtsService service)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < WaitMs)
            {
                if (service.Status.State == TtsServiceState.Ready)
                {
                    return true;
                }

                Thread.Sleep(1);
            }

            return service.Status.State == TtsServiceState.Ready;
        }
    }
}
