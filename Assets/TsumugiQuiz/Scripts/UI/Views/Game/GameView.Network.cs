using TsumugiQuiz.Core;
using TsumugiQuiz.Network;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、<see cref="TsumugiQuiz.Network.GameSession"/> /
    /// <see cref="NetworkService"/> のイベント購読とハンドラをまとめた部分（issue #14）。
    /// SE の発火（<see cref="SePlayer"/>）もここで行う
    /// （docs/architecture.md §3「UI 層が Network のイベントを購読して SePlayer.Play(SeKind) を呼ぶ」）。
    /// </summary>
    public sealed partial class GameView
    {
        private void SubscribeSessionEvents()
        {
            _session.Phase.OnValueChanged += HandlePhaseChanged;
            _session.QuestionShown += HandleQuestionShown;
            _session.BuzzLocked += HandleBuzzLocked;
            _session.BuzzReopened += HandleBuzzReopened;
            _session.QuestionResolved += HandleQuestionResolved;
            _session.ChoiceResolved += HandleChoiceResolved;
            _session.ScoreChanged += HandleScoreChanged;
            _session.ScoreTableChanged += HandleScoreTableChanged;
            _session.IsPaused.OnValueChanged += HandleIsPausedChanged;
            _session.QuestionProgressChanged += HandleQuestionProgressChangedForBuzz; // #194
            SubscribeReadingScheduled();
            SubscribeQuestionImageEvents();
        }

        private void UnsubscribeSessionEvents()
        {
            if (_session == null)
            {
                return;
            }

            _session.Phase.OnValueChanged -= HandlePhaseChanged;
            _session.QuestionShown -= HandleQuestionShown;
            _session.BuzzLocked -= HandleBuzzLocked;
            _session.BuzzReopened -= HandleBuzzReopened;
            _session.QuestionResolved -= HandleQuestionResolved;
            _session.ChoiceResolved -= HandleChoiceResolved;
            _session.ScoreChanged -= HandleScoreChanged;
            _session.ScoreTableChanged -= HandleScoreTableChanged;
            _session.IsPaused.OnValueChanged -= HandleIsPausedChanged;
            _session.QuestionProgressChanged -= HandleQuestionProgressChangedForBuzz; // #194
            UnsubscribeReadingScheduled();
            UnsubscribeQuestionImageEvents();
        }

        /// <summary>一時停止状態が変わったとき（M1）。バッジ表示を更新する。残り時間表示は
        /// <see cref="UpdateTimeDisplay"/> が毎 Tick 自分で <c>IsPaused</c> を見て止める。</summary>
        private void HandleIsPausedChanged(bool previous, bool current)
        {
            UpdatePausedBadge();

            // 統括判断 M-C: 一時停止の切り替えで「次へ」の活性状態も更新する。
            UpdateHostControlsVisible();
        }

        private void HandlePhaseChanged(QuizPhase previous, QuizPhase next)
        {
            UpdatePhaseText(GameViewPresenter.PhaseLabel(next));

            if (next == QuizPhase.BuzzOpen)
            {
                // 新しい受付（初回、または #18 の誤答後再開放）: ローカルの連打防止錠を外す。
                // レビュー H-4: _buzzResultLabel はここではクリアしない。次の問題の提示
                // （HandleQuestionShown）でクリアされるため、BuzzReopened が届く順序に関わらず
                // 「お手つきです」等の案内を消してしまわない。
                _hasBuzzedLocally = false;
            }

            if (next != QuizPhase.Result)
            {
                _resultLabel.text = string.Empty;
            }

            UpdateQuestionRevealForPhase(next);

            UpdateBuzzButtonEnabled();
            UpdateAnswerSectionVisible();
            UpdateQuestionSectionsVisible();
            UpdateHostControlsVisible();

            // #187: 作業領域に収まらないときは、操作が始まる要素（早押し・回答欄・選択肢）を表示範囲へ入れる。
            ScrollForPhase(next);

            if (next == QuizPhase.BuzzOpen)
            {
                // レビュー M-6: フォーカスをボタンに戻す。exit-button / next-button は
                // focusable="false" にしてあるため、Space キーがそれらのクリックとして
                // 誤解釈されることはないが、早押しボタン自体にフォーカスを合わせておくことで
                // アクセシビリティ（Tab / ゲームパッド操作）上も自然な状態にする。
                _buzzButton.Focus();
            }

            if (next == QuizPhase.Finished)
            {
                // ホストが FinishSession() を呼ぶと Phase が全ピアへ同期されるため、
                // 「次へ」を押していないクライアントも含め全員がここで Result View（#20）へ遷移する。
                _router?.ShowView(ViewNames.Result);
            }
        }

        /// <summary>
        /// 問題が提示されたとき（<see cref="GameSession.QuestionShown"/>）。
        /// </summary>
        /// <param name="index">問題インデックス。</param>
        /// <param name="question">配信された問題データ。</param>
        /// <param name="source">
        /// 発火の経路（#109）。途中参加・再接続の再同期（<see cref="QuestionShownSource.Resync"/>）でも
        /// 画面は現在問へ合わせるが、ゲーム開始のジングルは鳴らさない（PR #114 レビュー L-4）。
        /// </param>
        private void HandleQuestionShown(int index, QuestionDto question, QuestionShownSource source)
        {
            _questionIndexLabel.text = $"問題 {index + 1}";
            _resultLabel.text = string.Empty;
            _buzzResultLabel.text = string.Empty;

            // #132 レビュー M2-3 / M3-3: 出題の RPC がフェーズ変更より先に届くと、前問の判定結果パネルが
            // 中身だけ空になって一瞬残る。ここで result-label を空にしておけば、この直後の
            // UpdateQuestionSectionsVisible() → UpdateResultSectionVisible() が
            // 「文言が空なら隠す」条件で閉じてくれる（ここで style を直接いじると、その
            // UpdateQuestionSectionsVisible() 自身に打ち消されてしまう）。

            // レビュー H-5: お手つきによる除外は「次の問題」までの制限のため、ここでクリアする。
            _isExcludedFromBuzzing = false;

            _currentQuestion = question;
            _displayedQuestionIndex = index;

            // issue #185: 画像付きの問題なら配信済みの画像を貼る（無ければ隠して前問の画像を残さない）。
            ShowQuestionImageFor(index);

            // issue #144: 自由入力は文字送り、選択式・再同期・司会画面は全文（GameView.Reveal.cs）。
            BeginQuestionReveal(index, question, source);
            HandleChoiceQuestionShown(question, index);
            UpdateQuestionSectionsVisible();

            // #187 レビュー L2 / L1: 新しい問題の提示ではスクロールを先頭（問題文）へ戻す。フェーズ変更（Reading）より
            // 出題の RPC が後に届いても戻るように、ここでも行う。途中参加・再接続の再同期（Resync）では、
            // 合流した時点のフェーズの操作を表示範囲へ入れる。
            if (source == QuestionShownSource.Resync && _session != null)
            {
                ScrollForPhase(_session.Phase.Value);
            }
            else
            {
                ScrollToTop();
            }

            if (index == 0 && source == QuestionShownSource.Distribution)
            {
                // レビュー L-16: ゲーム開始のジングルは最初の出題のタイミングで鳴らす。
                // 再同期（#109）で 1 問目に合流した場合は「開始」ではないので鳴らさない
                // （PR #114 レビュー L-4。配信済み DTO からの復元経路も HandleQuestionShown を通らない）。
                SePlayer.Instance?.Play(SeKind.Start);
            }
        }

        private void HandleBuzzLocked(ulong winnerClientId, double lockedAtServerTime, bool wasTie)
        {
            // issue #144: 誰かが押した瞬間に文字送りを止める。
            FreezeQuestionReveal();
            _buzzResultLabel.text =
                GameViewPresenter.FormatBuzzWinner(winnerClientId, LocalClientIdOrNull, wasTie, PlayerNameResolver);
        }

        private void HandleBuzzReopened(ulong penalizedClientId, double buzzOpenServerTime)
        {
            // issue #144: 誤答・お手つきで受付を再開放したら、読み上げの位置へ追いついて送りを再開する。
            ResumeQuestionReveal();
            // レビュー H-5: 自分がお手つきの対象なら、この問題の間は再度早押しできないようにする。
            if (TryGetLocalClientId(out var localClientId) && penalizedClientId == localClientId)
            {
                _isExcludedFromBuzzing = true;
                UpdateBuzzButtonEnabled();
            }

            _buzzResultLabel.text =
                GameViewPresenter.FormatBuzzReopened(penalizedClientId, LocalClientIdOrNull, PlayerNameResolver);
        }

        private void HandleQuestionResolved(
            QuizJudgement judgement, ulong answererClientId, string correctAnswer, int totalScore, int scoreDelta)
        {
            _resultLabel.text = GameViewPresenter.FormatJudgement(
                judgement, answererClientId, LocalClientIdOrNull, correctAnswer, scoreDelta, totalScore, PlayerNameResolver);

            // #132 レビュー M3-3: 結果セクションは「Result フェーズ かつ 文言がある」ときだけ開く。
            // 判定の RPC はフェーズ変更とは別に届くため、文言をセットしたここでも表示を更新する。
            UpdateResultSectionVisible();

            // #187 レビュー M1: 判定の RPC がフェーズ同期（Result）より後に届くと、フェーズ変更時点では結果セクションが
            // まだ隠れていてスクロールされない。文言が入って表示されたここで判定結果を表示範囲へ入れる。
            ScrollToResultSection();

            // issue #144: 判定確定（正解・不正解）・時間切れで全文を表示する。
            CompleteQuestionReveal();

            PlayJudgementSe(judgement);
            UpdateHostControlsVisible();
        }

        private void HandleScoreChanged(ulong clientId, int delta, int total)
        {
            if (TryGetLocalClientId(out var localClientId) && clientId == localClientId)
            {
                UpdateScoreLabel(total);
            }
        }

        private void HandleScoreTableChanged()
        {
            if (_session != null && TryGetLocalClientId(out var localClientId))
            {
                UpdateScoreLabel(_session.GetScore(localClientId));
            }
        }

        /// <summary>
        /// ホストから切断されたとき（docs/network.md §2.4、JoinView と同じ作法、レビュー M-8）。
        /// ゲーム中の切断は復帰させず Title へ戻す。理由（日本語に対応づけた文言、#208）はログに残す。
        /// 自分で退出したとき（<see cref="OnConfirmExitClicked"/>）は <c>NetworkService</c> が通知しない（#208）。
        /// </summary>
        /// <param name="reason">日本語に対応づけた切断・拒否理由（<c>NetworkService.DisconnectedFromHost</c>）。</param>
        private void HandleDisconnectedFromHost(string reason)
        {
            UnityEngine.Debug.LogWarning($"[GameView] ホストから切断されたため Title へ戻ります: {reason}");

            _router?.ShowView(ViewNames.Title);
        }

        /// <summary>
        /// クライアント ID からプレイヤー名を解決する既定の実装（#7 のロビー名簿と接続）。
        /// <see cref="PlayerNameResolver"/> の既定値として <see cref="GameView.OnShow"/> から使う。
        /// 同名が同時に接続している場合（再接続経路でのみ起こる。#85、docs/network.md §2.3）は
        /// ロビー一覧と同じ連番（<c>つむぎ #2</c>）を付けて、誰が押したのか分かるようにする。
        /// </summary>
        /// <param name="clientId">解決したいクライアント ID。</param>
        /// <returns>名簿に見つかったプレイヤー名。見つからなければ null（呼び出し側でフォールバックする）。</returns>
        private static string ResolvePlayerName(ulong clientId)
        {
            var lobby = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Lobby : null;
            if (lobby == null)
            {
                return null;
            }

            var snapshot = lobby.GetPlayersSnapshot();
            var index = -1;
            for (var i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].ClientId == clientId)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return null;
            }

            // PlayerEntry.GetDisplayName() は string を返すため、TsumugiQuiz.UI が
            // Unity.Collections（FixedString64Bytes の定義元）を参照せずに済む。表示用に整えた名前を使う（#206）。
            var name = snapshot[index].GetDisplayName();

            // 一覧（LobbyView）と同じ規則で連番を付ける。
            // LobbyView は司会専任のホストを一覧から外すので母集団が 1 件違うが、承認時に
            // 「接続中の同名」は拒否される（docs/network.md §2.3 の判定 3）ため、接続中のホストと
            // 同じ名前のプレイヤーは入って来られない。つまりホスト行が重複の組を作ることはなく、
            // 母集団の差が他の行の番号を動かすこともない。
            var label = PlayerEntryDisplayNames.Resolve(snapshot).GetLabel(index);
            return string.IsNullOrEmpty(label) ? name : label;
        }

        private static void PlayJudgementSe(QuizJudgement judgement)
        {
            SeKind? kind = judgement switch
            {
                QuizJudgement.Correct => SeKind.Correct,
                QuizJudgement.Wrong => SeKind.Wrong,
                QuizJudgement.TimedOut => SeKind.TimeUp,
                QuizJudgement.NoEligibleBuzzers => SeKind.TimeUp, // #200: 効果音は時間切れと同じ
                _ => null,
            };

            if (kind.HasValue)
            {
                SePlayer.Instance?.Play(kind.Value);
            }
        }
    }
}
