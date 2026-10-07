using System;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 得点計算の規則（docs/room-settings.md §1「得点」、仮決め K19）。
    /// 不変オブジェクトとして扱い、変更は新しいインスタンスの生成で表す。
    /// ルーム設定（<c>TsumugiQuiz.Room.ScoringSettings</c>）から生成して
    /// <see cref="QuizStateMachine"/> に渡す（フル <c>RoomSettings</c> との接続は #26）。
    /// </summary>
    /// <remarks>
    /// 誤答時の得点変化は <see cref="WrongDelta"/> のとおり
    /// 「<c>score.incorrectPoints</c> ＋（<c>penaltyType</c> が <see cref="PenaltyKind.MinusPoints"/> のときだけ
    /// <c>score.penaltyMinusPoints</c>）」の合算とする。docs/room-settings.md の組み込みプリセット
    /// 「早押し重視」が <c>incorrectPoints=0</c> のまま <c>penaltyType="minusPoints"</c> を指定していることから、
    /// 両者は排他ではなく足し合わせる値として扱う（既定値では 0 + 0 = 0 で変化なし）。
    /// </remarks>
    public sealed class ScoreRules
    {
        /// <summary>正解時の加点の既定値（<c>score.correctPoints</c>）。</summary>
        public const int DefaultCorrectPoints = 10;

        /// <summary>誤答時の得点変化の既定値（<c>score.incorrectPoints</c>）。</summary>
        public const int DefaultWrongPoints = 0;

        /// <summary>お手つきペナルティ種別の既定値（<c>score.penaltyType</c>）。</summary>
        public const PenaltyKind DefaultPenaltyKind = PenaltyKind.SkipNext;

        /// <summary>減点ペナルティの既定値（<c>score.penaltyMinusPoints</c>）。</summary>
        public const int DefaultPenaltyPoints = -5;

        /// <summary>正解時の加点の下限（docs/room-settings.md §1）。</summary>
        public const int MinCorrectPoints = 0;

        /// <summary>正解時の加点の上限。</summary>
        public const int MaxCorrectPoints = 100;

        /// <summary>誤答時の得点変化の下限。</summary>
        public const int MinWrongPoints = -100;

        /// <summary>誤答時の得点変化の上限。</summary>
        public const int MaxWrongPoints = 100;

        /// <summary>減点ペナルティの下限。</summary>
        public const int MinPenaltyPoints = -100;

        /// <summary>減点ペナルティの上限（減点なので 0 以下）。</summary>
        public const int MaxPenaltyPoints = 0;

        private static readonly ScoreRules DefaultInstance = new ScoreRules(
            DefaultCorrectPoints, DefaultWrongPoints, DefaultPenaltyKind, DefaultPenaltyPoints);

        /// <summary>
        /// 得点規則を指定して生成する。範囲外の値は呼び出し側の不具合として例外にする
        /// （プリセット JSON 由来の値のクランプは #26 の <c>RoomSettings</c> 側で行う）。
        /// </summary>
        /// <param name="correctPoints">正解時の加点（0〜100）。</param>
        /// <param name="wrongPoints">誤答時の得点変化（-100〜100）。</param>
        /// <param name="penaltyKind">お手つきペナルティ種別。</param>
        /// <param name="penaltyPoints">減点ペナルティ（-100〜0）。</param>
        /// <exception cref="ArgumentOutOfRangeException">いずれかの値が範囲外のとき。</exception>
        public ScoreRules(
            int correctPoints = DefaultCorrectPoints,
            int wrongPoints = DefaultWrongPoints,
            PenaltyKind penaltyKind = DefaultPenaltyKind,
            int penaltyPoints = DefaultPenaltyPoints)
        {
            RequireInRange(correctPoints, MinCorrectPoints, MaxCorrectPoints, nameof(correctPoints));
            RequireInRange(wrongPoints, MinWrongPoints, MaxWrongPoints, nameof(wrongPoints));
            RequireInRange(penaltyPoints, MinPenaltyPoints, MaxPenaltyPoints, nameof(penaltyPoints));

            if (penaltyKind != PenaltyKind.SkipNext
                && penaltyKind != PenaltyKind.MinusPoints
                && penaltyKind != PenaltyKind.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(penaltyKind),
                    penaltyKind,
                    "penaltyKind は SkipNext / MinusPoints / None のいずれかである必要があります。");
            }

            CorrectPoints = correctPoints;
            WrongPoints = wrongPoints;
            PenaltyKind = penaltyKind;
            PenaltyPoints = penaltyPoints;
        }

        /// <summary>docs/room-settings.md の既定値（正解 +10 / 誤答 0 / 次問休み / 減点 -5）。</summary>
        public static ScoreRules Default => DefaultInstance;

        /// <summary>正解時の加点。</summary>
        public int CorrectPoints { get; }

        /// <summary>誤答時の得点変化。</summary>
        public int WrongPoints { get; }

        /// <summary>お手つきペナルティ種別。</summary>
        public PenaltyKind PenaltyKind { get; }

        /// <summary>
        /// 減点ペナルティ（<see cref="PenaltyKind"/> が <see cref="PenaltyKind.MinusPoints"/> のときだけ効く）。
        /// それ以外の種別では値を保持するだけで挙動に影響しない（docs/room-settings.md §5）。
        /// </summary>
        public int PenaltyPoints { get; }

        /// <summary>正解時に得点に加える値。</summary>
        public int CorrectDelta => CorrectPoints;

        /// <summary>
        /// 誤答（お手つき）時に得点に加える値。
        /// <see cref="PenaltyKind.MinusPoints"/> のときだけ <see cref="PenaltyPoints"/> を含む。
        /// </summary>
        public int WrongDelta =>
            WrongPoints + (PenaltyKind == PenaltyKind.MinusPoints ? PenaltyPoints : 0);

        /// <summary>「次問休み」ペナルティを課す設定か。</summary>
        public bool AppliesSkipNext => PenaltyKind == PenaltyKind.SkipNext;

        /// <summary>正解時の加点だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="correctPoints">正解時の加点。</param>
        /// <returns>新しいインスタンス。</returns>
        public ScoreRules WithCorrectPoints(int correctPoints) =>
            new ScoreRules(correctPoints, WrongPoints, PenaltyKind, PenaltyPoints);

        /// <summary>誤答時の得点変化だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="wrongPoints">誤答時の得点変化。</param>
        /// <returns>新しいインスタンス。</returns>
        public ScoreRules WithWrongPoints(int wrongPoints) =>
            new ScoreRules(CorrectPoints, wrongPoints, PenaltyKind, PenaltyPoints);

        /// <summary>ペナルティ設定だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="penaltyKind">ペナルティ種別。</param>
        /// <param name="penaltyPoints">減点幅（-100〜0）。</param>
        /// <returns>新しいインスタンス。</returns>
        public ScoreRules WithPenalty(PenaltyKind penaltyKind, int penaltyPoints) =>
            new ScoreRules(CorrectPoints, WrongPoints, penaltyKind, penaltyPoints);

        private static void RequireInRange(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} の範囲である必要があります。");
            }
        }
    }
}
