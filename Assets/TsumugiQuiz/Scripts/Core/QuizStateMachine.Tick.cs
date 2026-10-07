using System;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> のうち、時間経過による遷移（<see cref="QuizStateMachine.Tick"/>）と
    /// その内訳をまとめた部分（docs/network.md §6.6 の状態遷移図）。
    /// 状態・コマンド（出題・押下・回答）は QuizStateMachine.cs 側にある。
    /// </summary>
    public sealed partial class QuizStateMachine
    {
        /// <summary>
        /// 時間経過による遷移を 1 つだけ進める。サーバーのネットワーク tick ごとに呼ぶ。
        /// </summary>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        /// <param name="rng">同着抽選に使う乱数源。</param>
        /// <returns>行った遷移。遷移しなければ <see cref="QuizEvent.None"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rng"/> が null のとき。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="serverNow"/> が有限でないとき。</exception>
        public QuizEvent Tick(double serverNow, IRandom rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (!double.IsFinite(serverNow))
            {
                // serverNow はサーバー自身の時刻なので、異常値は呼び出し側のバグ。握りつぶさない。
                throw new ArgumentOutOfRangeException(
                    nameof(serverNow), serverNow, "serverNow は有限の値である必要があります。");
            }

            if (IsPaused)
            {
                // 司会の一時停止中（#20）は時間経過による遷移を一切行わない
                // （早押し・回答の制限時間もここで止まる）。再開時の残り時間の復元は
                // QuizStateMachine.Pause.cs の Resume が担う。
                return QuizEvent.None;
            }

            switch (Phase)
            {
                case QuizPhase.Reading:
                    return TickReading(serverNow);

                case QuizPhase.BuzzOpen:
                    return TickBuzzOpen(serverNow, rng);

                case QuizPhase.Locked:
                    // 勝者に回答入力を開放する。制限時間はここからの経過で測る。
                    AnswerAttemptCount = 0;
                    SetPhase(QuizPhase.Answering, serverNow);
                    return QuizEvent.AnswerOpened;

                case QuizPhase.Answering:
                    return TickAnswering(serverNow);

                case QuizPhase.ChoiceAnswering:
                    return TickChoiceAnswering(serverNow);

                case QuizPhase.Judging:
                    return TickJudging(serverNow);

                default:
                    return QuizEvent.None;
            }
        }

        private QuizEvent TickReading(double serverNow)
        {
            if (serverNow < _readingEndServerTime)
            {
                return QuizEvent.None;
            }

            // T0 は「読み上げ完了時刻」そのもの。serverNow ではない（docs/network.md §6.3）。
            BuzzOpenServerTime = _readingEndServerTime;

            if (_isChoiceQuestion)
            {
                // 選択式は早押しを介さない（仮決め: #17）。全員が answer.choiceTimeLimitSec の間に選択できる。
                SetPhase(QuizPhase.ChoiceAnswering, serverNow);
                return QuizEvent.ChoiceAnsweringOpened;
            }

            _arbiter = new BuzzArbiter(BuzzOpenServerTime, _limits.CollectWindowSec, CollectPenalized());
            SetPhase(QuizPhase.BuzzOpen, serverNow);
            return QuizEvent.BuzzOpened;
        }

        /// <summary>
        /// 選択式の回答受付（<see cref="QuizPhase.ChoiceAnswering"/>）の時間経過による遷移（仮決め: #17）。
        /// 誰も選択していなくても <c>answer.choiceTimeLimitSec</c> が経過したら Judging へ進む
        /// （一斉判定は <c>TickJudging</c> 側の <c>ApplyChoiceScores</c> で行う）。
        /// </summary>
        private QuizEvent TickChoiceAnswering(double serverNow)
        {
            if (serverNow - BuzzOpenServerTime < _limits.ChoiceTimeLimitSec)
            {
                return QuizEvent.None;
            }

            SetPhase(QuizPhase.Judging, serverNow);
            return QuizEvent.ChoiceTimedOut;
        }

        private QuizEvent TickBuzzOpen(double serverNow, IRandom rng)
        {
            if (_arbiter == null)
            {
                return QuizEvent.None;
            }

            if (_arbiter.TryResolve(serverNow, rng, out var resolution) && resolution.HasValue)
            {
                LastResolution = resolution;
                LockedClientId = resolution.Value.WinnerClientId;
                RecordBuzzResolution(resolution.Value); // #194: 押下順・回答順
                SetPhase(QuizPhase.Locked, serverNow);
                return QuizEvent.BuzzResolved;
            }

            // 集計窓が開いている（＝誰かが押している）間はタイムアウトさせない。
            if (_arbiter.DeadlineServerTime == null &&
                serverNow - BuzzOpenServerTime >= _limits.BuzzTimeLimitSec)
            {
                _arbiter.Close();
                LastJudgement = QuizJudgement.TimedOut;

                // 直前に再開放していた場合、誤答時の得点通知がまだ残っている。
                // ここでクリアしないと Result で同じ増減をもう一度配ってしまう（PR #58 レビュー H1）。
                ClearLastScoreChange();

                SetPhase(QuizPhase.Result, serverNow);
                return QuizEvent.BuzzTimedOut;
            }

            if (_arbiter.DeadlineServerTime == null && !HasEligibleBuzzers())
            {
                // 押せる参加者が 1 人も居ない（残りが全員誤答済み・次問休み、または席が消えた）なら、
                // 時間切れを待たずに締める（#200）。受理済みの押下（集計窓）があるときは上の裁定を優先する。
                return CloseBuzzWithoutEligibleBuzzers(serverNow);
            }

            return QuizEvent.None;
        }

        /// <summary>
        /// 押せる参加者が居ないので早押し受付を締め、誰も正解しなかった結果（<see cref="QuizJudgement.NoEligibleBuzzers"/>。
        /// 時間切れと区別できるよう別の判定にする）として Result へ進める（#200）。
        /// 後始末は時間切れ（<see cref="QuizEvent.BuzzTimedOut"/>）と同じ。
        /// </summary>
        private QuizEvent CloseBuzzWithoutEligibleBuzzers(double serverNow)
        {
            _arbiter.Close();
            LastJudgement = QuizJudgement.NoEligibleBuzzers;

            // 再開放のあとに締めた場合、誤答時の得点通知が残っている（時間切れと同じく二重に配らない）。
            ClearLastScoreChange();

            SetPhase(QuizPhase.Result, serverNow);
            return QuizEvent.BuzzClosedNoEligibleBuzzers;
        }

        /// <summary>
        /// まだ押せる参加者が居るか（判定シーム <c>hasEligibleBuzzers</c>、#200）。
        /// シームが無ければ（名簿を持たない呼び出し側・テスト）常に「居る」とみなし、時間切れまで待つ。
        /// </summary>
        private bool HasEligibleBuzzers() => _hasEligibleBuzzers == null || _hasEligibleBuzzers();

        private QuizEvent TickAnswering(double serverNow)
        {
            if (serverNow - PhaseStartServerTime < _limits.AnswerTimeLimitSec)
            {
                return QuizEvent.None;
            }

            // 時間切れは誤答扱い（docs/network.md §6.6: Answering → Judging → Wrong）。
            LastJudgement = QuizJudgement.Wrong;
            SetPhase(QuizPhase.Judging, serverNow);
            return QuizEvent.AnswerTimedOut;
        }

        /// <summary>
        /// 判定を反映し、誤答なら設定に応じて受付を再開放する（docs/network.md §6.6）。
        /// 選択式（仮決め: #17）は早押しの再開放が無いため、一斉判定して即 Result へ進む。
        /// </summary>
        private QuizEvent TickJudging(double serverNow)
        {
            if (_isChoiceQuestion)
            {
                ApplyChoiceScores();
                SetPhase(QuizPhase.Result, serverNow);
                return QuizEvent.ChoiceJudged;
            }

            var wasWrong = LastJudgement == QuizJudgement.Wrong;
            ApplyScore();

            if (wasWrong && _rules.ReopenAfterWrongAnswer && TryReopenBuzz(serverNow))
            {
                return QuizEvent.BuzzReopened;
            }

            SetPhase(QuizPhase.Result, serverNow);
            return QuizEvent.Judged;
        }

        /// <summary>
        /// 誤答・お手つき後に早押し受付を再開放する（docs/network.md §6.6 の <c>Wrong --&gt; BuzzOpen</c>）。
        /// T0 は据え置きなので、残り時間が無ければ再開放せず Result へ進む。
        /// 押せる参加者が残っていない場合も再開放しない（#200）。
        /// </summary>
        /// <returns>再開放したら true。</returns>
        private bool TryReopenBuzz(double serverNow)
        {
            if (serverNow - BuzzOpenServerTime >= _limits.BuzzTimeLimitSec)
            {
                // 早押しの持ち時間（T0 + buzz.timeLimitSec）を使い切っている。
                return false;
            }

            if (!HasEligibleBuzzers())
            {
                // 押せる参加者が居ない（残りが全員誤答済み・次問休み）なら開き直さず、最後の回答者の誤答として
                // Result へ進む（#200。判定は GameSession が接続中のクライアントと席を保持している切断中の参加者から行う）。
                return false;
            }

            // T0 は据え置きのまま新しい裁定器を作る（誤答者・ペナルティ中は受付対象外）。
            _arbiter = new BuzzArbiter(BuzzOpenServerTime, _limits.CollectWindowSec, CollectPenalized());
            LockedClientId = NoClientId;
            LastJudgement = QuizJudgement.None;
            LastResolution = null;
            LastAnswerText = string.Empty;
            AnswerAttemptCount = 0;

            // #194: 開き直した受付は別の競争なので、前の受付の押下順は消す（回答順は残す）。
            ClearBuzzRanking();

            SetPhase(QuizPhase.BuzzOpen, serverNow);
            return true;
        }
    }
}
