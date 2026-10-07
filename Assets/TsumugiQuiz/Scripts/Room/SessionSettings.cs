using System;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// 1 ゲーム（セッション）の進行に必要なルーム設定を束ねた不変オブジェクト（#19）。
    /// 問題選択（<see cref="QuestionSelectionSettings"/>）・制限時間（<see cref="QuizTimeLimits"/>）・
    /// 得点（<see cref="ScoringSettings"/>）と、結果表示からの自動進行・途中参加の可否を持つ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// フル <c>RoomSettings</c>（#26）の前身で、既定値だけを持つ（プリセット JSON の読み込み・
    /// 範囲外値のクランプは #26、クライアントへの同期は #27）。
    /// <c>TsumugiQuiz.Network.GameSession.StartSession</c> に渡して出題列と進行規則を確定させる。
    /// </para>
    /// <para>
    /// <see cref="ResultAutoAdvanceSec"/>（<c>result.autoAdvanceSec</c>）は本 issue で追加した
    /// <b>仮決め</b>のキー（docs/room-settings.md §1「結果表示」）。
    /// </para>
    /// </remarks>
    public sealed class SessionSettings
    {
        /// <summary><c>result.autoAdvanceSec</c> の既定値（秒）。<b>仮決め</b>（#19）。</summary>
        public const double DefaultResultAutoAdvanceSec = 5.0;

        /// <summary><c>result.autoAdvanceSec</c> が「自動で進めない（司会の「次へ」待ち）」を表す値。</summary>
        public const double ManualAdvance = 0.0;

        /// <summary><c>result.autoAdvanceSec</c> の上限（秒）。</summary>
        public const double MaxResultAutoAdvanceSec = 60.0;

        /// <summary><c>network.allowLateJoin</c> の既定値（docs/room-settings.md §2）。</summary>
        public const bool DefaultAllowLateJoin = false;

        private static readonly SessionSettings DefaultInstance = new SessionSettings();

        /// <summary>
        /// 設定値を指定して生成する。
        /// </summary>
        /// <param name="questions">問題選択の設定。null なら既定値。</param>
        /// <param name="timeLimits">制限時間。null なら <see cref="QuizTimeLimits.Default"/>。</param>
        /// <param name="scoring">得点・ペナルティ・再開放の設定。null なら <see cref="ScoringSettings.Default"/>。</param>
        /// <param name="resultAutoAdvanceSec">
        /// 結果表示から次の問題へ自動で進むまでの秒数。<see cref="ManualAdvance"/>（0）なら自動で進めない。
        /// </param>
        /// <param name="allowLateJoin">ゲーム進行中の途中参加を許可するか。</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="resultAutoAdvanceSec"/> が有限でない、負、または
        /// <see cref="MaxResultAutoAdvanceSec"/> を超えるとき。
        /// </exception>
        public SessionSettings(
            QuestionSelectionSettings questions = null,
            QuizTimeLimits timeLimits = null,
            ScoringSettings scoring = null,
            double resultAutoAdvanceSec = DefaultResultAutoAdvanceSec,
            bool allowLateJoin = DefaultAllowLateJoin)
        {
            if (!double.IsFinite(resultAutoAdvanceSec)
                || resultAutoAdvanceSec < 0.0
                || resultAutoAdvanceSec > MaxResultAutoAdvanceSec)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resultAutoAdvanceSec),
                    resultAutoAdvanceSec,
                    $"result.autoAdvanceSec は 0（手動）〜{MaxResultAutoAdvanceSec} 秒の有限の値である必要があります。");
            }

            Questions = questions ?? QuestionSelectionSettings.Default;
            TimeLimits = timeLimits ?? QuizTimeLimits.Default;
            Scoring = scoring ?? ScoringSettings.Default;
            ResultAutoAdvanceSec = resultAutoAdvanceSec;
            AllowLateJoin = allowLateJoin;
        }

        /// <summary>docs/room-settings.md の既定値（組み込みプリセット「標準」に相当）。</summary>
        public static SessionSettings Default => DefaultInstance;

        /// <summary>問題選択の設定（<c>questions.*</c>）。</summary>
        public QuestionSelectionSettings Questions { get; }

        /// <summary>制限時間（<c>buzz.timeLimitSec</c> / <c>answer.freeTextTimeLimitSec</c> ほか）。</summary>
        public QuizTimeLimits TimeLimits { get; }

        /// <summary>得点・お手つきペナルティ・誤答後の再開放の設定（<c>score.*</c> ほか）。</summary>
        public ScoringSettings Scoring { get; }

        /// <summary>
        /// 結果表示から次の問題へ自動で進むまでの秒数（<c>result.autoAdvanceSec</c>、仮決め）。
        /// 0（<see cref="ManualAdvance"/>）なら自動で進めず、司会の「次へ」（#20）を待つ。
        /// </summary>
        public double ResultAutoAdvanceSec { get; }

        /// <summary>ゲーム進行中の途中参加を許可するか（<c>network.allowLateJoin</c>）。</summary>
        public bool AllowLateJoin { get; }

        /// <summary>結果表示から自動で進むか（<see cref="ResultAutoAdvanceSec"/> が 0 より大きいか）。</summary>
        public bool AutoAdvancesAfterResult => ResultAutoAdvanceSec > ManualAdvance;

        /// <summary>問題選択の設定だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="questions">問題選択の設定。</param>
        /// <returns>新しいインスタンス。</returns>
        public SessionSettings WithQuestions(QuestionSelectionSettings questions) =>
            new SessionSettings(questions, TimeLimits, Scoring, ResultAutoAdvanceSec, AllowLateJoin);

        /// <summary>制限時間だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="timeLimits">制限時間。</param>
        /// <returns>新しいインスタンス。</returns>
        public SessionSettings WithTimeLimits(QuizTimeLimits timeLimits) =>
            new SessionSettings(Questions, timeLimits, Scoring, ResultAutoAdvanceSec, AllowLateJoin);

        /// <summary>得点設定だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="scoring">得点設定。</param>
        /// <returns>新しいインスタンス。</returns>
        public SessionSettings WithScoring(ScoringSettings scoring) =>
            new SessionSettings(Questions, TimeLimits, scoring, ResultAutoAdvanceSec, AllowLateJoin);

        /// <summary>結果表示からの自動進行だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="resultAutoAdvanceSec">自動進行までの秒数（0 = 手動）。</param>
        /// <returns>新しいインスタンス。</returns>
        public SessionSettings WithResultAutoAdvanceSec(double resultAutoAdvanceSec) =>
            new SessionSettings(Questions, TimeLimits, Scoring, resultAutoAdvanceSec, AllowLateJoin);

        /// <summary>途中参加の可否だけを差し替えた新しいインスタンスを返す。</summary>
        /// <param name="allowLateJoin">途中参加を許可するか。</param>
        /// <returns>新しいインスタンス。</returns>
        public SessionSettings WithAllowLateJoin(bool allowLateJoin) =>
            new SessionSettings(Questions, TimeLimits, Scoring, ResultAutoAdvanceSec, allowLateJoin);
    }
}
