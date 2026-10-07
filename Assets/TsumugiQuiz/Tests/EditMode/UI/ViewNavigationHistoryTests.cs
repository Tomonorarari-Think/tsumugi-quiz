using System;
using NUnit.Framework;
using TsumugiQuiz.UI;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// ViewRouter の遷移・履歴（戻る）ロジックを Unity 非依存で切り出した
    /// <see cref="ViewNavigationHistory"/> のユニットテスト。
    /// </summary>
    public class ViewNavigationHistoryTests
    {
        [Test]
        public void Push_SetsCurrentView()
        {
            var history = new ViewNavigationHistory();

            history.Push("title");

            Assert.AreEqual("title", history.Current);
            Assert.AreEqual(1, history.Count);
        }

        [Test]
        public void CanGoBack_IsFalse_WhenOnlyOneViewInHistory()
        {
            var history = new ViewNavigationHistory();
            history.Push("title");

            Assert.IsFalse(history.CanGoBack);
        }

        [Test]
        public void CanGoBack_IsTrue_AfterPushingSecondView()
        {
            var history = new ViewNavigationHistory();
            history.Push("title");
            history.Push("host-setup");

            Assert.IsTrue(history.CanGoBack);
        }

        [Test]
        public void GoBack_ReturnsPreviousView_AndRemovesCurrentFromHistory()
        {
            var history = new ViewNavigationHistory();
            history.Push("title");
            history.Push("host-setup");

            var result = history.GoBack();

            Assert.AreEqual("title", result);
            Assert.AreEqual("title", history.Current);
            Assert.AreEqual(1, history.Count);
        }

        [Test]
        public void GoBack_ThenGoBackAgain_FollowsPushOrder()
        {
            var history = new ViewNavigationHistory();
            history.Push("title");
            history.Push("host-setup");
            history.Push("lobby");

            Assert.AreEqual("host-setup", history.GoBack());
            Assert.AreEqual("title", history.GoBack());
            Assert.IsFalse(history.CanGoBack);
        }

        [Test]
        public void GoBack_Throws_WhenNoHistoryToReturnTo()
        {
            var history = new ViewNavigationHistory();
            history.Push("title");

            Assert.Throws<InvalidOperationException>(() => history.GoBack());
        }

        [Test]
        public void GoBack_Throws_WhenHistoryIsEmpty()
        {
            var history = new ViewNavigationHistory();

            Assert.Throws<InvalidOperationException>(() => history.GoBack());
        }

        [TestCase(null)]
        [TestCase("")]
        public void Push_Throws_WhenViewNameIsNullOrEmpty(string viewName)
        {
            var history = new ViewNavigationHistory();

            Assert.Throws<ArgumentException>(() => history.Push(viewName));
        }

        [Test]
        public void Reset_ClearsHistory()
        {
            var history = new ViewNavigationHistory();
            history.Push("title");
            history.Push("host-setup");

            history.Reset();

            Assert.IsNull(history.Current);
            Assert.AreEqual(0, history.Count);
            Assert.IsFalse(history.CanGoBack);
        }
    }
}
