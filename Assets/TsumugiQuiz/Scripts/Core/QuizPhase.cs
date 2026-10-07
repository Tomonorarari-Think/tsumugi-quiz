namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 1 問の進行フェーズ（docs/network.md §1.2 / §6.6、docs/architecture.md §4）。
    /// サーバーが権威を持ち、クライアントは <c>NetworkVariable</c> 経由で読むだけ。
    /// </summary>
    /// <remarks>
    /// docs/architecture.md §4 の一覧では早押し受付を 1 つの <c>Buzzing</c> として書いているが、
    /// 実装では docs/network.md §1.2 / §6.6 に合わせて「受付中（<see cref="BuzzOpen"/>）」と
    /// 「勝者確定（<see cref="Locked"/>）」を分ける。集計窓が閉じて勝者が決まってから
    /// 回答入力を開放するまでの間を、クライアントにも区別して見せる必要があるため。
    ///
    /// <b>値を追加するときは <see cref="QuizPhases"/> の述語（進行中 / 再同期の要否）を必ず見直すこと</b>
    /// （PR #104 レビュー L-1）。述語は除外リスト形で書いてあるので、追加したフェーズは既定で
    /// 「進行中」側に入る。それで正しいかを <c>QuizPhasesTests</c> とあわせて確認する。
    /// </remarks>
    public enum QuizPhase
    {
        /// <summary>ロビー。出題前。</summary>
        Lobby = 0,

        /// <summary>問題の提示・読み上げ中。読み上げ完了（TTS 無効時は提示と同時）で受付が開く。</summary>
        Reading = 1,

        /// <summary>早押し受付中。最初の押下で集計窓が開く（docs/network.md §6.3）。</summary>
        BuzzOpen = 2,

        /// <summary>集計窓が閉じ、勝者が確定した状態。</summary>
        Locked = 3,

        /// <summary>勝者だけが回答を入力できる状態。</summary>
        Answering = 4,

        /// <summary>回答の正誤を判定中。</summary>
        Judging = 5,

        /// <summary>正誤と正解を提示している状態。正解データはこのフェーズになるまでクライアントへ送らない。</summary>
        Result = 6,

        /// <summary>全問終了。</summary>
        Finished = 7,

        /// <summary>
        /// 選択式（<c>choice</c>）の回答受付中（<b>仮決め: #17</b>、docs/room-settings.md
        /// <c>answer.choiceTimeLimitSec</c>）。早押しを介さず、全員が
        /// <c>answer.choiceTimeLimitSec</c> の間に各自選択でき、時間切れで一斉に判定する
        /// （<see cref="Reading"/> → <see cref="ChoiceAnswering"/> → <see cref="Judging"/> →
        /// <see cref="Result"/>。<see cref="BuzzOpen"/> / <see cref="Locked"/> / <see cref="Answering"/>
        /// は経由しない）。
        /// </summary>
        ChoiceAnswering = 8,
    }
}
