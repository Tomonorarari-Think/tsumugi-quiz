using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Questions.Editing;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionEditorNaming"/>（issue #30「複製時の ID 採番」）の純ロジックを検証する。
    /// </summary>
    public class QuestionEditorNamingTests
    {
        [Test]
        public void NextQuestionId_NoExistingIds_ReturnsQuestion1()
        {
            Assert.AreEqual("question-1", QuestionEditorNaming.NextQuestionId(new string[0]));
        }

        [Test]
        public void NextQuestionId_ExistingIdsWithGap_SkipsOccupiedNumbersInOrder()
        {
            var existing = new[] { "question-1", "question-2" };
            Assert.AreEqual("question-3", QuestionEditorNaming.NextQuestionId(existing));
        }

        [Test]
        public void NextQuestionId_NonSequentialExistingIds_DoesNotCollide()
        {
            // 既存の id が "question-N" 形式以外（サンプルデータの "q1" 等）でも衝突しないこと。
            var existing = new[] { "q1", "sample-question" };
            Assert.AreEqual("question-1", QuestionEditorNaming.NextQuestionId(existing));
        }

        [Test]
        public void NextSetIdForNew_NoExisting_ReturnsNewSet()
        {
            Assert.AreEqual("new-set", QuestionEditorNaming.NextSetIdForNew(new string[0]));
        }

        [Test]
        public void NextSetIdForNew_Collides_AppendsIncrementingSuffix()
        {
            var existing = new[] { "new-set", "new-set-2" };
            Assert.AreEqual("new-set-3", QuestionEditorNaming.NextSetIdForNew(existing));
        }

        [Test]
        public void NextFileNameForNew_IsCaseInsensitive()
        {
            // Windows のファイル名は大文字小文字を区別しないため、"New-Set" があれば衝突とみなす。
            var existing = new[] { "New-Set" };
            Assert.AreEqual("new-set-2", QuestionEditorNaming.NextFileNameForNew(existing));
        }

        [Test]
        public void NextSetIdForDuplicate_NoCollision_AppendsCopySuffix()
        {
            Assert.AreEqual("sample-set-01-copy", QuestionEditorNaming.NextSetIdForDuplicate("sample-set-01", new string[0]));
        }

        [Test]
        public void NextSetIdForDuplicate_CopySuffixCollides_AppendsIncrementingNumber()
        {
            var existing = new List<string> { "sample-set-01", "sample-set-01-copy" };
            Assert.AreEqual("sample-set-01-copy-2", QuestionEditorNaming.NextSetIdForDuplicate("sample-set-01", existing));
        }

        [Test]
        public void NextSetIdForDuplicate_IsCaseSensitive()
        {
            // setId はファイル名と違い大文字小文字を区別する（QuestionSetValidator の正規表現も区別する）。
            var existing = new[] { "Sample-Set-01-copy" };
            Assert.AreEqual("sample-set-01-copy", QuestionEditorNaming.NextSetIdForDuplicate("sample-set-01", existing));
        }

        [Test]
        public void NextFileNameForDuplicate_CollidesTwice_AppendsNextIncrementingNumber()
        {
            var existing = new[] { "sample", "sample-copy", "sample-copy-2" };
            Assert.AreEqual("sample-copy-3", QuestionEditorNaming.NextFileNameForDuplicate("sample", existing));
        }

        [Test]
        public void DuplicateTitle_ShortTitle_AppendsCopySuffix()
        {
            Assert.AreEqual("サンプルのコピー", QuestionEditorNaming.DuplicateTitle("サンプル"));
        }

        [Test]
        public void DuplicateTitle_CombinedLengthExceeds100_TruncatesOriginalPart_KeepingSuffix()
        {
            // title の上限は100文字（QuestionLimits.MaxTitleLength）。95文字 + "のコピー"(4文字) = 99文字で
            // 収まるケース（切り詰め不要）との対比として、100文字ちょうどのタイトルで確認する。
            var originalTitle = new string('あ', 100);

            var result = QuestionEditorNaming.DuplicateTitle(originalTitle);

            Assert.AreEqual(100, result.Length, "結果は MaxTitleLength(100) を超えないこと。");
            Assert.IsTrue(result.EndsWith("のコピー"), "末尾の「のコピー」は必ず残ること。");
            Assert.AreEqual(new string('あ', 96) + "のコピー", result);
        }

        [Test]
        public void DuplicateTitle_CombinedLengthExactlyAtLimit_DoesNotTruncate()
        {
            // 96文字 + "のコピー"(4文字) = 100文字ちょうど。切り詰めは発生しない。
            var originalTitle = new string('あ', 96);

            var result = QuestionEditorNaming.DuplicateTitle(originalTitle);

            Assert.AreEqual(originalTitle + "のコピー", result);
        }

        [Test]
        public void DuplicateTitle_TruncationBoundaryFallsInsideSurrogatePair_TruncatesOneCharEarlier()
        {
            // PR #88 レビュー LOW: 切り詰め境界（96文字目）がサロゲートペア（絵文字1文字 = char 2個）の
            // 途中に来るケース。95文字の「あ」+ 絵文字（2 char）+ 「い」10文字、切り詰め上限は96文字。
            // 境界がペアの間（95文字目と96文字目の間）に来るため、ペアごと落として95文字目で切る。
            var originalTitle = new string('あ', 95) + "😀" + new string('い', 10);

            var result = QuestionEditorNaming.DuplicateTitle(originalTitle);

            // ペアを分断しないよう1文字早く切るため、結果は99文字（MaxTitleLength(100)未満）になる。
            Assert.LessOrEqual(result.Length, 100, "結果は MaxTitleLength(100) を超えないこと。");
            Assert.AreEqual(new string('あ', 95) + "のコピー", result, "サロゲートペアを分断せず、ペアごと切り詰めること。");
        }
    }
}
