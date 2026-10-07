using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 押せる参加者が居なくなったら時間切れを待たずに Result へ進むこと（#200、docs/network.md §6.6）のテスト。
    /// </summary>
    /// <remarks>
    /// 判定シーム（コンストラクタ引数 <c>hasEligibleBuzzers</c>）には、本番の <c>GameSession</c> と同じく
    /// <see cref="BuzzEligibility.HasEligibleBuzzer"/> に「接続中のクライアント」「席を保持している切断中の参加者」
    /// 「司会専任のホスト」「<see cref="QuizStateMachine.IsPenalized"/>」を渡したものを使う。
    /// 2 つの一覧はテストから書き換えて、切断・再接続・席の削除（<c>ForgetSeat</c>）を表す。
    /// </remarks>
    public class QuizStateMachineEligibilityTests : QuizStateMachineTestBase
    {
        private const double BuzzLimit = QuizTimeLimits.DefaultBuzzTimeLimitSec;   // 10
        private const ulong Client3 = 3;
        private const int CorrectChoiceIndex = 1;
        private const int ChoiceCount = 4;

        /// <summary>いま接続しているクライアント（テストが書き換える）。</summary>
        private List<ulong> _connected;

        /// <summary>切断中だが席を保持している参加者（切断時のクライアント ID）。</summary>
        private List<ulong> _retained;

        /// <summary>司会専任のホスト。ホストも参加者なら null。</summary>
        private ulong? _moderator;

        private QuizStateMachine _machine;

        [SetUp]
        public void SetUp()
        {
            _connected = new List<ulong> { Client1, Client2 };
            _retained = new List<ulong>();
            _moderator = null;
            _machine = new QuizStateMachine(
                hasEligibleBuzzers: () =>
                    BuzzEligibility.HasEligibleBuzzer(_connected, _retained, _moderator, _machine.IsPenalized));
        }

        /// <summary>切断した（席は保持期間中。ペナルティは切断時の ID のまま残る）。</summary>
        private void Disconnect(ulong clientId)
        {
            Assert.IsTrue(_connected.Remove(clientId));
            _retained.Add(clientId);
        }

        /// <summary>席が名簿から消えた（保持期間切れ・手動削除 → GameSession.ForgetSeat 相当）。</summary>
        private void ForgetSeat(ulong clientId)
        {
            Assert.IsTrue(_retained.Remove(clientId));
            _machine.ForgetClient(clientId);
        }

        /// <summary>切断していた席へ新しい ID で復帰した（GameSession.TransferSeat 相当）。</summary>
        private void Reconnect(ulong previousClientId, ulong clientId)
        {
            Assert.IsTrue(_retained.Remove(previousClientId));
            _connected.Add(clientId);
            _machine.TransferClient(previousClientId, clientId);
        }

        /// <summary>1 問目を出して受付を開く（T0 = <see cref="QuizStateMachineTestBase.T0"/>）。</summary>
        private void OpenFirstQuestion()
        {
            Assert.IsTrue(_machine.StartQuestion(0, Answers, T0, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, _machine.Tick(T0, Rng()));
        }

        /// <summary>
        /// 受付中に <paramref name="clientId"/> が押して誤答し、Judging まで進める。
        /// </summary>
        /// <returns>Judging に入ったサーバー時刻。</returns>
        private double BuzzAndAnswerWrong(ulong clientId, double buzzAt)
        {
            Assert.AreEqual(QuizPhase.BuzzOpen, _machine.Phase);
            Assert.IsTrue(_machine.AcceptBuzz(clientId, buzzAt, buzzAt + 0.02, out var reason), $"押下を受理するはず（{reason}）。");
            Assert.AreEqual(QuizEvent.BuzzResolved, _machine.Tick(buzzAt + 0.02 + Window, Rng()));
            Assert.AreEqual(QuizEvent.AnswerOpened, _machine.Tick(buzzAt + 0.3, Rng()));
            var wrongAt = buzzAt + 0.4;
            Assert.IsTrue(_machine.SubmitAnswer(clientId, "おおさか", wrongAt, out _));
            Assert.AreEqual(QuizPhase.Judging, _machine.Phase);
            return wrongAt;
        }

        /// <summary>Client1 が誤答して受付を開き直した状態にする。</summary>
        /// <returns>開き直したサーバー時刻。</returns>
        private double Client1WrongAndReopen()
        {
            OpenFirstQuestion();
            var wrongAt = BuzzAndAnswerWrong(Client1, T0 + 0.4);
            var reopenAt = wrongAt + 0.1;
            Assert.AreEqual(QuizEvent.BuzzReopened, _machine.Tick(reopenAt, Rng()), "Client2 が残っているので開き直す。");
            return reopenAt;
        }

        // --- 誤答後の再開放の直前 ---

        [Test]
        public void EveryoneWrong_GoesToResultAsWrongWithoutWaitingForTimeout()
        {
            var reopenAt = Client1WrongAndReopen();

            var secondWrongAt = BuzzAndAnswerWrong(Client2, reopenAt + 0.5);
            var judgedAt = secondWrongAt + 0.1;
            Assert.Less(judgedAt, T0 + BuzzLimit, "前提: 早押しの持ち時間はまだ残っている。");

            Assert.AreEqual(QuizEvent.Judged, _machine.Tick(judgedAt, Rng()), "押せる人が居ないので開き直さない。");
            Assert.AreEqual(QuizPhase.Result, _machine.Phase);
            Assert.AreEqual(QuizJudgement.Wrong, _machine.LastJudgement, "最後の回答者の誤答として結果を出す。");
            Assert.AreEqual(Client2, _machine.LockedClientId, "結果の回答者は最後に誤答した人。");
            Assert.AreEqual(Client2, _machine.LastScoredClientId);
        }

        [Test]
        public void PlayerHost_IsCountedSoBuzzReopens()
        {
            _connected = new List<ulong> { Host, Client1 };
            OpenFirstQuestion();

            var wrongAt = BuzzAndAnswerWrong(Client1, T0 + 0.4);

            Assert.AreEqual(QuizEvent.BuzzReopened, _machine.Tick(wrongAt + 0.1, Rng()), "参加者のホストはまだ押せる。");
        }

        [Test]
        public void ModeratorHost_IsNotCountedSoGoesToResult()
        {
            _connected = new List<ulong> { Host, Client1 };
            _moderator = Host;
            OpenFirstQuestion();

            var wrongAt = BuzzAndAnswerWrong(Client1, T0 + 0.4);

            Assert.AreEqual(QuizEvent.Judged, _machine.Tick(wrongAt + 0.1, Rng()), "司会専任のホストは押せないので数えない。");
            Assert.AreEqual(QuizPhase.Result, _machine.Phase);
            Assert.AreEqual(QuizJudgement.Wrong, _machine.LastJudgement);
        }

        [Test]
        public void RetainedDisconnectedParticipant_IsCountedSoBuzzReopens()
        {
            OpenFirstQuestion();
            var wrongAt = BuzzAndAnswerWrong(Client1, T0 + 0.4);

            Disconnect(Client2); // 回答中に Client2 が一瞬切断した（席は保持期間中）

            Assert.AreEqual(
                QuizEvent.BuzzReopened,
                _machine.Tick(wrongAt + 0.1, Rng()),
                "席を保持している切断中の人は戻ってくる可能性があるので数える（PR #203 レビュー H-1）。");
        }

        [Test]
        public void RetainedDisconnectedWrongAnswerer_IsNotCounted()
        {
            var reopenAt = Client1WrongAndReopen();
            Disconnect(Client1); // 誤答した Client1 が切断した（ペナルティは旧 ID のまま残る）

            var secondWrongAt = BuzzAndAnswerWrong(Client2, reopenAt + 0.5);

            Assert.AreEqual(
                QuizEvent.Judged,
                _machine.Tick(secondWrongAt + 0.1, Rng()),
                "切断中でも誤答済みの人は数えない。");
            Assert.AreEqual(QuizPhase.Result, _machine.Phase);
        }

        // --- 受付中に押せる人が居なくなった（席の削除・席の引き継ぎ） ---

        [Test]
        public void LastEligibleDisconnectsDuringReopenedBuzz_KeepsWaitingWhileSeatIsRetained()
        {
            var reopenAt = Client1WrongAndReopen();

            Disconnect(Client2);

            Assert.AreEqual(QuizEvent.None, _machine.Tick(reopenAt + 0.2, Rng()), "席が残っている間は締めない。");
            Assert.AreEqual(QuizPhase.BuzzOpen, _machine.Phase);
        }

        [Test]
        public void LastEligibleSeatIsForgotten_ClosesAsNoEligibleBuzzersWithoutWaiting()
        {
            var reopenAt = Client1WrongAndReopen();
            Disconnect(Client2);
            Assert.AreEqual(QuizEvent.None, _machine.Tick(reopenAt + 0.1, Rng()));

            ForgetSeat(Client2); // 保持期間切れ・ホストの手動削除
            var closedAt = reopenAt + 0.2;

            Assert.AreEqual(QuizEvent.BuzzClosedNoEligibleBuzzers, _machine.Tick(closedAt, Rng()));
            Assert.AreEqual(QuizPhase.Result, _machine.Phase);
            Assert.AreEqual(closedAt, _machine.PhaseStartServerTime, 1e-9, "時間切れを待たずにその tick で締める。");
            Assert.AreEqual(
                QuizJudgement.NoEligibleBuzzers,
                _machine.LastJudgement,
                "誰も正解しなかった扱い。時間切れとは区別する（PR #203 レビュー M-2）。");
            Assert.AreEqual(QuizStateMachine.NoClientId, _machine.LockedClientId);
            Assert.AreEqual(QuizStateMachine.NoClientId, _machine.LastScoredClientId, "誤答時の得点通知を Result で二重に配らない。");
            Assert.AreEqual(0, _machine.LastScoreDelta);
        }

        [Test]
        public void ReconnectedWrongAnswerer_KeepsPenaltyAndIsNotCounted()
        {
            var reopenAt = Client1WrongAndReopen();

            // Client1 が切断 → 新しい ID（Client3）で同じ席へ復帰した。
            Disconnect(Client1);
            Reconnect(Client1, Client3);
            Assert.AreEqual(QuizEvent.None, _machine.Tick(reopenAt + 0.1, Rng()), "Client2 は押せるので待つ。");

            Disconnect(Client2);
            ForgetSeat(Client2);

            Assert.AreEqual(
                QuizEvent.BuzzClosedNoEligibleBuzzers,
                _machine.Tick(reopenAt + 0.2, Rng()),
                "付け替えた誤答済みは引き継がれるので、復帰した Client3 は押せる人に数えない。");
        }

        [Test]
        public void ParticipantReturns_KeepsBuzzOpenAndCanBuzz()
        {
            var reopenAt = Client1WrongAndReopen();

            // Client2 が切断して、新しい ID（Client3）で復帰した（誤答していないので押せる）。
            Disconnect(Client2);
            Reconnect(Client2, Client3);

            Assert.AreEqual(QuizEvent.None, _machine.Tick(reopenAt + 0.1, Rng()));
            Assert.AreEqual(QuizPhase.BuzzOpen, _machine.Phase);
            Assert.IsTrue(_machine.AcceptBuzz(Client3, reopenAt + 0.2, reopenAt + 0.21, out _));
        }

        [Test]
        public void CandidateInCollectWindow_IsResolvedEvenIfNoOneElseCanBuzz()
        {
            var reopenAt = Client1WrongAndReopen();

            Assert.IsTrue(_machine.AcceptBuzz(Client2, reopenAt + 0.2, reopenAt + 0.21, out _));
            Disconnect(Client2); // 押した直後に切断し、そのまま席も消えた
            _retained.Remove(Client2);

            Assert.AreEqual(
                QuizEvent.None,
                _machine.Tick(reopenAt + 0.21 + (Window / 2), Rng()),
                "受理済みの押下がある（集計窓が開いている）間は締めない。");
            Assert.AreEqual(QuizEvent.BuzzResolved, _machine.Tick(reopenAt + 0.21 + Window, Rng()));
            Assert.AreEqual(Client2, _machine.LockedClientId);
        }

        // --- 参加者が 0 人（判定の材料が無い、PR #203 レビュー M-1） ---

        [Test]
        public void NoParticipants_WaitsForBuzzTimeLimit()
        {
            _connected = new List<ulong> { Host };
            _moderator = Host; // 司会専任のホストだけで開始した
            OpenFirstQuestion();

            Assert.AreEqual(QuizEvent.None, _machine.Tick(T0 + 0.1, Rng()), "参加者が居ない部屋で問題を流さない。");
            Assert.AreEqual(QuizEvent.None, _machine.Tick(T0 + BuzzLimit - 0.01, Rng()));
            Assert.AreEqual(QuizEvent.BuzzTimedOut, _machine.Tick(T0 + BuzzLimit, Rng()), "従来どおり時間切れまで待つ。");
            Assert.AreEqual(QuizJudgement.TimedOut, _machine.LastJudgement);
        }

        [Test]
        public void EveryParticipantSeatForgotten_WaitsForBuzzTimeLimit()
        {
            _moderator = Host;
            _connected = new List<ulong> { Host, Client1 };
            OpenFirstQuestion();

            Disconnect(Client1);
            ForgetSeat(Client1); // 参加者が全員いなくなった（司会専任のホストだけ残った）

            Assert.AreEqual(QuizEvent.None, _machine.Tick(T0 + 0.1, Rng()), "参加者が 0 人になったら締めずに待つ。");
            Assert.AreEqual(QuizPhase.BuzzOpen, _machine.Phase);
        }

        // --- 次問休み ---

        [Test]
        public void EveryoneSuspendedForNextQuestion_ClosesRightAfterBuzzOpens()
        {
            var reopenAt = Client1WrongAndReopen();
            var secondWrongAt = BuzzAndAnswerWrong(Client2, reopenAt + 0.5);
            Assert.AreEqual(QuizEvent.Judged, _machine.Tick(secondWrongAt + 0.1, Rng()));

            // 既定の score.penaltyType = "skipNext" で、2 問目は 2 人とも休み。
            var second = secondWrongAt + 5.0;
            Assert.IsTrue(_machine.StartQuestion(1, Answers, second, out _));
            Assert.IsTrue(_machine.IsPenalized(Client1) && _machine.IsPenalized(Client2), "前提: 2 人とも次問休み。");

            Assert.AreEqual(QuizEvent.BuzzOpened, _machine.Tick(second, Rng()), "受付はいったん開く（各フェーズは 1 tick 以上続く）。");
            Assert.AreEqual(QuizEvent.BuzzClosedNoEligibleBuzzers, _machine.Tick(second + 0.05, Rng()));
            Assert.AreEqual(QuizPhase.Result, _machine.Phase);
            Assert.AreEqual(QuizJudgement.NoEligibleBuzzers, _machine.LastJudgement);
            Assert.AreEqual(1, _machine.QuestionIndex);
        }

        [Test]
        public void ChoiceQuestion_IsNotAffected_WaitsForChoiceTimeLimit()
        {
            // 選択式は早押しを介さず、全員の選択を待たずに締めることもしない（確定: #17、2026-09-18 ユーザー承認）。
            var reopenAt = Client1WrongAndReopen();
            var secondWrongAt = BuzzAndAnswerWrong(Client2, reopenAt + 0.5);
            Assert.AreEqual(QuizEvent.Judged, _machine.Tick(secondWrongAt + 0.1, Rng()));

            var second = secondWrongAt + 5.0;
            Assert.IsTrue(_machine.StartQuestion(1, CorrectChoiceIndex, ChoiceCount, second, out _));
            Assert.AreEqual(QuizEvent.ChoiceAnsweringOpened, _machine.Tick(second, Rng()));

            Assert.AreEqual(QuizEvent.None, _machine.Tick(second + 1.0, Rng()));
            Assert.AreEqual(QuizPhase.ChoiceAnswering, _machine.Phase);
        }

        // --- 一時停止 ---

        [Test]
        public void Paused_DoesNotCloseEvenIfNoOneCanBuzz()
        {
            var reopenAt = Client1WrongAndReopen();
            Assert.IsTrue(_machine.Pause(reopenAt + 0.1, out _));

            Disconnect(Client2);
            ForgetSeat(Client2);

            Assert.AreEqual(QuizEvent.None, _machine.Tick(reopenAt + 0.2, Rng()), "一時停止中は遷移しない。");
            Assert.AreEqual(QuizPhase.BuzzOpen, _machine.Phase);

            Assert.IsTrue(_machine.Resume(reopenAt + 1.0, out _));
            Assert.AreEqual(QuizEvent.BuzzClosedNoEligibleBuzzers, _machine.Tick(reopenAt + 1.05, Rng()), "再開後に締める。");
        }

        // --- 判定シームが無い（null）ときは従来どおり ---

        [Test]
        public void WithoutEligibilitySeam_WaitsForBuzzTimeLimit()
        {
            var machine = new QuizStateMachine();
            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(T0, Rng()));

            Assert.AreEqual(QuizEvent.None, machine.Tick(T0 + BuzzLimit - 0.01, Rng()));
            Assert.AreEqual(QuizEvent.BuzzTimedOut, machine.Tick(T0 + BuzzLimit, Rng()));
        }

        [Test]
        public void TimeLimitReachedAtSameTickAsNoEligible_ReportsBuzzTimedOut()
        {
            var reopenAt = Client1WrongAndReopen();
            Disconnect(Client2);
            ForgetSeat(Client2);

            // 同じ tick で両方が成り立つなら、従来の時間切れを優先する（イベントを増やさない）。
            Assert.Greater(T0 + BuzzLimit, reopenAt, "前提: 再開放は持ち時間の内側。");
            Assert.AreEqual(QuizEvent.BuzzTimedOut, _machine.Tick(T0 + BuzzLimit, Rng()));
            Assert.AreEqual(QuizJudgement.TimedOut, _machine.LastJudgement);
        }
    }
}
