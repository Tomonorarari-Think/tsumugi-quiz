using System;
using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.TextLayout
{
    /// <summary>
    /// issue #206: <see cref="PlainText"/> を通したラベルが、リッチテキストのタグを解釈せず文字のまま表示すること。
    /// 既定のラベルはタグを解釈する（大きな文字・改行で画面を崩せる）ことも前提として確かめる。
    /// 幅・高さは実際の描画と同じテキスト生成器（<see cref="TextElement.MeasureTextSize"/>）で測る。
    /// </summary>
    public sealed class PlainTextSceneTests
    {
        private ConsentFileScope _consentScope;
        private AppSettingsFileScope _appSettingsScope;

        [SetUp]
        public void SetUp()
        {
            _consentScope = ConsentFileScope.Backup();
            _appSettingsScope = AppSettingsFileScope.Backup();
            ConsentGate.CreateDefaultStore().RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);
        }

        [TearDown]
        public void RestoreFiles()
        {
            _consentScope?.Restore();
            _appSettingsScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return UnloadMainSceneRoutine();
            TearDownMainSceneAndBootstrapSingletons();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 平文にしたラベルはタグを解釈せず文字のまま表示する()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var rich = new Label();
            var plain = PlainText.CreateLabel(string.Empty);
            panelRoot.Add(rich);
            panelRoot.Add(plain);
            yield return null;
            yield return null;

            Assert.IsTrue(rich.enableRichText, "前提: 既定のラベルはタグを解釈する。");
            Assert.IsFalse(plain.enableRichText);

            // 大きな文字: 既定のラベルでは 1 字が 60px の高さになるが、平文では 1 行の高さのまま。
            const string sized = "<size=60>あ</size>";
            var richSized = Measure(rich, sized);
            var plainSized = Measure(plain, sized);
            Debug.Log($"[PlainText] '{sized}' rich={richSized} plain={plainSized}");
            Assert.That(richSized.y, Is.GreaterThan(plainSized.y * 1.5f), "前提: 既定のラベルは <size> で文字を大きくする。");
            Assert.That(plainSized.x, Is.GreaterThan(richSized.x), "平文ではタグの文字も並べて表示する（横に長くなる）。");

            // 改行: 既定のラベルでは <br> で行が増えるが、平文では 1 行のまま。
            const string lineBreaks = "あ<br><br><br>あ";
            var richBreaks = Measure(rich, lineBreaks);
            var plainBreaks = Measure(plain, lineBreaks);
            var oneLine = Measure(plain, "あ");
            Debug.Log($"[PlainText] '{lineBreaks}' rich={richBreaks} plain={plainBreaks} oneLine={oneLine}");
            Assert.That(richBreaks.y, Is.GreaterThan(oneLine.y * 2f), "前提: 既定のラベルは <br> で改行する。");
            Assert.That(plainBreaks.y, Is.EqualTo(oneLine.y).Within(0.5f), "平文では <br> で改行しない。");

            // 空のタグ: 既定のラベルでは何も描かないが、平文では文字として描く。
            Assert.That(Measure(plain, "<b></b>").x, Is.GreaterThan(Measure(rich, "<b></b>").x));

            // PlainText.Apply は既存の要素にも効き、null は素通りする。
            var existing = new Label();
            Assert.AreSame(existing, PlainText.Apply(existing));
            Assert.IsFalse(existing.enableRichText);
            Assert.IsNull(PlainText.Apply<Label>(null));
        }

        private static Vector2 Measure(TextElement element, string text)
            => element.MeasureTextSize(text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
    }
}
