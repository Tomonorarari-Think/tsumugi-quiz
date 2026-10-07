using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// <see cref="QuestionSetEditorService"/> のうち、選択中セット内の「問題一覧」に対する
    /// 追加（空の問題）・削除・並び替えを扱う部分クラス。
    /// </summary>
    public sealed partial class QuestionSetEditorService
    {
        /// <summary>選択中セットの末尾に空の問題を1件追加し、ファイルへ書き戻す。</summary>
        public QuestionSetEditorResult AddEmptyQuestion(QuestionSetFileEntry entry)
        {
            if (entry?.Set == null)
            {
                return QuestionSetEditorResult.Fail("問題セットが読み込めていないため追加できません。");
            }

            var newQuestion = QuestionListEditor.CreateEmptyQuestion(entry.Set.Questions);
            var updatedQuestions = QuestionListEditor.AddQuestion(entry.Set.Questions, newQuestion);
            return WriteUpdatedQuestions(entry, updatedQuestions);
        }

        /// <summary>
        /// 指定した id の問題を削除する。残り1件になる削除は
        /// <see cref="QuestionListEditor.CanRemoveQuestion"/> により拒否する（docs/question-data.md §2）。
        /// </summary>
        public QuestionSetEditorResult RemoveQuestion(QuestionSetFileEntry entry, string questionId)
        {
            if (entry?.Set == null)
            {
                return QuestionSetEditorResult.Fail("問題セットが読み込めていないため削除できません。");
            }

            if (!QuestionListEditor.CanRemoveQuestion(entry.Set.Questions.Count))
            {
                return QuestionSetEditorResult.Fail("問題は1件以上必要なため、これ以上削除できません。");
            }

            var updatedQuestions = QuestionListEditor.RemoveQuestion(entry.Set.Questions, questionId);
            return WriteUpdatedQuestions(entry, updatedQuestions);
        }

        /// <summary>問題の並び順を入れ替える。</summary>
        public QuestionSetEditorResult MoveQuestion(QuestionSetFileEntry entry, int fromIndex, int toIndex)
        {
            if (entry?.Set == null)
            {
                return QuestionSetEditorResult.Fail("問題セットが読み込めていないため並び替えできません。");
            }

            var updatedQuestions = QuestionListEditor.Move(entry.Set.Questions, fromIndex, toIndex);
            return WriteUpdatedQuestions(entry, updatedQuestions);
        }

        /// <summary>
        /// 編集フォーム（issue #31）で編集した内容を、選択中の問題（<paramref name="updatedQuestion"/>.Id で
        /// 特定する）へ反映し、ファイルへ書き戻す。id は変更しない（一覧の識別子のため）。
        /// フォーム側の保存前バリデーション（<see cref="QuestionFormValidation"/>）を通過済みの内容を渡す
        /// 想定だが、外部変更検出・書き込み前の再検証は他の更新 API と同じ経路（<see cref="WriteUpdatedQuestions"/>）
        /// を通るため、ここでも二重に保証される。
        /// </summary>
        public QuestionSetEditorResult UpdateQuestion(QuestionSetFileEntry entry, Question updatedQuestion)
        {
            if (updatedQuestion == null)
            {
                throw new ArgumentNullException(nameof(updatedQuestion));
            }

            if (entry?.Set == null)
            {
                return QuestionSetEditorResult.Fail("問題セットが読み込めていないため更新できません。");
            }

            var index = IndexOfQuestion(entry.Set.Questions, updatedQuestion.Id);
            if (index < 0)
            {
                return QuestionSetEditorResult.Fail("更新対象の問題が見つかりません。再読込してください。");
            }

            var updatedQuestions = entry.Set.Questions
                .Select((q, i) => i == index ? updatedQuestion : q)
                .ToArray();
            return WriteUpdatedQuestions(entry, updatedQuestions);
        }

        private static int IndexOfQuestion(IReadOnlyList<Question> questions, string id)
        {
            for (var i = 0; i < questions.Count; i++)
            {
                if (questions[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private QuestionSetEditorResult WriteUpdatedQuestions(QuestionSetFileEntry entry, IReadOnlyList<Question> questions)
        {
            if (!IsWithinFolder(entry.FilePath))
            {
                Debug.LogError($"[QuestionSetEditorService] 問題フォルダ外のファイルは更新できません: {entry.FilePath}");
                return QuestionSetEditorResult.Fail("不正なファイルパスです。");
            }

            // 外部変更検出（PR #88 レビュー H3）。読み込み時点の LastWriteTimeUtc と実ファイルの現在値を
            // 比較し、他プロセス・他ウィンドウが書き換えていた場合は上書きせず再読込を促す。
            var currentLastWriteTimeUtc = SafeGetLastWriteTimeUtc(entry.FilePath);
            if (currentLastWriteTimeUtc != entry.LastWriteTimeUtc)
            {
                return QuestionSetEditorResult.Fail("ファイルが外部で変更されています。再読込してください。");
            }

            var updatedSet = new QuestionSet(
                entry.Set.SchemaVersion,
                entry.Set.SetId,
                entry.Set.Title,
                entry.Set.Description,
                questions);

            // 通常はここに来ないはずだが（追加/削除/並び替えはいずれも壊れた状態を作らない設計）、
            // 保存直前の境界として防御的に検証してから書き込む（CLAUDE.md「境界では必ず入力検証」）。
            var errors = new QuestionSetValidator().Validate(updatedSet, Path.GetDirectoryName(entry.FilePath));
            if (errors.Count > 0)
            {
                var message = string.Join(" / ", errors.Select(e => e.ToString()));
                Debug.LogError($"[QuestionSetEditorService] 更新後の検証に失敗しました: {entry.FilePath}: {message}");
                return QuestionSetEditorResult.Fail($"更新内容の検証でエラーが発生しました: {message}");
            }

            if (!QuestionSetWriter.TryWrite(entry.FilePath, updatedSet, out var error))
            {
                return QuestionSetEditorResult.Fail(error);
            }

            var newLastWriteTimeUtc = SafeGetLastWriteTimeUtc(entry.FilePath);
            return QuestionSetEditorResult.Ok(
                new QuestionSetFileEntry(entry.FilePath, entry.FileName, updatedSet, Array.Empty<string>(), newLastWriteTimeUtc));
        }
    }
}
