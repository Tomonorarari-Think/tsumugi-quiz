using System;
using NUnit.Framework;
using TsumugiQuiz.Core.Reveal;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="RevealText"/>（文字送り表示の文字の数え方と切り出し、issue #144）のテスト。
    /// </summary>
    public class RevealTextTests
    {
        [Test]
        public void GetTextElementStarts_JapaneseText_OnePerCharacter()
        {
            var starts = RevealText.GetTextElementStarts("日本の首都は？");

            Assert.AreEqual(7, starts.Length);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6 }, starts);
        }

        [Test]
        public void GetTextElementStarts_SurrogatePair_CountsAsOneCharacter()
        {
            // 「𠮷」（U+20BB7）は UTF-16 で 2 コード単位。途中で切ると表示が壊れる。
            const string text = "𠮷野家";

            var starts = RevealText.GetTextElementStarts(text);

            Assert.AreEqual(4, text.Length, "前提: UTF-16 では 4 コード単位。");
            Assert.AreEqual(3, starts.Length, "利用者から見た文字数は 3。");
            Assert.AreEqual("𠮷", RevealText.Take(text, starts, 1));
            Assert.AreEqual("𠮷野", RevealText.Take(text, starts, 2));
        }

        [TestCase(null)]
        [TestCase("")]
        public void GetTextElementStarts_NullOrEmpty_ReturnsEmpty(string text)
        {
            Assert.AreEqual(0, RevealText.GetTextElementStarts(text).Length);
        }

        [Test]
        public void Take_ClampsToRange()
        {
            const string text = "あいう";
            var starts = RevealText.GetTextElementStarts(text);

            Assert.AreEqual(string.Empty, RevealText.Take(text, starts, 0));
            Assert.AreEqual(string.Empty, RevealText.Take(text, starts, -3));
            Assert.AreEqual("あ", RevealText.Take(text, starts, 1));
            Assert.AreEqual("あいう", RevealText.Take(text, starts, 3));
            Assert.AreEqual("あいう", RevealText.Take(text, starts, 99));
        }

        [Test]
        public void Take_NullStarts_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RevealText.Take("あ", null, 1));
        }
    }
}
