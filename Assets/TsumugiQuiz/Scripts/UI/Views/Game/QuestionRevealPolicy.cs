using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Reveal;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// 問題文を文字送り表示するかどうかと、最初の進め方を決める純ロジック（issue #144）。
    /// Unity API に依存しないため EditMode でテストする（<see cref="GameViewPresenter"/> と同じ方針）。
    /// </summary>
    /// <remarks>
    /// 仕様（2026-09-30 ユーザー承認済み、issue #144 の提案 5 点をレビューで改めたもの。docs/requirements.md FR-43）:
    /// <list type="number">
    ///   <item><description>自由入力（早押し）形式だけを文字送りする</description></item>
    ///   <item><description>選択式は一括表示（選択肢を読む時間が要るため）</description></item>
    ///   <item><description>
    ///     途中参加・再接続の再同期（<see cref="QuestionShownSource.Resync"/>）は、この PC が既に再生開始時刻を
    ///     受け取っていればその時間軸で途中から。受け取っていなくても受付前・受付中なら受付開始から固定速度
    ///     （再接続では読み上げ同期に乗れないため不利側に倒す）。それ以外は全文表示（#144 レビュー M-2 / 再レビュー M-2）
    ///   </description></item>
    ///   <item><description>司会専任の司会画面は全文表示</description></item>
    ///   <item><description><c>question.revealMsPerChar</c> = 0 なら一括表示</description></item>
    /// </list>
    /// </remarks>
    public static class QuestionRevealPolicy
    {
        /// <summary>
        /// 読み上げのある部屋で読み上げの時間軸が取れなかったとき（再接続で記録が無い・ホストが読み上げ時間を
        /// 報告できない等）に使う固定速度の下限（ミリ秒／文字）。<b>2026-09-30 ユーザー承認済み</b>（#144 再レビュー 2 回目）。
        /// 実測の読み上げ（春日部つむぎ・速度 1.0）は約 200ms/文字なので、それより遅い側に置き、
        /// 時間軸に乗れない PC が読み上げに同期している PC より先に全文を読めないようにする。
        /// </summary>
        public const int ReadingRoomFallbackMinMsPerChar = 250;

        /// <summary>
        /// 固定速度で送るときに実際に使う速度（ミリ秒／文字）を決める。
        /// </summary>
        /// <param name="msPerChar">ルーム設定 <c>question.revealMsPerChar</c>（範囲内へ丸め済み）。0 は一括表示のまま。</param>
        /// <param name="roomHasReading">部屋として読み上げがあるか（ルーム設定 <c>tts.enabled</c>）。</param>
        /// <returns>読み上げのある部屋では <see cref="ReadingRoomFallbackMinMsPerChar"/> 以上。</returns>
        public static int ResolveFixedSpeedMsPerChar(int msPerChar, bool roomHasReading)
        {
            if (msPerChar <= 0 || !roomHasReading)
            {
                return msPerChar;
            }

            return msPerChar < ReadingRoomFallbackMinMsPerChar ? ReadingRoomFallbackMinMsPerChar : msPerChar;
        }

        /// <summary>
        /// この提示で問題文を文字送りするか。
        /// </summary>
        /// <param name="questionType">出題形式。</param>
        /// <param name="source">提示の経路（通常の配信か、再同期か）。</param>
        /// <param name="isModerator">司会専任の司会画面か。</param>
        /// <param name="msPerChar">ルーム設定 <c>question.revealMsPerChar</c>。</param>
        /// <param name="canRevealOnResync">
        /// 再同期のときに文字送りできるか（<see cref="CanRevealOnResync"/>）。再同期のときだけ使う。
        /// </param>
        /// <returns>文字送りするなら true（false なら全文表示）。</returns>
        public static bool ShouldReveal(
            QuestionType questionType,
            QuestionShownSource source,
            bool isModerator,
            int msPerChar,
            bool canRevealOnResync) =>
            questionType == QuestionType.FreeText
            && !isModerator
            && msPerChar > 0
            && (source == QuestionShownSource.Distribution || canRevealOnResync);

        /// <summary>
        /// 再同期（途中参加・再接続）で届いた問題を文字送りできるか。
        /// </summary>
        /// <param name="hasKnownReading">この問題の再生開始時刻（<c>PlayAtRpc</c>）を既に受け取っているか。</param>
        /// <param name="phase">現在のフェーズ。</param>
        /// <returns>
        /// 既知の再生開始時刻がある（その時間軸で途中から送る）、または受付前・受付中（Reading / BuzzOpen。
        /// 受付開始から固定速度で送る）なら true。回答中・判定後は false（全文）。
        /// </returns>
        public static bool CanRevealOnResync(bool hasKnownReading, QuizPhase phase) =>
            hasKnownReading || phase == QuizPhase.Reading || phase == QuizPhase.BuzzOpen;

        /// <summary>
        /// 出題中（受付前・受付中・回答中）のフェーズか。View の復元（<c>GameView.RefreshFromCurrentState</c>）で、
        /// 全文を出してよいか（= 判定済み）を見分けるのに使う（#144 レビュー H-1）。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <returns>
        /// <see cref="QuizPhase.Reading"/> / <see cref="QuizPhase.BuzzOpen"/> / <see cref="QuizPhase.Locked"/> /
        /// <see cref="QuizPhase.Answering"/> / <see cref="QuizPhase.Judging"/> なら true。
        /// </returns>
        public static bool IsQuestionInProgress(QuizPhase phase) =>
            phase == QuizPhase.Reading
            || phase == QuizPhase.BuzzOpen
            || IsBuzzLockedPhase(phase);

        /// <summary>誰かが早押しして文字送りを止めておくフェーズか（Locked / Answering / Judging）。</summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <returns>止めておくフェーズなら true。</returns>
        public static bool IsBuzzLockedPhase(QuizPhase phase) =>
            phase == QuizPhase.Locked || phase == QuizPhase.Answering || phase == QuizPhase.Judging;

        /// <summary>
        /// 提示の時点での進め方を決める。
        /// </summary>
        /// <param name="totalCount">問題文の文字数。</param>
        /// <param name="now">現在時刻（秒）。</param>
        /// <param name="shouldReveal"><see cref="ShouldReveal"/> の結果。</param>
        /// <param name="expectsReading">
        /// 部屋として読み上げが行われる見込みがあるか（ルーム設定 <c>tts.enabled</c>。この PC が音を鳴らすかは問わない、
        /// 統括判断 B 案）。true なら再生開始時刻を待ち（待ちの上限タイマーは持たない、#144 レビュー M-1）、
        /// 届いたらホストの読み上げの時間軸に合わせる。false なら固定速度で送る。
        /// </param>
        /// <param name="msPerChar">固定速度（ミリ秒／文字）。範囲外は範囲内へ丸める。</param>
        /// <param name="roomHasReading">
        /// 部屋として読み上げがあるか（ルーム設定 <c>tts.enabled</c>）。true なら固定速度（最初から・切り替え後とも）を
        /// <see cref="ReadingRoomFallbackMinMsPerChar"/> 以上にする（<see cref="ResolveFixedSpeedMsPerChar"/>）。
        /// </param>
        /// <returns>最初の進め方。</returns>
        public static QuestionRevealSchedule CreateInitial(
            int totalCount,
            double now,
            bool shouldReveal,
            bool expectsReading,
            int msPerChar,
            bool roomHasReading = false)
        {
            var clampedMsPerChar = ResolveFixedSpeedMsPerChar(ClampMsPerChar(msPerChar), roomHasReading);
            if (!shouldReveal || clampedMsPerChar == 0)
            {
                return QuestionRevealSchedule.Full(totalCount);
            }

            return expectsReading
                ? QuestionRevealSchedule.AwaitingReading(totalCount, clampedMsPerChar)
                : QuestionRevealSchedule.FixedSpeed(totalCount, now, clampedMsPerChar);
        }

        /// <summary>
        /// 読み上げの再生開始時刻（サーバー時刻軸）を、表示側の時計（<paramref name="localNow"/>）の時刻へ写す。
        /// </summary>
        /// <param name="playAtServerTime">再生開始時刻（サーバー時刻軸の秒、<c>PlayAtRpc</c>）。</param>
        /// <param name="networkTimeNow">
        /// <paramref name="playAtServerTime"/> と同じ時刻軸の「いま」。読み上げの予約
        /// （<c>TtsSyncPlayer.Schedule</c>）と同じく <c>NetworkManager.LocalTime.Time</c> を渡す（docs/tts.md §6.1）。
        /// </param>
        /// <param name="localNow">表示側の時計の「いま」。</param>
        /// <returns>表示側の時計での再生開始時刻。</returns>
        public static double ToLocalTime(double playAtServerTime, double networkTimeNow, double localNow) =>
            localNow + (playAtServerTime - networkTimeNow);

        private static int ClampMsPerChar(int msPerChar)
        {
            if (msPerChar < QuestionRevealSchedule.MinMsPerChar)
            {
                return QuestionRevealSchedule.MinMsPerChar;
            }

            return msPerChar > QuestionRevealSchedule.MaxMsPerChar ? QuestionRevealSchedule.MaxMsPerChar : msPerChar;
        }
    }
}
