using System;
using NUnit.Framework;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 再接続でクライアント ID が変わったときの引き継ぎ（<see cref="QuizStateMachine.TransferClient"/>）の
    /// テスト（#84、docs/network.md §2.4 の引き継ぎ表）。
    /// 「同じ人か」の判定は名簿（<c>LobbyRoster</c> の席）が行い、ここでは
    /// 「席が同じと決まったあとに何が移るか / 移らないか」だけを検証する。
    /// </summary>
    public class QuizStateMachineTransferTests : QuizStateMachineTestBase
    {
        /// <summary>再接続で NGO から割り当て直される新しいクライアント ID。</summary>
        private const ulong Reconnected = 41;

        /// <summary>
        /// お手つきで減点もする設定（既定は誤答 ±0 なので、得点の動きが見えるようにする）。
        /// </summary>
        /// <param name="reopen">誤答後に受付を再開放するか。false なら誤答でそのまま Result に入る。</param>
        private static QuizRules PenaltyRules(bool reopen = true) =>
            new QuizRules(
                new ScoreRules(correctPoints: 10, wrongPoints: -5, penaltyKind: PenaltyKind.SkipNext),
                reopenAfterWrongAnswer: reopen);

        [Test]
        public void TransferClient_MovesScoreToNewClientId()
        {
            var machine = CorrectAnswerBy(Client1);
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, machine.GetScore(Client1));

            Assert.IsTrue(machine.TransferClient(Client1, Reconnected), "得点の行が移るはず。");

            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                machine.GetScore(Reconnected),
                "再接続後の新しいクライアント ID で得点を引けるはず。");
            Assert.AreEqual(0, machine.GetScore(Client1), "古いクライアント ID の行は残らない。");
            Assert.AreEqual(1, machine.Scores.Count, "行が増えない（別人として二重に載らない）。");
            Assert.IsFalse(machine.Scores.Contains(Client1));
        }

        [Test]
        public void TransferClient_DoesNotGiveScoreToAnUnrelatedClient()
        {
            var machine = CorrectAnswerBy(Client1);

            // 別人（新規参加）は席が違うので、そもそも付け替えの対象にならない。
            Assert.IsFalse(
                machine.TransferClient(Client2, Reconnected),
                "得点を持たないクライアント ID からの付け替えは何も動かさない。");

            Assert.AreEqual(0, machine.GetScore(Reconnected), "別人に得点は付かない。");
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, machine.GetScore(Client1), "本人の得点は動かない。");
        }

        [Test]
        public void TransferClient_MovesSkipNextPenaltyAndWrongAnswerer()
        {
            var machine = WrongAnswerBy(Client1, PenaltyRules(reopen: false));

            Assert.IsTrue(machine.Penalties.IsPending(Client1), "誤答者は次問休みの予定に入る。");
            CollectionAssert.Contains(machine.WrongAnswerers, Client1);

            Assert.IsTrue(machine.TransferClient(Client1, Reconnected));

            Assert.IsFalse(machine.Penalties.IsPending(Client1), "古い ID のペナルティは残らない。");
            Assert.IsTrue(
                machine.Penalties.IsPending(Reconnected),
                "切断・再接続でお手つきの罰から逃れられないこと。");
            Assert.IsFalse(machine.IsPenalized(Client1));
            Assert.IsTrue(machine.IsPenalized(Reconnected), "現在の問題でも受付対象外のまま。");
            Assert.AreEqual(-5, machine.GetScore(Reconnected), "減点も一緒に引き継ぐ。");
        }

        [Test]
        public void TransferClient_KeepsSuspensionConfirmedForTheNextQuestion()
        {
            var machine = WrongAnswerBy(Client1, PenaltyRules(reopen: false));

            // 次の問題へ進むと「次問休み」が確定する（Pending → SuspendedFor(1)）。
            Assert.IsTrue(machine.StartQuestion(1, Answers, T0 + 100.0, out _));
            Assert.IsTrue(machine.Penalties.IsSuspended(1, Client1));

            Assert.IsTrue(machine.TransferClient(Client1, Reconnected));

            Assert.IsFalse(machine.Penalties.IsSuspended(1, Client1));
            Assert.IsTrue(
                machine.Penalties.IsSuspended(1, Reconnected),
                "確定済みの休み集合も新しいクライアント ID へ移る。");
        }

        [Test]
        public void TransferClient_MovesBuzzLockAndPenalizedSetOfTheOpenBuzz()
        {
            // Client2 だけを受付対象外にした状態を作る（誤答 → 再開放）。
            var machine = ReopenedBuzzAfterWrongAnswerBy(Client2);

            Assert.IsTrue(machine.TransferClient(Client2, Reconnected), "受付中の状態も付け替える。");

            Assert.IsFalse(
                machine.AcceptBuzz(Reconnected, T0 + 0.7, T0 + 0.72, out var reason),
                "再接続しても、同じ問題で誤答済みなら押し直せない。");
            Assert.AreEqual(BuzzReject.Penalized, reason);
        }

        [Test]
        public void TransferClient_MovesLockHolderWhileAnswering()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);
            Assert.AreEqual(Client1, machine.LockedClientId);

            Assert.IsTrue(machine.TransferClient(Client1, Reconnected));

            Assert.AreEqual(Reconnected, machine.LockedClientId, "回答権は同じ席のものとして引き継ぐ。");
            Assert.IsFalse(
                machine.SubmitAnswer(Client1, "とうきょう", answeringStart + 0.1, out var oldReason),
                "古いクライアント ID では回答できない。");
            Assert.AreEqual(AnswerReject.NotLockedPlayer, oldReason);
            Assert.IsTrue(
                machine.SubmitAnswer(Reconnected, "とうきょう", answeringStart + 0.2, out _),
                "復帰した本人はそのまま回答できる。");
        }

        [Test]
        public void TransferClient_MovesChoiceSelectionSoItCannotBeAnsweredTwice()
        {
            var machine = OpenChoiceQuestion();
            Assert.IsTrue(machine.SubmitChoice(Client1, 0, T0 + 0.1, out _));

            Assert.IsTrue(machine.TransferClient(Client1, Reconnected));

            Assert.IsFalse(
                machine.SubmitChoice(Reconnected, 1, T0 + 0.2, out var reason),
                "再接続で選択をやり直せてしまわないこと。");
            Assert.AreEqual(AnswerReject.AlreadyAttempted, reason);
        }

        [Test]
        public void TransferClient_WhenTargetAlreadySelected_KeepsTheTargetSelection()
        {
            // 通常は起こらない（復帰直後のクライアントは選択していない）が、起きても
            // 二重登録にならず「選び直せない」ことは保てる（レビュー L-5）。
            var machine = OpenChoiceQuestion();
            Assert.IsTrue(machine.SubmitChoice(Client1, 0, T0 + 0.1, out _));
            Assert.IsTrue(machine.SubmitChoice(Client2, 2, T0 + 0.1, out _));

            Assert.IsTrue(machine.TransferClient(Client1, Client2));

            Assert.IsFalse(
                machine.SubmitChoice(Client2, 3, T0 + 0.2, out var reason), "移動先の選択が残ること。");
            Assert.AreEqual(AnswerReject.AlreadyAttempted, reason);
            Assert.IsTrue(
                machine.SubmitChoice(Client1, 1, T0 + 0.2, out _), "移動元の選択は残らない（捨てる）。");
        }

        // --- 席が消えたときの掃除（TransferClient の裏返し、レビュー M-2） ---

        [Test]
        public void ForgetClient_ReleasesTheBuzzLock()
        {
            var machine = LockAndOpenAnswer(out var answeringStart);
            Assert.AreEqual(Client1, machine.LockedClientId);

            machine.ForgetClient(Client1);

            Assert.AreEqual(
                QuizStateMachine.NoClientId,
                machine.LockedClientId,
                "席が無くなったら回答権も解除する（docs/network.md §2.4 の表 2 行目）。");
            Assert.IsFalse(machine.SubmitAnswer(Client1, "とうきょう", answeringStart + 0.1, out var reason));
            Assert.AreEqual(AnswerReject.NotLockedPlayer, reason);
        }

        [Test]
        public void ForgetClient_ClearsTheChoiceSelection()
        {
            var machine = OpenChoiceQuestion();
            Assert.IsTrue(machine.SubmitChoice(Client1, 0, T0 + 0.1, out _));

            machine.ForgetClient(Client1);

            Assert.IsTrue(
                machine.SubmitChoice(Client1, 1, T0 + 0.2, out _),
                "選択の記録も捨てる（同じ ID が来ても別人なので、1 回目として受理してよい）。");
        }

        [Test]
        public void ForgetClient_ClearsThePenalizedSetOfTheOpenBuzz()
        {
            var machine = ReopenedBuzzAfterWrongAnswerBy(Client2);
            Assert.IsFalse(machine.AcceptBuzz(Client2, T0 + 0.7, T0 + 0.72, out _), "誤答済みなので押せない。");

            machine.ForgetClient(Client2);

            Assert.IsTrue(
                machine.AcceptBuzz(Client2, T0 + 0.8, T0 + 0.82, out _),
                "受付中のペナルティ集合からも外れること（TransferClient の裏返し）。");
        }

        [Test]
        public void ForgetClient_WhileTheCollectWindowIsOpen_KeepsTheBuzzUsable()
        {
            // 集計窓（最初の押下から 0.15 秒）の最中に、押した人の席が消えたケース。
            // 候補が空になっても締め切りが残っていると、TryResolve が例外を投げるか
            // 受付が永久に固まる（PR #98 レビュー H-2）。
            var machine = OpenBuzz();
            Assert.IsTrue(machine.AcceptBuzz(Client1, T0 + 0.1, T0 + 0.12, out _));
            Assert.IsTrue(machine.HasBuzzCandidates, "集計窓が開いている状態。");

            machine.ForgetClient(Client1);

            Assert.AreEqual(
                QuizEvent.None,
                machine.Tick(T0 + 0.12 + Window, Rng()),
                "締め切りを過ぎても例外を投げず、勝者も決まらないこと。");
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);
            Assert.AreEqual(QuizStateMachine.NoClientId, machine.LockedClientId);

            // 残っている参加者はそのまま押せる（受付が固まっていない）。
            Assert.IsTrue(machine.AcceptBuzz(Client2, T0 + 0.5, T0 + 0.52, out var reason), reason.ToString());
            Assert.AreEqual(QuizEvent.BuzzResolved, machine.Tick(T0 + 0.52 + Window, Rng()));
            Assert.AreEqual(Client2, machine.LockedClientId);
        }

        [Test]
        public void ForgetClient_WhileTheCollectWindowIsOpen_StillTimesOutNormally()
        {
            // 誰も押し直さなければ、通常どおり buzz.timeLimitSec で時間切れになること。
            var machine = OpenBuzz();
            Assert.IsTrue(machine.AcceptBuzz(Client1, T0 + 0.1, T0 + 0.12, out _));

            machine.ForgetClient(Client1);

            Assert.AreEqual(
                QuizEvent.BuzzTimedOut,
                machine.Tick(T0 + QuizTimeLimits.DefaultBuzzTimeLimitSec, Rng()));
            Assert.AreEqual(QuizPhase.Result, machine.Phase);
            Assert.AreEqual(QuizJudgement.TimedOut, machine.LastJudgement);
        }

        [Test]
        public void ForgetClient_KeepsScores()
        {
            var machine = CorrectAnswerBy(Client1);

            machine.ForgetClient(Client1);

            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                machine.GetScore(Client1),
                "得点は結果表示に使うので、席が消えても捨てない。");
        }

        [Test]
        public void TransferClient_MovesFinalScoresAfterTheSessionFinished()
        {
            var machine = new QuizStateMachine();
            Assert.IsTrue(machine.StartSession(1, T0 - 1.0, out _));
            Assert.IsTrue(machine.StartQuestion(0, Answers, T0, out _));
            Assert.AreEqual(QuizEvent.BuzzOpened, machine.Tick(T0, Rng()));
            Assert.IsTrue(machine.AcceptBuzz(Client1, T0 + 0.1, T0 + 0.12, out _));
            machine.Tick(T0 + 0.12 + Window, Rng());
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(T0 + 0.4, Rng()));
            Assert.IsTrue(machine.SubmitAnswer(Client1, "とうきょう", T0 + 0.5, out _));
            Assert.AreEqual(QuizEvent.Judged, machine.Tick(T0 + 0.6, Rng()));
            Assert.IsTrue(machine.Finish(T0 + 1.0, out _));
            Assert.IsNotNull(machine.FinalScores);

            Assert.IsTrue(machine.TransferClient(Client1, Reconnected));

            Assert.AreEqual(
                ScoreRules.DefaultCorrectPoints,
                machine.FinalScores.GetScore(Reconnected),
                "結果画面の順位表（#20）も新しいクライアント ID で引けること。");
            Assert.IsFalse(machine.FinalScores.Contains(Client1));
        }

        [Test]
        public void TransferClient_WithSameClientId_DoesNothing()
        {
            var machine = CorrectAnswerBy(Client1);

            Assert.IsFalse(machine.TransferClient(Client1, Client1));
            Assert.AreEqual(ScoreRules.DefaultCorrectPoints, machine.GetScore(Client1));
        }

        [Test]
        public void TransferClient_WithNoClientId_Throws()
        {
            var machine = new QuizStateMachine();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => machine.TransferClient(QuizStateMachine.NoClientId, Reconnected));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => machine.TransferClient(Client1, QuizStateMachine.NoClientId));
        }

        [Test]
        public void TransferClient_OnUntouchedMachine_ReturnsFalse()
        {
            var machine = new QuizStateMachine();

            Assert.IsFalse(machine.TransferClient(Client1, Reconnected), "移すものが無ければ false。");
        }

        /// <summary>選択式の問題で <see cref="QuizPhase.ChoiceAnswering"/> まで進めたマシンを返す。</summary>
        private static QuizStateMachine OpenChoiceQuestion()
        {
            var machine = new QuizStateMachine();
            Assert.IsTrue(machine.StartQuestion(0, correctChoiceIndex: 1, choiceCount: 4, T0, out _));
            Assert.AreEqual(QuizEvent.ChoiceAnsweringOpened, machine.Tick(T0, Rng()));
            Assert.AreEqual(QuizPhase.ChoiceAnswering, machine.Phase);
            return machine;
        }

        /// <summary>
        /// 指定クライアントが誤答して受付が再開放された（＝その人だけ受付対象外の）マシンを返す。
        /// </summary>
        private static QuizStateMachine ReopenedBuzzAfterWrongAnswerBy(ulong clientId)
        {
            var machine = OpenBuzz(rules: PenaltyRules());
            Assert.IsTrue(machine.AcceptBuzz(clientId, T0 + 0.1, T0 + 0.12, out _));
            machine.Tick(T0 + 0.12 + Window, Rng());
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(T0 + 0.4, Rng()));
            Assert.IsTrue(machine.SubmitAnswer(clientId, "ちがう", T0 + 0.5, out _));
            Assert.AreEqual(QuizEvent.BuzzReopened, machine.Tick(T0 + 0.6, Rng()));
            Assert.AreEqual(QuizPhase.BuzzOpen, machine.Phase);
            return machine;
        }

        /// <summary>指定クライアントが正解して Result まで進んだマシンを返す。</summary>
        private static QuizStateMachine CorrectAnswerBy(ulong clientId)
        {
            var machine = OpenBuzz();
            Assert.IsTrue(machine.AcceptBuzz(clientId, T0 + 0.1, T0 + 0.12, out _));
            machine.Tick(T0 + 0.12 + Window, Rng());
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(T0 + 0.4, Rng()));
            Assert.IsTrue(machine.SubmitAnswer(clientId, "とうきょう", T0 + 0.5, out _));
            Assert.AreEqual(QuizEvent.Judged, machine.Tick(T0 + 0.6, Rng()));
            return machine;
        }

        /// <summary>指定クライアントが誤答した（＝お手つき）マシンを返す。</summary>
        private static QuizStateMachine WrongAnswerBy(ulong clientId, QuizRules rules)
        {
            var machine = OpenBuzz(rules: rules);
            Assert.IsTrue(machine.AcceptBuzz(clientId, T0 + 0.1, T0 + 0.12, out _));
            machine.Tick(T0 + 0.12 + Window, Rng());
            Assert.AreEqual(QuizEvent.AnswerOpened, machine.Tick(T0 + 0.4, Rng()));
            Assert.IsTrue(machine.SubmitAnswer(clientId, "ちがう", T0 + 0.5, out _));
            machine.Tick(T0 + 0.6, Rng());
            Assert.IsFalse(machine.Penalties.Pending.Count == 0, "誤答でお手つきのペナルティが積まれるはず。");
            return machine;
        }
    }
}
