using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.QuestionEditor
{
    /// <summary>
    /// <see cref="QuestionEditorView"/> のうち、選択中の問題を編集するフォーム（issue #31）を扱う部分クラス。
    ///
    /// 「現在の入力値」は <see cref="_form"/>（Unity API に依存しない <see cref="QuestionFormData"/>）に
    /// 一本化する（CLAUDE.md「不変データを優先する」）。各コントロールの値変更コールバックは
    /// <c>_form = _form.With...(newValue)</c> の形で <see cref="_form"/> を差し替えるだけで、
    /// 保存時（<see cref="OnSaveQuestionFormClicked"/>）はコントロールの値を読み直さず <see cref="_form"/> を使う。
    ///
    /// 選択中の問題は <see cref="QuestionEditorSelectionState.Changed"/> を購読して検知する
    /// （<see cref="QuestionEditorSelectionState.SelectedQuestion"/> のドキュメントコメントが想定する結線）。
    /// 選択中の <see cref="Question"/> インスタンス自体が変わった場合（別の問題を選んだ・保存後に
    /// 新しいインスタンスへ差し替わった・再読込でセット全体が作り直された、のいずれか）だけフォームを
    /// 作り直す。並び替え等、同じ問題インスタンスが残ったままの操作ではフォームの入力途中の内容を保持する。
    /// </summary>
    public sealed partial class QuestionEditorView
    {
        private Question _formQuestion;
        private QuestionFormData _form;
        private IReadOnlyList<ValidationError> _formErrors = Array.Empty<ValidationError>();

        /// <summary>
        /// 保存直後の <see cref="OnSelectionChangedForForm"/> 呼び出し1回分だけ、未保存破棄の通知を抑止する
        /// （PR #93 レビュー M4、issue #32）。保存成功後は <see cref="ReplaceEntryEverywhere"/> 経由で
        /// 選択中の <see cref="Question"/> インスタンスが新しいものへ置き換わり、<see cref="OnSelectionChangedForForm"/>
        /// が発火するが、これは「破棄」ではなく「保存の反映」なので警告を出さない。
        /// </summary>
        private bool _suppressNextDiscardNotice;

        private DropdownField _formTypeField;
        private TextField _formTextField;
        private TextField _formReadingTextField;
        private VisualElement _formAnswersContainer;
        private VisualElement _formChoicesContainer;
        private RadioButtonGroup _formCorrectIndexGroup;
        private VisualElement _formImageSection;
        private Label _formSelectedImageLabel;
        private VisualElement _formImageListContainer;
        private Label _formImageStatusLabel;
        private TextField _formTagsField;
        private IntegerField _formDifficultyField;
        private VisualElement _formErrorListContainer;

        private void WireFormHandlers()
        {
            _selection.Changed += OnSelectionChangedForForm;
            LoadFormFromSelection();
        }

        private void UnwireFormHandlers()
        {
            _selection.Changed -= OnSelectionChangedForForm;
            DisposeTtsPreview();
        }

        private void OnSelectionChangedForForm()
        {
            if (ReferenceEquals(_selection.SelectedQuestion, _formQuestion))
            {
                // PR #103 レビュー L1: 実際には問題が変わらなかった Changed 通知（並び替え等）でも、
                // 保存直後に立てたフラグをここで確実にクリアする。クリアせずに return すると、
                // 次に本当に問題を切り替えたときまでフラグが生き残り、
                // 本来出すべき「未保存の変更を破棄しました。」が誤って抑止されてしまう。
                _suppressNextDiscardNotice = false;
                return;
            }

            var suppress = _suppressNextDiscardNotice;
            _suppressNextDiscardNotice = false;

            // 実際に入力値が変わっていた場合だけ「破棄した」旨を知らせる（PR #93 レビュー M4）。
            // 保存直後（suppress=true）は、保存済みの内容が新しい Question インスタンスとして
            // 戻ってくるだけで破棄ではないため、ここでは判定自体を行わない。
            var hadUnsavedChanges = !suppress && FormHasUnsavedChanges();

            LoadFormFromSelection();

            if (hadUnsavedChanges)
            {
                ShowQuestionStatus("未保存の変更を破棄しました。");
            }
        }

        /// <summary>
        /// 現在の <see cref="_form"/> の内容が、選択中の問題（<see cref="_formQuestion"/>）から
        /// 実際に変わっているかどうかを判定する（PR #93 レビュー M4）。
        ///
        /// <b>素の <see cref="_form"/> 同士を比較しない</b>（PR #103 レビュー M2）。<see cref="_form"/> は
        /// 入力中の生の値（末尾空白・空文字列の <c>readingText</c> 等）を保持するため、そのまま比較すると
        /// 「末尾に空白を1つ入力しただけ」「readingText が null と "" の違いだけ」でも
        /// 「未保存の変更あり」と誤検知する。保存時と同じ正規化（<see cref="QuestionFormConverter.ToQuestion"/>
        /// によるトリム・空文字列→null化）を通した結果同士を比較することで、実際に保存対象の値が
        /// 変わった場合だけを「変更あり」とする。
        /// </summary>
        private bool FormHasUnsavedChanges()
        {
            if (_formQuestion == null || _form == null)
            {
                return false;
            }

            var candidate = QuestionFormConverter.ToQuestion(_formQuestion.Id, _form);
            var normalizedCandidate = QuestionFormConverter.FromQuestion(candidate);
            var normalizedOriginal = QuestionFormConverter.FromQuestion(_formQuestion);
            return !normalizedCandidate.HasSameValuesAs(normalizedOriginal);
        }

        /// <summary>選択中の問題からフォームの入力値を作り直し、再描画する（入力途中の内容は破棄する）。</summary>
        private void LoadFormFromSelection()
        {
            // 別の問題・別のセットへ切り替わるので、前の問題の読み上げプレビューは続けない（issue #32）。
            StopTtsPreview();

            var question = _selection.SelectedQuestion;
            _formQuestion = question;
            _form = question != null ? QuestionFormConverter.FromQuestion(question) : null;
            _formErrors = Array.Empty<ValidationError>();
            RenderForm();
        }

        private void RenderForm()
        {
            if (_questionEditFormContainer == null)
            {
                return;
            }

            _questionEditFormContainer.Clear();
            ClearFormElementReferences();

            var question = _formQuestion;
            if (question == null || _form == null)
            {
                var placeholder = new Label("編集する問題を、上の問題一覧から選択してください。");
                placeholder.AddToClassList("body-text");
                _questionEditFormContainer.Add(placeholder);
                return;
            }

            // #206: 問題の id（問題データ）を含むため平文にする。
            var heading = PlainText.CreateLabel($"編集: {question.Id}");
            heading.AddToClassList("terms-section-heading");
            _questionEditFormContainer.Add(heading);

            BuildTypeField(_questionEditFormContainer);
            BuildTextFields(_questionEditFormContainer);

            if (_form.Type == QuestionType.Choice)
            {
                BuildChoicesSection(_questionEditFormContainer);
            }
            else
            {
                BuildAnswersSection(_questionEditFormContainer);
            }

            BuildImageSection(_questionEditFormContainer);
            BuildTagsAndDifficultyFields(_questionEditFormContainer);
            BuildTtsPreviewSection(_questionEditFormContainer);
            BuildErrorListAndButtons(_questionEditFormContainer);

            ApplyValidationHighlights();
        }

        private void BuildTypeField(VisualElement parent)
        {
            var typeChoices = new List<string> { "自由入力", "選択式" };
            var currentIndex = _form.Type == QuestionType.Choice ? 1 : 0;

            _formTypeField = new DropdownField("出題形式", typeChoices, currentIndex) { name = "question-form-type-field" };
            _formTypeField.RegisterValueChangedCallback(_ => OnTypeFieldChanged());
            parent.Add(_formTypeField);
        }

        private void OnTypeFieldChanged()
        {
            // 出題形式を切り替えると対象フィールド自体が入れ替わるため、直前の保存で出したエラーは
            // 実態と合わなくなる。まずクリアする（PR #93 レビュー L1）。
            _formErrors = Array.Empty<ValidationError>();

            var newType = _formTypeField.index == 1 ? QuestionType.Choice : QuestionType.FreeText;
            if (newType == _form.Type)
            {
                // 実際には値が変わらないと呼ばれない防御的な分岐。RenderForm を通らないので
                // ここでエラー表示のクリアだけ反映する。
                ApplyValidationHighlights();
                return;
            }

            _form = _form.WithType(newType);

            // 種別を切り替えたとき、まだ入力されていない側のリストを最低件数で初期化する
            // （choice には choices が2件以上、freeText には answers が1件以上必要なため。docs/question-data.md §2）。
            if (newType == QuestionType.Choice && _form.Choices.Count < QuestionLimits.MinChoiceCount)
            {
                _form = _form.WithChoices(new[] { string.Empty, string.Empty }).WithCorrectIndex(0);
            }
            else if (newType == QuestionType.FreeText && _form.Answers.Count == 0)
            {
                _form = _form.WithAnswers(new[] { string.Empty });
            }

            RenderForm();
        }

        private void BuildTextFields(VisualElement parent)
        {
            _formTextField = new TextField("問題文") { multiline = true, value = _form.Text, name = "question-form-text-field" };
            _formTextField.AddToClassList("question-editor-field");
            _formTextField.RegisterValueChangedCallback(evt => _form = _form.WithText(evt.newValue));
            parent.Add(_formTextField);

            // #189: 1 行のままだとラベル列（行幅の 50%）で「…読み上げま / す）」のように末尾だけが次の行に
            // 落ちるため、句点の後で明示的に改行する。
            _formReadingTextField = new TextField("読み上げ用テキスト（省略可。\n空欄なら問題文を読み上げます）")
            {
                multiline = true,
                value = _form.ReadingText,
                name = "question-form-reading-text-field",
            };
            _formReadingTextField.AddToClassList("question-editor-field");
            _formReadingTextField.RegisterValueChangedCallback(evt => _form = _form.WithReadingText(evt.newValue));
            parent.Add(_formReadingTextField);
        }

        private void BuildTagsAndDifficultyFields(VisualElement parent)
        {
            _formTagsField = new TextField("タグ（カンマ区切り、省略可）")
            {
                value = QuestionFormConverter.FormatTags(_form.Tags),
            };
            _formTagsField.AddToClassList("question-editor-field");
            _formTagsField.RegisterValueChangedCallback(
                evt => _form = _form.WithTags(QuestionFormConverter.ParseTags(evt.newValue)));
            parent.Add(_formTagsField);

            _formDifficultyField = new IntegerField($"難易度（{QuestionLimits.MinDifficulty}〜{QuestionLimits.MaxDifficulty}）")
            {
                value = _form.Difficulty,
            };
            _formDifficultyField.AddToClassList("question-editor-field");
            _formDifficultyField.RegisterValueChangedCallback(OnDifficultyFieldChanged);
            parent.Add(_formDifficultyField);
        }

        private void OnDifficultyFieldChanged(ChangeEvent<int> evt)
        {
            var clamped = Mathf.Clamp(evt.newValue, QuestionLimits.MinDifficulty, QuestionLimits.MaxDifficulty);
            if (clamped != evt.newValue)
            {
                _formDifficultyField.SetValueWithoutNotify(clamped);
            }

            _form = _form.WithDifficulty(clamped);
        }

        private void BuildErrorListAndButtons(VisualElement parent)
        {
            _formErrorListContainer = new VisualElement { name = "question-form-error-list-container" };
            _formErrorListContainer.AddToClassList("host-setup-warnings");
            _formErrorListContainer.style.display = DisplayStyle.None;
            parent.Add(_formErrorListContainer);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("menu-row");
            buttonRow.Add(new Button(OnSaveQuestionFormClicked) { text = "保存", name = "question-form-save-button" });
            buttonRow.Add(new Button(OnRevertQuestionFormClicked) { text = "元に戻す", name = "question-form-revert-button" });
            parent.Add(buttonRow);
        }

        private void OnSaveQuestionFormClicked()
        {
            var entry = _selection.SelectedSet;
            var question = _formQuestion;
            if (entry?.Set == null || question == null || _form == null)
            {
                return;
            }

            var candidate = QuestionFormConverter.ToQuestion(question.Id, _form);
            var setBaseDirectory = Path.GetDirectoryName(entry.FilePath);
            var errors = QuestionFormValidation.Validate(entry.Set, candidate, setBaseDirectory);

            if (errors.Count > 0)
            {
                _formErrors = errors;
                ApplyValidationHighlights();
                return;
            }

            var result = _service.UpdateQuestion(entry, candidate);
            if (!result.Success)
            {
                // ファイル外部変更等、フィールド単位に紐づかないエラー（CLAUDE.md「エラーを握りつぶさない」）。
                _formErrors = new[] { new ValidationError(result.ErrorMessage) };
                ApplyValidationHighlights();
                return;
            }

            ShowQuestionStatus("保存しました。");

            // ReplaceEntryEverywhere は _selection.ReplaceSelectedSet を呼び、その Changed 通知で
            // OnSelectionChangedForForm が「保存後の新しい Question インスタンス」を検知して
            // LoadFormFromSelection（フォームの作り直し・エラー解除）を行う。これは破棄ではないので、
            // 直後の1回だけ未保存破棄の通知（M4）を抑止する。
            _suppressNextDiscardNotice = true;
            ReplaceEntryEverywhere(result.Entry);
        }

        private void OnRevertQuestionFormClicked()
        {
            if (_formQuestion == null)
            {
                return;
            }

            _form = QuestionFormConverter.FromQuestion(_formQuestion);
            _formErrors = Array.Empty<ValidationError>();
            ShowQuestionStatus(string.Empty);
            RenderForm();
        }

        private void ApplyValidationHighlights()
        {
            foreach (QuestionFormField field in Enum.GetValues(typeof(QuestionFormField)))
            {
                GetFormFieldElement(field)?.RemoveFromClassList("question-editor-field--invalid");
            }

            if (_formErrorListContainer == null)
            {
                return;
            }

            _formErrorListContainer.Clear();

            if (_formErrors.Count == 0)
            {
                _formErrorListContainer.style.display = DisplayStyle.None;
                return;
            }

            _formErrorListContainer.style.display = DisplayStyle.Flex;

            var fieldsWithErrors = new HashSet<QuestionFormField>();
            foreach (var error in _formErrors)
            {
                // #206: 検証のエラー文は入力値（問題データ）を含みうるため平文にする。
                var label = PlainText.CreateLabel(error.Message);
                label.AddToClassList("body-text");
                label.AddToClassList("host-setup-warning");
                _formErrorListContainer.Add(label);

                fieldsWithErrors.Add(QuestionFormValidation.MapField(error.Message));
            }

            foreach (var field in fieldsWithErrors)
            {
                GetFormFieldElement(field)?.AddToClassList("question-editor-field--invalid");
            }
        }

        private VisualElement GetFormFieldElement(QuestionFormField field)
        {
            switch (field)
            {
                case QuestionFormField.Type: return _formTypeField;
                case QuestionFormField.Text: return _formTextField;
                case QuestionFormField.ReadingText: return _formReadingTextField;
                case QuestionFormField.Answers: return _formAnswersContainer;
                case QuestionFormField.Choices: return _formChoicesContainer;
                case QuestionFormField.CorrectIndex: return _formCorrectIndexGroup;
                case QuestionFormField.ImagePath: return _formImageSection;
                case QuestionFormField.Tags: return _formTagsField;
                case QuestionFormField.Difficulty: return _formDifficultyField;
                default: return null;
            }
        }

        private void ClearFormElementReferences()
        {
            _formTypeField = null;
            _formTextField = null;
            _formReadingTextField = null;
            _formAnswersContainer = null;
            _formChoicesContainer = null;
            _formCorrectIndexGroup = null;
            _formImageSection = null;
            _formSelectedImageLabel = null;
            _formImageListContainer = null;
            _formImageStatusLabel = null;
            _formTagsField = null;
            _formDifficultyField = null;
            _formErrorListContainer = null;
            _formTtsPreviewButton = null;
            _formTtsStatusLabel = null;
            _formTtsCheckStatusButton = null;
        }
    }
}
