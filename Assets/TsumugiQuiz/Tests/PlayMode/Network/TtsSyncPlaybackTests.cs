using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Tests.PlayMode.Tts;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 読み上げの同期再生（Ready 通知 → <c>PlayAtRpc</c> → <c>PlayScheduled</c> → 受付開始 T0）を
    /// 実プレハブ・実ネットワーク経路で通す統合テスト（docs/tts.md §6、docs/network.md §7.3 / §8.6、#23）。
    /// </summary>
    /// <remarks>
    /// 合成はフェイクエンジン（<see cref="FakeTtsSynthesisEngine"/>）に差し替える。
    /// External（voicevox_core / 辞書 / .vvm）が無い環境でも動き、
    /// ネイティブ DLL を読み込まないので他の PlayMode テストに影響しない。
    /// 実 DLL を使う 1 件は <c>VoicevoxTtsSyncPlaybackTests</c> にある。
    /// </remarks>
    public sealed class TtsSyncPlaybackTests : GameSessionTestFixture
    {
        private const string ReadingText = "にほんのしゅとはどこ";

        /// <summary>ホスト側の読み上げ長（1.0 秒）。全員に配られる正の値。</summary>
        private const int HostFrameCount = FakeTtsSynthesisEngine.SampleRate;

        /// <summary>クライアント側の読み上げ長（0.5 秒）。ホストの値で上書きされることを確かめる。</summary>
        private const int ClientFrameCount = FakeTtsSynthesisEngine.SampleRate / 2;

        private const double ExpectedHostDurationSec = 1.0d;
        private const double ExpectedClientDurationSec = 0.5d;

        /// <summary>
        /// ホストとクライアントの予約 dsp 時刻のずれとして許容する秒数。
        /// NGO の時刻同期の実際の精度（docs/network.md §7.2: RTT 誤差数 ms〜20ms、tick 粒度 33ms、
        /// クライアントの <c>LocalTime</c> と <c>ServerTime</c> のオフセット）を見込んだ上限。
        /// </summary>
        private const double AllowedScheduleSkewSec = 0.15d;

        /// <summary>タイムアウトの検証で使う短い Ready 待ち上限（秒）。</summary>
        private const double ShortReadyTimeoutSec = 0.5d;

        private ReadingPlaybackTestRig _rig;

        /// <summary>
        /// テスト用 seam（<c>SuppressDistributedQuestionShownForTests</c>）を立てたセッション。
        /// TearDown で必ず戻す（PR #114 再レビュー LOW-7）。使っていないテストでは null のまま。
        /// </summary>
        private GameSession _suppressedQuestionShownSession;

        [SetUp]
        public void CreateRig() => _rig = new ReadingPlaybackTestRig();

        [TearDown]
        public void DisposeRig()
        {
            // テスト用 seam を確実に戻す（PR #114 再レビュー LOW-7）。
            if (_suppressedQuestionShownSession != null)
            {
                _suppressedQuestionShownSession.SuppressDistributedQuestionShownForTests = false;
                _suppressedQuestionShownSession = null;
            }

            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>
        /// 全員 Ready → <c>PlayAtRpc</c> → 両者が同じサーバー時刻を指す予約を作り、
        /// 受付開始 T0 が <c>playAtServerTime</c> になること。
        /// </summary>
        [UnityTest]
        public IEnumerator Reading_WhenEveryoneIsReady_SchedulesTheSameServerTimeAndOpensBuzzAtPlayAt()
        {
            yield return ConnectHostAndClient(CreateQuestionSource());

            var hostEngine = new FakeTtsSynthesisEngine(HostFrameCount, "0.17.0-fake-host");
            var clientEngine = new FakeTtsSynthesisEngine(ClientFrameCount, "0.17.0-fake-client");
            var hostService = _rig.CreateService("TtsService(Host)", (_, __) => hostEngine);
            var clientService = _rig.CreateService("TtsService(Client)", (_, __) => clientEngine);

            var hostPlayer = ReadingPlaybackTestRig.AttachPlayer(HostSession, hostService);
            var clientPlayer = ReadingPlaybackTestRig.AttachPlayer(ClientSession, clientService);
            var hostCoordinator = ReadingPlaybackTestRig.GetCoordinator(HostSession);
            var clientCoordinator = ReadingPlaybackTestRig.GetCoordinator(ClientSession);

            yield return WaitUntil(
                () => hostService.Status.IsReady && clientService.Status.IsReady,
                () => $"読み上げの初期化が終わりませんでした（host: {hostService.Status}、client: {clientService.Status}）。");

            var roundQuestionIndex = -1;
            var roundTimedOut = true;
            var roundCompleted = false;
            hostCoordinator.ReadyRoundCompleted += (questionIndex, timedOut) =>
            {
                roundQuestionIndex = questionIndex;
                roundTimedOut = timedOut;
                roundCompleted = true;
            };

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => hostPlayer.IsReadingScheduled && clientPlayer.IsReadingScheduled,
                () => "ホスト・クライアントの両方で読み上げが予約されませんでした"
                      + $"（host: {hostPlayer.IsReadingScheduled}、client: {clientPlayer.IsReadingScheduled}、"
                      + $"host duration: {hostCoordinator.LastDurationSec}）。");

            Assert.IsTrue(roundCompleted, "Ready 待ちが終わっているはず。");
            Assert.AreEqual(0, roundQuestionIndex);
            Assert.IsFalse(roundTimedOut, "全員が Ready を返すのでタイムアウトしない。");
            Assert.IsFalse(hostCoordinator.ReadyTracker.IsAwaiting, "Ready 待ちは終わっているはず。");

            // 再生開始時刻は全員同じ値が配られる。
            Assert.AreEqual(0, hostCoordinator.LastReadingQuestionIndex);
            Assert.AreEqual(0, clientCoordinator.LastReadingQuestionIndex);
            Assert.AreEqual(
                hostCoordinator.LastPlayAtServerTime,
                clientCoordinator.LastPlayAtServerTime,
                1e-9,
                "playAtServerTime はサーバーが決めた 1 つの値を配る。");

            // 読み上げ時間はホストの合成結果を正とする（docs/tts.md §6.2）。
            Assert.AreEqual(
                ExpectedClientDurationSec, clientPlayer.PreparedDurationSec, 1e-6,
                "クライアント自身の合成結果は 0.5 秒（前提の確認）。");
            Assert.AreEqual(ExpectedHostDurationSec, hostCoordinator.LastDurationSec, 1e-6);
            Assert.AreEqual(
                ExpectedHostDurationSec, clientCoordinator.LastDurationSec, 1e-6,
                "クライアントにもホストの長さが配られる。");

            // 予約は両者とも「まだ未来」で、同じサーバー時刻（= ほぼ同じ dsp 時刻）を指す。
            var hostSchedule = hostPlayer.LastSchedule;
            var clientSchedule = clientPlayer.LastSchedule;
            Assert.IsTrue(hostSchedule.ShouldPlay, "ホストは再生するはず。");
            Assert.IsTrue(clientSchedule.ShouldPlay, "クライアントも再生するはず。");
            Assert.IsFalse(
                hostSchedule.PlayImmediately,
                $"ホストは lead {TtsSyncCoordinator.DefaultLeadTimeSec} 秒ぶん先に予約できるはず（{hostSchedule}）。");

            var skewSec = Math.Abs(hostSchedule.DspStartTime - clientSchedule.DspStartTime);
            Debug.Log(
                $"[TtsSyncPlaybackTests] 予約 dsp: host={hostSchedule.DspStartTime:F4} "
                + $"client={clientSchedule.DspStartTime:F4} ずれ={skewSec * 1000d:F1}ms "
                + $"(host lead={hostSchedule.LeadSec:F4}s client lead={clientSchedule.LeadSec:F4}s)");
            Assert.Less(
                skewSec,
                AllowedScheduleSkewSec,
                "両者の予約が同じサーバー時刻を指していれば、dsp 時刻のずれは時刻同期の精度に収まる。");

            // 受付開始 T0 = playAtServerTime（buzz.allowDuringReading = true、docs/network.md §8.6）。
            var playAtServerTime = hostCoordinator.LastPlayAtServerTime;
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"早押し受付が開きませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.IsFalse(HostDistributor.IsAwaitingAck, "配信の受信確認は先に揃っているはず。");
            Assert.AreEqual(
                playAtServerTime,
                HostSession.BuzzOpenServerTime.Value,
                1e-6,
                "読み上げ開始と同時に受付を開く（T0 = playAtServerTime）。");
            Assert.AreEqual(
                playAtServerTime,
                ClientSession.BuzzOpenServerTime.Value,
                1e-6,
                "T0 はクライアントにも同期される。");
        }

        /// <summary>
        /// <c>TtsSyncPlayer.ReadingStarted</c>（issue #24 で追加）が、
        /// <c>Schedule</c> → <c>StartPlayback</c> の実際の再生開始時にちょうど 1 回だけ発火すること
        /// （レビュー M3）。立ち絵（<c>CharacterView</c>）が読み上げ中の表示に切り替える起点になるイベント。
        /// </summary>
        [UnityTest]
        public IEnumerator Reading_WhenScheduled_RaisesReadingStartedExactlyOnce()
        {
            yield return ConnectHostAndClient(CreateQuestionSource());

            var hostService = _rig.CreateService("TtsService(Host)", (_, __) => new FakeTtsSynthesisEngine(HostFrameCount));
            var hostPlayer = ReadingPlaybackTestRig.AttachPlayer(HostSession, hostService);

            yield return WaitUntil(
                () => hostService.Status.IsReady,
                () => $"読み上げの初期化が終わりませんでした（{hostService.Status}）。");

            var readingStartedCount = 0;
            var readingStartedQuestionIndex = -1;
            hostPlayer.ReadingStarted += index =>
            {
                readingStartedCount++;
                readingStartedQuestionIndex = index;
            };

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => hostPlayer.IsReadingScheduled,
                () => "読み上げが予約されませんでした。");

            Assert.AreEqual(
                1, readingStartedCount,
                "ReadingStarted は Schedule → StartPlayback で実際に再生を開始したときに 1 回だけ発火するはず。");
            Assert.AreEqual(0, readingStartedQuestionIndex);
        }

        /// <summary>
        /// 読み上げが実際に予約・再生されている最中に <c>CancelReading()</c> を呼ぶと、
        /// 中断されたことを伝えるため <c>ReadingCompleted</c> がちょうど 1 回発火すること（レビュー M4/M-d）。
        /// 次の問題の準備開始などで前問の読み上げが打ち切られた場合に、立ち絵（<c>CharacterView</c>）が
        /// 「読み上げ中」のまま残らないようにするための挙動。
        /// </summary>
        [UnityTest]
        public IEnumerator Reading_InProgress_CancelReading_RaisesReadingCompletedExactlyOnce()
        {
            yield return ConnectHostAndClient(CreateQuestionSource());

            var hostService = _rig.CreateService("TtsService(Host)", (_, __) => new FakeTtsSynthesisEngine(HostFrameCount));
            var hostPlayer = ReadingPlaybackTestRig.AttachPlayer(HostSession, hostService);

            yield return WaitUntil(
                () => hostService.Status.IsReady,
                () => $"読み上げの初期化が終わりませんでした（{hostService.Status}）。");

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => hostPlayer.IsReadingScheduled,
                () => "読み上げが予約されませんでした。");

            var readingCompletedCount = 0;
            hostPlayer.ReadingCompleted += _ => readingCompletedCount++;

            hostPlayer.CancelReading();

            Assert.AreEqual(
                1, readingCompletedCount,
                "読み上げ中の CancelReading() は中断を ReadingCompleted で 1 回だけ通知するはず。");
            Assert.IsFalse(hostPlayer.IsReadingScheduled, "CancelReading 後は予約が解除されているはず。");
        }

        /// <summary>
        /// 何も準備・予約していない状態（前問が既に完了している、まだ何も始まっていない等）で
        /// <c>CancelReading()</c> を呼んでも <c>ReadingCompleted</c> は発火しないこと（レビュー M4/M-d）。
        /// <c>PrepareAsync</c> の冒頭で毎回 <c>CancelReading()</c> を呼ぶため、何も進行していない
        /// 通常のケース（1 問目の準備開始等）で不要なイベントが発火しないことを確認する。
        /// </summary>
        [UnityTest]
        public IEnumerator NothingInProgress_CancelReading_DoesNotRaiseReadingCompleted()
        {
            yield return ConnectHostAndClient(CreateQuestionSource());

            var hostService = _rig.CreateService("TtsService(Host)", (_, __) => new FakeTtsSynthesisEngine(HostFrameCount));
            var hostPlayer = ReadingPlaybackTestRig.AttachPlayer(HostSession, hostService);

            var readingCompletedCount = 0;
            hostPlayer.ReadingCompleted += _ => readingCompletedCount++;

            // まだ何も準備・予約していない（PrepareAsync/Schedule を一度も呼んでいない）状態。
            hostPlayer.CancelReading();
            hostPlayer.CancelReading(); // 連続で呼んでも同様に発火しないことも確認する。

            Assert.AreEqual(
                0, readingCompletedCount,
                "何も進行していない状態での CancelReading() は ReadingCompleted を発火しないはず。");
        }

        /// <summary>
        /// <c>tts.enabled = false</c>（サーバー側の読み上げ無効）では読み上げを配らず、
        /// 配信完了と同時に受付が開くこと（Ready 待ちで待たされない）。
        /// </summary>
        [UnityTest]
        public IEnumerator ReadingDisabled_SkipsSynchronizationAndOpensBuzzWithoutWaiting()
        {
            yield return ConnectHostAndClient(CreateQuestionSource());

            var hostService = _rig.CreateService(
                "TtsService(Host)", (_, __) => new FakeTtsSynthesisEngine(HostFrameCount));
            var hostPlayer = ReadingPlaybackTestRig.AttachPlayer(HostSession, hostService);
            var hostCoordinator = ReadingPlaybackTestRig.GetCoordinator(HostSession);
            var clientCoordinator = ReadingPlaybackTestRig.GetCoordinator(ClientSession);

            yield return WaitUntil(
                () => hostService.Status.IsReady,
                () => $"読み上げの初期化が終わりませんでした（{hostService.Status}）。");

            // TODO(#26): ルーム設定 tts.enabled と接続する。
            Assert.IsTrue(hostCoordinator.SetReadingEnabled(false), "サーバーなので読み上げを無効にできる。");
            yield return WaitUntil(
                () => !clientCoordinator.ReadingEnabled.Value,
                () => "読み上げの ON / OFF がクライアントへ同期されませんでした。");

            var startServerTime = HostManager.ServerTime.Time;
            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"早押し受付が開きませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.IsFalse(hostCoordinator.ReadyTracker.IsAwaiting, "Ready 待ちは行わない。");
            Assert.AreEqual(
                TtsReadyTracker.NoQuestionIndex,
                hostCoordinator.LastReadingQuestionIndex,
                "読み上げの再生開始時刻を配らない。");
            Assert.AreEqual(TtsReadyTracker.NoQuestionIndex, clientCoordinator.LastReadingQuestionIndex);
            Assert.IsFalse(hostPlayer.IsReadingScheduled, "読み上げは予約されない。");
            Assert.Less(
                HostSession.BuzzOpenServerTime.Value,
                startServerTime + TtsSyncCoordinator.DefaultReadyTimeoutSec,
                "読み上げ無効なら Ready のタイムアウトを待たずに受付が開く。");
        }

        /// <summary>
        /// 読み上げを使えないクライアント（<c>tts.enabled=false</c> / 未同意 / 配置不足）が居ても
        /// 即座に Ready を返すのでタイムアウトせず、配られる長さはホストの値になること。
        /// </summary>
        [UnityTest]
        public IEnumerator ClientWithoutReading_ReportsReadyImmediatelyAndUsesHostDuration()
        {
            yield return ConnectHostAndClient(CreateQuestionSource());

            var hostService = _rig.CreateService(
                "TtsService(Host)", (_, __) => new FakeTtsSynthesisEngine(HostFrameCount));
            var clientService = _rig.CreateService(
                "TtsService(Client)", (_, __) => new FakeTtsSynthesisEngine(ClientFrameCount));

            var hostPlayer = ReadingPlaybackTestRig.AttachPlayer(HostSession, hostService);
            var clientPlayer = ReadingPlaybackTestRig.AttachPlayer(ClientSession, clientService);
            var hostCoordinator = ReadingPlaybackTestRig.GetCoordinator(HostSession);
            var clientCoordinator = ReadingPlaybackTestRig.GetCoordinator(ClientSession);

            yield return WaitUntil(
                () => hostService.Status.IsReady,
                () => $"読み上げの初期化が終わりませんでした（{hostService.Status}）。");

            // このクライアントだけ読み上げを行わない（設定・同意で無効化された状態と同じ）。
            clientService.ReadingEnabled = false;

            var roundTimedOut = true;
            var roundCompleted = false;
            hostCoordinator.ReadyRoundCompleted += (_, timedOut) =>
            {
                roundTimedOut = timedOut;
                roundCompleted = true;
            };

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => roundCompleted,
                () => "Ready 待ちが終わりませんでした。");

            Assert.IsFalse(
                roundTimedOut,
                "読み上げを行わないクライアントも即座に Ready を返すので、タイムアウトしない。");
            Assert.AreEqual(
                ExpectedHostDurationSec, clientCoordinator.LastDurationSec, 1e-6,
                "読み上げなしのクライアントにもホストの長さが配られる（0 では上書きしない）。");
            Assert.IsTrue(hostPlayer.IsReadingScheduled, "ホストは読み上げを再生する。");
            Assert.IsFalse(clientPlayer.IsReadingScheduled, "読み上げを行わないクライアントは再生しない。");

            var playAtServerTime = hostCoordinator.LastPlayAtServerTime;
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"早押し受付が開きませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.AreEqual(
                playAtServerTime, HostSession.BuzzOpenServerTime.Value, 1e-6,
                "T0 は読み上げ開始時刻（docs/network.md §8.6）。");
        }

        /// <summary>
        /// <c>buzz.allowDuringReading = false</c> のときは、読み上げ完了時刻が T0 になること
        /// （docs/network.md §7.3 の <c>readingEndServerTime</c>）。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenBuzzIsNotAllowedDuringReading_BuzzOpensAtReadingEnd()
        {
            yield return ConnectHostAndClient(CreateQuestionSource());

            var hostService = _rig.CreateService(
                "TtsService(Host)", (_, __) => new FakeTtsSynthesisEngine(HostFrameCount));
            var clientService = _rig.CreateService(
                "TtsService(Client)", (_, __) => new FakeTtsSynthesisEngine(HostFrameCount));
            ReadingPlaybackTestRig.AttachPlayer(HostSession, hostService);
            ReadingPlaybackTestRig.AttachPlayer(ClientSession, clientService);
            var hostCoordinator = ReadingPlaybackTestRig.GetCoordinator(HostSession);

            yield return WaitUntil(
                () => hostService.Status.IsReady && clientService.Status.IsReady,
                () => $"読み上げの初期化が終わりませんでした（host: {hostService.Status}、client: {clientService.Status}）。");

            // TODO(#26): ルーム設定 buzz.allowDuringReading と接続する。
            hostCoordinator.AllowBuzzDuringReading = false;

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => hostCoordinator.LastReadingQuestionIndex == 0,
                () => "読み上げの再生開始時刻が配られませんでした。");

            var expectedT0 = hostCoordinator.LastPlayAtServerTime + hostCoordinator.LastDurationSec;
            Assert.AreEqual(
                ExpectedHostDurationSec, hostCoordinator.LastDurationSec, 1e-6,
                "読み上げ時間はホストの合成結果（前提の確認）。");

            yield return WaitUntil(
                () => HostSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"早押し受付が開きませんでした（現在: {HostSession.Phase.Value}）。");

            Assert.AreEqual(
                expectedT0, HostSession.BuzzOpenServerTime.Value, 1e-6,
                "読み上げ完了時刻（playAt + durationSec）が T0 になる。");
        }

        /// <summary>
        /// Ready を返さないクライアントが居ても、タイムアウトで読み上げを始め、
        /// T0 = <c>playAtServerTime</c> になること（docs/tts.md §6.2 の readyTimeoutMs）。
        /// </summary>
        [UnityTest]
        public IEnumerator WhenAClientNeverReportsReady_TimesOutThenStillSchedulesReading()
        {
            yield return ConnectHostAndClient(CreateQuestionSource());

            var hostService = _rig.CreateService(
                "TtsService(Host)", (_, __) => new FakeTtsSynthesisEngine(HostFrameCount));
            var hostPlayer = ReadingPlaybackTestRig.AttachPlayer(HostSession, hostService);
            var hostCoordinator = ReadingPlaybackTestRig.GetCoordinator(HostSession);
            var clientCoordinator = ReadingPlaybackTestRig.GetCoordinator(ClientSession);

            // このクライアントは合成が終わらない（Ready を返さない）。
            var neverReady = new NeverReadyReadingPlayback();
            clientCoordinator.SetPlayback(neverReady);

            yield return WaitUntil(
                () => hostService.Status.IsReady,
                () => $"読み上げの初期化が終わりませんでした（{hostService.Status}）。");

            // タイムアウトを短くして待ち時間を詰める（既定は 3 秒）。
            hostCoordinator.ReadyTimeoutSec = ShortReadyTimeoutSec;

            var roundTimedOut = false;
            var roundCompleted = false;
            hostCoordinator.ReadyRoundCompleted += (_, timedOut) =>
            {
                roundTimedOut = timedOut;
                roundCompleted = true;
            };

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => roundCompleted,
                () => "Ready 待ちがタイムアウトで終わりませんでした。");

            Assert.IsTrue(roundTimedOut, "Ready を返さないクライアントが居るのでタイムアウトするはず。");
            Assert.AreEqual(1, neverReady.PrepareCallCount, "クライアントは合成を始めたまま終わっていない。");

            var playAtServerTime = hostCoordinator.LastPlayAtServerTime;
            Assert.Greater(playAtServerTime, 0d, "タイムアウトでも再生開始時刻は配る。");
            Assert.IsTrue(hostPlayer.IsReadingScheduled, "ホストは読み上げを予約する。");

            if (HostManager.ServerTime.Time < playAtServerTime)
            {
                // 受付開始 T0 の NetworkVariable はフェーズが動いたときに publish されるため、
                // ここではサーバー側の進行（フェーズ）だけを見る。
                Assert.AreEqual(
                    QuizPhase.Reading, HostSession.ServerPhase,
                    "playAtServerTime までは Reading に留まる（受付は開かない）。");
            }

            // 間に合わなかったクライアントにも再生開始時刻は届く（合成が終わり次第そこから再生する）。
            yield return WaitUntil(
                () => neverReady.ScheduleCallCount > 0,
                () => "Ready を返さないクライアントに再生開始時刻が届きませんでした。");
            Assert.AreEqual(playAtServerTime, neverReady.LastPlayAtServerTime, 1e-9);
            Assert.AreEqual(ExpectedHostDurationSec, neverReady.LastDurationSec, 1e-6, "長さはホストの値。");

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"早押し受付が開きませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.AreEqual(
                playAtServerTime, HostSession.BuzzOpenServerTime.Value, 1e-6,
                "タイムアウトでも T0 = playAtServerTime。");
            Assert.AreEqual(playAtServerTime, ClientSession.BuzzOpenServerTime.Value, 1e-6);
        }

        /// <summary>
        /// 途中参加・再接続の再同期（#109）で受け取った現在問は<b>読み上げない</b>こと
        /// （PR #114 レビュー M-2、統括判断。docs/tts.md §6.7）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 再同期は「いま出題中の問題」を後から 1 人へ送り直すもので、合流した時点で読み上げは
        /// 既に進んでいる（あるいは終わっている）。ここで合成を始めると、同期して鳴らせない音声のために
        /// <c>TtsReadyRpc</c> を送ってしまい、待っていないサーバーが棄却ログを出す。
        /// </para>
        /// <para>
        /// 「提示の合図（<c>QuestionShownRpc</c>）を受け取っていないクライアント」は
        /// <c>SuppressDistributedQuestionShownForTests</c> で決定的に作る。通常配信の発火だけを止め、
        /// 再同期の経路はそのまま通すので、合成が走ればそれは再同期由来だと言い切れる。
        /// ホスト側は抑止していないので、読み上げ自体が有効に動いていることの対照になる。
        /// </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator Reading_IsNotSynthesized_WhenTheQuestionArrivesByResync()
        {
            yield return ConnectHostAndClient(CreateQuestionSource());

            var hostEngine = new FakeTtsSynthesisEngine(HostFrameCount, "0.17.0-fake-host");
            var clientEngine = new FakeTtsSynthesisEngine(ClientFrameCount, "0.17.0-fake-client");
            var hostService = _rig.CreateService("TtsService(Host)", (_, __) => hostEngine);
            var clientService = _rig.CreateService("TtsService(Client)", (_, __) => clientEngine);

            ReadingPlaybackTestRig.AttachPlayer(HostSession, hostService);
            ReadingPlaybackTestRig.AttachPlayer(ClientSession, clientService);

            yield return WaitUntil(
                () => hostService.Status.IsReady && clientService.Status.IsReady,
                () => $"読み上げの初期化が終わりませんでした（host: {hostService.Status}、client: {clientService.Status}）。");

            // 提示の合図を受け取れなかったクライアント（＝ 出題より後に合流した状態）を作る。
            // 後始末は TearDown（DisposeRig）が行う（PR #114 再レビュー LOW-7）。
            _suppressedQuestionShownSession = ClientSession;
            ClientSession.SuppressDistributedQuestionShownForTests = true;

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            // ホスト側は通常どおり合成する（読み上げが有効であることの確認）。
            yield return WaitUntil(
                () => hostEngine.SynthesizeCount >= 1,
                () => "ホスト側で読み上げの合成が始まりませんでした。");

            // クライアントへ DTO は届いている（届かないと再同期で QuestionShown を発火できない）。
            yield return WaitUntil(
                () => ClientDistributor.TryGetQuestion(0, out _),
                () => "クライアントへ問題データが配信されませんでした。");
            Assert.AreEqual(
                0, clientEngine.SynthesizeCount, "提示の合図を受けていないので、まだ合成していないはず。");

            // 再同期で現在問を受け取る（QuestionShownSource.Resync で発火する）。
            Assert.IsTrue(
                HostSession.ResyncClient(ClientManager.LocalClientId), "接続中のクライアントへ再同期できるはず。");

            yield return WaitUntil(
                () => ClientSession.QuestionIndex.Value == 0 && ClientDistributor.TryGetQuestion(0, out _),
                () => "再同期の内容がクライアントへ届きませんでした。");

            // 再同期の RPC が届いたあと、（合成するなら）合成が始まるだけの猶予を置いて確認する。
            var deadline = Time.realtimeSinceStartupAsDouble + 1.0d;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                Assert.AreEqual(
                    0,
                    clientEngine.SynthesizeCount,
                    "再同期で受け取った問題は読み上げないので、合成は走らないはず（#109 統括判断）。");
                yield return null;
            }
        }

        private static IQuestionSource CreateQuestionSource()
        {
            return new TestQuestionSource(
                TestQuestionSource.FreeText(
                    "q-1", QuestionText, ReadingText, new[] { "地理" }, 2, CorrectAnswer));
        }
    }
}
