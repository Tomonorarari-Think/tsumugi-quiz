using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 1 問分の TTS Ready 通知（docs/tts.md §6.2、docs/network.md §7.3）を集約するサーバー側の純ロジック。
    /// Unity API・NGO に依存しないので EditMode でそのままテストできる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// サーバーは出題ごとに、接続中の全クライアント（ホスト自身も含む）から
    /// <c>TtsReadyRpc(questionIndex, durationSec)</c> が届くのを待ち、
    /// <b>全員 Ready</b> または <b><c>tts.readyTimeoutMs</c>（既定 3000ms）経過</b>で次へ進む。
    /// </para>
    /// <para>
    /// 読み上げ時間は<b>ホストが合成した値を正</b>とする（docs/tts.md §6.2）。
    /// 同じテキスト・スタイル・速度なら各クライアントの合成結果も同じ長さになるはずだが、
    /// 僅差でも「読み上げ完了時刻」が全員でずれないようにするため、
    /// <see cref="AuthoritativeDurationSec"/> はホストの値を返す
    /// （ホストが合成できなかった場合だけ、報告された最大値へフォールバックする）。
    /// </para>
    /// </remarks>
    public sealed class TtsReadyTracker
    {
        /// <summary>Ready 待ちが無いことを表す問題インデックス。</summary>
        public const int NoQuestionIndex = -1;

        /// <summary>
        /// 受理する読み上げ時間の上限（秒）。これを超える報告は改竄・異常として棄却する。
        ///
        /// <c>QuizStateMachine.MaxReadingDurationSec</c>（600 秒）は「出題から T0 までの上限」で、
        /// 受付開始時刻そのものの妥当性を見る値。本定数はその内側に収まるよう<b>もっと厳しく</b>取る:
        /// <c>buzz.allowDuringReading = false</c> のとき T0 = <c>playAtServerTime + durationSec</c> に
        /// なるため、読み上げ時間が長すぎると受付開始が状態機械に棄却されて進行が止まりかねない。
        /// 1 問の読みは長くても数十秒（question-data.md の本文上限）なので 120 秒で十分に余裕がある。
        /// </summary>
        public const double MaxReportedDurationSec = 120d;

        private readonly HashSet<ulong> _pendingClientIds = new HashSet<ulong>();
        private readonly Dictionary<ulong, double> _reportedDurations = new Dictionary<ulong, double>();

        private int _questionIndex = NoQuestionIndex;
        private ulong _hostClientId;
        private double _deadlineServerTime;

        /// <summary>Ready 待ち中か。</summary>
        public bool IsAwaiting => _questionIndex != NoQuestionIndex;

        /// <summary>Ready を待っている問題インデックス。待っていなければ -1。</summary>
        public int QuestionIndex => _questionIndex;

        /// <summary>Ready 待ちの期限（サーバー時刻軸の秒）。</summary>
        public double DeadlineServerTime => _deadlineServerTime;

        /// <summary>まだ Ready が届いていないクライアント数。</summary>
        public int PendingCount => _pendingClientIds.Count;

        /// <summary>まだ Ready が届いていないクライアント（ログ用）。</summary>
        public IReadOnlyCollection<ulong> PendingClientIds => _pendingClientIds;

        /// <summary>全員分の Ready が揃ったか。</summary>
        public bool IsComplete => IsAwaiting && _pendingClientIds.Count == 0;

        /// <summary>
        /// 全員に配る読み上げ時間（秒）。<b>ホストの値だけを正</b>とする（docs/tts.md §6.2）。
        /// ホストが報告していない（合成できない・Ready がタイムアウトした）場合は <b>0</b> で、
        /// 読み上げ完了時刻 = <c>playAtServerTime</c> として扱う。
        /// </summary>
        /// <remarks>
        /// クライアントの報告値へフォールバックしない（統括判断 2026-09-13、PR #65 H-3）。
        /// クライアントの値は<b>信用できない入力</b>（docs/network.md §9）で、これを採用すると
        /// <c>buzz.allowDuringReading = false</c> のときに悪意のあるクライアントが
        /// 受付開始を最大 <see cref="MaxReportedDurationSec"/> 秒遅らせられてしまう。
        /// 報告値は範囲検証とログ・診断のためだけに保持する。
        /// </remarks>
        public double AuthoritativeDurationSec
            => _reportedDurations.TryGetValue(_hostClientId, out var hostDuration) ? hostDuration : 0d;

        /// <summary>
        /// 1 問分の Ready 待ちを始める。進行中の待ちがあれば破棄して上書きする。
        /// </summary>
        /// <param name="questionIndex">問題インデックス（0 以上）。</param>
        /// <param name="clientIds">Ready を待つクライアント（接続中の全クライアント。ホスト自身を含む）。</param>
        /// <param name="hostClientId">ホスト自身のクライアント ID（読み上げ時間の正とする送信元）。</param>
        /// <param name="startServerTime">待ちを始めたサーバー時刻（秒）。</param>
        /// <param name="timeoutSec">待ちの上限（秒、<c>tts.readyTimeoutMs</c> / 1000）。</param>
        /// <exception cref="ArgumentOutOfRangeException">問題インデックスが負、または時刻・上限が不正なとき。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="clientIds"/> が null のとき。</exception>
        public void Begin(
            int questionIndex, IEnumerable<ulong> clientIds, ulong hostClientId,
            double startServerTime, double timeoutSec)
        {
            if (questionIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(questionIndex), questionIndex, "問題インデックスは 0 以上でなければなりません。");
            }

            if (clientIds == null)
            {
                throw new ArgumentNullException(nameof(clientIds));
            }

            if (double.IsNaN(startServerTime) || double.IsInfinity(startServerTime))
            {
                throw new ArgumentOutOfRangeException(nameof(startServerTime), startServerTime, "有限の値でなければなりません。");
            }

            if (double.IsNaN(timeoutSec) || double.IsInfinity(timeoutSec) || timeoutSec < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutSec), timeoutSec, "0 以上の有限の値でなければなりません。");
            }

            Reset();

            foreach (var clientId in clientIds)
            {
                _pendingClientIds.Add(clientId);
            }

            _questionIndex = questionIndex;
            _hostClientId = hostClientId;
            _deadlineServerTime = startServerTime + timeoutSec;
        }

        /// <summary>
        /// Ready の報告を受け取る。範囲外・重複・待っていないクライアントからのものは棄却する
        /// （クライアントから届く値は必ず検証する。docs/network.md §9）。
        /// </summary>
        /// <param name="clientId">送信元のクライアント ID。</param>
        /// <param name="questionIndex">報告された問題インデックス。</param>
        /// <param name="durationSec">報告された読み上げ時間（秒）。読み上げない場合は 0。</param>
        /// <param name="rejectReason">棄却理由（ログ用）。受理した場合は null。</param>
        /// <returns>受理したら true。</returns>
        public bool TryReportReady(ulong clientId, int questionIndex, double durationSec, out string rejectReason)
        {
            if (!IsAwaiting)
            {
                rejectReason = "Ready を待っていません";
                return false;
            }

            if (questionIndex != _questionIndex)
            {
                rejectReason = $"待っている問題（{_questionIndex}）と一致しません";
                return false;
            }

            if (double.IsNaN(durationSec) || double.IsInfinity(durationSec)
                || durationSec < 0d || durationSec > MaxReportedDurationSec)
            {
                rejectReason = $"読み上げ時間が範囲外です（0〜{MaxReportedDurationSec} 秒）";
                return false;
            }

            if (_reportedDurations.ContainsKey(clientId))
            {
                rejectReason = "同じクライアントから 2 回目の Ready が届きました";
                return false;
            }

            if (!_pendingClientIds.Remove(clientId))
            {
                rejectReason = "Ready を待っていないクライアントです";
                return false;
            }

            _reportedDurations[clientId] = durationSec;
            rejectReason = null;
            return true;
        }

        /// <summary>
        /// 切断したクライアントを待ち対象から外す（1 人のために全員を待たせない）。
        /// </summary>
        /// <param name="clientId">切断したクライアント ID。</param>
        /// <returns>待ち対象から外したら true。</returns>
        public bool RemoveClient(ulong clientId)
        {
            _reportedDurations.Remove(clientId);
            return _pendingClientIds.Remove(clientId);
        }

        /// <summary>待ちの期限が来たか。</summary>
        /// <param name="serverTimeNow">いまのサーバー時刻（秒）。</param>
        /// <returns>待ち中で期限に達していたら true。</returns>
        public bool HasTimedOut(double serverTimeNow) => IsAwaiting && serverTimeNow >= _deadlineServerTime;

        /// <summary>報告された読み上げ時間を取り出す（診断・テスト用）。</summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <param name="durationSec">報告された読み上げ時間。</param>
        /// <returns>報告済みなら true。</returns>
        public bool TryGetReportedDuration(ulong clientId, out double durationSec)
            => _reportedDurations.TryGetValue(clientId, out durationSec);

        /// <summary>待ちを終える（状態を初期化する）。</summary>
        public void Reset()
        {
            _pendingClientIds.Clear();
            _reportedDurations.Clear();
            _questionIndex = NoQuestionIndex;
            _hostClientId = 0;
            _deadlineServerTime = 0d;
        }
    }
}
