using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using TsumugiQuiz.Tts;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// 実 DLL を使った <see cref="TtsService"/> の通し検証（docs/tts.md §11.2）。
    /// External / 配置物が無い環境では <c>Assert.Ignore</c> でスキップする。
    ///
    /// <b>クラス名を Voicevox で始めているのは意図的</b>。フィクスチャの実行順はクラス名の辞書順で、
    /// <c>VoicevoxDllSearchPathTests</c>（ONNX Runtime のロード手段を測るため、プロセス内で
    /// 最初に走る必要がある）より後に回す必要があるため（NUnit の OrderAttribute はメソッド専用）。
    /// </summary>
    public sealed class VoicevoxTtsServiceTests
    {
        private const string SampleText = "こんにちは";
        private const float SynthesisTimeoutSec = 180f;

        /// <summary>管理ヒープの増加として許容する量（M-9）。</summary>
        private const long AllowedGrowthBytes = 8L * 1024 * 1024;

        /// <summary>
        /// Unity のトータル割り当て（ネイティブ含む）の増加として許容する量。
        ///
        /// <c>Profiler.GetTotalAllocatedMemoryLong()</c> は Unity のネイティブヒープ全体を見るため、
        /// voicevox_core / ONNX Runtime のアリーナ、オーディオシステム、エディタ自身の割り当てが混ざり、
        /// <b>AudioClip 1 本（24kHz mono 約 1 秒 ≒ 90KB）より桁の大きいノイズが乗る</b>。
        /// そのため「解放できているか」の主判定は生存している <c>AudioClip</c> の本数で行い、
        /// こちらは暴走していないことを見るゆるい上限にしてある。
        /// </summary>
        private const long AllowedTotalGrowthBytes = 48L * 1024 * 1024;

        /// <summary>AudioClip の解放を確かめる際の合成回数。</summary>
        private const int ClipReleaseIterations = 30;

        private static readonly string[] Kana = { "あ", "い", "う", "え", "お" };

        private GameObject _gameObject;
        private TtsService _service;
        private string _cacheRoot;

        [SetUp]
        public void SetUp()
        {
            _cacheRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsServiceTests", Guid.NewGuid().ToString("N"));

            // Boot シーンを読み込む他の PlayMode テスト（BootSceneBootstrapTests）が
            // DontDestroyOnLoad の TtsService を残していることがある。放置すると
            // TtsService.Awake の重複ガードでこちらのインスタンスが破棄されてしまうので先に片付ける
            // （残っている合成エンジンもここで解放される）。
            if (TtsService.Instance != null)
            {
                UnityEngine.Object.DestroyImmediate(TtsService.Instance.gameObject);
            }

            // 非アクティブで生成して Initialize を先に呼ぶことで、キャッシュ先をテスト用に差し替える
            // （アクティブ化後の Awake での Initialize は 2 回目なので何もしない）。
            _gameObject = new GameObject(nameof(VoicevoxTtsServiceTests));
            _gameObject.SetActive(false);
            _service = _gameObject.AddComponent<TtsService>();
            _service.EnsureInitializedAsync(new FixedTtsSettingsProvider(TtsSettings.Default), _cacheRoot);
            _gameObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null) UnityEngine.Object.DestroyImmediate(_gameObject);
            _gameObject = null;
            _service = null;

            if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true);
        }

        [UnityTest]
        public IEnumerator 合成できて2回目はキャッシュに命中する()
        {
            if (!VoicevoxTestFixture.IsAvailable) Assert.Ignore(VoicevoxTestFixture.SkipReason);

            yield return WaitForReady();

            var stopwatch = Stopwatch.StartNew();
            var firstTask = _service.SynthesizeAsync(SampleText);
            yield return WaitFor(firstTask);
            var firstMs = stopwatch.ElapsedMilliseconds;
            var first = firstTask.Result;

            Assert.That(first, Is.Not.Null, "読み上げが有効なら結果が返ること");
            Assert.That(first.FromCache, Is.False, "1 回目はキャッシュミスであること");
            Assert.That(first.Clip, Is.Not.Null);
            Assert.That(first.Clip.length, Is.GreaterThan(0f), "AudioClip.length > 0");
            Assert.That(first.DurationSec, Is.GreaterThan(0d));
            Assert.That(first.CacheKey, Is.Not.Null.And.Length.EqualTo(TtsCacheKey.HexLength));

            stopwatch.Restart();
            var secondTask = _service.SynthesizeAsync(SampleText);
            yield return WaitFor(secondTask);
            var secondMs = stopwatch.ElapsedMilliseconds;
            var second = secondTask.Result;

            Debug.Log($"[VoicevoxTtsServiceTests] 初回={firstMs}ms キャッシュ命中={secondMs}ms " +
                      $"duration={first.DurationSec:F3}s clip={first.Clip.length:F3}s key={first.CacheKey} " +
                      $"style={_service.ResolvedStyle?.SpeakerName}/{_service.ResolvedStyle?.StyleName}");

            Assert.That(second, Is.Not.Null);
            Assert.That(second.FromCache, Is.True, "2 回目はキャッシュに命中すること");
            Assert.That(second.CacheKey, Is.EqualTo(first.CacheKey));
            Assert.That(second.DurationSec, Is.EqualTo(first.DurationSec).Within(1e-6));
            Assert.That(secondMs, Is.LessThanOrEqualTo(firstMs), "キャッシュ命中のほうが速いこと");
            Assert.That(_service.Cache.Count, Is.EqualTo(1));
            Assert.That(File.Exists(_service.Cache.GetWavPath(first.CacheKey)), Is.True);

            // AudioClip の所有権は呼び出し側にある（docs/tts.md §6.3）。
            first.ReleaseClip();
            second.ReleaseClip();
            Assert.That(first.IsReleased, Is.True);
            Assert.DoesNotThrow(() => first.ReleaseClip(), "二重呼び出しでも安全であること");
        }

        /// <summary>
        /// <see cref="TtsResult.ReleaseClip"/> を呼んでいれば、繰り返し合成しても
        /// <c>AudioClip</c> が溜まらず、Unity 側のトータル割り当ても増え続けないこと（docs/tts.md §6.3）。
        /// <c>AudioClip</c> のサンプルはネイティブメモリに載るため、管理ヒープでは捕まえられない。
        ///
        /// <b>主判定は「生存している <c>AudioClip</c> の本数」</b>にしている。
        /// <c>Profiler.GetTotalAllocatedMemoryLong()</c> は Unity のネイティブヒープ全体を見るので、
        /// voicevox_core / ONNX Runtime が確保するアリーナ（合成のたびに伸びる）、オーディオシステム、
        /// エディタ自身の割り当てが混ざる。実測では解放していても 30 回で +20MB 程度動き、
        /// <c>AudioClip</c> 1 本（24kHz mono 約 1 秒 ≒ 90KB、30 本で 2.7MB）は
        /// そのノイズに埋もれてしまうため、Profiler 値では解放漏れを判別できない。
        /// そこで Profiler 値は<b>暴走していないことを見るゆるい上限</b>として扱う
        /// （管理ヒープ側のしきい値は <see cref="AllowedGrowthBytes"/> で厳しく見ている）。
        /// </summary>
        [UnityTest]
        [Category("Slow")]
        public IEnumerator AudioClipを解放すればメモリが増え続けない()
        {
            if (!VoicevoxTestFixture.IsAvailable) Assert.Ignore(VoicevoxTestFixture.SkipReason);

            yield return WaitForReady();

            // 1 回目でウォームアップしてから測る（初期化直後の確保を増加分に数えないため）。
            var warmUp = _service.SynthesizeAsync(MakeText(0));
            yield return WaitFor(warmUp);
            warmUp.Result?.ReleaseClip();
            yield return null;

            var before = MeasureTotalAllocated();
            var clipsBefore = CountLiveAudioClips();

            for (var i = 1; i <= ClipReleaseIterations; i++)
            {
                var task = _service.SynthesizeAsync(MakeText(i));
                yield return WaitFor(task);

                var result = task.Result;
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Clip.length, Is.GreaterThan(0f));

                result.ReleaseClip();
                Assert.That(result.IsReleased, Is.True);

                // Destroy は次のフレームで実際に解放される。
                yield return null;
            }

            var after = MeasureTotalAllocated();
            var clipsAfter = CountLiveAudioClips();
            var growth = after - before;

            Debug.Log($"[VoicevoxTtsServiceTests] ReleaseClip {ClipReleaseIterations} 回: " +
                      $"AudioClip {clipsBefore} → {clipsAfter} 本 / " +
                      $"total {before / 1024}KB → {after / 1024}KB（増加 {growth / 1024}KB）");

            Assert.That(clipsAfter, Is.LessThanOrEqualTo(clipsBefore),
                $"生成した AudioClip が溜まっていないこと（{ClipReleaseIterations} 本分）");
            Assert.That(growth, Is.LessThan(AllowedTotalGrowthBytes),
                $"トータル割り当ての増加が {AllowedTotalGrowthBytes / 1024 / 1024}MB 未満であること");
        }

        [UnityTest]
        public IEnumerator 読み上げが無効なら合成せずnullを返す()
        {
            if (!VoicevoxTestFixture.IsAvailable) Assert.Ignore(VoicevoxTestFixture.SkipReason);

            yield return WaitForReady();

            _service.ReadingEnabled = false;
            var task = _service.SynthesizeAsync(SampleText);
            yield return WaitFor(task);

            Assert.That(task.Result, Is.Null, "呼び出し側は読み上げなしで続行できること");
            Assert.That(_service.Cache.Count, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator 事前合成したものはキャッシュに載る()
        {
            if (!VoicevoxTestFixture.IsAvailable) Assert.Ignore(VoicevoxTestFixture.SkipReason);

            yield return WaitForReady();

            var texts = new[] { "あいうえお", "かきくけこ" };
            var prefetch = _service.PrefetchAsync(texts);
            yield return WaitFor(prefetch);

            Assert.That(prefetch.Result, Is.EqualTo(2), "2 件とも新規合成されること");
            Assert.That(_service.Cache.Count, Is.EqualTo(2));

            // 2 回目は合成が走らない。
            var again = _service.PrefetchAsync(texts);
            yield return WaitFor(again);
            Assert.That(again.Result, Is.EqualTo(0));

            var task = _service.SynthesizeAsync(texts[0]);
            yield return WaitFor(task);
            Assert.That(task.Result.FromCache, Is.True, "事前合成の結果がそのまま使われること");
            task.Result.ReleaseClip();
        }

        /// <summary>
        /// 100 回合成しても管理ヒープが単調に増え続けないことの簡易チェック（docs/tts.md §11.2 のメモリ項）。
        /// ネイティブ側の wav は <c>voicevox_wav_free</c> で解放されるので、
        /// ここでは C# 側に持ち越しが無いことを見る。
        /// </summary>
        [UnityTest]
        [Category("Slow")]
        public IEnumerator 繰り返し合成してもメモリが増え続けない()
        {
            if (!VoicevoxTestFixture.IsAvailable) Assert.Ignore(VoicevoxTestFixture.SkipReason);

            yield return WaitForReady();

            const int Iterations = 100;
            var texts = new List<string>(Iterations);
            for (var i = 0; i < Iterations; i++) texts.Add(MakeText(i));

            var before = CollectAndMeasure();

            var stopwatch = Stopwatch.StartNew();
            var prefetch = _service.PrefetchAsync(texts);
            yield return WaitFor(prefetch, SynthesisTimeoutSec * 4f);
            stopwatch.Stop();

            var after = CollectAndMeasure();
            var growth = after - before;

            Debug.Log($"[VoicevoxTtsServiceTests] {Iterations} 回合成: {stopwatch.ElapsedMilliseconds}ms " +
                      $"managed {before / 1024}KB → {after / 1024}KB（増加 {growth / 1024}KB） " +
                      $"cache={_service.Cache.Count}件 {_service.Cache.TotalBytes / 1024}KB");

            Assert.That(prefetch.Result, Is.EqualTo(Iterations));
            Assert.That(growth, Is.LessThan(AllowedGrowthBytes),
                $"管理ヒープの増加が {AllowedGrowthBytes / 1024 / 1024}MB 未満であること");
        }

        /// <summary>100 通り以上の短い読みを作る（かなの 3 桁組み合わせ）。</summary>
        private static string MakeText(int index)
            => Kana[index % 5] + Kana[(index / 5) % 5] + Kana[(index / 25) % 5];

        /// <summary>生存している <see cref="AudioClip"/> の本数（解放できているかの主判定）。</summary>
        private static int CountLiveAudioClips()
            => Resources.FindObjectsOfTypeAll<AudioClip>().Length;

        /// <summary>Unity のトータル割り当て（ネイティブ側を含む）を測る。</summary>
        private static long MeasureTotalAllocated()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Resources.UnloadUnusedAssets();
            return Profiler.GetTotalAllocatedMemoryLong();
        }

        private static long CollectAndMeasure()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(forceFullCollection: true);
        }

        private IEnumerator WaitForReady()
        {
            var task = _service.EnsureInitializedAsync();
            yield return WaitFor(task);

            if (!_service.Status.IsReady)
            {
                Assert.Ignore($"読み上げを初期化できなかったためスキップします: {_service.Status}");
            }
        }

        /// <summary>
        /// タスクの完了を待つ。<c>yield return null</c> でメインスレッドを回すことで、
        /// <c>UnitySynchronizationContext</c> に戻る継続（<c>AudioClip</c> 化）も進む。
        /// </summary>
        private static IEnumerator WaitFor(Task task, float timeoutSec = SynthesisTimeoutSec)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSec;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail($"{timeoutSec} 秒以内に完了しませんでした。");
                }
                yield return null;
            }

            if (task.IsFaulted)
            {
                Assert.Fail($"タスクが失敗しました: {task.Exception}");
            }
        }
    }
}
