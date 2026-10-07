using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core.Participants;
using TsumugiQuiz.Tests.PlayMode.UI.TextLayout;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.PlayMode.UI.Game
{
    /// <summary>
    /// 参加者パネル（#194）の行の文字の配置を、docs/architecture.md §10.2 の 3 つの論理ビューポートで確かめる
    /// （PR #201 レビュー M-3、再レビュー M-B / M-C）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// game-view.uxml を実際の PanelSettings（panel-settings.asset）の UIDocument に読み込み、ルートを論理ビューポートの
    /// 大きさに固定する（GameViewVerticalFitSceneTests と同じ作法）。パネルの中身は <see cref="ParticipantPanel"/> に
    /// 表示内容を直接渡して描く（ネットワークは使わない）。
    /// </para>
    /// <para>
    /// 保証する範囲: 各行について、(1) 押下順位・名前・状態・得点の文字を要素自身の折り返し規則で組んだ大きさが
    /// 要素の内側に収まる（はみ出さない）、(2) それぞれの<b>文字の組み範囲</b>（要素の枠ではない）が互いに重ならない、
    /// (3) 得点の列がパネルの内側にある。読みやすい位置で折り返すか（語の途中で切れないか）は保証しない。
    /// 900x750 で代表的な状態の文言が 1 行に収まることは、別のテスト（<see cref="MinimumWindow_TypicalStatusesFitOnOneLine"/>）で見る。
    /// </para>
    /// </remarks>
    public sealed class GameViewParticipantPanelLayoutSceneTests
    {
        private const string ViewPath = "Assets/TsumugiQuiz/UI/Views/game-view.uxml";
        private const string PanelSettingsPath = "Assets/TsumugiQuiz/Settings/panel-settings.asset";
        private const float Tolerance = GameViewLayoutProbe.LayoutTolerance;

        private GameObject _documentObject;
        private VisualElement _gameRoot;

        [SetUp]
        public void CreateDocument()
        {
#if UNITY_EDITOR
            var view = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ViewPath);
            Assert.IsNotNull(view, $"{ViewPath} が見つかりません。");
            var panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(panelSettings, $"{PanelSettingsPath} が見つかりません。");

            _documentObject = new GameObject(nameof(GameViewParticipantPanelLayoutSceneTests));
            var document = _documentObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.rootVisualElement.Add(view.Instantiate());
            _gameRoot = document.rootVisualElement.Q<VisualElement>("game-root");
            Assert.IsNotNull(_gameRoot, "game-root が見つかりません。");
#else
            Assert.Ignore("PlayMode テストは Editor 上でのみ実行する（AssetDatabase を使うため）。");
#endif
        }

        [TearDown]
        public void DestroyDocument()
        {
            if (_documentObject != null)
            {
                Object.DestroyImmediate(_documentObject);
            }

            _documentObject = null;
            _gameRoot = null;
        }

        [UnityTest]
        public IEnumerator MinimumWindow_LongestTextsStayInsideAndDoNotOverlap()
            => RunWorstCase(PanelScaleProbe.ViewportMinimumWindow, "900x750");

        [UnityTest]
        public IEnumerator Logical16By9_LongestTextsStayInsideAndDoNotOverlap()
            => RunWorstCase(PanelScaleProbe.Viewport16By9, "16:9");

        [UnityTest]
        public IEnumerator LogicalUltraWide_LongestTextsStayInsideAndDoNotOverlap()
            => RunWorstCase(PanelScaleProbe.ViewportUltraWide, "21:9");

        /// <summary>
        /// PR #201 再レビュー M-B: 左列が最も狭い 900x750 でも、押下順位の列がある状態で
        /// 「回答1人目・× 不正解」「休み（お手つき）」が 1 行に収まる（不自然な位置で折り返さない）。
        /// </summary>
        [UnityTest]
        public IEnumerator MinimumWindow_TypicalStatusesFitOnOneLine()
        {
            var rows = new List<ParticipantPanelRow>
            {
                new ParticipantPanelRow(1, "とても長い名前のテストプレイヤー", false, true, ParticipantStatus.Wrong, 0, false, 1, -100),
                new ParticipantPanelRow(2, "あかり", true, true, ParticipantStatus.Suspended, 0, false, 0, 30),
                new ParticipantPanelRow(3, "ちなつ", false, true, ParticipantStatus.Answering, 1, false, 2, 10),
            };

            yield return Render(PanelScaleProbe.ViewportMinimumWindow, new ParticipantPanelState(
                rows, true, ParticipantPanelSummaryKind.BuzzEligible, 1, 3));

            foreach (var expected in new[] { "回答1人目・× 不正解", "休み（お手つき）" })
            {
                var status = FindStatus(expected);
                var lineWidth = TextLayoutProbe.SingleLineWidth(status, status.text);
                TestContext.WriteLine(
                    $"[900x750] 状態「{status.text}」の 1 行の文字幅 {lineWidth:F1} / 要素の幅 {status.contentRect.width:F1}");
                Assert.That(
                    lineWidth,
                    Is.LessThanOrEqualTo(status.contentRect.width + Tolerance),
                    $"「{status.text}」は 900x750 でも 1 行に収まるはず（文字幅 {lineWidth:F1}、要素の幅 {status.contentRect.width:F1}）。");
            }
        }

        private IEnumerator RunWorstCase(Vector2 viewport, string label)
        {
            yield return Render(viewport, BuildWorstCaseState());

            var panelBound = _gameRoot.Q<VisualElement>("participant-panel").worldBound;
            TestContext.WriteLine($"[{label}] 参加者パネルの幅 {panelBound.width:F1}");

            var rows = _gameRoot.Query<VisualElement>(className: ParticipantPanelPresenter.RowClass).ToList();
            Assert.AreEqual(12, rows.Count, $"[{label}] 12 行が描かれるはず。");

            foreach (var row in rows)
            {
                var rank = row.Q<Label>("participant-rank");
                var status = row.Q<Label>("participant-status");
                var name = row.Q<Label>("participant-name");
                var score = row.Q<Label>("participant-score");
                Assert.IsNotNull(rank, $"[{label}] {row.name}: 押下順位の列があるはず。");
                Assert.IsNotNull(status, $"[{label}] {row.name}: 状態の行があるはず。");
                Assert.IsNotNull(score, $"[{label}] {row.name}: 得点の列があるはず。");

                var prefix = $"[{label}] {row.name}";
                var rankText = TextRectFitsInside(rank, $"{prefix}: 押下順位");
                var nameText = TextRectFitsInside(name, $"{prefix}: 名前");
                var statusText = TextRectFitsInside(status, $"{prefix}: 状態");
                var scoreText = TextRectFitsInside(score, $"{prefix}: 得点");

                AssertNoOverlap(statusText, scoreText, $"{prefix}: 状態「{status.text}」と得点「{score.text}」の文字");
                AssertNoOverlap(nameText, scoreText, $"{prefix}: 名前と得点の文字");
                AssertNoOverlap(rankText, nameText, $"{prefix}: 押下順位と名前の文字");
                AssertNoOverlap(rankText, statusText, $"{prefix}: 押下順位と状態の文字");
                Assert.IsTrue(
                    TextLayoutProbe.IsHorizontallyInside(score.worldBound, panelBound),
                    $"{prefix}: 得点の列がパネルの外へはみ出している。");
            }
        }

        private IEnumerator Render(Vector2 viewport, ParticipantPanelState state)
        {
            GameViewLayoutProbe.SetLogicalViewport(_gameRoot, viewport);

            var panel = ParticipantPanel.Create(_gameRoot);
            Assert.IsNotNull(panel, "参加者パネルの要素が見つかりません。");
            panel.Render(state);

            for (var i = 0; i < 4; i++)
            {
                yield return null;
            }
        }

        private Label FindStatus(string text)
        {
            foreach (var status in _gameRoot.Query<Label>("participant-status").ToList())
            {
                if (status.text == text)
                {
                    return status;
                }
            }

            Assert.Fail($"状態「{text}」の行がありません。");
            return null;
        }

        /// <summary>
        /// 要素の文字を要素自身の折り返し規則で組み、要素の内側に収まることを確かめて、文字が占める範囲（パネル座標）を返す。
        /// 折り返さない（nowrap）要素は 1 行の文字幅、折り返す要素は要素の幅を上限に組んだ幅と高さで判定する。
        /// 文字の配置（左寄せ・右寄せ・中央）は要素の -unity-text-align に合わせる。
        /// </summary>
        private static Rect TextRectFitsInside(TextElement element, string label)
        {
            var content = element.contentRect;
            Vector2 size;
            var whiteSpace = element.resolvedStyle.whiteSpace;
            if (whiteSpace == WhiteSpace.NoWrap || whiteSpace == WhiteSpace.Pre)
            {
                size = element.MeasureTextSize(
                    element.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            }
            else
            {
                size = element.MeasureTextSize(
                    element.text, content.width, VisualElement.MeasureMode.AtMost, 0, VisualElement.MeasureMode.Undefined);
            }

            Assert.That(
                size.x,
                Is.LessThanOrEqualTo(content.width + Tolerance),
                $"{label}「{element.text}」の文字幅 {size.x:F1} が要素の幅 {content.width:F1} を超えている（はみ出す）。");
            Assert.That(
                size.y,
                Is.LessThanOrEqualTo(content.height + Tolerance),
                $"{label}「{element.text}」の文字の高さ {size.y:F1} が要素の高さ {content.height:F1} を超えている。");

            var world = element.LocalToWorld(content);
            float xMin;
            switch (element.resolvedStyle.unityTextAlign)
            {
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    xMin = world.xMax - size.x;
                    break;
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter:
                    xMin = world.center.x - (size.x / 2f);
                    break;
                default:
                    xMin = world.xMin;
                    break;
            }

            return new Rect(xMin, world.yMin, size.x, size.y);
        }

        /// <summary>2 つの文字の範囲が重ならない（許容誤差ぶん縮めて判定する）。</summary>
        private static void AssertNoOverlap(Rect a, Rect b, string label)
        {
            var shrunkA = new Rect(a.x + Tolerance, a.y + Tolerance, Mathf.Max(0f, a.width - (2 * Tolerance)), Mathf.Max(0f, a.height - (2 * Tolerance)));
            Assert.IsFalse(shrunkA.Overlaps(b), $"{label}が重なっている（{a} と {b}）。");
        }

        /// <summary>
        /// 最も長くなる行を集めた表示内容（定員の上限 12 人、全員に 2 桁の押下順位、最長の状態文言、負の 3 桁の得点）。
        /// </summary>
        private static ParticipantPanelState BuildWorstCaseState()
        {
            var rows = new List<ParticipantPanelRow>();
            for (var i = 0; i < 12; i++)
            {
                rows.Add(new ParticipantPanelRow(
                    (ulong)(i + 1),
                    "とても長い名前のテストプレイヤー",
                    isLocal: i == 0,
                    isConnected: i % 2 == 0,
                    status: i % 3 == 0 ? ParticipantStatus.Correct : ParticipantStatus.Wrong,
                    buzzRank: 12,
                    tiedWithWinner: true,
                    answerOrder: 12,
                    score: -100));
            }

            return new ParticipantPanelState(rows, true, ParticipantPanelSummaryKind.BuzzEligible, 12, 12);
        }
    }
}
