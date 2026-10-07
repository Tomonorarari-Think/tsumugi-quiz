using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.TextLayout
{
    /// <summary>
    /// issue #189: 文字のはみ出し・改行位置の崩れの回帰テスト（ネットワーク不要の画面）。
    /// 各 View のルートを docs/architecture.md §10.2 の論理ビューポートに固定して確かめる
    /// （<see cref="TextLayoutProbe.LogicalViewports"/>）。
    /// </summary>
    public sealed class TextWrappingLayoutSceneTests
    {
        /// <summary>入力欄の幅を十分に超える問題文（複数行入力欄の折り返し確認用）。</summary>
        private const string LongQuestionText =
            "とても長い問題文の例です。入力欄の幅を超える文字列は、入力欄の中で折り返して全体が見えなければなりません。";

        private ConsentFileScope _consentScope;
        private AppSettingsFileScope _appSettingsScope;
        private string _testSetFilePath;
        private string _testSetTitle;

        [SetUp]
        public void SetUp()
        {
            _consentScope = ConsentFileScope.Backup();
            _appSettingsScope = AppSettingsFileScope.Backup();
            var store = ConsentGate.CreateDefaultStore();
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);

            var folder = QuestionRepository.GetDefaultQuestionsFolderPath();
            Directory.CreateDirectory(folder);
            var fileName = "i189-text-layout-" + Guid.NewGuid().ToString("N");
            _testSetFilePath = Path.Combine(folder, fileName + ".json");
            _testSetTitle = "i189 文字配置テスト " + fileName;
            var question = new Question("q1", QuestionType.FreeText, "短い問題文", answers: new[] { "こたえ" });
            var set = new QuestionSet(1, fileName, _testSetTitle, string.Empty, new[] { question });
            Assert.IsTrue(QuestionSetWriter.TryWriteNew(_testSetFilePath, set, out var error), error);
        }

        [TearDown]
        public void RestoreFiles()
        {
            if (_testSetFilePath != null && File.Exists(_testSetFilePath))
            {
                File.Delete(_testSetFilePath);
            }

            _consentScope?.Restore();
            _appSettingsScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return UnloadMainSceneRoutine();
            TearDownMainSceneAndBootstrapSingletons();
        }

        /// <summary>
        /// 修正前: 見出し（.heading = 折り返さない）がパネル幅 420px を超えてパネルの外まではみ出していた（見出しの幅は実測 487px）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 読み上げ状態パネルの見出しと案内文はパネルの内側に収まる()
        {
            VisualElement panel = null;
            VisualElement overlay = null;
            yield return OpenTtsStatusPanel((o, p) => { overlay = o; panel = p; });
            var headline = panel.Q<Label>("tts-status-headline");
            var guidance = panel.Q<Label>("tts-status-guidance");

            // 既定（未確認）の見出しは、パネルが実際に表示している文言のまま 1 行に収まること（#189 レビュー L-3）。
            var defaultHeadline = headline.text;
            Assert.IsNotEmpty(defaultHeadline, "読み上げ状態パネルの見出しが空です。");

            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return ResizeOverlay(overlay, viewport);

                headline.text = defaultHeadline;
                yield return null;
                yield return null;
                Assert.That(
                    TextLayoutProbe.SingleLineWidth(headline, headline.text),
                    Is.LessThanOrEqualTo(headline.contentRect.width + 0.5f),
                    $"{viewport}: 見出し「{headline.text}」が 1 行に収まっていません。");

                // 理由別の見出し・案内文（定義元 TtsStatusMessages）はすべてパネルの内側で折り返すこと。
                foreach (TtsUnavailableReason reason in Enum.GetValues(typeof(TtsUnavailableReason)))
                {
                    var message = TtsStatusMessages.For(reason);
                    headline.text = message.Headline;
                    guidance.text = message.Guidance;
                    yield return null;
                    yield return null;

                    var panelContentWorld = ContentWorldRect(panel);
                    foreach (var label in new[] { headline, guidance })
                    {
                        Assert.AreEqual(WhiteSpace.Normal, label.resolvedStyle.whiteSpace, $"{label.name} は折り返せること。");
                        Assert.IsTrue(
                            TextLayoutProbe.IsHorizontallyInside(label.worldBound, panelContentWorld),
                            $"{viewport} / {reason}: {label.name} がパネルの内側からはみ出しています（{label.worldBound} / {panelContentWorld}）。");
                    }
                }
            }
        }

        /// <summary>
        /// #189 レビュー M-1: 見出し・案内文の中央ぞろえ（.tts-status-panel > .body-text）が、折りたたみの
        /// 配置手順（tts-setup-instructions-text、同じ .body-text）に当たらないこと。手順は左ぞろえのまま読む長文。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 読み上げ状態パネルの配置手順は左ぞろえのまま表示する()
        {
            VisualElement panel = null;
            yield return OpenTtsStatusPanel((_, p) => panel = p);

            // 未確認の状態では「配置手順を表示」ボタンは隠れているが、クリック処理自体は状態に依存しない。
            yield return SimulateClickRoutine(panel.Q<Button>("tts-setup-guide-button"));
            var setupScroll = panel.Q<ScrollView>("tts-setup-instructions-scroll");
            yield return WaitUntil(
                () => setupScroll.resolvedStyle.display == DisplayStyle.Flex, "配置手順が開きません。", dumpRoot: panel);

            var instructions = panel.Q<Label>("tts-setup-instructions-text");
            Assert.IsNotEmpty(instructions.text, "配置手順の本文が読み込まれていません。");
            Assert.AreEqual(TextAnchor.UpperLeft, instructions.resolvedStyle.unityTextAlign,
                "配置手順の本文は左ぞろえのままであること（中央ぞろえは見出し・案内文だけ）。");
            Assert.AreEqual(TextAnchor.UpperCenter, panel.Q<Label>("tts-status-guidance").resolvedStyle.unityTextAlign,
                "案内文は中央ぞろえであること。");
        }

        /// <summary>
        /// 修正前: ラベルの幅が中身任せで、長いラベルが入力欄を押し縮め（1290x1075 で出題セットID欄が 199px）、行ごとに入力欄の左端がずれていた。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 設定画面のラベルは列にそろい入力欄と重ならない()
        {
            VisualElement settingsRoot = null;
            yield return OpenSettings(r => settingsRoot = r);

            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(settingsRoot, viewport);

                var fields = settingsRoot.Q<VisualElement>("room-settings-fields")
                    .Query<VisualElement>(className: "unity-base-field")
                    .ToList()
                    .Where(f => !(f is Toggle) && f.Q<Label>(className: "unity-base-field__label") != null)
                    .ToList();
                Assert.That(fields.Count, Is.GreaterThanOrEqualTo(10), "ルーム設定の入力項目が見つかりません。");

                var inputLefts = new List<float>();
                foreach (var field in fields)
                {
                    var label = field.Q<Label>(className: "unity-base-field__label");
                    var input = field.Q<VisualElement>(className: "unity-base-field__input");
                    Assert.That(label.worldBound.xMax, Is.LessThanOrEqualTo(input.worldBound.xMin + 0.5f),
                        $"{viewport}: {field.name} のラベルが入力欄に重なっています。");
                    Assert.That(input.worldBound.width, Is.GreaterThanOrEqualTo(field.worldBound.width * 0.4f),
                        $"{viewport}: {field.name} の入力欄が細すぎます（{input.worldBound.width:0}px）。");
                    inputLefts.Add(input.worldBound.xMin);
                }

                Assert.That(inputLefts.Max() - inputLefts.Min(), Is.LessThanOrEqualTo(1f),
                    $"{viewport}: 入力欄（ドロップダウンを含む）の左端がそろっていません（{inputLefts.Min():0.0}〜{inputLefts.Max():0.0}）。");
            }
        }

        /// <summary>
        /// ラベルを列に固定したため、長いラベルが自動で折り返すと「…タイムアウト（秒 / ）」のように末尾だけ・語の途中で
        /// 改行する（#189 の途中版で実測）。明示改行の位置以外では折り返さないこと（明示改行を外すと失敗する。レビュー L-5）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 設定画面のラベルは明示改行の位置でだけ改行する()
        {
            VisualElement settingsRoot = null;
            yield return OpenSettings(r => settingsRoot = r);

            yield return SimulateClickRoutine(settingsRoot.Q<Button>("settings-tab-app-button"));
            settingsRoot.Q<Foldout>("app-advanced-foldout").value = true;
            settingsRoot.Q<Foldout>("room-advanced-foldout").value = true;

            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(settingsRoot, viewport);

                // アプリ設定タブを表示中。ルーム設定タブ側は非表示で測れないため、両方のタブを順に表示して確認する。
                foreach (var tab in new[] { "settings-tab-app-button", "settings-tab-room-button" })
                {
                    yield return SimulateClickRoutine(settingsRoot.Q<Button>(tab));
                    yield return null;
                    yield return null;

                    var orphans = settingsRoot.Q<ScrollView>("settings-scroll-view")
                        .Query<Label>(className: "unity-base-field__label")
                        .ToList()
                        .Where(label => TextLayoutProbe.IsDisplayed(label))
                        .SelectMany(label => TextLayoutProbe.FindImplicitlyWrappedLines(label))
                        .ToList();
                    Assert.IsEmpty(orphans, $"{viewport} / {tab}: 明示改行以外の位置で折り返すラベル（語の途中・末尾だけの改行になりうる）: {string.Join(" / ", orphans)}");
                }
            }
        }

        /// <summary>
        /// issue #199: 「詳細設定」（Foldout）の中の入力欄が Foldout の字下げの分だけ右にずれ、右端もパネルの外
        /// （スクロール領域の端）まで伸びていた（900x750 で左端 14px・右端 16px のずれ）。各タブで、表示中の入力項目
        /// （トグルを除く）がすべてパネル（.host-setup-panel）の内側にあり、入力欄の左端・右端がそろうこと。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 設定画面の詳細設定の入力欄はほかのパネルの入力欄とそろう()
        {
            VisualElement settingsRoot = null;
            yield return OpenSettings(r => settingsRoot = r);
            settingsRoot.Q<Foldout>("app-advanced-foldout").value = true;
            settingsRoot.Q<Foldout>("room-advanced-foldout").value = true;

            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(settingsRoot, viewport);
                foreach (var (tab, advancedField) in new[]
                         {
                             ("settings-tab-app-button", "app-network-port-field"),
                             ("settings-tab-room-button", "room-buzz-collect-window-field"),
                         })
                {
                    yield return SimulateClickRoutine(settingsRoot.Q<Button>(tab));
                    yield return null;
                    yield return null;

                    var fields = settingsRoot.Q<ScrollView>("settings-scroll-view")
                        .Query<VisualElement>(className: "unity-base-field")
                        .ToList()
                        .Where(f => !(f is Toggle) && TextLayoutProbe.IsDisplayed(f)
                                    && f.Q<Label>(className: "unity-base-field__label") != null)
                        .ToList();
                    Assert.That(fields.Select(f => f.name), Has.Member(advancedField), $"{tab}: 詳細設定の入力項目が表示されていません。");

                    var lefts = new List<float>();
                    var rights = new List<float>();
                    foreach (var field in fields)
                    {
                        var panel = FindAncestorWithClass(field, "host-setup-panel");
                        Assert.IsNotNull(panel, $"{viewport} / {tab}: {field.name} がパネル（.host-setup-panel）の中にありません。");
                        Assert.IsTrue(TextLayoutProbe.IsHorizontallyInside(field.worldBound, ContentWorldRect(panel)),
                            $"{viewport} / {tab}: {field.name} がパネルの内側からはみ出しています（{field.worldBound} / {ContentWorldRect(panel)}）。");
                        var input = field.Q<VisualElement>(className: "unity-base-field__input");
                        lefts.Add(input.worldBound.xMin);
                        rights.Add(input.worldBound.xMax);
                    }

                    Debug.Log($"[TextWrapping] {viewport} / {tab}: 入力欄の左端 {lefts.Min():0.0}〜{lefts.Max():0.0}、右端 {rights.Min():0.0}〜{rights.Max():0.0}（{fields.Count} 項目）");
                    Assert.That(lefts.Max() - lefts.Min(), Is.LessThanOrEqualTo(1f),
                        $"{viewport} / {tab}: 入力欄の左端がそろっていません（{lefts.Min():0.0}〜{lefts.Max():0.0}）。");
                    Assert.That(rights.Max() - rights.Min(), Is.LessThanOrEqualTo(1f),
                        $"{viewport} / {tab}: 入力欄の右端がそろっていません（{rights.Min():0.0}〜{rights.Max():0.0}）。");
                }
            }
        }

        /// <summary>
        /// 修正前: 複数行の入力欄（問題文・読み上げ用テキスト）が折り返さず、入力欄の右端で文字が切れていた。
        /// あわせて「読み上げ用テキスト（…）」のラベルが入力欄を押し縮めていた（入力欄が行幅の 40% 未満。このテストの幅の検証で検出する）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 問題エディタの複数行入力欄は入力欄の幅で折り返す()
        {
            VisualElement panelRoot = null;
            yield return OpenQuestionForm(r => panelRoot = r);

            var textField = panelRoot.Q<TextField>("question-form-text-field");
            var readingField = panelRoot.Q<TextField>("question-form-reading-text-field");
            Assert.IsNotNull(readingField, "読み上げ用テキストの入力欄が見つかりません。");
            textField.value = LongQuestionText;

            var editorRoot = panelRoot.Q<VisualElement>("question-editor-root");
            var form = panelRoot.Q<VisualElement>("question-edit-form-container");
            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(editorRoot, viewport);

                var input = textField.Q<VisualElement>(className: "unity-base-text-field__input");
                var inner = textField.Q<TextElement>(className: "unity-text-element--inner-input-field-component");
                Assert.IsNotNull(inner, "入力欄の中の TextElement が見つかりません。");
                Assert.IsTrue(TextLayoutProbe.IsHorizontallyInside(inner.worldBound, input.worldBound),
                    $"{viewport}: 問題文が入力欄の幅を超えて 1 行に伸びています（{inner.worldBound} / {input.worldBound}）。");
                var oneLine = inner.MeasureTextSize("あ", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).y;
                Assert.That(inner.worldBound.height, Is.GreaterThanOrEqualTo(oneLine * 1.8f),
                    $"{viewport}: 長い問題文が複数行に折り返されていません（高さ {inner.worldBound.height:0}px）。");

                var readingInput = readingField.Q<VisualElement>(className: "unity-base-field__input");
                Assert.That(readingInput.worldBound.width, Is.GreaterThanOrEqualTo(readingField.worldBound.width * 0.4f),
                    $"{viewport}: 読み上げ用テキストの入力欄が細すぎます（{readingInput.worldBound.width:0}px）。");

                // #189 レビュー L-4: フォームのラベル（明示改行を入れた「読み上げ用テキスト（省略可。…」を含む）。
                var orphans = form.Query<Label>(className: "unity-base-field__label").ToList()
                    .Where(label => TextLayoutProbe.IsDisplayed(label))
                    .SelectMany(label => TextLayoutProbe.FindImplicitlyWrappedLines(label))
                    .ToList();
                Assert.IsEmpty(orphans, $"{viewport}: 明示改行以外の位置で折り返すラベル（語の途中・末尾だけの改行になりうる）: {string.Join(" / ", orphans)}");
            }
        }

        /// <summary>
        /// #189 レビュー L-2: 複数行の入力欄の white-space が pre-wrap であること（normal に戻すと失敗するのは
        /// resolvedStyle.whiteSpace の確認だけ）。あわせて、折り返しを有効にした状態で連続スペースと改行が
        /// カーソル位置に反映されることも見る（こちらは #189 の実測で normal でも通り、差は見られなかった。
        /// 折り返しでカーソル位置が崩れていないことの確認として残す）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 問題エディタの複数行入力欄はpre_wrapで折り返しカーソル位置が崩れない()
        {
            VisualElement panelRoot = null;
            yield return OpenQuestionForm(r => panelRoot = r);

            const string value = "あいう  えお\nかき";
            var textField = panelRoot.Q<TextField>("question-form-text-field");
            textField.value = value;
            yield return null;
            yield return null;

            Assert.AreEqual(WhiteSpace.PreWrap, textField.Q<VisualElement>(className: "unity-base-text-field__input").resolvedStyle.whiteSpace,
                "複数行の入力欄は pre-wrap であること。");

            var selection = textField.textSelection;
            var afterFirstSpace = selection.GetCursorPositionFromStringIndex(4);
            var afterSecondSpace = selection.GetCursorPositionFromStringIndex(5);
            Assert.That(afterSecondSpace.x, Is.GreaterThan(afterFirstSpace.x + 1f),
                $"2 つ目のスペースが詰められています（4 文字目の後 x={afterFirstSpace.x:0.0}、5 文字目の後 x={afterSecondSpace.x:0.0}）。");

            var start = selection.GetCursorPositionFromStringIndex(0);
            var end = selection.GetCursorPositionFromStringIndex(value.Length);
            Assert.That(end.y, Is.GreaterThan(start.y + 1f),
                $"改行の後の文字が 2 行目に置かれていません（先頭 y={start.y:0.0}、末尾 y={end.y:0.0}）。");
        }

        /// <summary>
        /// #189 レビュー L-8: 選択式の「正解」（RadioButtonGroup）のラベルが、同じフォームの TextField のラベルと左端でそろうこと。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 問題エディタの正解のラベルは他の項目と左端がそろう()
        {
            VisualElement panelRoot = null;
            yield return OpenQuestionForm(r => panelRoot = r);

            panelRoot.Q<DropdownField>("question-form-type-field").value = "選択式";
            RadioButtonGroup group = null;
            yield return WaitForElement<RadioButtonGroup>(panelRoot, "question-form-correct-index-group", g => group = g);

            var editorRoot = panelRoot.Q<VisualElement>("question-editor-root");
            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(editorRoot, viewport);

                var groupLabel = group.Q<Label>(className: "unity-base-field__label");
                var textLabel = panelRoot.Q<TextField>("question-form-text-field").Q<Label>(className: "unity-base-field__label");
                Assert.That(groupLabel.worldBound.xMin, Is.EqualTo(textLabel.worldBound.xMin).Within(1f),
                    $"{viewport}: 「正解」のラベルの左端（{groupLabel.worldBound.xMin:0.0}）が「問題文」（{textLabel.worldBound.xMin:0.0}）とずれています。");
            }
        }

        private IEnumerator OpenQuestionForm(Action<VisualElement> onRoot)
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);
            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            router.ShowView(ViewNames.QuestionEditor);

            VisualElement setList = null;
            yield return WaitForElement<VisualElement>(panelRoot, "set-list-container", f => setList = f);
            VisualElement setRow = null;
            yield return WaitUntil(() => (setRow = FindRow(setList, _testSetTitle)) != null, "テスト用セットの行が見つかりません。", dumpRoot: panelRoot);
            yield return SimulateRowClickRoutine(setRow, dumpRoot: panelRoot);
            var questionList = panelRoot.Q<VisualElement>("question-list-container");
            VisualElement questionRow = null;
            yield return WaitUntil(() => (questionRow = FindRow(questionList, "q1")) != null, "テスト用問題の行が見つかりません。", dumpRoot: panelRoot);
            yield return SimulateRowClickRoutine(questionRow, dumpRoot: panelRoot);

            yield return WaitForElement<TextField>(panelRoot, "question-form-text-field", _ => { });
            onRoot(panelRoot);
        }

        private static IEnumerator OpenTtsStatusPanel(Action<VisualElement, VisualElement> onOpened)
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            Button statusButton = null;
            yield return WaitForElement<Button>(panelRoot, "tts-status-button", b => statusButton = b);
            yield return SimulateClickRoutine(statusButton);

            VisualElement overlay = null;
            yield return WaitForElement<VisualElement>(panelRoot, "tts-status-overlay", o => overlay = o);
            var panel = overlay.Q<VisualElement>("tts-status-panel");
            yield return null;
            yield return null;
            onOpened(overlay, panel);
        }

        /// <summary>
        /// 読み上げ状態パネルのオーバーレイ（Document のルート直下、position: absolute で全面を覆う）を
        /// 論理ビューポートの大きさに固定する。
        /// </summary>
        private static IEnumerator ResizeOverlay(VisualElement overlay, Vector2 size)
        {
            overlay.style.right = StyleKeyword.Auto;
            overlay.style.bottom = StyleKeyword.Auto;
            overlay.style.width = size.x;
            overlay.style.height = size.y;
            for (var i = 0; i < 3; i++)
            {
                yield return null;
            }
        }

        /// <summary>パネルの内容領域（枠線・内側余白を除く）をワールド座標で返す。</summary>
        private static Rect ContentWorldRect(VisualElement panel)
        {
            var style = panel.resolvedStyle;
            var bound = panel.worldBound;
            return Rect.MinMaxRect(
                bound.xMin + style.borderLeftWidth + style.paddingLeft,
                bound.yMin + style.borderTopWidth + style.paddingTop,
                bound.xMax - style.borderRightWidth - style.paddingRight,
                bound.yMax - style.borderBottomWidth - style.paddingBottom);
        }

        private static IEnumerator OpenSettings(Action<VisualElement> onRoot)
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);
            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Settings);

            VisualElement settingsRoot = null;
            yield return WaitForElement<VisualElement>(panelRoot, "settings-root", r => settingsRoot = r);
            yield return WaitForElement<Button>(panelRoot, "settings-tab-room-button", _ => { });
            onRoot(settingsRoot);
        }

        private static VisualElement FindAncestorWithClass(VisualElement element, string className)
        {
            for (var e = element.parent; e != null; e = e.parent)
            {
                if (e.ClassListContains(className))
                {
                    return e;
                }
            }

            return null;
        }

        private static VisualElement FindRow(VisualElement list, string containedText)
        {
            foreach (var row in list.Children())
            {
                var label = row.Q<Label>();
                if (label?.text != null && label.text.Contains(containedText))
                {
                    return row;
                }
            }

            return null;
        }
    }
}
