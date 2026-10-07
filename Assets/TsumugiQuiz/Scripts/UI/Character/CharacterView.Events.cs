using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tts;
using UnityEngine;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// <see cref="CharacterView"/> のうち、イベント源（読み上げ・判定・回答権・出題）の接続とハンドラ
    /// （#24 / #139 / #212）。受け取った通知を <see cref="CharacterStateMachine"/> へ伝えるだけで、
    /// 見た目への反映は <c>CharacterView.cs</c> の <c>ApplyState</c> が行う。
    /// </summary>
    public sealed partial class CharacterView
    {
        /// <summary>
        /// TTS・判定結果のイベント源を接続する。GameView が表示されるタイミングで呼ぶ。
        /// 既存の接続があれば先に解除する（多重登録防止）。
        /// </summary>
        /// <param name="ttsSyncPlayer">読み上げの再生窓口（null 可。TTS 無効環境等）。</param>
        /// <param name="gameSession">
        /// 判定結果（<see cref="GameSession.QuestionResolved"/> / <see cref="GameSession.ChoiceResolved"/>）、
        /// 回答権（<see cref="GameSession.BuzzLocked"/> / <see cref="GameSession.BuzzReopened"/>、#212）と出題
        /// （<see cref="GameSession.QuestionShown"/>、#139 の同意再評価の契機）の発生源（null 可）。
        /// </param>
        /// <param name="localClientIdProvider">
        /// このクライアントの ID を返す関数（#212、null 可）。回答権を得たのが自分かどうかと、
        /// 選択式で自分の正誤を優先するのに使う。null または null を返す間は、常に「他人」として扱う。
        /// </param>
        /// <param name="isLocalChoiceAnswererProvider">
        /// このクライアントが、いまの選択式の問題で選べる立場かを返す関数（#212、null 可）。
        /// 選べる立場で選ばなかったら時間切れ、選べない立場（司会専任のホスト・休み）なら全体の結果にする。
        /// null の間は「選べない立場」として扱う。
        /// </param>
        public void Bind(
            TtsSyncPlayer ttsSyncPlayer,
            GameSession gameSession,
            Func<ulong?> localClientIdProvider = null,
            Func<bool> isLocalChoiceAnswererProvider = null)
        {
            Unbind();

            _ttsSyncPlayer = ttsSyncPlayer;
            _gameSession = gameSession;
            _localClientIdProvider = localClientIdProvider;
            _isLocalChoiceAnswererProvider = isLocalChoiceAnswererProvider;

            if (!ReferenceEquals(_ttsSyncPlayer, null))
            {
                _ttsSyncPlayer.ReadingStarted += HandleReadingStarted;
                _ttsSyncPlayer.ReadingCompleted += HandleReadingCompleted;
            }

            if (!ReferenceEquals(_gameSession, null))
            {
                _gameSession.QuestionResolved += HandleQuestionResolved;
                _gameSession.ChoiceResolved += HandleChoiceResolved;
                _gameSession.BuzzLocked += HandleBuzzLocked;
                _gameSession.BuzzReopened += HandleBuzzReopened;

                // #139: 出題のたびに同意を評価し直す（読み上げ #127 と同じ粒度）。
                _gameSession.QuestionShown += HandleQuestionShown;
            }
        }

        /// <summary>接続済みのイベント源を解除し、状態を待機に戻す（レビュー M4）。</summary>
        public void Unbind()
        {
            if (!ReferenceEquals(_ttsSyncPlayer, null))
            {
                _ttsSyncPlayer.ReadingStarted -= HandleReadingStarted;
                _ttsSyncPlayer.ReadingCompleted -= HandleReadingCompleted;
                _ttsSyncPlayer = null;
            }

            if (!ReferenceEquals(_gameSession, null))
            {
                _gameSession.QuestionResolved -= HandleQuestionResolved;
                _gameSession.ChoiceResolved -= HandleChoiceResolved;
                _gameSession.BuzzLocked -= HandleBuzzLocked;
                _gameSession.BuzzReopened -= HandleBuzzReopened;
                _gameSession.QuestionShown -= HandleQuestionShown;
                _gameSession = null;
            }

            _localClientIdProvider = null;
            _isLocalChoiceAnswererProvider = null;
            _stateMachine.Reset();
        }

        /// <summary>テスト専用: GameSession を用意せずに判定結果の配線を直接検証する（レビュー L5）。</summary>
        internal void SimulateQuestionResolvedForTesting(QuizJudgement judgement) =>
            HandleQuestionResolved(judgement, GameSession.NoClientId, string.Empty, 0, 0);

        /// <summary>テスト専用: 回答権の確定（<see cref="GameSession.BuzzLocked"/>）を直接流す（#212）。</summary>
        internal void SimulateBuzzLockedForTesting(ulong winnerClientId) =>
            HandleBuzzLocked(winnerClientId, 0d, false);

        /// <summary>テスト専用: 受付の開き直し（<see cref="GameSession.BuzzReopened"/>）を直接流す（#212）。</summary>
        internal void SimulateBuzzReopenedForTesting(ulong wrongClientId) =>
            HandleBuzzReopened(wrongClientId, 0d);

        /// <summary>テスト専用: 選択式の一斉判定（<see cref="GameSession.ChoiceResolved"/>）を直接流す（#212）。</summary>
        internal void SimulateChoiceResolvedForTesting(IReadOnlyList<ChoiceAnswerEntry> entries) =>
            HandleChoiceResolved(0, entries);

        /// <summary>テスト専用: 出題（<see cref="GameSession.QuestionShown"/>）を直接流す（#212）。</summary>
        internal void SimulateQuestionShownForTesting() =>
            HandleQuestionShown(0, null, QuestionShownSource.Distribution);

        /// <summary>このクライアントの ID（分からなければ null）。取得に失敗しても例外は外へ出さない。</summary>
        private ulong? SafeGetLocalClientId()
        {
            try
            {
                return _localClientIdProvider?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterView] 自分のクライアント ID を取得できませんでした（{e.GetType().Name}）: {e.Message}");
                return null;
            }
        }

        private void HandleReadingStarted(int questionIndex) => _stateMachine.NotifyReadingStarted();

        private void HandleReadingCompleted(int questionIndex) => _stateMachine.NotifyReadingCompleted();

        private void HandleQuestionResolved(
            QuizJudgement judgement, ulong answererClientId, string correctAnswer, int score, int scoreDelta)
            => _stateMachine.NotifyQuestionResolved(judgement);

        /// <summary>選択式の一斉判定（#212）。自分の正誤を優先して表情を決める（<see cref="CharacterChoiceOutcome"/>）。</summary>
        private void HandleChoiceResolved(int correctChoiceIndex, IReadOnlyList<ChoiceAnswerEntry> entries)
            => _stateMachine.NotifyQuestionResolved(
                CharacterChoiceOutcome.ToJudgement(entries, SafeGetLocalClientId(), SafeIsLocalChoiceAnswerer()));

        /// <summary>このクライアントが選択式で選べる立場か。取得に失敗しても例外は外へ出さず、選べない立場として扱う。</summary>
        private bool SafeIsLocalChoiceAnswerer()
        {
            try
            {
                return _isLocalChoiceAnswererProvider != null && _isLocalChoiceAnswererProvider();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterView] 選択式の回答者かどうかを判定できませんでした（{e.GetType().Name}）: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 回答権の確定（#212）。司会専用モードのホストは押さないので、自分の ID と一致することは無く
        /// 常に「他人」になる。途中参加・再接続の再同期ではこのイベントは届かない（表情は待機のまま、許容）。
        /// </summary>
        private void HandleBuzzLocked(ulong winnerClientId, double lockedAtServerTime, bool wasTie)
        {
            var localClientId = SafeGetLocalClientId();
            _stateMachine.NotifyBuzzLocked(localClientId.HasValue && localClientId.Value == winnerClientId);
        }

        /// <summary>誤答・お手つきで受付が開き直された瞬間（#212）。誰が誤答しても同じ表情にする。</summary>
        private void HandleBuzzReopened(ulong wrongClientId, double buzzOpenServerTime)
            => _stateMachine.NotifyBuzzReopened();

        /// <summary>
        /// 問題が提示されたとき（<see cref="GameSession.QuestionShown"/>、#139）。
        /// 問題の内容自体は立ち絵に関係しないが、<b>出題のたびに同意状況を評価し直す</b>ための契機として使う
        /// （進行中に撤回されていれば、ここで非表示に切り替わる。FR-75 / NFR-08）。
        /// 再同期（<see cref="QuestionShownSource.Resync"/>）由来でも同じ扱いでよい
        /// （評価が 1 回増えるだけで、判定内容は経路に依存しない）。
        /// </summary>
        private void HandleQuestionShown(int index, QuestionDto question, QuestionShownSource source)
        {
            // #212: 前の問題の回答権・誤答の瞬間の表情が残っていれば戻す（結果の表示は保持時間まで残す）。
            _stateMachine.NotifyQuestionShown();
            RefreshVisibility();
        }
    }
}
