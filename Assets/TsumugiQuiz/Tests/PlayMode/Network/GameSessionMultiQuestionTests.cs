using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 複数問の進行（出題フィルタ・出題数・自動進行・タイムアウト・全問終了、#19）を
    /// 実ネットワーク経路で通す統合テスト（docs/network.md §6.6 / §10.2）。
    /// </summary>
    public class GameSessionMultiQuestionTests : GameSessionTestFixture
    {
        private const int Seed = 20260913;

        /// <summary>選択式の再同期テスト（PR #123 レビュー M-1）で使う問題。</summary>
        private const string ChoiceQuestionId = "q-choice-resync";
        private const string ChoiceQuestionText = "選択式の出題中に合流したときの問題";

        /// <summary>
        /// 3 問を現実的な時間で流すための制限時間。押下・回答を挟む問題でも
        /// 実行環境の遅さで時間切れにならないよう、早押し・回答とも 5 秒を確保する
        /// （タイムアウトを見る問題はこの 5 秒を待つ）。
        /// </summary>
        private static QuizTimeLimits FastLimits =>
            new QuizTimeLimits(buzzTimeLimitSec: 5.0, answerTimeLimitSec: 5.0, collectWindowSec: 0.15);

        [UnityTest]
        public IEnumerator ThreeQuestionSession_AdvancesAutomaticallyAndFinishesWithFinalScores()
        {
            yield return ConnectHostAndClient(ThreeQuestionSource());

            var hostSession = HostSession;
            var clientSession = ClientSession;
            var clientId = ClientManager.LocalClientId;

            var shown = new List<int>();
            clientSession.QuestionShown += (index, _, _) => shown.Add(index);

            var results = new List<(QuizJudgement Judgement, ulong ClientId, int Score, int Delta)>();
            clientSession.QuestionResolved += (judgement, answerer, _, score, delta)
                => results.Add((judgement, answerer, score, delta));

            var finalScores = new List<IReadOnlyList<ScoreEntry>>();
            clientSession.SessionFinished += entries => finalScores.Add(entries);

            // --- セッション開始（全 3 問・シャッフル無し・0.5 秒で自動進行） ---
            Assert.IsTrue(hostSession.StartSession(FastSettings(), Seed), "セッションを開始できるはず。");

            yield return WaitUntil(
                () => clientSession.TotalQuestions.Value == 3,
                () => $"総問題数が同期されませんでした（{clientSession.TotalQuestions.Value}）。");

            // --- 1 問目: 正解 ---
            yield return WaitUntil(
                () => shown.Count >= 1,
                () => $"1 問目が提示されませんでした（提示 {shown.Count} 件）。");
            yield return BuzzAndAnswer(clientSession, "とうきょう");

            yield return WaitUntil(
                () => results.Count >= 1,
                () => "1 問目の結果が届きませんでした。");
            Assert.AreEqual(QuizJudgement.Correct, results[0].Judgement);
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, results[0].Delta);

            // --- 自動進行で 2 問目へ（result.autoAdvanceSec） ---
            yield return WaitUntil(
                () => shown.Count >= 2,
                () => $"2 問目へ自動進行しませんでした（提示 {shown.Count} 件 / "
                    + $"問題 {clientSession.QuestionIndex.Value} / フェーズ {clientSession.Phase.Value}）。");

            // --- 2 問目: 誰も押さずにタイムアウト ---
            yield return WaitUntil(
                () => results.Count >= 2,
                () => $"2 問目のタイムアウトが届きませんでした（結果 {results.Count} 件）。");
            Assert.AreEqual(QuizJudgement.TimedOut, results[1].Judgement, "誰も押さなければタイムアウト。");
            Assert.AreEqual(GameSession.NoClientId, results[1].ClientId, "回答者は居ない。");
            Assert.AreEqual(0, results[1].Delta, "タイムアウトでは得点が動かない。");

            // --- 3 問目: 誤答（再開放しない設定なのでそのまま結果へ） ---
            yield return WaitUntil(
                () => shown.Count >= 3,
                () => $"3 問目へ自動進行しませんでした（提示 {shown.Count} 件 / "
                    + $"問題 {clientSession.QuestionIndex.Value} / フェーズ {clientSession.Phase.Value}）。");
            yield return BuzzAndAnswer(clientSession, "まちがい");

            yield return WaitUntil(
                () => results.Count >= 3,
                () => $"3 問目の結果が届きませんでした（結果 {results.Count} 件）。");
            Assert.AreEqual(QuizJudgement.Wrong, results[2].Judgement);

            // --- 全問終了と最終得点 ---
            yield return WaitUntil(
                () => finalScores.Count >= 1,
                () => $"最終得点の RPC が届きませんでした（{finalScores.Count} 件）。");

            Assert.AreEqual(QuizPhase.Finished, hostSession.ServerPhase, "全問終了で Finished になるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.Finished,
                () => $"クライアントに Finished が届きませんでした（{clientSession.Phase.Value}）。");

            var entries = finalScores[0];
            Assert.AreEqual(1, entries.Count, "回答したのはクライアント 1 人だけ。");
            Assert.AreEqual(clientId, entries[0].ClientId);
            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                entries[0].Score,
                "最終得点は 1 問目の正解ぶんだけ（既定では誤答の減点なし）。");
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, clientSession.GetScore(clientId), "得点表とも一致するはず。");

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, shown, "3 問が順に提示されるはず。");

            // 追加の自動進行が走って Finished から動いていないこと。
            yield return WaitFrames(20);
            Assert.AreEqual(QuizPhase.Finished, hostSession.ServerPhase);
            Assert.AreEqual(1, finalScores.Count, "最終得点は 1 度だけ配る。");
        }

        [UnityTest]
        public IEnumerator StartSession_AppliesFilterAndCountAndShuffle()
        {
            yield return ConnectHostAndClient(TaggedQuestionSource());

            var hostSession = HostSession;
            var clientSession = ClientSession;

            var shownQuestions = new List<(int Index, string Text)>();
            clientSession.QuestionShown += (index, dto, _) => shownQuestions.Add((index, dto.Text));

            // 「地理」タグの freeText を 2 問だけ、シャッフルあり（シード固定）で出題する。
            var questions = new QuestionSelectionSettings(
                typeFilter: QuestionTypeFilter.FreeText,
                tagFilter: new[] { "地理" },
                count: 2,
                shuffleOrder: true);
            var settings = new SessionSettings(
                questions,
                FastLimits,
                ScoringSettings.Default,
                SessionSettings.ManualAdvance);

            Assert.IsTrue(hostSession.StartSession(settings, Seed), "セッションを開始できるはず。");

            yield return WaitUntil(
                () => clientSession.TotalQuestions.Value == 2,
                () => $"出題数（questions.count）が反映されませんでした（{clientSession.TotalQuestions.Value}）。");

            yield return WaitUntil(
                () => shownQuestions.Count == 1,
                () => "1 問目が提示されませんでした。");

            // 同じ設定・同じシードなら QuestionSelector と同じ並びになるはず（決定的なシャッフル）。
            var expected = QuestionSelector.Select(GeoAndHistoryQuestions(), questions, Seed);
            Assert.AreEqual(2, expected.Count);
            Assert.AreEqual(expected.Questions[0].Text, shownQuestions[0].Text, "1 問目は選択結果の先頭と一致するはず。");

            for (var i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(
                    QuestionType.FreeText, expected.Questions[i].Type, "freeText フィルタが効いているはず。");
                CollectionAssert.Contains(expected.Questions[i].Tags, "地理", "タグフィルタが効いているはず。");
            }

            // 自動進行は 0（手動）なので、結果表示のあとホストが NextQuestion を呼ぶまで進まない。
            Assert.IsFalse(hostSession.NextQuestion(), "結果表示中でなければ次へは進めない。");
        }

        [UnityTest]
        public IEnumerator StartSession_WithNoMatchingQuestion_DoesNotStart()
        {
            yield return ConnectHostAndClient(ThreeQuestionSource());

            var questions = new QuestionSelectionSettings(
                tagFilter: new[] { "存在しないタグ" }, count: QuestionSelectionSettings.AllQuestions);
            var settings = new SessionSettings(questions, FastLimits, ScoringSettings.Default);

            LogAssert.Expect(LogType.Warning, new Regex("問題選択の条件に合う問題が 1 件もない"));

            Assert.IsFalse(HostSession.StartSession(settings, Seed), "候補 0 件ならセッションを開始しない。");

            yield return WaitFrames(5);

            Assert.AreEqual(QuizPhase.Lobby, HostSession.ServerPhase, "ロビーのままであるはず。");
            Assert.AreEqual(0, ClientSession.TotalQuestions.Value);
        }

        [UnityTest]
        public IEnumerator ResyncClient_SendsCurrentStateAndQuestionToLateJoiner()
        {
            // 途中参加の再同期（network.allowLateJoin の口）。時間切れしないよう受付を長めに取る。
            yield return ConnectHostAndClient(ThreeQuestionSource());

            var settings = new SessionSettings(
                new QuestionSelectionSettings(count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false),
                new QuizTimeLimits(buzzTimeLimitSec: 30.0, answerTimeLimitSec: 15.0, collectWindowSec: 0.15),
                ScoringSettings.Default,
                SessionSettings.ManualAdvance,
                allowLateJoin: true);

            Assert.IsTrue(HostSession.StartSession(settings, Seed));
            Assert.IsTrue(HostSession.AllowLateJoin, "途中参加の許可が設定から読めるはず。");

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"1 問目の受付が開きませんでした（{ClientSession.Phase.Value}）。");

            // --- 出題中に 2 人目が参加する ---
            yield return ConnectSecondClient();

            var lateSession = SecondClientSession;
            var resynced = new List<SessionStateSnapshot>();
            lateSession.SessionResynced += state => resynced.Add(state);

            var shown = new List<(int Index, string Text, QuestionShownSource Source)>();
            lateSession.QuestionShown += (index, dto, source) => shown.Add((index, dto.Text, source));

            Assert.IsFalse(
                lateSession.Distributor.TryGetQuestion(0, out _),
                "途中参加したクライアントには出題済みの問題データが届いていない。");

            Assert.IsTrue(
                HostSession.ResyncClient(SecondClientManager.LocalClientId), "再同期を送れるはず。");

            yield return WaitUntil(
                () => resynced.Count >= 1 && shown.Count >= 1,
                () => $"再同期が届きませんでした（状態 {resynced.Count} 件 / 提示 {shown.Count} 件）。");

            Assert.AreEqual(QuizPhase.BuzzOpen, resynced[0].Phase);
            Assert.AreEqual(0, resynced[0].QuestionIndex);
            Assert.AreEqual(3, resynced[0].TotalQuestions);
            Assert.IsTrue(resynced[0].HasQuestion, "出題中なので問題インデックスが確定している。");
            Assert.AreEqual(0, shown[0].Index);
            Assert.AreEqual(QuestionText, shown[0].Text, "現在問の問題文が届くはず。");
            Assert.AreEqual(
                QuestionShownSource.Resync,
                shown[0].Source,
                "再同期由来の提示は Resync として届くはず（#109。読み上げ・開始ジングルを動かさないため）。");
            Assert.IsTrue(
                lateSession.Distributor.TryGetQuestion(0, out _), "再送した DTO が保持されるはず。");

            // フェーズ・問題インデックス・総問題数・得点表は NetworkVariable / NetworkList で同期される。
            yield return WaitUntil(
                () => lateSession.Phase.Value == QuizPhase.BuzzOpen
                    && lateSession.QuestionIndex.Value == 0
                    && lateSession.TotalQuestions.Value == 3,
                () => "途中参加したクライアントに進行状態が同期されませんでした"
                    + $"（{lateSession.Phase.Value} / 問題 {lateSession.QuestionIndex.Value} / "
                    + $"総数 {lateSession.TotalQuestions.Value}）。");
        }

        /// <summary>
        /// 選択式の回答受付中（<see cref="QuizPhase.ChoiceAnswering"/>）に合流したクライアントへも
        /// 再同期が届くこと（#117、PR #123 レビュー M-1）。
        /// </summary>
        /// <remarks>
        /// <c>SessionStateRpc</c> の受信検証が「<see cref="QuizPhase.Lobby"/> 以上
        /// <see cref="QuizPhase.Finished"/> 以下」の範囲判定だった頃は、その外側に定義されている
        /// <see cref="QuizPhase.ChoiceAnswering"/>（= 8）が未定義扱いになり、
        /// <b>再同期がまるごと捨てられていた</b>（<c>SessionResynced</c> も <c>QuestionShown</c> も
        /// 発火しない）。判定を <see cref="QuizPhases.IsDefined"/> へ寄せた回帰テスト。
        /// </remarks>
        [UnityTest]
        public IEnumerator ResyncClient_DuringChoiceAnswering_DeliversStateAndQuestion()
        {
            // 合流を待つ間に時間切れで判定へ進まないよう、選択式の受付を長めに取る。
            var choiceLimits = new QuizTimeLimits(
                buzzTimeLimitSec: 30.0, answerTimeLimitSec: 30.0, choiceTimeLimitSec: 30.0, collectWindowSec: 0.15);
            var questionSource = new TestQuestionSource(
                TestQuestionSource.Choice(ChoiceQuestionId, ChoiceQuestionText, correctIndex: 1, "大阪", "東京", "京都"));

            yield return ConnectHostAndClient(questionSource, limits: choiceLimits);

            Assert.IsTrue(HostSession.StartQuestion(), "選択式の出題を開始できるはず。");
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.ChoiceAnswering,
                () => $"選択式の回答受付が開きませんでした（{ClientSession.Phase.Value}）。");

            // --- 回答受付中に 2 人目が参加する ---
            yield return ConnectSecondClient();

            var lateSession = SecondClientSession;
            var resynced = new List<SessionStateSnapshot>();
            lateSession.SessionResynced += state => resynced.Add(state);

            var shown = new List<(int Index, string Text, QuestionShownSource Source)>();
            lateSession.QuestionShown += (index, dto, source) => shown.Add((index, dto.Text, source));

            Assert.IsTrue(
                HostSession.ResyncClient(SecondClientManager.LocalClientId), "再同期を送れるはず。");

            yield return WaitUntil(
                () => resynced.Count >= 1 && shown.Count >= 1,
                () => $"選択式の回答受付中の再同期が届きませんでした（状態 {resynced.Count} 件 / 提示 {shown.Count} 件）。");

            Assert.AreEqual(
                QuizPhase.ChoiceAnswering,
                resynced[0].Phase,
                "合流時点のフェーズがそのまま届くはず（範囲判定では捨てられていた値）。");
            Assert.AreEqual(0, resynced[0].QuestionIndex, "単問経路（StartQuestion）でも問題 0 は正当。");
            Assert.AreEqual(0, resynced[0].TotalQuestions, "出題列を確定していないので総問題数は 0。");
            Assert.AreEqual(ChoiceQuestionText, shown[0].Text, "現在問の問題文が届くはず。");
            Assert.AreEqual(
                QuestionShownSource.Resync, shown[0].Source, "再同期由来の提示として届くはず（#109）。");
        }

        [UnityTest]
        public IEnumerator AutoAdvance_SkipsUndeliverableQuestionWithoutStalling()
        {
            // 配信できない問題（DTO 検証に落ちる問題文）を 2 問目に混ぜても進行が止まらないこと（C1）。
            yield return ConnectHostAndClient(SourceWithUndeliverableSecondQuestion());

            var clientSession = ClientSession;

            var shown = new List<int>();
            clientSession.QuestionShown += (index, _, _) => shown.Add(index);

            var finalScores = new List<IReadOnlyList<ScoreEntry>>();
            clientSession.SessionFinished += entries => finalScores.Add(entries);

            Assert.IsTrue(HostSession.StartSession(FastSettings(), Seed), "セッションを開始できるはず。");

            yield return WaitUntil(
                () => shown.Count >= 1,
                () => $"1 問目が提示されませんでした（提示 {shown.Count} 件）。");
            yield return BuzzAndAnswer(clientSession, "とうきょう");

            // 2 問目は配信できないので「出題を中止」＋「読み飛ばす」の 2 本のエラーログが出る。
            LogAssert.Expect(LogType.Error, new Regex("問題を配信できないため出題を中止しました"));
            LogAssert.Expect(LogType.Error, new Regex("問題 1 を出題できなかったため読み飛ばします"));

            // --- 2 問目を飛ばして 3 問目へ ---
            yield return WaitUntil(
                () => shown.Count >= 2,
                () => $"3 問目へ進みませんでした（提示 {shown.Count} 件 / "
                    + $"問題 {clientSession.QuestionIndex.Value} / フェーズ {clientSession.Phase.Value}）。");

            CollectionAssert.AreEqual(new[] { 0, 2 }, shown, "配信できない 2 問目（index 1）は飛ばすはず。");

            // 提示の RPC と NetworkVariable の差分同期は別経路なので、同期を待ってから確かめる。
            yield return WaitUntil(
                () => clientSession.QuestionIndex.Value == 2,
                () => $"問題インデックスが同期されませんでした（{clientSession.QuestionIndex.Value}）。");

            // --- 3 問目のタイムアウト後は全問終了へ（Result で止まらない） ---
            yield return WaitUntil(
                () => finalScores.Count >= 1,
                () => $"全問終了に進みませんでした（最終得点 {finalScores.Count} 件 / "
                    + $"フェーズ {HostSession.ServerPhase}）。");

            Assert.AreEqual(QuizPhase.Finished, HostSession.ServerPhase);
            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                finalScores[0][0].Score,
                "1 問目の正解ぶんが最終得点に残るはず。");
        }

        /// <summary>受付中に押下 → 回答を送る（提示済みであることを確認したあとに呼ぶ）。</summary>
        private IEnumerator BuzzAndAnswer(GameSession session, string answer)
        {
            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.BuzzOpen,
                () => $"早押しの受付が開きませんでした（{session.Phase.Value}）。");

            Assert.IsTrue(session.RequestBuzz(), "受付中なので押下を送れるはず。");

            yield return WaitUntil(
                () => session.Phase.Value == QuizPhase.Answering,
                () => $"回答フェーズに進みませんでした（{session.Phase.Value}）。");

            Assert.IsTrue(session.RequestAnswer(answer), "勝者は回答を送れるはず。");
        }

        /// <summary>2 問目だけ配信できない（問題文が上限超過）freeText 3 問。</summary>
        private static IQuestionSource SourceWithUndeliverableSecondQuestion() =>
            new TestQuestionSource(
                TestQuestionSource.FreeText("q-1", QuestionText, CorrectAnswer),
                TestQuestionSource.FreeText(
                    "q-broken", new string('あ', QuestionLimits.MaxTextLength + 100), "こたえ"),
                TestQuestionSource.FreeText("q-3", "日本の旧都はどこ？", "きょうと"));

        /// <summary>自動進行 0.5 秒・誤答で再開放しない・全問出題の設定。</summary>
        private static SessionSettings FastSettings() =>
            new SessionSettings(
                new QuestionSelectionSettings(count: QuestionSelectionSettings.AllQuestions, shuffleOrder: false),
                FastLimits,
                ScoringSettings.Default.WithReopenAfterWrongAnswer(false),
                resultAutoAdvanceSec: 0.5);

        /// <summary>freeText 3 問（1 問目は既定の問題文・正解）。</summary>
        private static IQuestionSource ThreeQuestionSource() =>
            new TestQuestionSource(
                TestQuestionSource.FreeText("q-1", QuestionText, CorrectAnswer),
                TestQuestionSource.FreeText("q-2", "日本一高い山は？", "ふじさん"),
                TestQuestionSource.FreeText("q-3", "日本の旧都はどこ？", "きょうと"));

        /// <summary>タグ・出題形式の混ざった供給元（フィルタの確認用）。</summary>
        private static IQuestionSource TaggedQuestionSource() =>
            new TestQuestionSource(GeoAndHistoryQuestions().ToArray());

        /// <summary>「地理」3 問（うち 1 問は選択式）＋「歴史」1 問。</summary>
        private static List<Question> GeoAndHistoryQuestions() =>
            new List<Question>
            {
                new Question("g-1", QuestionType.FreeText, "地理の問題 1", answers: new[] { "こたえ1" },
                    tags: new[] { "地理" }),
                new Question("g-2", QuestionType.FreeText, "地理の問題 2", answers: new[] { "こたえ2" },
                    tags: new[] { "地理" }),
                new Question("g-3", QuestionType.FreeText, "地理の問題 3", answers: new[] { "こたえ3" },
                    tags: new[] { "地理" }),
                new Question("c-1", QuestionType.Choice, "地理の選択式", choices: new[] { "A", "B" },
                    correctIndex: 0, tags: new[] { "地理" }),
                new Question("h-1", QuestionType.FreeText, "歴史の問題 1", answers: new[] { "こたえ4" },
                    tags: new[] { "歴史" }),
            };
    }
}
