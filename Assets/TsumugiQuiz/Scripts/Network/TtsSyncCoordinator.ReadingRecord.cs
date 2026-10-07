using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="TtsSyncCoordinator"/> のうち、直近の再生開始の記録（<see cref="LastReadingQuestionIndex"/> /
    /// <see cref="LastPlayAtServerTime"/> / <see cref="LastDurationSec"/>）の管理をまとめた部分（#144）。
    /// 文字送り（<c>GameView.Reveal.cs</c>）が View の復元・再同期で読み上げの時間軸へ追いつくのに使う。
    /// </summary>
    public sealed partial class TtsSyncCoordinator
    {
        /// <summary>
        /// 直近の再生開始の記録（<see cref="LastReadingQuestionIndex"/> ほか）を初期値に戻す。
        /// </summary>
        /// <remarks>
        /// 戻さないと 2 回目のゲームで、(1) 1 回目の最後の問題番号 L より小さい問題の <see cref="PlayAtRpc"/> が
        /// 「古い配信」として捨てられて読み上げが鳴らない、(2) 問題番号 L で 1 回目の再生開始時刻が
        /// 「既知」として文字送り（View の復元・再同期）に使われる（#144 再レビュー NH-1）。
        /// <para>
        /// <b>戻す契機は通常の出題（<see cref="HandleQuestionShown"/> の <see cref="QuestionShownSource.Distribution"/>）</b>。
        /// 出題の合図（<c>QuestionShownRpc</c>）と <see cref="PlayAtRpc"/> はどちらもサーバーからの RPC で到着順が保たれ、
        /// その問題の <see cref="PlayAtRpc"/> は必ず出題の合図の後に届く。フェーズ（<c>NetworkVariable</c>）の
        /// Lobby / Finished → Reading で戻す案は、フェーズの同期（ネットワーク tick ごと）が RPC より遅れて届くと
        /// 受け取ったばかりの再生開始時刻まで消してしまうため採らなかった（PlayMode テストで再現）。
        /// ゲームの境目に限らず出題のたびに戻すので、1 回目・2 回目のゲームを区別する必要もない。
        /// </para>
        /// </remarks>
        private void ResetLastReading()
        {
            LastReadingQuestionIndex = TtsReadyTracker.NoQuestionIndex;
            LastPlayAtServerTime = 0d;
            LastDurationSec = 0d;
        }

        /// <summary>直近の再生開始を記録する（<see cref="PlayAtRpc"/> と再同期の両方から）。</summary>
        private void RecordReading(int questionIndex, double playAtServerTime, double durationSec)
        {
            LastReadingQuestionIndex = questionIndex;
            LastPlayAtServerTime = playAtServerTime;
            LastDurationSec = durationSec;
        }

        /// <summary>
        /// 指定した問題の再生開始時刻を記録しているか（サーバーが再同期の応答に載せるため、#144 再レビュー 2 回目）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="playAtServerTime">記録している再生開始時刻（サーバー時刻軸の秒）。</param>
        /// <param name="durationSec">記録している読み上げ時間（秒）。</param>
        /// <returns>その問題の記録があれば true。</returns>
        internal bool TryGetReading(int questionIndex, out double playAtServerTime, out double durationSec)
        {
            var found = questionIndex >= 0 && LastReadingQuestionIndex == questionIndex;
            playAtServerTime = found ? LastPlayAtServerTime : 0d;
            durationSec = found ? LastDurationSec : 0d;
            return found;
        }

        /// <summary>
        /// 途中参加・再接続の再同期（<c>GameSession.SessionStateRpc</c>）で受け取った、現在の問題の
        /// 再生開始時刻を記録する（クライアント。#144 再レビュー 2 回目の統括判断）。
        /// </summary>
        /// <remarks>
        /// 合流した PC はその問題の読み上げを<b>鳴らさない</b>（§6.7、再生の予約はしない）が、
        /// 文字送りは同じ時間軸（読み上げ時間の按分）で途中から揃えられるよう、記録だけを残す。
        /// 文字送り側（<c>GameView</c>）は再同期の提示を受けたときにこの記録を読む。
        /// 受信した値は検証し（<see cref="PlayAtRpc"/> と同じ範囲）、不正なら記録しない。
        /// </remarks>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="playAtServerTime">再生開始時刻（サーバー時刻軸の秒）。</param>
        /// <param name="durationSec">ホストの読み上げ時間（秒）。</param>
        /// <returns>記録したら true。</returns>
        internal bool ApplyResyncReading(int questionIndex, double playAtServerTime, double durationSec)
        {
            if (questionIndex < 0
                || double.IsNaN(playAtServerTime) || double.IsInfinity(playAtServerTime)
                || double.IsNaN(durationSec) || double.IsInfinity(durationSec)
                || durationSec < 0d || durationSec > TtsReadyTracker.MaxReportedDurationSec)
            {
                Debug.LogWarning(
                    "[TtsSyncCoordinator] 再同期で受け取った再生開始時刻が不正なため使いません"
                    + $"（問題 {questionIndex}、playAt {playAtServerTime}、長さ {durationSec}）。");
                return false;
            }

            RecordReading(questionIndex, playAtServerTime, durationSec);
            return true;
        }
    }
}
