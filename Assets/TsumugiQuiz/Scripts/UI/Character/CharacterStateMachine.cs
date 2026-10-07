using System;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// 立ち絵の状態遷移（issue #24、場面ごとの表情は #212）。Unity API に依存しない純 C# で、EditMode テストで検証する。
    /// </summary>
    /// <remarks>
    /// <para>遷移規則（docs/tts.md §8.3）:</para>
    /// <list type="bullet">
    ///   <item><description>読み上げ開始 → <see cref="CharacterState.Reading"/>（<see cref="CharacterState.Idle"/> のときだけ。
    ///     ほかの表情の間は「音声が鳴っている」ことだけを覚え、表情が戻るときに使う）</description></item>
    ///   <item><description>読み上げ完了 → <see cref="CharacterState.Idle"/>（<see cref="CharacterState.Reading"/> 中のみ）</description></item>
    ///   <item><description>回答権の確定 → <see cref="CharacterState.BuzzSelf"/> / <see cref="CharacterState.BuzzOther"/>。
    ///     時間では戻らず、誤答の瞬間・結果・次の出題で切り替わる</description></item>
    ///   <item><description>誤答して受付が開き直された → <see cref="CharacterState.WrongMoment"/> を
    ///     <see cref="WrongMomentHoldSeconds"/> 秒</description></item>
    ///   <item><description>判定結果 → <see cref="CharacterState.Correct"/> / <see cref="CharacterState.Wrong"/> /
    ///     <see cref="CharacterState.TimedOut"/> / <see cref="CharacterState.NoEligibleBuzzers"/> を
    ///     <see cref="ResultHoldSeconds"/> 秒。TTS の状態とは独立に、いつでも割り込む</description></item>
    ///   <item><description>保持時間が切れたら、読み上げの音声がまだ鳴っていれば <see cref="CharacterState.Reading"/>、
    ///     鳴っていなければ <see cref="CharacterState.Idle"/> に戻る（読み上げの音声は早押しで止まらない、docs/tts.md §6.8）</description></item>
    ///   <item><description>出題 → 回答権・誤答の瞬間の表情が残っていれば戻す（結果の表示は保持時間まで残す）</description></item>
    /// </list>
    /// </remarks>
    public sealed class CharacterStateMachine
    {
        /// <summary>正解・不正解・時間切れなど、結果の表示を保持する秒数（確定事項、#24）。</summary>
        public const double ResultHoldSeconds = 2.0;

        /// <summary>誤答の瞬間の表情を保持する秒数（#212）。受付は開き直されているので、結果より短くする。</summary>
        public const double WrongMomentHoldSeconds = 1.5;

        private double _holdRemainingSec;

        /// <summary>このクライアントで読み上げの音声が鳴っている区間か（ReadingStarted 〜 ReadingCompleted）。</summary>
        private bool _isReadingAudio;

        /// <summary>現在の状態。既定は <see cref="CharacterState.Idle"/>。</summary>
        public CharacterState State { get; private set; } = CharacterState.Idle;

        /// <summary>状態が変わるたびに呼ばれる。</summary>
        public event Action<CharacterState> StateChanged;

        /// <summary>読み上げが始まったことを通知する。待機中だけ表情を切り替え、ほかの表情は割り込まない。</summary>
        public void NotifyReadingStarted()
        {
            _isReadingAudio = true;
            if (State == CharacterState.Idle)
            {
                SetState(CharacterState.Reading);
            }
        }

        /// <summary>読み上げが完了したことを通知する。<see cref="CharacterState.Reading"/> 中でなければ表情は変えない。</summary>
        public void NotifyReadingCompleted()
        {
            _isReadingAudio = false;
            if (State == CharacterState.Reading)
            {
                SetState(CharacterState.Idle);
            }
        }

        /// <summary>
        /// 早押しの回答権が確定したことを通知する（#212、<c>GameSession.BuzzLocked</c>）。
        /// 結果が出るまで保持する（時間では戻らない）。
        /// </summary>
        /// <param name="isLocalWinner">このクライアントの参加者が回答権を得たか。</param>
        public void NotifyBuzzLocked(bool isLocalWinner)
        {
            _holdRemainingSec = 0;
            SetState(isLocalWinner ? CharacterState.BuzzSelf : CharacterState.BuzzOther);
        }

        /// <summary>
        /// 誤答・お手つきで早押しの受付が開き直されたことを通知する（#212、<c>GameSession.BuzzReopened</c>）。
        /// </summary>
        public void NotifyBuzzReopened() => Hold(CharacterState.WrongMoment, WrongMomentHoldSeconds);

        /// <summary>
        /// 1 問の判定結果を通知する。<see cref="QuizJudgement.None"/>（未判定）は無視する。
        /// 読み上げ中でも即座に割り込んで結果の表情に切り替える（TTS の状態とは独立、issue #24）。
        /// </summary>
        /// <param name="judgement">判定結果。</param>
        public void NotifyQuestionResolved(QuizJudgement judgement)
        {
            CharacterState next;
            switch (judgement)
            {
                case QuizJudgement.Correct:
                    next = CharacterState.Correct;
                    break;
                case QuizJudgement.Wrong:
                    next = CharacterState.Wrong;
                    break;
                case QuizJudgement.TimedOut:
                    next = CharacterState.TimedOut;
                    break;
                case QuizJudgement.NoEligibleBuzzers:
                    next = CharacterState.NoEligibleBuzzers;
                    break;
                default:
                    return;
            }

            Hold(next, ResultHoldSeconds);
        }

        /// <summary>
        /// 問題が提示されたことを通知する（#212）。前の問題の回答権・誤答の瞬間の表情が残っていれば戻す。
        /// 結果の表示（<see cref="ResultHoldSeconds"/>）は保持時間まで残す。
        /// </summary>
        public void NotifyQuestionShown()
        {
            if (State == CharacterState.BuzzSelf || State == CharacterState.BuzzOther
                || State == CharacterState.WrongMoment)
            {
                ReturnToBase();
            }
        }

        /// <summary>
        /// 経過時間を進める。保持時間のある表情の間だけ意味を持ち、保持時間を過ぎたら
        /// <see cref="CharacterState.Reading"/>（音声が鳴っている）か <see cref="CharacterState.Idle"/> に戻る。
        /// </summary>
        /// <param name="deltaSeconds">前回からの経過秒（0 以上）。</param>
        public void Tick(double deltaSeconds)
        {
            if (deltaSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "経過時間は 0 以上である必要があります。");
            }

            if (!IsTimed(State))
            {
                return;
            }

            _holdRemainingSec -= deltaSeconds;
            if (_holdRemainingSec <= 0)
            {
                ReturnToBase();
            }
        }

        /// <summary>状態を <see cref="CharacterState.Idle"/> に戻す（画面切替時などに使う）。音声の記録も消す。</summary>
        public void Reset()
        {
            _holdRemainingSec = 0;
            _isReadingAudio = false;
            SetState(CharacterState.Idle);
        }

        /// <summary>保持時間で戻る表情か（結果・誤答の瞬間）。回答権の表情は時間では戻らない。</summary>
        private static bool IsTimed(CharacterState state) =>
            state == CharacterState.Correct
            || state == CharacterState.Wrong
            || state == CharacterState.TimedOut
            || state == CharacterState.NoEligibleBuzzers
            || state == CharacterState.WrongMoment;

        private void Hold(CharacterState next, double seconds)
        {
            _holdRemainingSec = seconds;
            SetState(next);
        }

        private void ReturnToBase()
        {
            _holdRemainingSec = 0;
            SetState(_isReadingAudio ? CharacterState.Reading : CharacterState.Idle);
        }

        private void SetState(CharacterState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            StateChanged?.Invoke(State);
        }
    }
}
