using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、選択式（<c>choice</c>）の選択肢表示・シャッフル・回答送信・
    /// 結果ハイライトをまとめた部分（issue #17）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 選択肢は問題提示（Reading）と同時に全員へ表示する（早押しを介さないため、freeText の
    /// 回答欄のようにロック保持者だけへ開放する必要が無い）。表示順は <see cref="ChoiceShuffle"/> で
    /// クライアントごとに決定的にシャッフルし、ボタン 1 つ 1 つに「元 <c>choices</c> インデックス」を
    /// 割り当てて保持する。クリック時はこの元インデックスをサーバーへ送る
    /// （docs/question-data.md §6「サーバーは常に元インデックスで判定する」）。
    /// </para>
    /// </remarks>
    public sealed partial class GameView
    {
        /// <summary>選択肢ボタンの基本 USS クラス（レビュー L7: マジックストリングを 1 箇所にまとめる）。</summary>
        private const string ChoiceButtonUssClass = "game-choice-button";

        /// <summary>自分が選択したボタン（判定前）の USS クラス。</summary>
        private const string ChoiceButtonSelectedUssClass = "game-choice-button--selected";

        /// <summary>正解の選択肢（判定後）の USS クラス。</summary>
        private const string ChoiceButtonCorrectUssClass = "game-choice-button--correct";

        /// <summary>自分が選んだが不正解だった選択肢（判定後）の USS クラス。</summary>
        private const string ChoiceButtonWrongUssClass = "game-choice-button--wrong";

        /// <summary>判定後に選択肢セクションを詰めて表示する USS クラス（#187）。</summary>
        internal const string ChoiceSectionCompactUssClass = "game-choice-section--compact";

        /// <summary>表示中のボタン（表示位置の順）。</summary>
        private readonly List<Button> _choiceButtons = new List<Button>();

        /// <summary>表示位置 → 元 <c>choices</c> インデックスの対応表（<see cref="ChoiceShuffle.BuildDisplayOrder"/>）。</summary>
        private int[] _choiceDisplayOrder = Array.Empty<int>();

        /// <summary>この問題で既に選択を送ったか（設定値に関わらず常に 1 回。#221、実装は #26）。</summary>
        private bool _hasSelectedChoiceLocally;

        /// <summary>
        /// 直近にボタンを作った問題の ID（レビュー L2）。<see cref="HandleChoiceQuestionShown"/> が
        /// 同じ問題に対して重複して呼ばれても（途中参加の再同期等）、選択済みのローカル錠を
        /// 誤って解除しないようにするための比較用。
        /// </summary>
        private string _lastBuiltChoiceQuestionId;

        private void OnChoiceButtonClicked(int originalIndex)
        {
            if (_isModerator)
            {
                // 司会専用モードでは選択しない（M-A。早押し・回答と同じ扱い、#20）。
                return;
            }

            if (_session == null
                || !GameViewPresenter.IsChoiceButtonInteractable(_session.Phase.Value, _hasSelectedChoiceLocally))
            {
                return;
            }

            if (_session.RequestChoice(originalIndex))
            {
                _hasSelectedChoiceLocally = true;
                HighlightChoice(originalIndex, ChoiceButtonSelectedUssClass);
                UpdateChoiceButtonsInteractable();
            }
        }

        /// <summary>
        /// 新しい問題が提示されたときにボタンを作り直す（issue #17）。
        /// 選択式でなければボタンは作らない（<see cref="UpdateChoiceSectionVisible"/> が非表示にする）。
        /// </summary>
        /// <remarks>
        /// レビュー L2: 途中参加の再同期などで同じ問題に対して重複して呼ばれ、かつ
        /// <see cref="QuizPhase.ChoiceAnswering"/> 中に既に選択済みの場合は、ローカル錠
        /// （<see cref="_hasSelectedChoiceLocally"/>）を解除せずボタンも作り直さない
        /// （解除すると、既にサーバーへ送信済みなのにもう一度押せるように見えてしまうため）。
        /// </remarks>
        /// <param name="question">提示された問題。</param>
        /// <param name="questionIndex">問題インデックス（シャッフルのシードに使う）。</param>
        private void HandleChoiceQuestionShown(QuestionDto question, int questionIndex)
        {
            var isSameQuestionStillAnswering =
                question != null
                && question.Type == QuestionType.Choice
                && _lastBuiltChoiceQuestionId == question.Id
                && _session != null
                && _session.Phase.Value == QuizPhase.ChoiceAnswering;

            if (isSameQuestionStillAnswering && _hasSelectedChoiceLocally)
            {
                UpdateChoiceButtonsInteractable();
                return;
            }

            ClearChoiceButtons();
            _hasSelectedChoiceLocally = false;

            if (question == null || question.Type != QuestionType.Choice || _choiceButtonsContainer == null)
            {
                _lastBuiltChoiceQuestionId = null;
                return;
            }

            // 自分のクライアント ID が分からない間（レビュー L-14 と同じ理由）は 0 を使う。
            // シードが多少ずれても「決定的である」こと自体は保たれる。
            var localClientId = TryGetLocalClientId(out var clientId) ? clientId : 0UL;
            var seed = ChoiceShuffle.BuildSeed(questionIndex, localClientId);
            _choiceDisplayOrder = ChoiceShuffle.BuildDisplayOrder(
                question.Choices.Count, ChoiceShuffle.DefaultShuffleDisplay, seed);

            for (var displayIndex = 0; displayIndex < _choiceDisplayOrder.Length; displayIndex++)
            {
                var originalIndex = _choiceDisplayOrder[displayIndex];
                // #206: 選択肢（問題データ）は平文として表示する。
                var button = PlainText.Apply(new Button { text = GameViewPresenter.ToQuestionDisplayText(question.Choices[originalIndex]) });
                button.AddToClassList(ChoiceButtonUssClass);
                button.clicked += () => OnChoiceButtonClicked(originalIndex);
                _choiceButtonsContainer.Add(button);
                _choiceButtons.Add(button);
            }

            _lastBuiltChoiceQuestionId = question.Id;
            UpdateChoiceButtonsInteractable();
        }

        private void ClearChoiceButtons()
        {
            _choiceButtonsContainer?.Clear();
            _choiceButtons.Clear();
            _choiceDisplayOrder = Array.Empty<int>();
            _lastBuiltChoiceQuestionId = null;
        }

        private void UpdateChoiceSectionVisible()
        {
            if (_choiceSection == null)
            {
                return;
            }

            var phase = _session?.Phase.Value ?? QuizPhase.Lobby;
            // 司会専用モードでは選択 UI も出さない（M-A、K18 取りこぼし）。buzz-section / answer-section と同じ扱い。
            var visible = !_isModerator && GameViewPresenter.IsChoiceSectionVisible(_currentQuestion?.Type, phase);
            _choiceSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            // #187: 判定後は選択肢を詰めて表示する（ボタンを低くし間隔を詰める。theme-views-game.uss）。
            _choiceSection.EnableInClassList(ChoiceSectionCompactUssClass, GameViewPresenter.IsChoiceSectionCompact(phase));

            UpdateChoiceButtonsInteractable();
        }

        private void UpdateChoiceButtonsInteractable()
        {
            if (_session == null)
            {
                return;
            }

            var interactable = GameViewPresenter.IsChoiceButtonInteractable(_session.Phase.Value, _hasSelectedChoiceLocally);
            for (var i = 0; i < _choiceButtons.Count; i++)
            {
                _choiceButtons[i].SetEnabled(interactable);
            }
        }

        /// <summary>元インデックスに対応するボタンへ USS クラスを付ける（正解・自分の選択のハイライト用）。</summary>
        private void HighlightChoice(int originalIndex, string ussClass)
        {
            for (var displayIndex = 0; displayIndex < _choiceDisplayOrder.Length; displayIndex++)
            {
                if (_choiceDisplayOrder[displayIndex] == originalIndex)
                {
                    _choiceButtons[displayIndex].AddToClassList(ussClass);
                    return;
                }
            }
        }

        /// <summary>
        /// 選択式の一斉判定が届いたとき（<see cref="GameSession.ChoiceResolved"/>）。
        /// 正解の選択肢をハイライトし、自分が選択していれば正誤・得点を結果欄に出す。
        /// </summary>
        /// <param name="correctChoiceIndex">正解の元 <c>choices</c> インデックス。</param>
        /// <param name="entries">選択した全クライアント分の結果。</param>
        private void HandleChoiceResolved(int correctChoiceIndex, IReadOnlyList<ChoiceAnswerEntry> entries)
        {
            HighlightChoice(correctChoiceIndex, ChoiceButtonCorrectUssClass);
            UpdateChoiceButtonsInteractable();

            ChoiceAnswerEntry? own = null;
            if (TryGetLocalClientId(out var localClientId))
            {
                foreach (var entry in entries)
                {
                    if (entry.ClientId == localClientId)
                    {
                        own = entry;
                        break;
                    }
                }
            }

            if (own.HasValue && !own.Value.IsCorrect)
            {
                HighlightChoice(own.Value.ChoiceIndex, ChoiceButtonWrongUssClass);
            }

            var correctText =
                _currentQuestion != null && correctChoiceIndex >= 0 && correctChoiceIndex < _currentQuestion.Choices.Count
                    ? _currentQuestion.Choices[correctChoiceIndex]
                    : string.Empty;

            _resultLabel.text = GameViewPresenter.FormatChoiceResult(
                correctText,
                own.HasValue ? own.Value.IsCorrect : (bool?)null,
                own?.ScoreDelta ?? 0,
                own?.TotalScore ?? 0);

            // #132 レビュー M3-3: 文言が入ったので結果セクションの表示を更新する（自由入力側と同じ）。
            UpdateResultSectionVisible();

            // #187 レビュー M1: 判定の RPC がフェーズ同期より後に届いた場合も判定結果を表示範囲へ入れる。
            ScrollToResultSection();

            if (own.HasValue)
            {
                PlayJudgementSe(own.Value.IsCorrect ? QuizJudgement.Correct : QuizJudgement.Wrong);
            }
        }
    }
}
