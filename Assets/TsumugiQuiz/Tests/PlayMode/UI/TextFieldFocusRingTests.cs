using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// 入力欄のフォーカス枠がテーマ色（<c>--color-focus-ring</c>）になることの回帰テスト（#132 レビュー H2-2）。
    ///
    /// Unity 既定ランタイムテーマは入力欄のフォーカス枠を独自の青（実測 rgb(0,106,166)）で描く。
    /// 既定テーマの規則は <c>:focus:enabled</c> を含む詳細度 0,4,0 相当で、<c>:enabled</c> を付けない
    /// こちらの規則（0,3,0）は枠色だけ負けていた。theme.uss で <c>:enabled</c> を足した規則にしてあり、
    /// このテストはそれが将来崩れたら気づけるようにするためのもの（実際の <c>Focus()</c> を使う）。
    ///
    /// <para>
    /// **注意**: 権威ある確認はビルドしたプレイヤーのスクリーンショットに対するピクセル計測であり、
    /// このテストはあくまで早期検知用。Unity のバージョンを上げたときは docs/dev-workflow.md の
    /// リリース前チェックに従って実機のスクリーンショットを撮り直すこと。
    /// </para>
    /// </summary>
    public class TextFieldFocusRingTests
    {
        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        /// <summary>theme.uss の <c>--color-focus-ring</c>（#2c2320、#174 で #8a6a12 から変更）。</summary>
        private static readonly Color32 ExpectedFocusRing = ParseColor("#2c2320");

        /// <summary>theme.uss の <c>--color-border</c>（#8d7768）＝フォーカスしていないときの枠。</summary>
        private static readonly Color32 ExpectedNormalBorder = ParseColor("#8d7768");

        [SetUp]
        public void SeedConsentedState()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreConsent()
        {
            _consentScope?.Restore();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator JoinView_FocusedTextField_UsesThemeFocusRingColor()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Join);

            TextField playerNameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => playerNameField = found);

            var input = playerNameField.Q("unity-text-input");
            Assert.IsNotNull(input, "TextField の内側に 'unity-text-input' が見つかりません。");

            playerNameField.Focus();

            // --color-* の transition（0.09s）が終わるまで待ってから読む。
            var deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline
                   && !ColorsApproximatelyEqual(input.resolvedStyle.borderTopColor, ExpectedFocusRing))
            {
                yield return null;
            }

            var focused = playerNameField.focusController?.focusedElement as VisualElement;
            var focusedDescription = focused == null
                ? "(none)"
                : $"name='{focused.name}' type={focused.GetType().Name} classes=[{string.Join(" ", focused.GetClasses())}]";

            var inlineTop = input.style.borderTopColor;
            var resolved = (Color32)input.resolvedStyle.borderTopColor;

            Assert.That(
                ColorsApproximatelyEqual(input.resolvedStyle.borderTopColor, ExpectedFocusRing),
                Is.True,
                "フォーカス中の入力欄の枠がテーマ色になっていません。"
                + $"\n  resolved={resolved}"
                + $"\n  inline.keyword={inlineTop.keyword} inline.value={inlineTop.value}"
                + $"\n  focusedElement={focusedDescription}"
                + $"\n  input.classes=[{string.Join(" ", input.GetClasses())}]");
        }

        /// <summary>
        /// 8bit 換算で 2 段階までの差を許容して色を比較する（transition の丸めと
        /// Color（float）→Color32 の変換誤差を吸収するため）。
        /// </summary>
        private static bool ColorsApproximatelyEqual(Color actual, Color32 expected, int tolerance = 2)
        {
            Color32 actual32 = actual;
            return Mathf.Abs(actual32.r - expected.r) <= tolerance
                   && Mathf.Abs(actual32.g - expected.g) <= tolerance
                   && Mathf.Abs(actual32.b - expected.b) <= tolerance;
        }

        /// <summary>
        /// #132 レビュー M3-1: 入力欄と親を共有するボタン（join-view の「戻る」）にフォーカスしても、
        /// 入力欄の枠が通常色（<c>--color-border</c> #8d7768）のままであることを確認する。
        /// フォーカス対象の探索が部分木まで見ていると、関係のない入力欄が光ってしまう。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator JoinView_FocusedButton_DoesNotHighlightTextField()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Join);

            TextField playerNameField = null;
            yield return WaitForElement<TextField>(panelRoot, "player-name-field", found => playerNameField = found);
            Button backButton = null;
            yield return WaitForElement<Button>(panelRoot, "back-button", found => backButton = found);

            var input = playerNameField.Q("unity-text-input");
            Assert.IsNotNull(input, "TextField の内側に 'unity-text-input' が見つかりません。");

            backButton.Focus();

            // フォーカス移動とスタイルの反映を待つ（transition 0.09s 込み）。
            var deadline = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            var resolved = (Color32)input.resolvedStyle.borderTopColor;
            Assert.That(
                ColorsApproximatelyEqual(input.resolvedStyle.borderTopColor, ExpectedNormalBorder),
                Is.True,
                "ボタンにフォーカスしただけで入力欄の枠が変わってはいけません。"
                + $"\n  resolved={resolved} expected={ExpectedNormalBorder}");
        }

        /// <summary>
        /// #132 レビュー L3-4: DropdownField（<c>unity-base-popup-field__input</c>）のフォーカス枠も
        /// テーマ色になることを確認する。
        /// </summary>
        /// <remarks>
        /// 調査時の実測では DropdownField も TextField と同じく既定テーマの青（rgb(0,106,166)）になり、
        /// <c>.unity-base-popup-field:focus .unity-base-popup-field__input</c>（0,3,0）は枠色だけ負けていた。
        /// theme.uss で <c>:focus:enabled</c> を足した規則にしてあり、このテストはそれが崩れたら気づくためのもの。
        /// </remarks>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator DropdownField_Focused_UsesThemeFocusRingColor()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var dropdown = new DropdownField("テスト", new List<string> { "A", "B" }, 0) { name = "focus-ring-probe-dropdown" };
            panelRoot.Add(dropdown);
            yield return null;

            var input = dropdown.Q(className: "unity-base-popup-field__input");
            Assert.IsNotNull(input, "DropdownField の内側に 'unity-base-popup-field__input' が見つかりません。");

            dropdown.Focus();

            var deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline
                   && !ColorsApproximatelyEqual(input.resolvedStyle.borderTopColor, ExpectedFocusRing))
            {
                yield return null;
            }

            // ツリーから外すと resolvedStyle は既定値に戻るため、判定に使う値は外す前に取っておく。
            var actual = input.resolvedStyle.borderTopColor;
            var resolved = (Color32)actual;
            dropdown.RemoveFromHierarchy();

            Assert.That(
                ColorsApproximatelyEqual(actual, ExpectedFocusRing),
                Is.True,
                "フォーカス中の DropdownField の枠がテーマ色になっていません。"
                + $"\n  resolved={resolved} expected={ExpectedFocusRing}");
        }

        private static Color32 ParseColor(string html)
        {
            if (!ColorUtility.TryParseHtmlString(html, out var color))
            {
                throw new System.ArgumentException($"色のパースに失敗しました: {html}", nameof(html));
            }

            return color;
        }
    }
}
