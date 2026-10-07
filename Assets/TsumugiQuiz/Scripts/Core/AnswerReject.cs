namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 回答送信（<c>SubmitAnswerRpc</c> / 選択式の <c>SubmitChoiceRpc</c>、<b>仮決め: #17</b>）を
    /// 棄却した理由（docs/network.md §9 の入力検証）。
    /// サーバーのログに残す用途で、クライアントへはそのまま返さない。
    /// </summary>
    public enum AnswerReject
    {
        /// <summary>棄却していない（受理された）。</summary>
        None = 0,

        /// <summary>回答フェーズ（Answering）ではない。</summary>
        NotAnswering = 1,

        /// <summary>早押しの勝者以外からの回答。</summary>
        NotLockedPlayer = 2,

        /// <summary>空文字・空白のみの回答。</summary>
        Empty = 3,

        /// <summary>文字数が上限（<see cref="QuizStateMachine.MaxAnswerLength"/>）を超えている。</summary>
        TooLong = 4,

        /// <summary>制御文字（U+0000〜U+001F / U+007F / Unicode カテゴリ Control）を含む。</summary>
        InvalidCharacter = 6,

        /// <summary>
        /// サーバーが渡した受信時刻が有限の値でない（NaN / Infinity）。
        /// クライアント入力ではなく呼び出し側の不具合なので、握りつぶさず棄却して記録する。
        /// </summary>
        NonFiniteTime = 5,

        /// <summary>
        /// <c>answer.singleAttemptOnly</c> が true のときの 2 回目以降の回答送信
        /// （選択式では、同じクライアントからの 2 回目以降の選択、<b>仮決め: #17</b>）。
        /// </summary>
        AlreadyAttempted = 7,

        /// <summary>
        /// 出題形式と合わない送信（freeText の問題に <c>SubmitChoice</c>、または choice の問題に
        /// <c>SubmitAnswer</c>）。通常はフェーズ判定（<see cref="NotAnswering"/>）で先に弾かれるため
        /// 到達しない防御的な分岐（<b>仮決め: #17</b>）。
        /// </summary>
        WrongQuestionKind = 8,

        /// <summary>選択肢のインデックスが範囲外（<b>仮決め: #17</b>）。</summary>
        InvalidChoiceIndex = 9,

        /// <summary>
        /// お手つきペナルティ（<c>score.penaltyType = "skipNext"</c>）で今問は休みのクライアントからの選択
        /// （<b>仮決め: #17</b>。<see cref="BuzzReject.Penalized"/> の選択式版）。
        /// </summary>
        Penalized = 10,

        /// <summary>
        /// 選択式の回答受付締切（<c>answer.choiceTimeLimitSec</c>）を過ぎてから届いた選択
        /// （<b>仮決め: #17</b>）。フェーズはネットワーク tick が締切超過を処理するまで
        /// <c>ChoiceAnswering</c> のままなので、<see cref="NotAnswering"/> とは区別してログで
        /// 追えるようにする（レビュー R-2）。
        /// </summary>
        Expired = 11,

        /// <summary>司会の判定上書き（#20）に <see cref="QuizJudgement.Correct"/> / <see cref="QuizJudgement.Wrong"/> 以外を指定した。</summary>
        InvalidJudgement = 12,

        /// <summary>司会が一時停止中（#20、docs/tasks/setup-brief.md K18）。</summary>
        Paused = 13,
    }
}
