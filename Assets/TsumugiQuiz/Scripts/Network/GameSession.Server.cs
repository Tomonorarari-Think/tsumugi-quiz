using System;
using TsumugiQuiz.Core;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、サーバー（ホスト）だけが呼ぶ進行 API をまとめた部分。
    /// 出題の開始、早押し受付開始 T0 の指定（TTS 連携 #23 のフック）、セッションの終了を扱う。
    /// 状態・フェーズ進行は GameSession.cs、RPC は GameSession.Rpc.cs にある。
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>
        /// サーバー側の初期設定。ホストが出題を始める前に 1 度呼ぶ。
        /// </summary>
        /// <param name="questionSource">問題の供給元（ホストの <see cref="QuestionRepository"/> 由来）。</param>
        /// <param name="limits">
        /// 制限時間。null なら docs/room-settings.md の既定値。
        /// <b>ここで渡した値がそのまま使われるのは <see cref="StartSession"/> を経由しない単問経路
        /// （<see cref="StartQuestion"/> を直接呼ぶ）だけ</b>で、<see cref="StartSession"/> を呼ぶと
        /// 「引数 → <see cref="RoomSettingsSync.Current"/>（ロビーのルーム設定） → 前回の進行設定 →
        /// <see cref="SessionSettings.Default"/>」の優先順位で解決した値に置き換わる
        /// （#27、docs/network.md §12.2）。実際に使う値はゲーム開始操作の時点で
        /// <see cref="RoomSettingsSync"/> へ書き出され、クライアントへ読み取り専用で同期される
        /// （範囲外の値はルーム設定側でクランプされ、差分が警告ログに出る）。
        /// </param>
        /// <param name="random">同着抽選に使う乱数源。null なら <see cref="CryptoRandom"/>。</param>
        /// <param name="scoring">
        /// 得点・お手つきペナルティ・誤答後の再開放の設定（<c>TsumugiQuiz.Room</c>）。
        /// null なら docs/room-settings.md の既定値（正解 +10 / 誤答 0 / 次問休み / 再開放する）。
        /// <paramref name="limits"/> と同じく、<see cref="StartSession"/> を呼ぶとそちらの解決結果に置き換わる。
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="questionSource"/> が null のとき。</exception>
        /// <exception cref="InvalidOperationException">サーバー以外で呼ばれたとき。</exception>
        public void Configure(
            IQuestionSource questionSource,
            QuizTimeLimits limits = null,
            IRandom random = null,
            ScoringSettings scoring = null)
        {
            if (questionSource == null)
            {
                throw new ArgumentNullException(nameof(questionSource));
            }

            if (IsSpawned && !IsServer)
            {
                throw new InvalidOperationException("GameSession.Configure はサーバーでのみ呼べます。");
            }

            _questionSource = questionSource;
            _limits = limits ?? QuizTimeLimits.Default;
            _progressSettingsChangedSinceCommit = true;

            // 配信（#13）も同じ供給元から DTO を作る。正解は DTO に写らない（docs/question-data.md §7）。
            if (_distributor != null)
            {
                _distributor.SetQuestionSource(questionSource);
            }

            DisposeOwnedRandom();
            _random = random ?? CreateDefaultRandom();

            // #27: ここで渡された制限時間・得点設定は、ゲーム開始操作の時点で RoomSettingsSync へ
            // 書き出してからロックする（CommitRoomSettingsForStart）。クライアントはそれを読む。
            _scoringSettings = scoring ?? ScoringSettings.Default;
            _rules = _scoringSettings.ToQuizRules();
            _machine = CreateQuizStateMachine();

            // 新しい進行を始めるので、前回の得点表は捨てる（スポーン前なら NetworkList は空のまま）。
            ClearScores();

            // 前回のセッション（#19）の残り（出題列の長さ・終了通知の済み・出題失敗の記録）も捨てる。
            ResetSessionState();
        }

        /// <summary>
        /// 出題を開始する（サーバーのみ）。読み上げが無い設定では、そのまま次の tick で受付が開く。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <returns>出題を開始できたら true。</returns>
        public bool StartQuestion(int questionIndex = 0)
        {
            if (!IsSpawned || !IsServer)
            {
                Debug.LogWarning("[GameSession] 出題はサーバーでのみ開始できます。");
                return false;
            }

            if (_machine == null || _questionSource == null)
            {
                Debug.LogWarning("[GameSession] Configure が済んでいないため出題できません。");
                return false;
            }

            if (!_questionSource.TryGetQuestion(questionIndex, out var question))
            {
                Debug.LogWarning($"[GameSession] 問題インデックス {questionIndex} は範囲外です（問題数 {_questionSource.Count}）。");
                return false;
            }

            if (question.Type == QuestionType.Choice && !question.CorrectIndex.HasValue)
            {
                // QuestionSetValidator（読み込み時）が弾いているはずなので、ここに来る＝検証を通っていない。
                Debug.LogWarning($"[GameSession] correctIndex が無い選択式の問題は出題できません: {question.Id}");
                return false;
            }

            // 配信内容（現在問 + 先読み）は送信前に組み立てて検証する（docs/network.md §8.1、#13）。
            // ここで落ちた場合は 1 バイトも送らず、出題自体を始めない。
            if (_distributor != null && !_distributor.TryPrepareDistribution(questionIndex, out var distributionError))
            {
                Debug.LogError($"[GameSession] 問題を配信できないため出題を中止しました: {distributionError}");
                return false;
            }

            // #27: ここが「ゲーム開始操作」の確定点（docs/room-settings.md §4）。
            // 実際に使う設定を RoomSettingsSync へ書いてからロックするので、
            // クライアントは 1 問目の Reading より前に確定値を受け取る。
            CommitRoomSettingsForStart();

            var serverNow = NetworkManager.ServerTime.Time;

            // 選択式（choice）は早押しを介さず全員が回答する（仮決め: #17、docs/room-settings.md
            // answer.choiceTimeLimitSec）。判定は常に元の choices 配列インデックスで行う（docs/question-data.md §6）。
            bool started;
            QuizReject reason;
            if (question.Type == QuestionType.Choice)
            {
                started = _machine.StartQuestion(
                    questionIndex, question.CorrectIndex.Value, question.Choices.Count, serverNow, out reason);
            }
            else
            {
                started = _machine.StartQuestion(questionIndex, question.Answers, serverNow, out reason);
            }

            if (!started)
            {
                // 用意した配信内容は破棄する（次の出題に持ち越さない）。
                _distributor?.CancelPrepared();
                Debug.LogWarning($"[GameSession] 出題を開始できません（理由: {reason}、フェーズ: {_machine.Phase}）。");
                return false;
            }

            _requestedBuzzOpenTime = null;
            PublishState();

            if (_distributor == null)
            {
                // 配信器が外されている構成では、従来どおり提示だけを行う（クライアントに問題文は届かない）。
                Debug.LogWarning("[GameSession] QuestionDistributor が無いため問題データを配信できません。");
                QuestionShownRpc(questionIndex);
                return true;
            }

            // 受信確認が揃うまで Reading に留める（docs/network.md §8.6）。
            // 保留時刻は配信器が持つ Ack 期限 + マージンで、タイムアウト判定より必ず後になる
            // （同じ NetworkObject 上のコンポーネント順＝ tick 購読順に依存しないため）。
            if (!_machine.SetBuzzOpenTime(_distributor.HoldUntilServerTime, out var holdReason))
            {
                Debug.LogWarning($"[GameSession] 受信確認の待ち時間を設定できませんでした（理由: {holdReason}）。");
            }

            _distributor.DistributePrepared();
            return true;
        }

        /// <summary>
        /// 現在問の配信（受信確認待ち）が終わったときの処理（サーバーのみ、docs/network.md §8.4 / §8.5）。
        /// 問題を提示し、外部から T0 が指定されていなければ早押し受付を開く。
        /// </summary>
        /// <param name="questionIndex">配信が完了した問題インデックス。</param>
        /// <param name="timedOut">全員分の受信確認が揃わずタイムアウトしたか。</param>
        private void HandleDistributionCompleted(int questionIndex, bool timedOut)
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            // 提示はフェーズ判定より前に必ず送る。tick の順番で状態機械が先に Reading を抜けていても、
            // クライアントが問題を表示できないまま取り残されないようにするため。
            QuestionShownRpc(questionIndex);

            if (_machine == null
                || _machine.Phase != QuizPhase.Reading
                || _machine.QuestionIndex != questionIndex)
            {
                // 既に出題が切り替わっている / Reading を抜けている場合、T0 は触らない。
                return;
            }

            ApplyBuzzOpenGate(NetworkManager.ServerTime.Time);
        }

        /// <summary>
        /// 早押し受付開始 T0 を「ゲートの最大値」で確定する（サーバーのみ、Reading 中に呼ぶ）。
        /// ゲートは「問題データの受信確認が終わった時刻」と
        /// 「TTS（#23）が要求した読み上げ開始 / 完了時刻」の 2 つで、遅い方を採用する
        /// （docs/network.md §8.6）。
        /// </summary>
        /// <param name="distributionCompletedServerTime">配信（受信確認）が完了したサーバー時刻。</param>
        private void ApplyBuzzOpenGate(double distributionCompletedServerTime)
        {
            var target = _requestedBuzzOpenTime.HasValue
                ? Math.Max(distributionCompletedServerTime, _requestedBuzzOpenTime.Value)
                : distributionCompletedServerTime;

            if (!_machine.SetBuzzOpenTime(target, out var reason))
            {
                Debug.LogWarning($"[GameSession] 受付開始時刻を設定できませんでした（理由: {reason}）。");
            }
        }

        /// <summary>
        /// 早押し受付開始時刻 T0 を指定する（TTS 連携 #23 のフック、サーバーのみ）。
        /// Reading 中にだけ受理し、指定した時刻に達した tick で受付が開く（docs/network.md §6.3）。
        /// </summary>
        /// <remarks>
        /// 読み上げの有無・<c>buzz.allowDuringReading</c> の値に依存しない中立な入口。
        /// 用途がはっきりしている場合は <see cref="NotifyReadingStarted"/> /
        /// <see cref="NotifyReadingCompleted"/> を使う。
        /// </remarks>
        /// <param name="buzzOpenServerTime">受付開始のサーバー時刻（秒）。出題時刻以降、かつ出題から 600 秒以内。</param>
        /// <returns>受理したら true。</returns>
        /// <remarks>
        /// 実際の T0 は、ここで要求した時刻と「問題データの受信確認が完了した時刻」の
        /// **大きい方**になる（docs/network.md §8.6）。Reading 中にのみ受理するため、
        /// TTS（#23）は出題直後から配信完了までの間に呼ぶこと。
        /// </remarks>
        public bool SetBuzzOpenTime(double buzzOpenServerTime)
        {
            if (!IsSpawned || !IsServer || _machine == null)
            {
                Debug.LogWarning("[GameSession] 受付開始時刻の指定はサーバーでのみ受け付けます。");
                return false;
            }

            if (!_machine.SetBuzzOpenTime(buzzOpenServerTime, out var reason))
            {
                Debug.LogWarning($"[GameSession] 受付開始時刻を受け付けませんでした（理由: {reason}、フェーズ: {_machine.Phase}）。");
                return false;
            }

            _requestedBuzzOpenTime = buzzOpenServerTime;

            // 配信の受信確認を待っている間は、要求時刻が早くても受付を開かない（ゲートの最大値）。
            // 受信確認が終わった時点で ApplyBuzzOpenGate が要求時刻との大きい方へ再設定する。
            if (_distributor != null && _distributor.IsAwaitingAck)
            {
                var gated = Math.Max(buzzOpenServerTime, _distributor.HoldUntilServerTime);
                if (!_machine.SetBuzzOpenTime(gated, out var gateReason))
                {
                    Debug.LogWarning($"[GameSession] 受信確認の待ち時間を設定できませんでした（理由: {gateReason}）。");
                }
            }

            return true;
        }

        /// <summary>
        /// 読み上げ開始（再生予定時刻）を通知する（サーバーのみ）。
        /// <c>buzz.allowDuringReading = true</c>（既定、仮決め K12）のとき、
        /// 読み上げ開始と同時に早押しを受け付けるため、この時刻が T0 になる。
        /// </summary>
        /// <param name="playAtServerTime">TTS の再生開始サーバー時刻（docs/tts.md §6 の <c>playAtServerTime</c>）。</param>
        /// <returns>受理したら true。</returns>
        public bool NotifyReadingStarted(double playAtServerTime) => SetBuzzOpenTime(playAtServerTime);

        /// <summary>
        /// 読み上げ完了を通知する（サーバーのみ）。
        /// <c>buzz.allowDuringReading = false</c> のとき、読み上げ完了時刻が T0 になる
        /// （docs/network.md §7.3 の <c>readingEndServerTime</c>）。
        /// </summary>
        /// <param name="readingEndServerTime">読み上げ完了のサーバー時刻（秒）。</param>
        /// <returns>受理したら true。</returns>
        public bool NotifyReadingCompleted(double readingEndServerTime) => SetBuzzOpenTime(readingEndServerTime);

        /// <summary>
        /// セッションを終了する（Result → Finished、サーバーのみ）。
        /// 本 issue では 1 問のみなので、結果表示のあとホストがこれを呼んで締める。
        /// </summary>
        /// <returns>終了できたら true。</returns>
        public bool FinishSession()
        {
            if (!IsSpawned || !IsServer || _machine == null)
            {
                Debug.LogWarning("[GameSession] セッションの終了はサーバーでのみ行えます。");
                return false;
            }

            if (!_machine.Finish(NetworkManager.ServerTime.Time, out var reason))
            {
                Debug.LogWarning($"[GameSession] セッションを終了できません（理由: {reason}、フェーズ: {_machine.Phase}）。");
                return false;
            }

            PublishState();
            return true;
        }
    }
}
