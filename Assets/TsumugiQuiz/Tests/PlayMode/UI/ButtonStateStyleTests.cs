using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// ボタンの状態（通常 / :hover / :active / :focus）ごとの見た目が、Unity 既定ランタイムテーマではなく
    /// theme.uss のトークンで描かれていることの回帰テスト（issue #132 撮影時の実測）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 既定テーマはボタンの状態を <c>.unity-button:hover:enabled</c> のような「クラス + 疑似クラス 2 つ」
    /// （詳細度 0,3,0 相当）の規則で塗っているとみられ、<c>.unity-button:hover</c>（0,2,0）と書いた規則は
    /// 負けて既定のグレーのままになった（ビルドしたプレイヤーのスクリーンショットで実測）。
    /// 入力欄・ドロップダウン・トグルも同じで、<c>:enabled</c> を足した規則にして揃えてある。
    /// ここでは疑似状態（PseudoStates）を反射で直接立てて、各状態の resolvedStyle を確かめる
    /// （マウスやキーの実入力に頼らないので安定する）。
    /// </para>
    /// <para>
    /// 権威ある確認はビルドしたプレイヤーのスクリーンショットに対するピクセル計測であり、
    /// このテストは早期検知用（docs/dev-workflow.md §8.6）。
    /// </para>
    /// </remarks>
    public class ButtonStateStyleTests
    {
        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        [SetUp]
        public void SeedConsentedState()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();

            var store = TsumugiQuiz.UI.ConsentGate.CreateDefaultStore();
            var requiredTerms = TsumugiQuiz.UI.TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreConsent()
        {
            _consentScope?.Restore();
        }

        /// <summary>1 ケース分の期待値。null の項目は検証しない。</summary>
        private sealed class Case
        {
            public string Variant;
            public string[] Classes;
            public string States;
            public string Background;
            public string Border;
        }

        private static readonly Case[] Cases =
        {
            // 中立（.unity-button のみ）
            new Case { Variant = "中立", Classes = new string[0], States = "", Background = "#f0e2d4", Border = "#8d7768" },
            new Case { Variant = "中立", Classes = new string[0], States = "Hover", Background = "#e6d5c4", Border = "#8d7768" },
            new Case { Variant = "中立", Classes = new string[0], States = "Active", Background = "#fffaf4" },
            new Case { Variant = "中立", Classes = new string[0], States = "Hover,Active", Background = "#fffaf4" },
            new Case { Variant = "中立", Classes = new string[0], States = "Focus", Border = "#2c2320" },
            new Case { Variant = "中立", Classes = new string[0], States = "Hover,Focus", Background = "#e6d5c4", Border = "#2c2320" },
            // 無効状態（:enabled を付けた状態規則が無効時に紛れ込まないこと、#132 レビュー L5-2）
            new Case { Variant = "中立", Classes = new string[0], States = "Disabled", Background = "#ded0c2", Border = "#d8c6b5" },
            new Case { Variant = "中立", Classes = new string[0], States = "Hover,Disabled", Background = "#ded0c2", Border = "#d8c6b5" },

            // 主要導線（.button-primary）。通常時の枠は #174 で --color-primary-text から
            // --color-border に統一した（フォーカス色との差を 3:1 以上にする余地を作るため）。
            new Case { Variant = "主要", Classes = new[] { "button-primary" }, States = "", Background = "#e4ac15", Border = "#8d7768" },
            new Case { Variant = "主要", Classes = new[] { "button-primary" }, States = "Hover", Background = "#f3c64a", Border = "#8d7768" },
            new Case { Variant = "主要", Classes = new[] { "button-primary" }, States = "Hover,Active", Background = "#c08f0b" },
            new Case { Variant = "主要", Classes = new[] { "button-primary" }, States = "Focus", Border = "#2c2320" },
            new Case { Variant = "主要", Classes = new[] { "button-primary" }, States = "Hover,Focus", Background = "#f3c64a", Border = "#2c2320" },
            new Case { Variant = "主要", Classes = new[] { "button-primary" }, States = "Disabled", Background = "#ded0c2", Border = "#d8c6b5" },
            new Case { Variant = "主要", Classes = new[] { "button-primary" }, States = "Hover,Disabled", Background = "#ded0c2", Border = "#d8c6b5" },

            // 破壊的操作（.button-danger）。通常時の枠は #174 で --color-error-active から
            // --color-border に統一した（理由は主要導線と同じ）。
            new Case { Variant = "危険", Classes = new[] { "button-danger" }, States = "", Background = "#b62e56", Border = "#8d7768" },
            new Case { Variant = "危険", Classes = new[] { "button-danger" }, States = "Hover", Background = "#c1355f", Border = "#8d7768" },
            new Case { Variant = "危険", Classes = new[] { "button-danger" }, States = "Hover,Active", Background = "#8f2342" },
            new Case { Variant = "危険", Classes = new[] { "button-danger" }, States = "Focus", Border = "#2c2320" },
            new Case { Variant = "危険", Classes = new[] { "button-danger" }, States = "Disabled", Background = "#ded0c2", Border = "#d8c6b5" },
            new Case { Variant = "危険", Classes = new[] { "button-danger" }, States = "Hover,Disabled", Background = "#ded0c2", Border = "#d8c6b5" },

            // 早押しボタン（theme-views-game.uss）
            new Case { Variant = "早押し", Classes = new[] { "game-buzz-button" }, States = "", Background = "#e4ac15" },
            new Case { Variant = "早押し", Classes = new[] { "game-buzz-button" }, States = "Hover", Background = "#f3c64a" },
            new Case { Variant = "早押し", Classes = new[] { "game-buzz-button" }, States = "Hover,Active", Background = "#c08f0b" },
            new Case { Variant = "早押し", Classes = new[] { "game-buzz-button" }, States = "Focus", Border = "#2c2320" },

            // 選択式で自分が選んだ選択肢（選んだ直後に無効化される。#132 レビュー L5-5）
            new Case { Variant = "選択中", Classes = new[] { "game-choice-button", "game-choice-button--selected" }, States = "Disabled", Background = "#e6d5c4", Border = "#6f5410" },
        };

        [UnityTest]
        [Timeout(90000)]
        public IEnumerator ButtonStates_UseThemeTokens_NotDefaultTheme()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var pseudoStatesProperty = typeof(VisualElement).GetProperty(
                "pseudoStates", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(pseudoStatesProperty, "VisualElement.pseudoStates が見つかりません（Unity の内部 API が変わった可能性）。");

            var failures = new List<string>();

            foreach (var testCase in Cases)
            {
                var button = new Button { text = "テスト" };
                foreach (var ussClass in testCase.Classes)
                {
                    button.AddToClassList(ussClass);
                }

                panelRoot.Add(button);
                yield return null;

                SetPseudoStates(pseudoStatesProperty, button, testCase.States);

                // transition（0.07〜0.09s）が終わるまで待つ。
                var deadline = Time.realtimeSinceStartup + 0.4f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                var background = (Color32)button.resolvedStyle.backgroundColor;
                var border = (Color32)button.resolvedStyle.borderTopColor;
                var label = $"{testCase.Variant} [{(testCase.States.Length == 0 ? "通常" : testCase.States)}]";

                if (testCase.Background != null && !Approximately(background, testCase.Background))
                {
                    failures.Add($"{label} 背景: 期待 {testCase.Background} / 実際 {ToHex(background)}");
                }

                if (testCase.Border != null && !Approximately(border, testCase.Border))
                {
                    failures.Add($"{label} 枠: 期待 {testCase.Border} / 実際 {ToHex(border)}");
                }

                button.RemoveFromHierarchy();
            }

            Assert.That(failures, Is.Empty, "テーマ色になっていない状態があります:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// 入力欄・トグル・Foldout のホバー／フォーカスの見た目も同じ方法で確かめる。
        /// 疑似状態はフィールド自身（ホバー判定の対象）に立て、内側の部品の resolvedStyle を読む。
        /// </summary>
        [UnityTest]
        [Timeout(90000)]
        public IEnumerator FieldStates_UseThemeTokens_NotDefaultTheme()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var pseudoStatesProperty = typeof(VisualElement).GetProperty(
                "pseudoStates", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(pseudoStatesProperty, "VisualElement.pseudoStates が見つかりません。");

            var failures = new List<string>();

            // (フィールド生成, 疑似状態, 内側の部品のクラス, 読む項目, 期待値)
            var checks = new List<(Func<VisualElement> create, string states, string partClass, string property, string expected)>
            {
                (() => new TextField("テスト"), "Hover", "unity-base-text-field__input", "border", "#c08f0b"),
                (() => new DropdownField("テスト", new List<string> { "A" }, 0), "Hover", "unity-base-popup-field__input", "border", "#c08f0b"),
                // 未チェックの箱（#132 レビュー M5-1。既定テーマでは #f0f0f0 一色で枠が無く、パネルに対して 1.10:1）
                (() => new Toggle("テスト"), "", "unity-toggle__checkmark", "border", "#8d7768"),
                (() => new Toggle("テスト"), "", "unity-toggle__checkmark", "background", "#ffffff"),
                (() => new Toggle("テスト"), "Hover", "unity-toggle__checkmark", "background", "#ffffff"),
                (() => new Toggle("テスト") { value = true }, "Checked", "unity-toggle__checkmark", "background", "#ffffff"),
                (() => new Toggle("テスト"), "Disabled", "unity-toggle__checkmark", "background", "#ded0c2"),
                (() => new Toggle("テスト"), "Hover", "unity-toggle__checkmark", "border", "#c08f0b"),
                // フォーカスリングは #174 で #8a6a12 から --color-text（#2c2320）に変更した。
                (() => new Toggle("テスト"), "Focus", "unity-toggle__checkmark", "border", "#2c2320"),
                // 疑似状態を直接立てるので、USS の規則だけで決まる色を見ている。
                (() => new TextField("テスト"), "Focus", "unity-base-text-field__input", "border", "#2c2320"),
                (() => new DropdownField("テスト", new List<string> { "A" }, 0), "Focus", "unity-base-popup-field__input", "border", "#2c2320"),
                // メニューを開いている間（フィールドが :active）の入力欄。既定テーマでは #959595 になっていた。
                (() => new DropdownField("テスト", new List<string> { "A" }, 0), "Active", "unity-base-popup-field__input", "background", "#fff8ec"),
                (() => new DropdownField("テスト", new List<string> { "A" }, 0), "Hover,Active", "unity-base-popup-field__input", "background", "#fff8ec"),
            };

            foreach (var (create, states, partClass, property, expected) in checks)
            {
                var field = create();
                panelRoot.Add(field);
                yield return null;

                SetPseudoStates(pseudoStatesProperty, field, states);
                var deadline = Time.realtimeSinceStartup + 0.4f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                var part = field.Q(className: partClass);
                if (part == null)
                {
                    failures.Add($"{field.GetType().Name} の内側に .{partClass} が見つかりません。");
                }
                else
                {
                    var actual = (Color32)(property == "border"
                        ? part.resolvedStyle.borderTopColor
                        : property == "background" ? part.resolvedStyle.backgroundColor : part.resolvedStyle.color);
                    if (!Approximately(actual, expected))
                    {
                        // 原因調査のため、部品から上の要素のクラスも出す。
                        var chain = new List<string>();
                        for (var e = part; e != null && e != field.parent; e = e.parent)
                        {
                            chain.Add($"{e.name}[{string.Join(" ", e.GetClasses())}]");
                        }

                        failures.Add($"{field.GetType().Name} [{states}] .{partClass} {property}: 期待 {expected} / 実際 {ToHex(actual)}"
                                     + $" / 祖先: {string.Join(" < ", chain)}");
                    }
                }

                field.RemoveFromHierarchy();
            }

            // Foldout の見出し（ホバーは Foldout 内側のトグルに立てる）
            var foldout = new Foldout { text = "テスト" };
            panelRoot.Add(foldout);
            yield return null;
            var foldoutToggle = foldout.Q(className: "unity-foldout__toggle");
            if (foldoutToggle == null)
            {
                failures.Add("Foldout の内側に .unity-foldout__toggle が見つかりません。");
            }
            else
            {
                SetPseudoStates(pseudoStatesProperty, foldoutToggle, "Hover");
                var deadline = Time.realtimeSinceStartup + 0.4f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                var text = foldout.Q(className: "unity-foldout__text");
                var actual = text == null ? default : (Color32)text.resolvedStyle.color;
                if (text == null || !Approximately(actual, "#6f5410"))
                {
                    failures.Add($"Foldout [Hover] 見出しの文字色: 期待 #6f5410 / 実際 {(text == null ? "（要素なし）" : ToHex(actual))}");
                }
            }

            foldout.RemoveFromHierarchy();

            Assert.That(failures, Is.Empty, "テーマ色になっていない状態があります:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// 疑似状態を丸ごと上書きする（Disabled ビットを立てなければ :enabled が一致する）。
        /// Unity の内部 enum <c>PseudoStates</c> やプロパティが変わったときに原因が分かるよう、
        /// 反射の失敗は分かりやすいメッセージで落とす（#132 レビュー L5-3）。
        /// </summary>
        private static void SetPseudoStates(PropertyInfo property, VisualElement element, string states)
        {
            var enumType = property.PropertyType;
            var value = 0;
            foreach (var name in states.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    value |= Convert.ToInt32(Enum.Parse(enumType, name.Trim()));
                }
                catch (ArgumentException e)
                {
                    Assert.Fail($"Unity の内部 enum {enumType.FullName} に '{name.Trim()}' がありません"
                                + $"（定義: {string.Join(", ", Enum.GetNames(enumType))}）。Unity の更新で名前が変わった可能性があります: {e.Message}");
                }
            }

            try
            {
                property.SetValue(element, Enum.ToObject(enumType, value));
            }
            catch (Exception e) when (e is ArgumentException || e is TargetInvocationException || e is MethodAccessException)
            {
                Assert.Fail($"VisualElement.pseudoStates に値を設定できません（Unity の内部 API が変わった可能性）: {e.GetType().Name}: {e.Message}");
            }
        }

        private static bool Approximately(Color32 actual, string expectedHex, int tolerance = 3)
        {
            ColorUtility.TryParseHtmlString(expectedHex, out var expectedColor);
            Color32 expected = expectedColor;
            return Mathf.Abs(actual.r - expected.r) <= tolerance
                   && Mathf.Abs(actual.g - expected.g) <= tolerance
                   && Mathf.Abs(actual.b - expected.b) <= tolerance;
        }

        private static string ToHex(Color32 color) => $"#{color.r:x2}{color.g:x2}{color.b:x2}";
    }
}
