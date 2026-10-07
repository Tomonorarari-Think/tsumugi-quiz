using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> のうち、得点（<see cref="ScoreBoard"/>）とお手つきペナルティ
    /// （<see cref="PenaltyTracker"/>）に関する状態と操作をまとめた部分
    /// （docs/room-settings.md §1「得点」、仮決め K19、#18）。
    /// </summary>
    public sealed partial class QuizStateMachine
    {
        /// <summary>現在の得点表（不変。更新のたびに新しいインスタンスに差し替わる）。</summary>
        public ScoreBoard Scores { get; private set; }

        /// <summary>「次問休み」ペナルティの保持状態（不変）。</summary>
        public PenaltyTracker Penalties { get; private set; }

        /// <summary>現在の問題で既に誤答したクライアント（再開放時は受付対象外）。</summary>
        public IReadOnlyList<ulong> WrongAnswerers => _wrongAnswerersView;

        /// <summary>
        /// 直近に得点が動いたクライアント。まだ動いていなければ <see cref="NoClientId"/>。
        /// 得点が動かない遷移（早押しのタイムアウト）では <see cref="NoClientId"/> に戻す。
        /// </summary>
        public ulong LastScoredClientId { get; private set; } = NoClientId;

        /// <summary>直近の得点の増減（<see cref="LastScoredClientId"/> に対する差分）。</summary>
        public int LastScoreDelta { get; private set; }

        /// <summary>
        /// 現在のロック保持者が送信した回答の回数（<c>answer.singleAttemptOnly</c> の判定用）。
        /// 回答入力の開放・受付の再開放・出題でリセットする。
        /// </summary>
        public int AnswerAttemptCount { get; private set; }

        /// <summary>指定クライアントの得点。未登録なら 0。</summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>得点。</returns>
        public int GetScore(ulong clientId) => Scores.GetScore(clientId);

        /// <summary>
        /// 指定クライアントが現在の早押し受付で棄却される（ペナルティ中・既に誤答済み）か。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>棄却されるなら true。</returns>
        public bool IsPenalized(ulong clientId) =>
            _wrongAnswerers.Contains(clientId) || Penalties.IsSuspended(QuestionIndex, clientId);

        /// <summary>
        /// 席が無くなったクライアントの進行状態を捨てる（#84）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="TransferClient"/>（再接続での付け替え）の裏返しで、対象は同じ
        /// 「クライアント ID をキーに持つ進行中の状態」。ペナルティ・誤答済み・選択式の選択・
        /// 受付中の <see cref="BuzzArbiter"/>（ペナルティ集合と押下候補）を消し、
        /// ロック保持者・直近の得点通知先が本人ならそれも解除する。
        /// </para>
        /// <para>
        /// <b>得点表（<see cref="Scores"/> / <see cref="FinalScores"/>）からは消さない</b>。
        /// 結果表示（#20）で使うためで、席が消えた人は名簿から引けないので
        /// <c>ResultView</c> が仮の名前（「プレイヤーN」）で順位表に載せる。
        /// </para>
        /// <para>
        /// 呼ぶのは<b>席が名簿から消えたとき</b>（保持期間切れの掃除・ホストの手動削除）だけで、
        /// 切断しただけでは呼ばない。切断で捨てると「切断すればお手つきの罰から逃れられる」
        /// 抜け道になる（docs/network.md §2.4 の引き継ぎ表）。
        /// </para>
        /// </remarks>
        /// <param name="clientId">席が無くなったクライアント ID。</param>
        public void ForgetClient(ulong clientId)
        {
            Penalties = Penalties.WithoutClient(clientId);
            _wrongAnswerers.Remove(clientId);
            _choiceSelections.Remove(clientId);
            _arbiter?.ForgetClient(clientId);
            ForgetProgress(clientId); // #194: 押下順・回答順

            if (LockedClientId == clientId)
            {
                // 回答権の持ち主が居なくなったので解除する（docs/network.md §2.4 の表 2 行目）。
                LockedClientId = NoClientId;
            }

            if (LastScoredClientId == clientId)
            {
                ClearLastScoreChange();
            }
        }

        /// <summary>
        /// 判定結果を得点表に反映し、「次問休み」ペナルティを積む。
        /// 誰も押していない（ロック保持者が居ない）ときは得点を動かさない。
        /// </summary>
        private void ApplyScore()
        {
            LastScoreDelta = 0;

            if (LockedClientId == NoClientId)
            {
                LastScoredClientId = NoClientId;
                return;
            }

            switch (LastJudgement)
            {
                case QuizJudgement.Correct:
                    LastScoreDelta = _rules.Score.CorrectDelta;
                    Scores = Scores.WithCorrect(LockedClientId);
                    break;

                case QuizJudgement.Wrong:
                    LastScoreDelta = _rules.Score.WrongDelta;
                    Scores = Scores.WithWrong(LockedClientId);

                    // 同じ問題では再度押せないようにし、「次問休み」なら次の問題も棄却する。
                    if (!_wrongAnswerers.Contains(LockedClientId))
                    {
                        _wrongAnswerers.Add(LockedClientId);
                    }

                    if (_rules.Score.AppliesSkipNext)
                    {
                        Penalties = Penalties.WithSkipNext(LockedClientId);
                    }

                    break;

                default:
                    // ロック保持者が居るのに Correct / Wrong 以外で Judging を抜けることは無い（到達不能）。
                    // 万一到達しても得点は動かさず、回答した事実だけを残す。
                    Scores = Scores.WithDelta(LockedClientId, 0);
                    break;
            }

            LastScoredClientId = LockedClientId;
        }

        /// <summary>直近の得点通知を「無し」に戻す（得点が動かない遷移で二重通知しないため）。</summary>
        private void ClearLastScoreChange()
        {
            LastScoredClientId = NoClientId;
            LastScoreDelta = 0;
        }

        /// <summary>
        /// 現在の問題で早押しを棄却するクライアント（既に誤答した者＋「次問休み」中の者）。
        /// </summary>
        private List<ulong> CollectPenalized()
        {
            var penalized = new List<ulong>(_wrongAnswerers);
            var suspended = Penalties.SuspendedFor(QuestionIndex);
            for (var i = 0; i < suspended.Count; i++)
            {
                if (!penalized.Contains(suspended[i]))
                {
                    penalized.Add(suspended[i]);
                }
            }

            return penalized;
        }
    }
}
