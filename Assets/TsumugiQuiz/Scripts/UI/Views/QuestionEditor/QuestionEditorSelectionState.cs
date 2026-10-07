using System;
using System.Linq;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.UI.Views.QuestionEditor
{
    /// <summary>
    /// 問題エディタ（issue #30）の「現在選択中のセット・問題」を保持する。
    /// Unity API に依存しない純 C# で、EditMode から直接テストできる。
    /// <see cref="SelectedQuestion"/> を公開することで、編集フォーム（#31）が
    /// このクラスを購読するだけで「選択中の問題」を差し込めるようにする（統括メモ）。
    /// </summary>
    public sealed class QuestionEditorSelectionState
    {
        /// <summary>現在選択中の問題セット（未選択なら null）。</summary>
        public QuestionSetFileEntry SelectedSet { get; private set; }

        /// <summary>現在選択中の問題 id（未選択なら null）。</summary>
        public string SelectedQuestionId { get; private set; }

        /// <summary>
        /// 選択中の問題そのもの。<see cref="SelectedSet"/> が未選択・読み込みエラー、または
        /// <see cref="SelectedQuestionId"/> に対応する問題が存在しない場合は null。
        /// </summary>
        public Question SelectedQuestion
            => SelectedSet?.Set?.Questions.FirstOrDefault(q => q.Id == SelectedQuestionId);

        /// <summary>選択状態が変わるたびに発火する（View 側の再描画トリガー用）。</summary>
        public event Action Changed;

        public void SelectSet(QuestionSetFileEntry entry)
        {
            SelectedSet = entry;
            SelectedQuestionId = null;
            Changed?.Invoke();
        }

        public void SelectQuestion(string questionId)
        {
            SelectedQuestionId = questionId;
            Changed?.Invoke();
        }

        public void Clear()
        {
            SelectedSet = null;
            SelectedQuestionId = null;
            Changed?.Invoke();
        }

        /// <summary>
        /// セットの追加/削除/複製・問題の追加/削除/並び替え後、書き込み直後の最新の
        /// <see cref="QuestionSetFileEntry"/> に選択状態を差し替える。
        /// 選択していた問題が消えていた場合（削除など）は問題選択を解除する。
        /// <paramref name="updatedEntry"/> が null（セット自体が削除された等）の場合は選択を解除する。
        /// </summary>
        public void ReplaceSelectedSet(QuestionSetFileEntry updatedEntry)
        {
            SelectedSet = updatedEntry;

            if (updatedEntry == null)
            {
                SelectedQuestionId = null;
            }
            else if (SelectedQuestionId != null && SelectedQuestion == null)
            {
                SelectedQuestionId = null;
            }

            Changed?.Invoke();
        }
    }
}
