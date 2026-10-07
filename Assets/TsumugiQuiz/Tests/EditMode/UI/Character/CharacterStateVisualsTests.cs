using NUnit.Framework;
using TsumugiQuiz.UI;

namespace TsumugiQuiz.Tests.EditMode.UI.Character
{
    /// <summary>
    /// 状態ごとのマーク（○ / ×）とティント（#192）の対応を検証する（issue #212）。
    /// </summary>
    public class CharacterStateVisualsTests
    {
        [TestCase(CharacterState.Idle, false, false)]
        [TestCase(CharacterState.Reading, false, false)]
        [TestCase(CharacterState.Correct, true, false)]
        [TestCase(CharacterState.Wrong, false, true)]
        [TestCase(CharacterState.BuzzSelf, false, false)]
        [TestCase(CharacterState.BuzzOther, false, false)]
        [TestCase(CharacterState.WrongMoment, false, false)] // 結果がまだ確定していないのでマークは出さない
        [TestCase(CharacterState.TimedOut, false, true)] // 従来の時間切れと同じ（×）
        [TestCase(CharacterState.NoEligibleBuzzers, false, true)] // 時間切れと同じ扱い
        public void Marks_FollowResultCategory(CharacterState state, bool correctMark, bool wrongMark)
        {
            Assert.AreEqual(correctMark, CharacterStateVisuals.ShowsCorrectMark(state), "○");
            Assert.AreEqual(wrongMark, CharacterStateVisuals.ShowsWrongMark(state), "×");
        }

        [TestCase(CharacterState.Correct, CharacterTint.Correct)]
        [TestCase(CharacterState.Wrong, CharacterTint.Wrong)]
        [TestCase(CharacterState.TimedOut, CharacterTint.Wrong)]
        [TestCase(CharacterState.NoEligibleBuzzers, CharacterTint.Wrong)]
        [TestCase(CharacterState.Idle, CharacterTint.None)]
        [TestCase(CharacterState.Reading, CharacterTint.None)]
        [TestCase(CharacterState.BuzzSelf, CharacterTint.None)]
        [TestCase(CharacterState.BuzzOther, CharacterTint.None)]
        [TestCase(CharacterState.WrongMoment, CharacterTint.None)]
        public void Tint_WhenFallingBack_FollowsResultCategory(CharacterState state, CharacterTint expected)
        {
            Assert.AreEqual(expected, CharacterStateVisuals.GetTint(state, hasDedicatedImage: false));
        }

        [TestCase(CharacterState.Correct)]
        [TestCase(CharacterState.Wrong)]
        [TestCase(CharacterState.TimedOut)]
        [TestCase(CharacterState.NoEligibleBuzzers)]
        public void Tint_WithDedicatedImage_IsNone(CharacterState state)
        {
            // その状態専用の表情差分が読めたときは、表情で伝わるのでティントを掛けない。
            Assert.AreEqual(CharacterTint.None, CharacterStateVisuals.GetTint(state, hasDedicatedImage: true));
        }
    }
}
