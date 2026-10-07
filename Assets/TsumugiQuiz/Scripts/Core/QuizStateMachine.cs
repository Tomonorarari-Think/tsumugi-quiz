using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 1 問の進行（フェーズ遷移・タイムアウト・正誤判定）をつかさどるサーバー側の純ロジック
    /// （docs/network.md §6.6 の状態遷移図、docs/architecture.md §4 の <c>GameSession</c>）。
    /// Unity API に依存せず、時刻はすべて「サーバー時刻軸の double 秒」で受け取る。
    /// <c>GameSession</c>（<c>TsumugiQuiz.Network</c>）は本クラスの薄いアダプタとして振る舞う。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Tick"/> は 1 回の呼び出しで最大 1 遷移しか行わない。
    /// サーバーはネットワーク tick ごとに呼ぶ想定で、こうすることで各フェーズが
    /// 必ず 1 tick 以上継続し、<c>NetworkVariable</c> の差分同期でクライアントにも
    /// すべてのフェーズが届く（同一 tick 内で連続遷移すると中間のフェーズは潰れて届かない）。
    /// </para>
    /// <para>
    /// 正解データ（<c>answers</c>）は本クラス（＝サーバー）の中だけに置き、
    /// クライアントへは Result になるまで渡さない（docs/network.md §1.2 の仮決め K14）。
    /// スレッドセーフではない。サーバーのメインループからのみ呼ぶこと。
    /// </para>
    /// </remarks>
    public sealed partial class QuizStateMachine
    {
        /// <summary>ロック保持者が居ないことを表すクライアント ID（docs/network.md §1.2）。</summary>
        public const ulong NoClientId = ulong.MaxValue;

        /// <summary>
        /// 受理する回答の最大文字数。docs/question-data.md §1 の <c>answers</c> 上限（100 文字）に合わせる
        /// （docs/network.md §9 の 128 文字より厳しい側に倒している）。
        /// </summary>
        public const int MaxAnswerLength = 100;

        /// <summary>
        /// 正解時の加点の既定値（<c>score.correctPoints</c> 既定 10）。
        /// 実際に使う値は <see cref="Rules"/> の <see cref="ScoreRules.CorrectPoints"/>（#18 で設定化した）。
        /// </summary>
        public const int DefaultCorrectPoints = ScoreRules.DefaultCorrectPoints;

        /// <summary>
        /// 誤答時の得点変化の既定値（<c>score.incorrectPoints</c> 既定 0）。
        /// 実際に使う値は <see cref="Rules"/> の <see cref="ScoreRules.WrongDelta"/>（#18 で設定化した）。
        /// </summary>
        public const int DefaultIncorrectPoints = ScoreRules.DefaultWrongPoints;

        /// <summary>
        /// 出題から早押し受付開始 T0 までに許す最大の間隔（秒）。
        /// 読み上げ（#23）がどれだけ長くてもこの範囲に収まる想定で、
        /// これを超える時刻は時刻破綻・誤配線として棄却する（10 分）。
        /// </summary>
        public const double MaxReadingDurationSec = 600.0;

        /// <summary>診断用に保持するフェーズ履歴の上限。超えた分は古いものから捨てる。</summary>
        private const int PhaseHistoryCapacity = 128;

        private readonly QuizTimeLimits _limits;
        private readonly QuizRules _rules;

        /// <summary>
        /// まだ押せる参加者が居るかの判定（#200）。誤答後の再開放の直前と、受付中の毎 tick に見る。
        /// null なら常に居るものとして扱う。
        /// </summary>
        private readonly Func<bool> _hasEligibleBuzzers;
        private readonly List<QuizPhase> _phaseHistory = new List<QuizPhase>();
        private readonly ReadOnlyCollection<QuizPhase> _phaseHistoryView;
        private readonly List<string> _answers = new List<string>();

        /// <summary>現在の問題で既に誤答したクライアント（再開放時の受付対象外、docs/network.md §6.4）。</summary>
        private readonly List<ulong> _wrongAnswerers = new List<ulong>();

        private readonly ReadOnlyCollection<ulong> _wrongAnswerersView;

        private BuzzArbiter _arbiter;
        private double _readingEndServerTime;

        /// <summary>
        /// 制限時間・進行規則を指定して生成する。生成直後は <see cref="QuizPhase.Lobby"/>。
        /// </summary>
        /// <param name="limits">制限時間。null なら <see cref="QuizTimeLimits.Default"/>。</param>
        /// <param name="rules">得点・再開放・再入力の規則。null なら <see cref="QuizRules.Default"/>。</param>
        /// <param name="hasEligibleBuzzers">
        /// 「まだ押せる参加者が居るか」を返す判定（判定シーム、#200）。<see cref="QuizStateMachine"/> は
        /// 接続状況・名簿を持たないため、呼び出し側（<c>GameSession</c>）が <see cref="BuzzEligibility"/> と
        /// <see cref="IsPenalized"/> で判定して渡す（接続中の参加者と、席を保持している切断中の参加者を数える。
        /// 参加者が 0 人なら true）。居なければ、誤答後は受付を開き直さずに Result へ進み、
        /// 受付中（受理済みの押下が無いとき）は時間切れを待たずに Result へ進む
        /// （<see cref="QuizEvent.BuzzClosedNoEligibleBuzzers"/> / <see cref="QuizJudgement.NoEligibleBuzzers"/>、
        /// docs/network.md §6.6）。
        /// null なら常に「居る」とみなし、従来どおり <c>buzz.timeLimitSec</c> の時間切れまで待つ。
        /// </param>
        public QuizStateMachine(
            QuizTimeLimits limits = null,
            QuizRules rules = null,
            Func<bool> hasEligibleBuzzers = null)
        {
            _limits = limits ?? QuizTimeLimits.Default;
            _rules = rules ?? QuizRules.Default;
            _hasEligibleBuzzers = hasEligibleBuzzers;
            _phaseHistoryView = new ReadOnlyCollection<QuizPhase>(_phaseHistory);
            _wrongAnswerersView = new ReadOnlyCollection<ulong>(_wrongAnswerers);
            _phaseHistory.Add(QuizPhase.Lobby);
            Scores = new ScoreBoard(_rules.Score);
            Penalties = PenaltyTracker.Empty;
        }

        /// <summary>制限時間の設定。</summary>
        public QuizTimeLimits Limits => _limits;

        /// <summary>得点・誤答後の再開放・回答の再入力に関する規則。</summary>
        public QuizRules Rules => _rules;

        /// <summary>現在のフェーズ。</summary>
        public QuizPhase Phase { get; private set; } = QuizPhase.Lobby;

        /// <summary>現在の問題インデックス。未出題なら -1。</summary>
        public int QuestionIndex { get; private set; } = -1;

        /// <summary>現在のフェーズに入ったサーバー時刻（秒）。残り時間はクライアントがこの値から計算する。</summary>
        public double PhaseStartServerTime { get; private set; }

        /// <summary>早押し受付開始時刻 T0（サーバー時刻軸の秒）。受付前は 0（docs/network.md §6.3）。</summary>
        public double BuzzOpenServerTime { get; private set; }

        /// <summary>早押しロック保持者。ロック中でなければ <see cref="NoClientId"/>。</summary>
        public ulong LockedClientId { get; private set; } = NoClientId;

        /// <summary>直近の判定結果。</summary>
        public QuizJudgement LastJudgement { get; private set; } = QuizJudgement.None;

        /// <summary>直近の早押し裁定結果（同着抽選の有無・時刻補正の有無を含む）。未確定なら null。</summary>
        public BuzzResolution? LastResolution { get; private set; }

        /// <summary>直近に受理した回答文字列。未受理なら空文字。</summary>
        public string LastAnswerText { get; private set; } = string.Empty;

        /// <summary>
        /// 現在の問題の代表的な正解（先頭の候補）。未出題なら空文字。
        /// Result フェーズでのみクライアントへ送ってよい。
        /// </summary>
        public string CorrectAnswer => _answers.Count > 0 ? _answers[0] : string.Empty;

        /// <summary>フェーズ履歴（診断・テスト用）。先頭は <see cref="QuizPhase.Lobby"/>。</summary>
        public IReadOnlyList<QuizPhase> PhaseHistory => _phaseHistoryView;

        /// <summary>早押し受付が開いてから 1 件以上の押下を受理しているか。</summary>
        public bool HasBuzzCandidates => _arbiter != null && _arbiter.Candidates.Count > 0;

        /// <summary>
        /// 早押し受付開始時刻 T0 を指定する（TTS 連携 #23 のためのフック）。Reading 中のみ受理する。
        /// </summary>
        /// <remarks>
        /// docs/network.md §6.3 の T0 の定義に対応する中立な入口。
        /// <c>buzz.allowDuringReading = true</c> なら読み上げ開始（<c>playAtServerTime</c>）、
        /// <c>false</c> なら読み上げ完了時刻を渡す。呼び分けは <c>GameSession</c> 側の
        /// <c>NotifyReadingStarted</c> / <c>NotifyReadingCompleted</c> で行う。
        /// 出題時刻より前、または出題から <see cref="MaxReadingDurationSec"/> 秒を超えて先の時刻は
        /// 時刻破綻・誤配線として棄却する（受付前に押下を受理してしまう / 永久に受付が開かないのを防ぐ）。
        /// </remarks>
        /// <param name="buzzOpenServerTime">受付開始のサーバー時刻（秒）。</param>
        /// <param name="reason">拒否理由。成功時は <see cref="QuizReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool SetBuzzOpenTime(double buzzOpenServerTime, out QuizReject reason)
        {
            if (Phase != QuizPhase.Reading)
            {
                reason = QuizReject.InvalidPhase;
                return false;
            }

            if (!double.IsFinite(buzzOpenServerTime))
            {
                reason = QuizReject.NonFiniteTime;
                return false;
            }

            // Reading に入った時刻（＝出題時刻）が下限。
            if (buzzOpenServerTime < PhaseStartServerTime)
            {
                reason = QuizReject.InvalidTime;
                return false;
            }

            if (buzzOpenServerTime > PhaseStartServerTime + MaxReadingDurationSec)
            {
                reason = QuizReject.InvalidTime;
                return false;
            }

            _readingEndServerTime = buzzOpenServerTime;
            reason = QuizReject.None;
            return true;
        }

        /// <summary>
        /// 押下を受理する（BuzzOpen 中のみ）。検証は <see cref="BuzzArbiter"/> に委譲する。
        /// </summary>
        /// <param name="clientId">送信元クライアント ID（RPC の SenderClientId から取ること）。</param>
        /// <param name="reportedTime">クライアントが報告した押下時刻（<c>LocalTime.Time</c>）。</param>
        /// <param name="serverNow">サーバーが受信した時刻（秒）。</param>
        /// <param name="reason">棄却理由。受理時は <see cref="BuzzReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool AcceptBuzz(ulong clientId, double reportedTime, double serverNow, out BuzzReject reason)
        {
            if (IsPaused)
            {
                // 司会が一時停止中は押下を受け付けない（#20）。
                reason = BuzzReject.Paused;
                return false;
            }

            if (Phase != QuizPhase.BuzzOpen || _arbiter == null)
            {
                reason = BuzzReject.NotOpen;
                return false;
            }

            return _arbiter.Accept(clientId, reportedTime, serverNow, out reason);
        }

        /// <summary>
        /// 回答を受理する（Answering 中、ロック保持者本人のみ）。
        /// 正誤は <see cref="AnswerMatcher"/> で判定し、フェーズを Judging に進める。
        /// 得点の反映は Judging からの遷移（<see cref="Tick"/>）で行う。
        /// </summary>
        /// <remarks>
        /// 最初の送信で判定が確定する（<c>answer.singleAttemptOnly</c> 既定 true の挙動）。
        /// <c>answer.singleAttemptOnly = false</c>（制限時間内の再送信を許す）の挙動は本 issue では
        /// 実装せず、常に 1 回のみとして扱う（docs/room-settings.md §1 の注記。
        /// 本人へ「誤答なのでもう一度」を伝える RPC と合わせて #26 で実装する）。
        /// </remarks>
        /// <param name="clientId">送信元クライアント ID。</param>
        /// <param name="text">回答文字列。</param>
        /// <param name="serverNow">サーバーが受信した時刻（秒）。</param>
        /// <param name="reason">棄却理由。受理時は <see cref="AnswerReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool SubmitAnswer(ulong clientId, string text, double serverNow, out AnswerReject reason)
        {
            if (IsPaused)
            {
                // 司会が一時停止中は回答を受け付けない（#20）。
                reason = AnswerReject.Paused;
                return false;
            }

            if (Phase != QuizPhase.Answering)
            {
                reason = AnswerReject.NotAnswering;
                return false;
            }

            if (clientId != LockedClientId)
            {
                reason = AnswerReject.NotLockedPlayer;
                return false;
            }

            if (AnswerAttemptCount > 0)
            {
                // 最初の送信で必ず Judging に移るため、通常はこの手前の NotAnswering で弾かれる。
                // 将来 singleAttemptOnly=false を実装したときに意味を持つ保険の分岐（到達不能）。
                reason = AnswerReject.AlreadyAttempted;
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                reason = AnswerReject.Empty;
                return false;
            }

            if (text.Length > MaxAnswerLength)
            {
                reason = AnswerReject.TooLong;
                return false;
            }

            if (TextRules.ContainsControlCharacter(text))
            {
                // 改行・タブ等を含む回答は UI 表示やログを壊すので受け付けない（docs/network.md §9）。
                reason = AnswerReject.InvalidCharacter;
                return false;
            }

            if (!double.IsFinite(serverNow))
            {
                // 他の進行操作（StartQuestion / SetReadingCompleted / Finish）と同じ境界検証。
                // serverNow はサーバー自身の時刻なので、異常値は呼び出し側の不具合として棄却する。
                reason = AnswerReject.NonFiniteTime;
                return false;
            }

            AnswerAttemptCount++;
            LastAnswerText = text;
            LastJudgement = AnswerMatcher.IsCorrect(text, _answers) ? QuizJudgement.Correct : QuizJudgement.Wrong;
            SetPhase(QuizPhase.Judging, serverNow);
            reason = AnswerReject.None;
            return true;
        }

        /// <summary>
        /// 全問終了として締める（Result → Finished）。終了時点の得点は <see cref="FinalScores"/> に確定する。
        /// 複数問のセッションでは <see cref="AdvanceToNextQuestion"/> が「次が無いとき」に本メソッドを呼ぶ（#19）。
        /// </summary>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        /// <param name="reason">拒否理由。成功時は <see cref="QuizReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool Finish(double serverNow, out QuizReject reason)
        {
            if (Phase != QuizPhase.Result)
            {
                reason = QuizReject.InvalidPhase;
                return false;
            }

            if (!double.IsFinite(serverNow))
            {
                reason = QuizReject.NonFiniteTime;
                return false;
            }

            SetPhase(QuizPhase.Finished, serverNow);
            reason = QuizReject.None;
            return true;
        }

        private void SetPhase(QuizPhase next, double serverNow)
        {
            Phase = next;
            PhaseStartServerTime = serverNow;

            if (next == QuizPhase.Finished)
            {
                // 全問終了時点の得点を確定させる（以後は得点が動かない、#19）。
                FinalScores = Scores;
            }

            _phaseHistory.Add(next);
            if (_phaseHistory.Count > PhaseHistoryCapacity)
            {
                _phaseHistory.RemoveAt(0);
            }
        }
    }
}
