using System;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Reveal;
using TsumugiQuiz.Network;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、問題文の文字送り表示（ノベルゲーム風、issue #144、
    /// docs/requirements.md FR-43、docs/tts.md §6.8）をまとめた部分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 「いま何文字見せるか」の計算は Core の <see cref="QuestionRevealSchedule"/>（純関数・不変）に置き、
    /// 本ファイルはイベントの受け取り・時計・描画だけを行う。表示は各クライアントのローカル処理で、
    /// ネットワーク同期はしない（止める・全文にする契機は既存の <see cref="GameSession"/> のイベント）。
    /// </para>
    /// <list type="bullet">
    ///   <item><description>出題（<see cref="GameSession.QuestionShown"/>）: <see cref="QuestionRevealPolicy"/> で進め方を決める</description></item>
    ///   <item><description>再生開始時刻（<see cref="TtsSyncCoordinator.ReadingScheduled"/>）: 読み上げ時間を文字数で按分して同期する</description></item>
    ///   <item><description>早押し受付の開始（Phase = BuzzOpen）: 再生開始時刻がまだ届いていなければ固定速度で送り始める</description></item>
    ///   <item><description>早押しの確定（<see cref="GameSession.BuzzLocked"/>）: 止める</description></item>
    ///   <item><description>誤答・お手つき後の再開放（<see cref="GameSession.BuzzReopened"/>）: 再開して読み上げの位置へ追いつく</description></item>
    ///   <item><description>判定確定・時間切れ（<see cref="GameSession.QuestionResolved"/>）/ Result 以降: 全文表示</description></item>
    /// </list>
    /// <para>
    /// 文字が増えるたびに後続の要素（残り時間バー・早押しボタン）が下へずれないよう、
    /// 全文を持つ不可視の <c>question-text-sizer</c> で高さを先に確保し、その上に
    /// <c>question-text-label</c>（表示中の部分）を重ねて描く（game-view.uxml / theme-views-game.uss）。
    /// </para>
    /// </remarks>
    public sealed partial class GameView
    {
        /// <summary>文字送り中の描画間隔（ミリ秒）。1 文字 80ms 程度の速度でも取りこぼさない細かさにする。</summary>
        internal const long RevealTickIntervalMs = 16;

        private Label _questionTextSizerLabel;
        private TtsSyncCoordinator _revealCoordinator;
        private IVisualElementScheduledItem _revealScheduled;

        private string _revealText = string.Empty;
        private int[] _revealElementStarts = Array.Empty<int>();
        private QuestionRevealSchedule _reveal = QuestionRevealSchedule.Full(0);
        private int _revealQuestionIndex = -1;
        private int _renderedRevealCount = -1;

        /// <summary>いまの文字送りの状態（PlayMode テスト用）。</summary>
        internal QuestionRevealSchedule CurrentReveal => _reveal;

        /// <summary>文字送りの描画更新がスケジュールされているか（PlayMode テスト用。OnHide 後は false）。</summary>
        internal bool IsRevealTickScheduled => _revealScheduled != null && _revealScheduled.isActive;

        /// <summary>表示側の時計。フレームレートや <c>Time.timeScale</c> に左右されない実時間を使う。</summary>
        private static double RevealNow => Time.realtimeSinceStartupAsDouble;

        /// <summary>UI 要素を取得し、表示を空にする。<see cref="GameView.OnShow"/> から呼ぶ。</summary>
        private void InitializeQuestionReveal(VisualElement root)
        {
            // 見つからなくても文字送り自体は動く（高さの先取りだけができない）ので必須要素にはしない。
            // #206: 文字送りの表示欄（question-text-label）と同じく平文にする。そろえないと高さの先取りがずれる。
            _questionTextSizerLabel = PlainText.Apply(root.Q<Label>("question-text-sizer"));
            SetRevealText(-1, string.Empty, QuestionRevealSchedule.Full(0));
        }

        /// <summary>購読を外し、描画の更新を止める。<see cref="GameView.OnHide"/> から呼ぶ。</summary>
        private void TeardownQuestionReveal()
        {
            StopRevealTick();
            UnsubscribeReadingScheduled();
            _questionTextSizerLabel = null;
            _revealText = string.Empty;
            _revealElementStarts = Array.Empty<int>();
            _reveal = QuestionRevealSchedule.Full(0);
            _revealQuestionIndex = -1;
            _renderedRevealCount = -1;
        }

        /// <summary>再生開始時刻の通知を購読する（セッション取得時）。</summary>
        private void SubscribeReadingScheduled()
        {
            UnsubscribeReadingScheduled();
            _revealCoordinator = _session != null ? _session.GetComponent<TtsSyncCoordinator>() : null;
            if (_revealCoordinator != null)
            {
                _revealCoordinator.ReadingScheduled += HandleReadingScheduled;
            }
        }

        private void UnsubscribeReadingScheduled()
        {
            if (_revealCoordinator != null)
            {
                _revealCoordinator.ReadingScheduled -= HandleReadingScheduled;
            }

            _revealCoordinator = null;
        }

        /// <summary>
        /// 出題（<see cref="GameSession.QuestionShown"/>。通常の配信・再同期）を受けたときに文字送りを始める。
        /// </summary>
        /// <remarks>
        /// 再同期（途中参加・再接続）は、この PC が既に再生開始時刻を受け取っていればその時間軸で途中から送る。
        /// 受け取っていなくても受付前・受付中（Reading / BuzzOpen）なら読み上げ待ちから始め、受付開始の時点から
        /// 固定速度で送る（再接続では読み上げ同期に乗れないため、不利側に倒す。#144 再レビュー M-2）。
        /// それ以外は全文。受付・回答の最中に合流した場合はフェーズにも合わせる。
        /// 通常の配信ではフェーズを見ず、既知の再生開始時刻も使わない（出題の RPC はフェーズの同期より先に届きうるため
        /// 前問の Result を見て全文にしないように。また再生開始時刻は必ず出題の後に届くので、既知の値は前のゲームの
        /// 同じ問題番号のものでしかありえない。#144 再レビュー NH-1）。
        /// </remarks>
        private void BeginQuestionReveal(int questionIndex, QuestionDto question, QuestionShownSource source)
        {
            StartQuestionReveal(questionIndex, question, source, useKnownReading: source == QuestionShownSource.Resync);
            if (source == QuestionShownSource.Resync && _session != null)
            {
                ApplyJoinedPhaseToReveal(_session.Phase.Value);
            }
        }

        /// <summary>
        /// View を開いた時点で進行中だった問題の表示を復元する（<see cref="GameView.RefreshFromCurrentState"/>、
        /// #95 の取りこぼし復元 <see cref="GameView.TryRestoreMissedQuestion"/> を含む）。
        /// </summary>
        /// <remarks>
        /// #144 レビュー H-1: 受付前・受付中（Reading / BuzzOpen）や回答中に全文を出すと、この PC だけ受付前に
        /// 全文が読めてしまう。判定前なら通常の出題と同じ経路で始め、取りこぼした再生開始時刻
        /// （<c>PlayAtRpc</c>）は <see cref="TtsSyncCoordinator"/> が覚えている値から追いつく。Result 以降は全文。
        /// </remarks>
        private void RestoreQuestionReveal(int questionIndex, QuestionDto question)
        {
            var phase = _session != null ? _session.Phase.Value : QuizPhase.Result;
            if (!QuestionRevealPolicy.IsQuestionInProgress(phase))
            {
                var text = GameViewPresenter.ToQuestionDisplayText(question?.Text);
                SetRevealText(questionIndex, text, QuestionRevealSchedule.Full(RevealText.GetTextElementStarts(text).Length));
                return;
            }

            StartQuestionReveal(questionIndex, question, QuestionShownSource.Distribution, useKnownReading: true);
            ApplyJoinedPhaseToReveal(phase);
        }

        /// <param name="useKnownReading">
        /// <see cref="TtsSyncCoordinator"/> が覚えている再生開始時刻を使うか。View の復元と再同期だけ true
        /// （通常の配信で使うと、前のゲームの同じ問題番号の値を拾ってしまう。#144 再レビュー NH-1）。
        /// </param>
        private void StartQuestionReveal(
            int questionIndex, QuestionDto question, QuestionShownSource source, bool useKnownReading)
        {
            var text = GameViewPresenter.ToQuestionDisplayText(question?.Text);
            var total = RevealText.GetTextElementStarts(text).Length;
            var msPerChar = ResolveRevealMsPerChar();
            var playAtServerTime = 0d;
            var durationSec = 0d;
            var hasKnownReading = useKnownReading
                && TryGetKnownReading(questionIndex, out playAtServerTime, out durationSec);
            var phase = _session != null ? _session.Phase.Value : QuizPhase.Lobby;
            var canRevealOnResync = QuestionRevealPolicy.CanRevealOnResync(hasKnownReading, phase);
            var shouldReveal = question != null
                && QuestionRevealPolicy.ShouldReveal(question.Type, source, _isModerator, msPerChar, canRevealOnResync);

            // 再同期は既知の読み上げに合わせるか、受付開始を待って固定速度にするので、読み上げ待ちから始める。
            var expectsReading = shouldReveal && (source == QuestionShownSource.Resync || ExpectsRoomReading());
            var roomHasReading = _revealCoordinator != null && _revealCoordinator.ReadingEnabled.Value;
            var schedule = QuestionRevealPolicy.CreateInitial(
                total, RevealNow, shouldReveal, expectsReading, msPerChar, roomHasReading);
            SetRevealText(questionIndex, text, schedule);

            if (hasKnownReading)
            {
                HandleReadingScheduled(questionIndex, playAtServerTime, durationSec);
            }
        }

        /// <summary>
        /// 途中から見た（View の復元・再同期）ときに、いまのフェーズへ表示を合わせる。
        /// 受付が既に開いていれば読み上げ待ちをやめて固定速度にし、誰かが回答中なら止め、判定後なら全文にする。
        /// </summary>
        private void ApplyJoinedPhaseToReveal(QuizPhase phase)
        {
            if (phase == QuizPhase.Result || phase == QuizPhase.Finished)
            {
                CompleteQuestionReveal();
                return;
            }

            var now = RevealNow;
            if (phase == QuizPhase.BuzzOpen || QuestionRevealPolicy.IsBuzzLockedPhase(phase))
            {
                UpdateReveal(_reveal.FallBackToFixedSpeed(ResolveBuzzOpenLocalTime(now), now));
            }

            if (QuestionRevealPolicy.IsBuzzLockedPhase(phase))
            {
                FreezeQuestionReveal();
            }
        }

        /// <summary>
        /// 受付開始時刻（<see cref="GameSession.BuzzOpenServerTime"/>）を表示側の時計へ写す（#144 再レビュー LOW）。
        /// 固定速度へ切り替える起点を「フェーズの同期が届いた時刻」ではなく受付開始そのものにそろえる。
        /// 値が使えない（未設定・非有限・未来）場合は <paramref name="now"/>。
        /// </summary>
        private double ResolveBuzzOpenLocalTime(double now)
        {
            var manager = _session != null ? _session.NetworkManager : null;
            if (manager == null)
            {
                return now;
            }

            var buzzOpenServerTime = _session.BuzzOpenServerTime.Value;
            if (double.IsNaN(buzzOpenServerTime) || double.IsInfinity(buzzOpenServerTime) || buzzOpenServerTime <= 0d)
            {
                return now;
            }

            var local = QuestionRevealPolicy.ToLocalTime(buzzOpenServerTime, manager.LocalTime.Time, now);
            return local > now ? now : local;
        }

        /// <summary>
        /// この問題の再生開始時刻を既に受け取っているか（<see cref="TtsSyncCoordinator.LastReadingQuestionIndex"/> ほか）。
        /// </summary>
        private bool TryGetKnownReading(int questionIndex, out double playAtServerTime, out double durationSec)
        {
            playAtServerTime = 0d;
            durationSec = 0d;
            if (_revealCoordinator == null || questionIndex < 0
                || _revealCoordinator.LastReadingQuestionIndex != questionIndex)
            {
                return false;
            }

            playAtServerTime = _revealCoordinator.LastPlayAtServerTime;
            durationSec = _revealCoordinator.LastDurationSec;
            return true;
        }

        /// <summary>
        /// 読み上げの再生開始時刻が届いたとき（<see cref="TtsSyncCoordinator.ReadingScheduled"/>、全ピア）。
        /// 読み上げ時間を文字数で按分して、読み上げの進行に合わせる。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。表示中の問題と違えば捨てる。</param>
        /// <param name="playAtServerTime">再生開始時刻（サーバー時刻軸の秒）。</param>
        /// <param name="durationSec">ホストの読み上げ時間（秒）。0 なら読み上げなしとして固定速度で送る。</param>
        internal void HandleReadingScheduled(int questionIndex, double playAtServerTime, double durationSec)
        {
            if (questionIndex != _revealQuestionIndex || _reveal.Mode == QuestionRevealMode.Full)
            {
                return;
            }

            var manager = _session != null ? _session.NetworkManager : null;
            if (manager == null || double.IsNaN(playAtServerTime) || double.IsInfinity(playAtServerTime))
            {
                return;
            }

            var now = RevealNow;
            var readingStart = QuestionRevealPolicy.ToLocalTime(playAtServerTime, manager.LocalTime.Time, now);

            // durationSec が 0（ホストが読み上げ時間を報告できなかった）なら、readingStart（= 受付開始）を起点に
            // 固定速度で送る（#144 レビュー L-1。QuestionRevealSchedule.WithReading）。
            UpdateReveal(_reveal.WithReading(readingStart, durationSec, now));

            // 実機検証で文字送りの時間軸をログから追えるようにする（開始までの秒数・1 文字あたりの秒数）。
            Debug.Log(
                $"[GameView] 問題 {questionIndex} の文字送りを読み上げに合わせます"
                + $"（再生開始まで {readingStart - now:F2} 秒、読み上げ {durationSec:F2} 秒、{_reveal}）。");
        }

        /// <summary>
        /// フェーズの変化に合わせて文字送りを進める・止める（<see cref="GameView.HandlePhaseChanged"/> から呼ぶ）。
        /// 早押しの確定・再開放・判定は RPC（イベント）でも届くが、フェーズ（<c>NetworkVariable</c>）とは
        /// 到着順が保証されないため、両方から同じ操作を行う（どの操作も何度呼んでも同じ結果になる）。
        /// </summary>
        /// <param name="phase">新しいフェーズ。</param>
        private void UpdateQuestionRevealForPhase(QuizPhase phase)
        {
            switch (phase)
            {
                case QuizPhase.BuzzOpen:
                    // 受付開始（初回）: 再生開始時刻をまだ待っていたなら、読み上げなしの進行とみなして
                    // 固定速度で送り始める（ホストが読み上げられない構成など）。
                    // 誤答後の再開放: 止めていた送りを再開する。
                    var now = RevealNow;
                    UpdateReveal(_reveal.Resume(now).FallBackToFixedSpeed(ResolveBuzzOpenLocalTime(now), now));
                    break;

                case QuizPhase.Locked:
                case QuizPhase.Answering:
                case QuizPhase.Judging:
                    FreezeQuestionReveal();
                    break;

                case QuizPhase.Result:
                case QuizPhase.Finished:
                    CompleteQuestionReveal();
                    break;
            }
        }

        /// <summary>誰かが早押しした（<see cref="GameSession.BuzzLocked"/>）。文字送りを止める。</summary>
        private void FreezeQuestionReveal() => UpdateReveal(_reveal.Freeze(RevealNow));

        /// <summary>誤答・お手つきの後に受付を再開放した（<see cref="GameSession.BuzzReopened"/>）。再開する。</summary>
        private void ResumeQuestionReveal() => UpdateReveal(_reveal.Resume(RevealNow));

        /// <summary>判定確定・時間切れ・結果表示。全文表示へ切り替える。</summary>
        private void CompleteQuestionReveal() => UpdateReveal(_reveal.ShowAll());

        private void SetRevealText(int questionIndex, string text, QuestionRevealSchedule schedule)
        {
            _revealQuestionIndex = questionIndex;
            _revealText = text;
            _revealElementStarts = RevealText.GetTextElementStarts(text);
            _renderedRevealCount = -1;

            if (_questionTextSizerLabel != null)
            {
                _questionTextSizerLabel.text = text;
            }

            UpdateReveal(schedule);
        }

        private void UpdateReveal(QuestionRevealSchedule schedule)
        {
            _reveal = schedule;
            RenderReveal();

            if (_reveal.IsSettled(RevealNow))
            {
                StopRevealTick();
            }
            else
            {
                StartRevealTick();
            }
        }

        private void TickReveal()
        {
            var now = RevealNow;
            RenderReveal();
            if (_reveal.IsSettled(now))
            {
                StopRevealTick();
            }
        }

        private void RenderReveal()
        {
            if (_questionTextLabel == null)
            {
                return;
            }

            var count = _reveal.VisibleCount(RevealNow);
            if (count == _renderedRevealCount)
            {
                return;
            }

            _renderedRevealCount = count;
            _questionTextLabel.text = RevealText.Take(_revealText, _revealElementStarts, count);
        }

        private void StartRevealTick()
        {
            if (_revealScheduled != null || _root == null)
            {
                return;
            }

            _revealScheduled = _root.schedule.Execute(TickReveal).Every(RevealTickIntervalMs);
        }

        private void StopRevealTick()
        {
            _revealScheduled?.Pause();
            _revealScheduled = null;
        }

        /// <summary>ルーム設定 <c>question.revealMsPerChar</c>。同期前・取得できない場合は既定値。</summary>
        private int ResolveRevealMsPerChar()
        {
            var current = _session != null && _session.SettingsSync != null ? _session.SettingsSync.Current : null;
            return current?.QuestionRevealMsPerChar ?? QuestionRevealSchedule.DefaultMsPerChar;
        }

        /// <summary>
        /// 部屋として読み上げが行われる見込みがあるか（ルーム設定 <c>tts.enabled</c>）。
        /// </summary>
        /// <remarks>
        /// 統括判断（issue #144、B 案）: <b>この PC が読み上げるかどうかは見ない</b>。同意撤回・読み上げ未準備で
        /// 自分は音を鳴らさない PC も、ホストの読み上げの時間軸（<c>ReadingScheduled</c>）に合わせて表示する。
        /// 出題と同時に固定速度で送ると、受付開始（= 再生開始時刻）より前に全文が読めてしまい、
        /// 早押しの公平性が崩れるため。部屋として読み上げが無い場合（<c>tts.enabled = false</c>、
        /// 受付が既に開いている）だけ最初から固定速度で送る。読み上げを待っている間に再生開始時刻が届かないまま
        /// 受付が開いた場合は、その時点で固定速度へ切り替える（<see cref="UpdateQuestionRevealForPhase"/>）。
        /// 待ちの上限タイマーは持たない（#144 レビュー M-1）。
        /// </remarks>
        private bool ExpectsRoomReading()
        {
            if (_revealCoordinator == null || !_revealCoordinator.ReadingEnabled.Value)
            {
                return false;
            }

            if (_session.Phase.Value == QuizPhase.BuzzOpen)
            {
                // 受付が既に開いている = 読み上げを待たない進行（ホストが読み上げられない等）。
                return false;
            }

            return true;
        }
    }
}
