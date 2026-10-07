using NUnit.Framework;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// ViewRouter の遷移ロジックを、実際の UXML / PanelSettings なしに検証する EditMode テスト。
    /// UIDocument に実パネルが無い環境では画面の見た目の切り替えは行われないが、
    /// ShowView/GoBack が更新する CurrentViewName・CanGoBack の状態遷移はこの環境でも検証できる。
    /// </summary>
    public class ViewRouterTests
    {
        private GameObject _gameObject;
        private ViewRouter _router;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("ViewRouterTestObject");
            _gameObject.AddComponent<UIDocument>();
            _router = _gameObject.AddComponent<ViewRouter>();

            // Inspector（_viewDefinitions）を介さず、テスト用のダミーテンプレートを直接登録する。
            _router.RegisterTemplate(ViewNames.Title, ScriptableObject.CreateInstance<VisualTreeAsset>());
            _router.RegisterTemplate(ViewNames.HostSetup, ScriptableObject.CreateInstance<VisualTreeAsset>());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void ShowInitialView_ShowsConfiguredInitialView()
        {
            _router.ShowInitialView();

            Assert.AreEqual(ViewNames.Title, _router.CurrentViewName);
            Assert.IsFalse(_router.CanGoBack);
        }

        [Test]
        public void ShowView_UnregisteredView_IsIgnoredAndHistoryUnchanged()
        {
            _router.ShowView(ViewNames.Title);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("未登録の View"));
            _router.ShowView("not-registered-view");

            Assert.AreEqual(ViewNames.Title, _router.CurrentViewName);
            Assert.IsFalse(_router.CanGoBack);
        }

        [Test]
        public void ShowView_ThenGoBack_ReturnsToPreviousView()
        {
            _router.ShowView(ViewNames.Title);
            _router.ShowView(ViewNames.HostSetup);

            Assert.IsTrue(_router.CanGoBack);
            Assert.AreEqual(ViewNames.HostSetup, _router.CurrentViewName);

            _router.GoBack();

            Assert.AreEqual(ViewNames.Title, _router.CurrentViewName);
            Assert.IsFalse(_router.CanGoBack);
        }

        [Test]
        public void GoBack_WithoutHistory_LogsWarningAndKeepsCurrentView()
        {
            _router.ShowView(ViewNames.Title);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("戻り先の履歴がない"));
            _router.GoBack();

            Assert.AreEqual(ViewNames.Title, _router.CurrentViewName);
        }

        [Test]
        public void ShowView_SameViewTwice_IsIgnoredAndDoesNotGrowHistory()
        {
            _router.ShowView(ViewNames.Title);
            _router.ShowView(ViewNames.Title);

            Assert.AreEqual(ViewNames.Title, _router.CurrentViewName);
            Assert.IsFalse(_router.CanGoBack);
        }
    }
}
