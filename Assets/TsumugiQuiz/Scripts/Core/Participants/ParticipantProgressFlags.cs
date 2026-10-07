using System;

namespace TsumugiQuiz.Core.Participants
{
    /// <summary>
    /// 現在の問題での参加者 1 人の状態（#194、参加者パネル）。複数を同時に持ちうる。
    /// </summary>
    /// <remarks>
    /// 同期用のペイロード（<c>TsumugiQuiz.Network.QuestionProgressPayload</c>）へは 1 バイトで載せるため、
    /// 値は 1 バイトに収まるビットだけを使う。<b>選んだ番号・正誤は判定までここに入れない</b>
    /// （<see cref="Correct"/> / <see cref="WrongAnswered"/> は判定の確定後にだけ立つ）。
    /// </remarks>
    [Flags]
    public enum ParticipantProgressFlags : byte
    {
        /// <summary>何も無い（まだ押せる・未回答）。</summary>
        None = 0,

        /// <summary>この問題で誤答した（回答権を失った）。選択式では一斉判定で不正解だった。</summary>
        WrongAnswered = 1 << 0,

        /// <summary>前の問題のお手つきで、この問題は休み（<c>score.penaltyType = "skipNext"</c>）。</summary>
        SuspendedSkipNext = 1 << 1,

        /// <summary>選択式で選択を送った（番号は含めない）。</summary>
        ChoiceSubmitted = 1 << 2,

        /// <summary>この問題で正解した（結果の確定後だけ立つ）。</summary>
        Correct = 1 << 3,

        /// <summary>直近の早押しで勝者と同着（抽選の対象）だった。</summary>
        TiedWithWinner = 1 << 4,
    }

    /// <summary><see cref="ParticipantProgressFlags"/> の補助。</summary>
    public static class ParticipantProgressFlagsExtensions
    {
        /// <summary>定義済みのビットすべて（受信した値のうち未知のビットを落とすのに使う）。</summary>
        public const ParticipantProgressFlags All =
            ParticipantProgressFlags.WrongAnswered
            | ParticipantProgressFlags.SuspendedSkipNext
            | ParticipantProgressFlags.ChoiceSubmitted
            | ParticipantProgressFlags.Correct
            | ParticipantProgressFlags.TiedWithWinner;

        /// <summary>指定したビットをすべて持っているか。</summary>
        /// <param name="flags">対象。</param>
        /// <param name="flag">調べるビット。</param>
        /// <returns>持っていれば true。</returns>
        public static bool Has(this ParticipantProgressFlags flags, ParticipantProgressFlags flag) =>
            (flags & flag) == flag && flag != ParticipantProgressFlags.None;
    }
}
