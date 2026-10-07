using System;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using TsumugiQuiz.Tests.Shared.Core;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Tts
{
    /// <summary>
    /// <see cref="ITtsSynthesisEngine"/> をフェイクに差し替えて、<see cref="TtsService"/> の
    /// 分岐（キャッシュ命中・ミス・読み上げ無効・同意ゲート・取り消し・初期化失敗）を検証する。
    ///
    /// EditMode では Unity の <c>SynchronizationContext</c> がテストの実行中にポンプされないため、
    /// <see cref="SetUp"/> で <see cref="QueuingSynchronizationContext"/>（#97 L-5、
    /// <c>TtsStatusPanelTests</c> と共通の <c>Tests/Shared</c> 実装）に差し替える。
    /// 待ち合わせは <see cref="WaitForCompletion"/> が担い、ポーリングのたびに
    /// <see cref="QueuingSynchronizationContext.Drain"/> でポンプするため、
    /// <c>ConfigureAwait(true)</c> で捕捉された継続（<c>TtsService</c> 内部の各 <c>await</c>）が
    /// スレッドプール上で無秩序に走ることも、<c>Task.Wait</c> がデッドロックすることもない。
    /// そのため、ここで確かめられるのは <b><c>AudioClip</c> を作らない経路だけ</b>。
    /// 実 DLL と <c>AudioClip</c> 化は PlayMode の <c>VoicevoxTtsServiceTests</c> で確認する。
    /// </summary>
    public sealed class TtsServiceFakeEngineTests
    {
        private const int WaitMs = 20000;

        /// <summary>
        /// 合成失敗ログの行頭タグ（<c>TtsService.SynthesisFailureLogTag</c> と対応）。
        /// <c>scripts/common.ps1</c> の <c>$ignorePatterns</c> もこの文字列に依存している。
        /// </summary>
        private const string SynthesisFailureLogTag = "[TtsService] 合成に失敗しました";

        private GameObject _gameObject;
        private TtsService _service;
        private FakeTtsSynthesisEngine _engine;
        private string _cacheRoot;
        private SynchronizationContext _originalContext;
        private QueuingSynchronizationContext _testContext;

        [SetUp]
        public void SetUp()
        {
            _originalContext = SynchronizationContext.Current;
            _testContext = new QueuingSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_testContext);

            _cacheRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsServiceFakeTests", Guid.NewGuid().ToString("N"));

            // 非アクティブなので Awake は走らない（DontDestroyOnLoad や Instance を触らない）。
            _gameObject = new GameObject(nameof(TtsServiceFakeEngineTests));
            _gameObject.SetActive(false);
            _service = _gameObject.AddComponent<TtsService>();
            _engine = new FakeTtsSynthesisEngine();
        }

        [TearDown]
        public void TearDown()
        {
            // #97: 前のテストの継続が積み残らないよう、他の後始末より先に drain する。
            _testContext?.Drain();

            if (_gameObject != null) UnityEngine.Object.DestroyImmediate(_gameObject);
            _gameObject = null;
            _service = null;

            SynchronizationContext.SetSynchronizationContext(_originalContext);
            LogAssert.ignoreFailingMessages = false;

            try
            {
                if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true);
            }
            catch (IOException)
            {
                // 取り消し後も走り続けている合成が書き込み中のことがある。一時ディレクトリなので放置してよい。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void Initialize(Func<bool> consentCheck = null, TtsSynthesisEngineFactory factory = null)
        {
            // 失敗系の分岐を確かめるテストが多く、意図した Debug.LogError / LogException が出る。
            // 個別に LogAssert.Expect を並べると（ワーカースレッドから出るので）順序に依存して脆くなるため、
            // ここで無効化する。SetUp で設定してもテストフレームワークに上書きされるので本体側で行う。
            LogAssert.ignoreFailingMessages = true;

            _service.Initialize(
                new FixedTtsSettingsProvider(TtsSettings.Default),
                _cacheRoot,
                consentCheck,
                factory ?? ((location, settings) => _engine));

            Assert.That(WaitForCompletion(_service.EnsureInitializedAsync()), Is.True, "初期化が終わること");
        }

        private T Wait<T>(Task<T> task)
        {
            Assert.That(WaitForCompletion(task), Is.True, $"{WaitMs}ms 以内に完了すること");
            return task.Result;
        }

        /// <summary>
        /// <paramref name="task"/> の完了を、<see cref="_testContext"/> に貯まった継続を吐き出しながら待つ
        /// （#97 M-4/L-5）。<c>Task.Wait</c> は自分自身が呼び出し元スレッドをブロックするだけで
        /// <see cref="SynchronizationContext"/> をポンプしないため、<see cref="QueuingSynchronizationContext"/>
        /// に切り替えた以上、待ち合わせ自身がポンプ役を兼ねる必要がある。
        /// </summary>
        private bool WaitForCompletion(Task task, int timeoutMs = WaitMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                _testContext.Drain();
                if (task.IsCompleted) return true;
                Thread.Sleep(10);
            }

            _testContext.Drain();
            return task.IsCompleted;
        }

        [Test]
        public void 初期化に成功するとReadyになりスタイルが公開される()
        {
            Initialize();

            Assert.That(_service.Status.State, Is.EqualTo(TtsServiceState.Ready));
            Assert.That(_service.Status.IsReady, Is.True);
            Assert.That(_service.ResolvedStyle.HasValue, Is.True);
            Assert.That(_service.ResolvedStyle.Value.SpeakerName, Is.EqualTo("春日部つむぎ"));
        }

        [Test]
        public void EnsureInitializedAsyncは何度呼んでも同じTaskを返す()
        {
            LogAssert.ignoreFailingMessages = true;

            _service.Initialize(
                new FixedTtsSettingsProvider(TtsSettings.Default), _cacheRoot, null, (l, s) => _engine);

            var first = _service.EnsureInitializedAsync();
            var second = _service.EnsureInitializedAsync();

            Assert.That(second, Is.SameAs(first));
            Assert.That(WaitForCompletion(first), Is.True);
        }

        [Test]
        public void 事前合成はキャッシュミスのときだけ合成する()
        {
            Initialize();

            var texts = new[] { "こんにちは", "こんばんは" };

            Assert.That(Wait(_service.PrefetchAsync(texts)), Is.EqualTo(2), "1 回目は 2 件とも合成する");
            Assert.That(_engine.SynthesizeCount, Is.EqualTo(2));
            Assert.That(_service.Cache.Count, Is.EqualTo(2));

            Assert.That(Wait(_service.PrefetchAsync(texts)), Is.EqualTo(0), "2 回目はキャッシュに命中する");
            Assert.That(_engine.SynthesizeCount, Is.EqualTo(2), "合成は増えないこと");
        }

        [Test]
        public void 読み上げが無効ならnullを返し合成もしない()
        {
            Initialize();
            _service.ReadingEnabled = false;

            Assert.That(Wait(_service.SynthesizeAsync("こんにちは")), Is.Null);
            Assert.That(Wait(_service.PrefetchAsync(new[] { "こんにちは" })), Is.EqualTo(0));
            Assert.That(_engine.SynthesizeCount, Is.EqualTo(0));
            Assert.That(_service.Cache.Count, Is.EqualTo(0));
        }

        /// <summary>同意ゲート（#37、FR-74 / FR-75）。未同意なら合成しない。</summary>
        [Test]
        public void 未同意なら合成せずnullを返す()
        {
            var consented = false;
            Initialize(consentCheck: () => consented);

            Assert.That(Wait(_service.SynthesizeAsync("こんにちは")), Is.Null, "未同意では読み上げない");
            Assert.That(_engine.SynthesizeCount, Is.EqualTo(0));

            consented = true;
            Assert.That(Wait(_service.PrefetchAsync(new[] { "こんにちは" })), Is.EqualTo(1), "同意後は合成する");
            Assert.That(_engine.SynthesizeCount, Is.EqualTo(1));
        }

        [Test]
        public void 同意判定が例外を投げたら安全側に倒して合成しない()
        {
            Initialize(consentCheck: () => throw new InvalidOperationException("判定できません（テスト）。"));

            Assert.That(Wait(_service.SynthesizeAsync("こんにちは")), Is.Null);
            Assert.That(_engine.SynthesizeCount, Is.EqualTo(0));
        }

        [Test]
        public void 初期化に失敗するとNotAvailableになり合成しない()
        {
            LogAssert.ignoreFailingMessages = true;

            _service.Initialize(
                new FixedTtsSettingsProvider(TtsSettings.Default),
                _cacheRoot,
                null,
                (location, settings) => throw new TtsSetupException("音声モデルが見つかりません（テスト）。"));

            Assert.That(WaitForCompletion(_service.EnsureInitializedAsync()), Is.True, "失敗しても待ちは完了すること");

            Assert.That(_service.Status.State, Is.EqualTo(TtsServiceState.NotAvailable));
            Assert.That(_service.Status.Reason, Does.Contain("音声モデル"), "UI に出してよい文言が入ること");
            Assert.That(Wait(_service.SynthesizeAsync("こんにちは")), Is.Null);
        }

        [Test]
        public void 取り消すとOperationCanceledExceptionになる()
        {
            _engine.Delay = TimeSpan.FromSeconds(2);
            Initialize();

            using (var cts = new CancellationTokenSource())
            {
                var task = _service.SynthesizeAsync("こんにちは", TtsSpeed.Default, cts.Token);
                cts.Cancel();

                // #97 M-4/L-5: QueuingSynchronizationContext はポンプしないと継続が進まないため、
                // まず WaitForCompletion で完了（キャンセル済みとして完了する）を待ってから、
                // 既に完了済みのタスクに対して同期的に Wait/例外検査を行う。
                Assert.That(WaitForCompletion(task), Is.True, "取り消し後にキャンセル済みとして完了すること");

                var aggregate = Assert.Throws<AggregateException>(() => task.Wait());
                Assert.That(aggregate.InnerException, Is.InstanceOf<OperationCanceledException>());
            }
        }

        /// <summary>
        /// 合成が失敗しても null を返して続行できること（docs/tts.md §9）。
        /// 失敗ログは 1 行のエラー（行頭タグ付き）＋詳細の警告に分かれている。
        /// </summary>
        [Test]
        public void 合成が失敗してもnullを返して続行できる()
        {
            _engine.ThrowOnSynthesize = true;
            Initialize();

            // 意図したエラーログなので、無視ではなく明示的に期待する。
            LogAssert.ignoreFailingMessages = false;
            LogAssert.Expect(LogType.Error, new Regex("^" + Regex.Escape(SynthesisFailureLogTag)));

            Assert.That(Wait(_service.SynthesizeAsync("こんにちは")), Is.Null);
            Assert.That(_service.Cache.Count, Is.EqualTo(0), "失敗した結果はキャッシュしないこと");
        }

        [Test]
        public void 同じ読みの合成は同時に走らない()
        {
            _engine.Delay = TimeSpan.FromMilliseconds(300);
            Initialize();

            var a = _service.PrefetchAsync(new[] { "おなじよみ" });
            var b = _service.PrefetchAsync(new[] { "おなじよみ" });

            Assert.That(WaitForCompletion(Task.WhenAll(a, b)), Is.True);
            Assert.That(_engine.SynthesizeCount, Is.EqualTo(1), "進行中の合成に相乗りすること");
            Assert.That(_service.Cache.Count, Is.EqualTo(1));
        }

        [Test]
        public void キャッシュ上限が0なら書き込まない()
        {
            LogAssert.ignoreFailingMessages = true;

            _service.Initialize(
                new FixedTtsSettingsProvider(TtsSettings.Create(cacheMaxBytes: 0, cacheMaxEntries: 0)),
                _cacheRoot,
                null,
                (location, settings) => _engine);
            Assert.That(WaitForCompletion(_service.EnsureInitializedAsync()), Is.True);

            Assert.That(Wait(_service.PrefetchAsync(new[] { "こんにちは" })), Is.EqualTo(1));
            Assert.That(_service.Cache.IsDisabled, Is.True);
            Assert.That(_service.Cache.Count, Is.EqualTo(0));
            Assert.That(Directory.GetFiles(_cacheRoot, "*.wav", SearchOption.AllDirectories), Is.Empty);
        }
    }
}
