using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Core.Audio;
using UnityEngine;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// <see cref="TtsService"/> のうち、読み上げ音声の合成（<see cref="SynthesizeAsync"/> /
    /// <see cref="PrefetchAsync"/> / <see cref="ProduceAsync"/>）と、その周辺（メモリ不足からの回復、
    /// 合成失敗ログ、メインスレッド確認）をまとめた部分（#140）。
    /// </summary>
    public sealed partial class TtsService
    {
        /// <summary>
        /// 読み上げ音声を得る。キャッシュに命中すれば即返り、なければワーカースレッドで合成する。
        ///
        /// 読み上げを行わない場合（<see cref="ReadingEnabled"/> が false、同意ゲートが false、
        /// 配置不足、合成失敗）は <c>null</c> を返す。
        /// 呼び出し側は読み上げなしで続行すること（docs/tts.md §9）。
        ///
        /// <b>同意ゲート（#37 / #127、FR-74 / FR-75）</b>: 未同意・撤回後に読み上げてはいけない。
        /// 判定は UI 層の <c>ConsentGate.HasUserConsented()</c> が持ち、
        /// Tts 層からは参照できない（asmdef の依存方向は UI → Tts の一方向）ので、
        /// <b>呼び出し側が <see cref="Initialize"/> の <c>consentCheck</c> を渡す</b>
        /// （#127 で全呼び出し元が <c>TtsConsentCheckFactory.Build()</c> を渡すようにした）。
        ///
        /// <b>戻り値の <c>AudioClip</c> は呼び出し側が所有する。</b>
        /// 再生が終わったら（遅くとも次の問題の合成を始める前に）
        /// <see cref="TtsResult.ReleaseClip"/> を呼んで解放すること（docs/tts.md §6.3）。
        /// </summary>
        /// <param name="readingText">読み（空なら問題文を渡すのは呼び出し側の責務。docs/tts.md §7.1）</param>
        /// <param name="speed">読み上げ速度（0.5〜2.0 にクランプされる）</param>
        /// <param name="cancellationToken">取り消し（ロビーを抜けたときなど）</param>
        /// <exception cref="OperationCanceledException">取り消されたとき</exception>
        /// <exception cref="InvalidOperationException">メインスレッド以外から呼んだとき</exception>
        public async Task<TtsResult> SynthesizeAsync(
            string readingText, float speed = TtsSpeed.Default, CancellationToken cancellationToken = default)
        {
            EnsureMainThread();

            var outcome = await ProduceAsync(readingText, speed, cancellationToken, forCache: false)
                .ConfigureAwait(true);
            if (outcome == null) return null;

            // ここはメインスレッド。AudioClip.Create / SetData はメインスレッド専用（docs/tts.md §6.3）。
            try
            {
                var clip = TtsAudioClipFactory.Create(outcome.Wav);
                return new TtsResult(clip, outcome.Wav.DurationSec, outcome.FromCache, outcome.Key);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                Debug.LogError($"[TtsService] AudioClip を生成できませんでした（key={outcome.Key}）。読み上げなしで続行します。");
                Debug.LogException(e);
                return null;
            }
        }

        /// <summary>
        /// 事前合成（docs/tts.md §7.4）。キャッシュに無いものだけを順番に合成して保存する。
        /// <c>AudioClip</c> は作らないので、ワーカースレッドだけで完結する。
        /// 個々の失敗はログに残して次へ進む（進行を止めない）。
        /// </summary>
        /// <returns>実際に合成した件数（キャッシュ命中分は含まない）。</returns>
        /// <exception cref="InvalidOperationException">メインスレッド以外から呼んだとき</exception>
        public async Task<int> PrefetchAsync(
            IEnumerable<string> readingTexts, float speed = TtsSpeed.Default, CancellationToken cancellationToken = default)
        {
            if (readingTexts == null) throw new ArgumentNullException(nameof(readingTexts));

            // 合成自体はワーカースレッドで走るが、初期化は Application.* を読むのでメインスレッドから始める。
            EnsureMainThread();

            var synthesized = 0;
            foreach (var readingText in readingTexts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var outcome = await ProduceAsync(readingText, speed, cancellationToken, forCache: true)
                    .ConfigureAwait(true);
                if (outcome != null && !outcome.FromCache) synthesized++;
            }

            return synthesized;
        }

        /// <summary>キャッシュ照会 → 合成 → キャッシュ保存までを行う（<c>AudioClip</c> 化は含まない）。</summary>
        private async Task<TtsSynthesisOutcome> ProduceAsync(
            string readingText, float speed, CancellationToken cancellationToken, bool forCache)
        {
            if (string.IsNullOrWhiteSpace(readingText)) return null;
            if (!_readingEnabled) return null;
            if (!HasConsent()) return null;

            var engine = await GetEngineAsync(cancellationToken).ConfigureAwait(true);
            if (engine == null) return null;

            var clamped = TtsSpeed.Clamp(speed);
            var key = TtsCacheKey.Compute(
                readingText, engine.StyleName, engine.SpeakerName, clamped, engine.CoreVersion, engine.ModelsVersion);

            var lifetime = _lifetime;
            if (lifetime == null) return null;   // 終了処理が走ったあと。読み上げなしで続行する。

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken))
            {
                var token = linked.Token;
                try
                {
                    // 事前合成では AudioClip を作らないので、キャッシュにあるかどうかだけ確かめる
                    // （200MB のキャッシュを全部読み直さないため）。
                    if (forCache)
                    {
                        var hit = await Task.Run(() => Cache.Contains(key), token).ConfigureAwait(false);
                        if (hit) return new TtsSynthesisOutcome(default, fromCache: true, key: key);
                    }
                    else
                    {
                        var cached = await Task.Run(() => TryLoadFromCache(key), token).ConfigureAwait(false);
                        if (cached.HasValue) return new TtsSynthesisOutcome(cached.Value, fromCache: true, key: key);
                    }

                    // 同じ読みの合成が同時に走らないよう、進行中のタスクがあればそれに相乗りする
                    // （ホストの事前合成とクライアントの要求が重なるケース。docs/tts.md §7.4）。
                    var work = _inFlight.GetOrAdd(
                        key,
                        k => new Lazy<Task<WavData>>(
                            () => SynthesizeAndCacheAsync(engine, readingText, clamped, k),
                            LazyThreadSafetyMode.ExecutionAndPublication));

                    var parsed = await WithCancellation(work.Value, token).ConfigureAwait(false);
                    return new TtsSynthesisOutcome(parsed, fromCache: false, key: key);
                }
                catch (OutOfMemoryException e)
                {
                    return await RecoverFromOutOfMemoryAsync(engine, readingText, clamped, key, token, e)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 呼び出し側のトークンによる取り消しはそのまま伝える。
                    // 終了処理（_lifetime）による取り消しは「読み上げなし」に落とす。
                    if (cancellationToken.IsCancellationRequested) throw;
                    return null;
                }
                catch (WavFormatException e)
                {
                    Cache.Invalidate(key);
                    LogSynthesisFailure(key, "WAV の解析", e.GetType().Name, e.Message, e.ToString());
                    return null;
                }
                catch (Exception e)
                {
                    LogSynthesisFailure(key, "音声合成", e.GetType().Name, e.Message, e.ToString());
                    return null;
                }
            }
        }

        /// <summary>
        /// 実際に合成してキャッシュへ保存する。<see cref="_inFlight"/> に載せて共有するため、
        /// <b>呼び出し側個々の <see cref="CancellationToken"/> は使わない</b>
        /// （相乗りしている他の呼び出しまで巻き添えで止めないため）。
        /// サービスの終了（<see cref="_lifetime"/>）だけで止まる。
        /// </summary>
        private async Task<WavData> SynthesizeAndCacheAsync(
            ITtsSynthesisEngine engine, string readingText, float speed, string key)
        {
            try
            {
                var lifetimeToken = _lifetime?.Token ?? CancellationToken.None;
                var wav = await engine.SynthesizeAsync(readingText, speed, lifetimeToken).ConfigureAwait(false);

                // ここはワーカースレッド（ConfigureAwait(false) で継続しているため）。
                // 解析とキャッシュ保存もメインスレッドに戻さずここで行う。
                var parsed = WavParser.ParseWav(wav);
                Cache.Put(key, wav, parsed.DurationSec, engine.SpeakerName, engine.StyleName);
                return parsed;
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 共有タスク（<see cref="_inFlight"/>）を、呼び出し側のトークンで待つ。
        /// 取り消してもタスク自体は止めない（他の呼び出しが待っているため）。
        /// </summary>
        private static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || task.IsCompleted)
            {
                return await task.ConfigureAwait(false);
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(
                state => ((TaskCompletionSource<bool>)state).TrySetResult(true), cancelled))
            {
                var completed = await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false);
                if (completed != task)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            return await task.ConfigureAwait(false);
        }

        /// <summary>
        /// メモリ不足からの回復（docs/tts.md §9）。
        /// <b>キャッシュを全消しして 1 回だけリトライ</b>し、それでも失敗したら読み上げを無効化する。
        /// </summary>
        private async Task<TtsSynthesisOutcome> RecoverFromOutOfMemoryAsync(
            ITtsSynthesisEngine engine, string readingText, float speed, string key,
            CancellationToken token, OutOfMemoryException original)
        {
            if (_cacheClearedForOutOfMemory)
            {
                SetState(
                    TtsServiceState.NotAvailable, "メモリが不足したため読み上げを停止しました。",
                    TtsUnavailableReason.InitializationFailed);
                Debug.LogError("[TtsService] キャッシュを消してもメモリが不足しました。読み上げを無効にします。");
                Debug.LogException(original);
                return null;
            }

            _cacheClearedForOutOfMemory = true;
            Debug.LogWarning("[TtsService] メモリが不足しました。キャッシュを全消しして 1 回だけやり直します。");
            Debug.LogException(original);

            try
            {
                await Task.Run(() => Cache.Clear(), token).ConfigureAwait(false);

                var wav = await engine.SynthesizeAsync(readingText, speed, token).ConfigureAwait(false);
                var parsed = WavParser.ParseWav(wav);
                Cache.Put(key, wav, parsed.DurationSec, engine.SpeakerName, engine.StyleName);
                return new TtsSynthesisOutcome(parsed, fromCache: false, key: key);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception e)
            {
                SetState(
                    TtsServiceState.NotAvailable, "メモリが不足したため読み上げを停止しました。",
                    TtsUnavailableReason.InitializationFailed);
                Debug.LogError("[TtsService] メモリ不足からの再試行にも失敗しました。読み上げを無効にします。");
                Debug.LogException(e);
                return null;
            }
        }

        /// <summary>
        /// 合成失敗のログ（docs/tts.md §9）。<b>エラー行は 1 行だけ</b>にし、
        /// スタックトレースなどの詳細は警告行に分ける。
        ///
        /// どちらの行も <c>[TtsService] 合成に失敗しました</c> で始め、詳細は改行を潰して 1 行に収める。
        /// <c>scripts/verify.ps1</c>（<c>Test-LogHasErrors</c>）がログ中の <c>error</c> / <c>exception</c> を
        /// 失敗として扱うため、<b>テストが意図的に出した失敗ログだけ</b>を行頭タグで除外できるようにするため。
        /// 同じ理由で <c>Debug.LogException</c> は使わず、<b>引数にも <see cref="Exception"/> を取らない</b>
        /// （Unity が付けるスタックトレースにメソッドのシグネチャ "System.Exception" が出てしまい、
        /// それ自体が error/exception 検出に引っかかる）。呼び出し側で型名・メッセージ・詳細に分解して渡すこと。
        ///
        /// ユーザー向け文言に ResultCode の数値やネイティブの原文を出さないこと（docs/tts.md §9）。
        /// </summary>
        private static void LogSynthesisFailure(
            string key, string what, string exceptionTypeName, string message, string detail)
        {
            Debug.LogError(
                $"{SynthesisFailureLogTag}（{what}、key={key}）: {exceptionTypeName}: {message}");
            Debug.LogWarning(
                $"{SynthesisFailureLogTag}（詳細、key={key}）: {Flatten(detail)}");
        }

        /// <summary>複数行の詳細を 1 行に潰す（行頭タグでの除外を効かせるため）。</summary>
        private static string Flatten(string text)
            => string.IsNullOrEmpty(text)
                ? string.Empty
                : text.Replace("\r", " ").Replace("\n", " ");

        private void EnsureMainThread()
        {
            if (_mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId == _mainThreadId) return;

            throw new InvalidOperationException(
                "SynthesizeAsync はメインスレッドから呼んでください（AudioClip.Create がメインスレッド専用のため）。" +
                "ワーカースレッドからは PrefetchAsync を使ってください。");
        }

        /// <summary>キャッシュ照会・合成の結果（<c>AudioClip</c> 化の前段）。生成後は不変。</summary>
        private sealed class TtsSynthesisOutcome
        {
            public TtsSynthesisOutcome(WavData wav, bool fromCache, string key)
            {
                Wav = wav;
                FromCache = fromCache;
                Key = key;
            }

            public WavData Wav { get; }
            public bool FromCache { get; }
            public string Key { get; }
        }
    }
}
