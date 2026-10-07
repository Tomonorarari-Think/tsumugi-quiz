using System;

namespace TsumugiQuiz.Core.Reveal
{
    /// <summary>
    /// 問題文の文字送り表示（issue #144、docs/requirements.md FR-43、docs/tts.md §6.8）の進行状態。
    /// 「いま何文字見せるか」を時刻から求める純関数の集まりで、Unity API に依存しない。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 不変オブジェクト。状態を変える操作（<see cref="WithReading"/> / <see cref="Freeze"/> など）は
    /// すべて新しいインスタンスを返す。時刻は呼び出し側が決めた単調増加の秒（UI では
    /// <c>Time.realtimeSinceStartupAsDouble</c>）で、本クラスは時計を持たない。
    /// </para>
    /// <para>
    /// 1 文字目は開始時刻ちょうどに出し、i 文字目（0 始まり）は <c>開始 + i × 1 文字あたりの秒数</c> に出す。
    /// 読み上げ同期（<see cref="QuestionRevealMode.Synced"/>）では 1 文字あたりの秒数を
    /// <c>読み上げ時間 ÷ 文字数</c>（按分）とする。
    /// </para>
    /// <para>
    /// 表示文字数は<b>減らない</b>: モードを切り替えるときは、切り替え時点で見えていた文字数を下限
    /// （<see cref="FloorCount"/>）として引き継ぐ。
    /// </para>
    /// </remarks>
    public sealed class QuestionRevealSchedule
    {
        /// <summary>ルーム設定 <c>question.revealMsPerChar</c> の既定値（ミリ秒／文字、2026-09-30 ユーザー承認済み）。</summary>
        public const int DefaultMsPerChar = 80;

        /// <summary><c>question.revealMsPerChar</c> の下限。0 は「文字送りしない（一括表示）」。</summary>
        public const int MinMsPerChar = 0;

        /// <summary><c>question.revealMsPerChar</c> の上限（2026-09-30 ユーザー承認済み）。</summary>
        public const int MaxMsPerChar = 500;

        /// <summary>
        /// 時刻の割り算で生じる丸め誤差の吸収幅。「ちょうど 2 文字目の時刻」が
        /// 1.9999… になって 1 文字少なく数えられるのを防ぐ。
        /// </summary>
        private const double Epsilon = 1e-9;

        private QuestionRevealSchedule(
            int totalCount,
            QuestionRevealMode mode,
            double startTime,
            double secondsPerChar,
            int floorCount,
            bool isFrozen,
            int frozenCount,
            double frozenAt,
            int fallbackMsPerChar)
        {
            TotalCount = totalCount;
            Mode = mode;
            StartTime = startTime;
            SecondsPerChar = secondsPerChar;
            FloorCount = floorCount;
            IsFrozen = isFrozen;
            FrozenCount = frozenCount;
            FrozenAt = frozenAt;
            FallbackMsPerChar = fallbackMsPerChar;
        }

        /// <summary>問題文の文字数（テキスト要素数、<see cref="RevealText.GetTextElementStarts"/>）。</summary>
        public int TotalCount { get; }

        /// <summary>進め方。</summary>
        public QuestionRevealMode Mode { get; }

        /// <summary>1 文字目を出す時刻（<see cref="QuestionRevealMode.FixedSpeed"/> / <see cref="QuestionRevealMode.Synced"/>）。</summary>
        public double StartTime { get; }

        /// <summary>1 文字あたりの秒数（<see cref="QuestionRevealMode.FixedSpeed"/> / <see cref="QuestionRevealMode.Synced"/>）。</summary>
        public double SecondsPerChar { get; }

        /// <summary>表示文字数の下限（モード切り替え・再開の前に見えていた文字数）。</summary>
        public int FloorCount { get; }

        /// <summary>早押しで止めているか（<see cref="Freeze"/>）。</summary>
        public bool IsFrozen { get; }

        /// <summary>止めた時点の表示文字数（<see cref="IsFrozen"/> のときだけ意味を持つ）。</summary>
        public int FrozenCount { get; }

        /// <summary>止めた時刻（<see cref="IsFrozen"/> のときだけ意味を持つ）。</summary>
        public double FrozenAt { get; }

        /// <summary>読み上げ待ちから固定速度へ切り替えるときの速度（ミリ秒／文字）。</summary>
        public int FallbackMsPerChar { get; }

        /// <summary>全文表示の状態を作る。</summary>
        /// <param name="totalCount">文字数（0 以上）。</param>
        /// <returns>全文表示の状態。</returns>
        public static QuestionRevealSchedule Full(int totalCount)
        {
            RequireNonNegative(totalCount, nameof(totalCount));
            return new QuestionRevealSchedule(
                totalCount, QuestionRevealMode.Full, 0d, 0d, totalCount, false, 0, 0d, DefaultMsPerChar);
        }

        /// <summary>
        /// 固定速度で送る状態を作る。<paramref name="msPerChar"/> が 0、または文字数が 0 なら全文表示。
        /// </summary>
        /// <param name="totalCount">文字数（0 以上）。</param>
        /// <param name="startTime">1 文字目を出す時刻（有限値）。</param>
        /// <param name="msPerChar">1 文字あたりのミリ秒（<see cref="MinMsPerChar"/>〜<see cref="MaxMsPerChar"/>）。</param>
        /// <returns>固定速度の状態。</returns>
        public static QuestionRevealSchedule FixedSpeed(int totalCount, double startTime, int msPerChar)
        {
            RequireNonNegative(totalCount, nameof(totalCount));
            RequireFinite(startTime, nameof(startTime));
            RequireMsPerChar(msPerChar);

            if (msPerChar == 0 || totalCount == 0)
            {
                return Full(totalCount);
            }

            return new QuestionRevealSchedule(
                totalCount, QuestionRevealMode.FixedSpeed, startTime, msPerChar / 1000.0, 0, false, 0, 0d, msPerChar);
        }

        /// <summary>
        /// 読み上げの再生開始時刻を待つ状態を作る（まだ 1 文字も出さない）。
        /// 待ちの上限（タイマー）は持たない。再生開始時刻が届けば <see cref="WithReading"/>、
        /// 届かないまま受付が開けば呼び出し側が <see cref="FallBackToFixedSpeed"/> で固定速度へ切り替える
        /// （#144 レビュー M-1）。<paramref name="fallbackMsPerChar"/> が 0、または文字数が 0 なら全文表示。
        /// </summary>
        /// <param name="totalCount">文字数（0 以上）。</param>
        /// <param name="fallbackMsPerChar">固定速度へ切り替えるときの速度（ミリ秒／文字）。</param>
        /// <returns>読み上げ待ちの状態。</returns>
        public static QuestionRevealSchedule AwaitingReading(int totalCount, int fallbackMsPerChar)
        {
            RequireNonNegative(totalCount, nameof(totalCount));
            RequireMsPerChar(fallbackMsPerChar);

            if (fallbackMsPerChar == 0 || totalCount == 0)
            {
                return Full(totalCount);
            }

            return new QuestionRevealSchedule(
                totalCount, QuestionRevealMode.AwaitingReading, 0d, 0d, 0, false, 0, 0d, fallbackMsPerChar);
        }

        /// <summary>
        /// 読み上げとして使える再生時間か（有限かつ正）。ネットワークから届いた値の検証に使う。
        /// </summary>
        /// <param name="durationSec">読み上げ時間（秒）。</param>
        /// <returns>按分に使えるなら true。</returns>
        public static bool IsUsableReadingDuration(double durationSec) => IsFinite(durationSec) && durationSec > 0d;

        /// <summary>
        /// 読み上げの進行に合わせて送る状態へ切り替える（再生開始時刻が届いたとき）。
        /// 全文表示中なら何もしない。止めている（<see cref="IsFrozen"/>）場合は止めたまま時間軸だけ差し替え、
        /// <see cref="Resume"/> 後に読み上げへ追いつく。
        /// </summary>
        /// <param name="readingStartTime">読み上げの再生開始時刻（<paramref name="now"/> と同じ時間軸）。</param>
        /// <param name="durationSec">
        /// 読み上げ時間（秒）。<see cref="IsUsableReadingDuration"/> が false なら読み上げなしとみなし、
        /// 読み上げ待ちからは <paramref name="readingStartTime"/> を起点に固定速度で送る
        /// （受付開始 = 再生開始時刻にそろえる、#144 レビュー L-1）。
        /// </param>
        /// <param name="now">現在時刻。</param>
        /// <returns>新しい状態。</returns>
        public QuestionRevealSchedule WithReading(double readingStartTime, double durationSec, double now)
        {
            RequireFinite(readingStartTime, nameof(readingStartTime));
            RequireFinite(now, nameof(now));

            if (Mode == QuestionRevealMode.Full)
            {
                return this;
            }

            if (!IsUsableReadingDuration(durationSec))
            {
                // ホストが読み上げ時間を報告できなかった（0 秒）。読み上げなしとして固定速度で送る。
                return FallBackToFixedSpeed(readingStartTime, now);
            }

            return new QuestionRevealSchedule(
                TotalCount, QuestionRevealMode.Synced, readingStartTime, durationSec / TotalCount,
                VisibleCount(now), IsFrozen, FrozenCount, FrozenAt, FallbackMsPerChar);
        }

        /// <summary>
        /// 読み上げ待ち（<see cref="QuestionRevealMode.AwaitingReading"/>）なら、<paramref name="startTime"/> から
        /// 固定速度で送る状態へ切り替える。それ以外のモードでは何もしない（既に送り始めている表示の速度は変えない）。
        /// </summary>
        /// <param name="startTime">1 文字目を出す時刻。</param>
        /// <param name="now">現在時刻（切り替え時点の表示文字数を下限として引き継ぐため）。</param>
        /// <returns>新しい状態。</returns>
        public QuestionRevealSchedule FallBackToFixedSpeed(double startTime, double now)
        {
            RequireFinite(startTime, nameof(startTime));
            RequireFinite(now, nameof(now));

            if (Mode != QuestionRevealMode.AwaitingReading)
            {
                return this;
            }

            return new QuestionRevealSchedule(
                TotalCount, QuestionRevealMode.FixedSpeed, startTime, FallbackMsPerChar / 1000.0,
                VisibleCount(now), IsFrozen, FrozenCount, FrozenAt, FallbackMsPerChar);
        }

        /// <summary>
        /// 文字送りを止める（誰かが早押ししたとき）。止めた時点の表示文字数と時刻を保持する。
        /// 既に止めている・全文表示中なら何もしない。
        /// </summary>
        /// <param name="now">現在時刻。</param>
        /// <returns>新しい状態。</returns>
        public QuestionRevealSchedule Freeze(double now)
        {
            RequireFinite(now, nameof(now));

            if (IsFrozen || Mode == QuestionRevealMode.Full)
            {
                return this;
            }

            return new QuestionRevealSchedule(
                TotalCount, Mode, StartTime, SecondsPerChar, FloorCount, true, VisibleCount(now), now,
                FallbackMsPerChar);
        }

        /// <summary>
        /// 止めていた文字送りを再開する（誤答・お手つきの後に早押しを再開放したとき）。止めていなければ何もしない。
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        ///   <item><description>
        ///     読み上げ同期（<see cref="QuestionRevealMode.Synced"/>）: 時間軸はずらさず、止めていた間に進んだぶんへ
        ///     追いつく（読み上げ音声は早押しで止まらないため）
        ///   </description></item>
        ///   <item><description>
        ///     固定速度（<see cref="QuestionRevealMode.FixedSpeed"/>）: 止めていた時間ぶん開始時刻を後ろへずらし、
        ///     止めた位置から続ける（読み上げの無い部屋で、再開放の直後に全文が出てしまうのを防ぐ。#144 レビュー M-3）
        ///   </description></item>
        /// </list>
        /// </remarks>
        /// <param name="now">現在時刻。</param>
        /// <returns>新しい状態。</returns>
        public QuestionRevealSchedule Resume(double now)
        {
            RequireFinite(now, nameof(now));

            if (!IsFrozen)
            {
                return this;
            }

            var startTime = StartTime;
            if (Mode == QuestionRevealMode.FixedSpeed)
            {
                // 固定速度へ切り替わる前から止めていた場合は、切り替え（開始時刻）以降のぶんだけずらす。
                var pausedSec = now - Math.Max(FrozenAt, StartTime);
                if (pausedSec > 0d)
                {
                    startTime += pausedSec;
                }
            }

            return new QuestionRevealSchedule(
                TotalCount, Mode, startTime, SecondsPerChar, Math.Max(FloorCount, FrozenCount), false, 0, 0d,
                FallbackMsPerChar);
        }

        /// <summary>全文表示へ切り替える（判定確定・時間切れ）。</summary>
        /// <returns>全文表示の状態（既に全文表示なら同じインスタンス）。</returns>
        public QuestionRevealSchedule ShowAll() => Mode == QuestionRevealMode.Full ? this : Full(TotalCount);

        /// <summary>
        /// 時刻 <paramref name="now"/> に表示する文字数（0〜<see cref="TotalCount"/>）。
        /// </summary>
        /// <param name="now">現在時刻。</param>
        /// <returns>表示する文字数。</returns>
        public int VisibleCount(double now)
        {
            if (Mode == QuestionRevealMode.Full)
            {
                return TotalCount;
            }

            if (IsFrozen)
            {
                return FrozenCount;
            }

            if (Mode == QuestionRevealMode.AwaitingReading)
            {
                return FloorCount;
            }

            return Math.Min(TotalCount, Math.Max(FloorCount, TimelineCount(now)));
        }

        /// <summary>
        /// 時間が進むだけでは表示が変わらないか（全文表示済み・止めている・読み上げ待ち）。
        /// UI はこれが true の間、表示の更新を止めてよい（次の変化はイベントで起きる）。
        /// </summary>
        /// <param name="now">現在時刻。</param>
        /// <returns>表示が時間で変わらなければ true。</returns>
        public bool IsSettled(double now) =>
            Mode == QuestionRevealMode.Full
            || Mode == QuestionRevealMode.AwaitingReading
            || IsFrozen
            || VisibleCount(now) >= TotalCount;

        /// <inheritdoc />
        public override string ToString() =>
            $"mode={Mode} total={TotalCount} start={StartTime:F3} spc={SecondsPerChar:F3} "
            + $"floor={FloorCount} frozen={IsFrozen}({FrozenCount})";

        private int TimelineCount(double now)
        {
            var elapsed = now - StartTime;
            if (!IsFinite(elapsed) || elapsed < 0d || SecondsPerChar <= 0d)
            {
                return 0;
            }

            var index = Math.Floor((elapsed / SecondsPerChar) + Epsilon);
            return index >= TotalCount ? TotalCount : (int)index + 1;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static void RequireFinite(double value, string name)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, value, "有限の値である必要があります。");
            }
        }

        private static void RequireNonNegative(int value, string name)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(name, value, "0 以上である必要があります。");
            }
        }

        private static void RequireMsPerChar(int msPerChar)
        {
            if (msPerChar < MinMsPerChar || msPerChar > MaxMsPerChar)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(msPerChar), msPerChar, $"{MinMsPerChar}〜{MaxMsPerChar} の範囲である必要があります。");
            }
        }
    }
}
