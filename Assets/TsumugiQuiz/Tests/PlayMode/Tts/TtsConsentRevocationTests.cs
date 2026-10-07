using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tests.PlayMode.Network;
using TsumugiQuiz.Tests.Shared.Tts;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using TsumugiQuiz.UI.Views.Settings;
using UnityEngine;
using UnityEngine.TestTools;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// ゲームプレイ中に利用規約の同意を撤回したときの読み上げの挙動（issue #127、
    /// requirements.md FR-74 / FR-75 / NFR-08、docs/tts.md §6.6）を、実プレハブ・実ネットワーク経路で固定する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 配線は本番と同じ <c>GameView.WireTtsSyncPlayer</c>（<c>GameView.Tts.cs</c>。
    /// 実際には <c>GameView.BindCharacterViewToSession</c> がセッション取得時に呼ぶ）を使う。
    /// 合成はフェイクエンジン（<see cref="FakeTtsSynthesisEngine"/>）に差し替えるので、
    /// External（voicevox_core / 辞書 / .vvm）が無い環境でも動く。
    /// </para>
    /// <para>
    /// <b>撤回の効き方</b>: 進行中の再生は完了まで許容し、<b>次の問題から</b>合成しない（統括判断 #127）。
    /// 合成しないクライアントは「配置不足で読み上げられないクライアント」と同じ扱いになり、
    /// 長さ 0 で即 Ready を返すためホストの Ready 待ちを引き延ばさない（docs/tts.md §6.6）。
    /// </para>
    /// </remarks>
    public sealed class TtsConsentRevocationTests : GameSessionTestFixture
    {
        private const int Seed = 20260918;
        private const string FirstReading = "にほんのしゅとはどこ";
        private const string SecondReading = "にほんいちたかいやまは";

        /// <summary>
        /// 1 問目を誰も押さずにタイムアウトさせ、結果表示から 0.5 秒で 2 問目へ自動進行させる制限時間。
        /// 撤回は 1 問目の合成が終わってから行うので、早押しの持ち時間はそのぶんの余裕として使う。
        /// </summary>
        private static QuizTimeLimits FastLimits =>
            new QuizTimeLimits(buzzTimeLimitSec: 3.0, answerTimeLimitSec: 3.0, collectWindowSec: 0.15);

        private ReadingPlaybackTestRig _rig;
        private ConsentFileScope _consentScope;

        /// <summary>購読解除のために覚えておく <c>QuestionShown</c> のハンドラ（レビュー L-6）。</summary>
        private GameSession _questionShownSession;
        private Action<int, QuestionDto, QuestionShownSource> _questionShownHandler;

        [SetUp]
        public void CreateRigAndSeedConsent()
        {
            _rig = new ReadingPlaybackTestRig();

            // 実際の consent.json（テスト用データルート配下、#71）を退避してから同意済み状態を作る。
            _consentScope = ConsentFileScope.Backup();
            ConsentGate.CreateDefaultStore()
                .RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void DisposeRigAndRestoreConsent()
        {
            if (_questionShownSession != null && _questionShownHandler != null)
            {
                _questionShownSession.QuestionShown -= _questionShownHandler;
            }

            _questionShownSession = null;
            _questionShownHandler = null;

            _rig?.Dispose();
            _rig = null;

            _consentScope?.Restore();
            _consentScope = null;
        }

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator 同意を撤回すると次の問題から読み上げを合成しない()
        {
            yield return ConnectHostAndClient(TwoQuestionSource());

            var hostEngine = new FakeTtsSynthesisEngine();
            var clientEngine = new FakeTtsSynthesisEngine();
            // レビュー M-2: TtsService 側の同意ゲートも本番と同じ状態にする（UI 層がアプリ起動時に
            // ConfigureDefaults で登録するもの）。渡さないと TtsService のゲートは素通りのままで、
            // 「TtsSyncPlayer が止めているだけ」の状態を本番と誤認してしまう。
            var consentCheck = TtsConsentCheckFactory.Build();
            var hostService = _rig.CreateService("TtsService(Host)", (_, __) => hostEngine, consentCheck);
            var clientService = _rig.CreateService("TtsService(Client)", (_, __) => clientEngine, consentCheck);

            var hostPlayer = ReadingPlaybackTestRig.AttachPlayer(HostSession, hostService);
            var clientPlayer = ReadingPlaybackTestRig.AttachPlayer(ClientSession, clientService);

            // 本番と同じ配線（GameView がセッション取得時に呼ぶもの、#127）。
            GameView.WireTtsSyncPlayer(hostPlayer);
            GameView.WireTtsSyncPlayer(clientPlayer);

            yield return WaitUntil(
                () => hostService.Status.IsReady && clientService.Status.IsReady,
                () => $"読み上げの初期化が終わりませんでした（host: {hostService.Status}、client: {clientService.Status}）。");

            Assert.IsTrue(clientPlayer.IsReadingPossible, "同意済みなら読み上げられる状態のはず。");

            // レビュー L-6: セッションはフィクスチャが破棄するが、購読は明示的に外す
            // （購読したまま次のテストへ持ち越さない）。
            var shown = new List<int>();
            _questionShownSession = ClientSession;
            _questionShownHandler = (index, _, _) => shown.Add(index);
            ClientSession.QuestionShown += _questionShownHandler;

            // --- 1 問目: 同意済みなので合成される ---
            Assert.IsTrue(HostSession.StartSession(FastSettings(), Seed), "セッションを開始できるはず。");

            yield return WaitUntil(
                () => shown.Count >= 1, () => "1 問目が提示されませんでした。");
            yield return WaitUntil(
                () => hostEngine.SynthesizeCount >= 1 && clientEngine.SynthesizeCount >= 1,
                () => $"1 問目が合成されませんでした（host: {hostEngine.SynthesizeCount}、"
                      + $"client: {clientEngine.SynthesizeCount}）。");

            Assert.AreEqual(FirstReading, clientEngine.LastRequestedText, "1 問目の読みが渡るはず。");

            var hostCount = hostEngine.SynthesizeCount;
            var clientCount = clientEngine.SynthesizeCount;

            // --- 同意を撤回（設定・クレジット画面からの撤回に相当。FR-75） ---
            _consentScope.DeleteCurrentFile();

            Assert.IsFalse(
                clientPlayer.IsReadingPossible,
                "撤回した時点で「読み上げられる見込みなし」に変わるはず（Ready 待ちの対象から外れる）。");
            Assert.IsFalse(hostPlayer.IsReadingPossible, "ホストも同じ判定になるはず。");

            // --- 2 問目: 配信の前に撤回済みなので、TtsSyncCoordinator がスキップ理由をログに残す（issue #164） ---
            // ホスト・クライアントの両方の TtsSyncCoordinator が出す（LogAssert.Expect は 1 件揃えば満たされる）。
            // Network 層は Tts 層を参照できないため、理由は「未同意・読み上げ無効・準備未完了のいずれか」に
            // まとめている（区別しない理由は TtsSyncCoordinator.Rpc.cs の DescribeSkipReason を参照）。
            LogAssert.Expect(
                LogType.Log,
                "[TtsSyncCoordinator] 問題 1 は読み上げません（未同意・読み上げ無効・準備未完了のいずれか）。");

            // --- 2 問目（1 問目のタイムアウト → 自動進行）: 合成されない ---
            yield return WaitUntil(
                () => shown.Count >= 2,
                () => $"2 問目へ自動進行しませんでした（提示 {shown.Count} 件 / "
                      + $"問題 {ClientSession.QuestionIndex.Value} / フェーズ {ClientSession.Phase.Value}）。");

            // 合成は非同期なので、提示の直後だけでなく数フレーム進めてから確かめる。
            yield return WaitFrames(30);

            Assert.AreEqual(
                clientCount, clientEngine.SynthesizeCount,
                "撤回後に提示された問題は合成しないこと（FR-74 / FR-75、#127）。");
            Assert.AreEqual(
                hostCount, hostEngine.SynthesizeCount, "ホスト自身も合成しないこと。");
            Assert.AreNotEqual(
                SecondReading, clientEngine.LastRequestedText, "2 問目の読みがエンジンへ渡っていないこと。");

            // --- 同意し直せば次の問題から読み上げに戻る（撤回でルーム設定を潰していないこと） ---
            ConsentGate.CreateDefaultStore()
                .RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);

            Assert.IsTrue(clientPlayer.IsReadingPossible, "同意し直せば読み上げに戻るはず（FR-75）。");
        }

        private static SessionSettings FastSettings() =>
            new SessionSettings(
                new QuestionSelectionSettings(count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false),
                FastLimits,
                ScoringSettings.Default.WithReopenAfterWrongAnswer(false),
                resultAutoAdvanceSec: 0.5);

        private static IQuestionSource TwoQuestionSource() =>
            new TestQuestionSource(
                TestQuestionSource.FreeText(
                    "q-consent-1", QuestionText, FirstReading, new[] { "地理" }, 2, CorrectAnswer),
                TestQuestionSource.FreeText(
                    "q-consent-2", "日本でいちばん高い山は？", SecondReading, new[] { "地理" }, 2, "ふじさん"));
    }
}
