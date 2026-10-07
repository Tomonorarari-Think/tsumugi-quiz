using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Core.TextLayout;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.TextLayout
{
    /// <summary>
    /// issue #199: 動的な文言（Join 画面のエラー・状態表示、読み上げ状態パネルの見出し・案内文）が語の途中で改行しないこと。
    /// Advanced Text Generator は日本語を文字単位で折り返すため、<see cref="PhraseWrappedText"/> が区切りの位置
    /// （<see cref="PhraseSegmenter"/>）に改行を入れる。ここでは実際に出る文言をすべて表示し、
    /// 描画された各行が明示改行の位置でだけ区切られていること（自動の折り返しが無いこと）と、改行が区切りの位置に
    /// あることを確かめる。<see cref="PhraseWrappedText"/> を通さずに設定すると、長い文言で失敗する（#199 で確認）。
    /// </summary>
    public sealed class DynamicTextWrappingSceneTests
    {
        /// <summary>13 文字（長さ違い）の参加コード。実在しないダミー。</summary>
        private const string ThirteenCharacterCode = "ABCDEFGHJKMNP";

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

        /// <summary>
        /// issue の再現手順 2: 13 文字の参加コードを入力すると、長さ違いの文言が語の途中（「12 / 文字」）で折り返していた。
        /// JoinView が表示する経路そのもの（入力 → 文言の表示）で確かめる。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Join画面の長さ違いの文言は区切りの位置で改行する()
        {
            VisualElement joinRoot = null;
            yield return OpenJoin(r => joinRoot = r);
            var label = joinRoot.Q<Label>("join-code-error-label");

            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(joinRoot, viewport);
                joinRoot.Q<TextField>("join-code-field").value = string.Empty;
                yield return null;
                joinRoot.Q<TextField>("join-code-field").value = ThirteenCharacterCode;
                yield return WaitUntil(
                    () => !string.IsNullOrEmpty(label.text), "長さ違いの文言が表示されません。", dumpRoot: joinRoot);
                yield return null;
                yield return null;

                Assert.AreEqual(JoinCodeErrorMessages.InvalidLength, PhraseWrappedText.GetSourceText(label));
                AssertBreaksOnlyAtPhraseBoundaries(label, JoinCodeErrorMessages.InvalidLength, $"{viewport}");
            }
        }

        /// <summary>
        /// Join 画面の 3 つの表示欄（名前のエラー・参加コードのエラー・接続状況）に、実際に出る文言をすべて入れて確かめる
        /// （接続状況にはホストから届く拒否理由も出る。定義元は <see cref="ConnectionRejectionMessages"/>）。
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Join画面のエラーと状態の文言は区切りの位置で改行する()
        {
            VisualElement joinRoot = null;
            yield return OpenJoin(r => joinRoot = r);
            var labels = new[] { "player-name-error-label", "join-code-error-label", "join-status-label" }
                .Select(name => joinRoot.Q<Label>(name))
                .ToList();

            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(joinRoot, viewport);
                foreach (var message in JoinMessages())
                {
                    foreach (var label in labels)
                    {
                        PhraseWrappedText.SetText(label, message);
                        label.style.display = DisplayStyle.Flex;
                    }

                    yield return null;
                    yield return null;

                    foreach (var label in labels)
                    {
                        AssertBreaksOnlyAtPhraseBoundaries(label, message, $"{viewport} / {label.name}");
                    }
                }
            }
        }

        /// <summary>
        /// #206: タグを含む文言も、平文のまま（リッチテキストとして解釈させずに）表示し、区切りの位置で改行する。
        /// #199 の時点では「&lt;」を含む文言には改行を入れていなかったため、上限の長さの文言が自動で折り返していた
        /// （語の途中で改行しうる）。#206 ではホストから届いた切断理由を整えて表示していたための確認だったが、#208 から
        /// 切断理由として画面に出るのは自前の文言だけになった。<c>PhraseWrappedText</c> 自体の平文の扱いの確認として残している
        /// （文字列は <c>DisconnectReasonSanitizer.Sanitize</c> で整えた上限の長さのものを使う）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator タグを含む文言はタグを解釈せず区切りの位置で改行する()
        {
            // 区切り（PhraseSegmenter）はタグの前後では生じないので、1 つの片が列の幅を超えると描画側の折り返しに任せるしかない
            // （PhraseLineComposer の仕様）。ここではタグを含む片が列（351px）に収まる長さにする。
            var reason = DisconnectReasonSanitizer.Sanitize(
                "<b>ホスト</b>から、<size=60>通知</size>です。\n\n"
                + string.Concat(Enumerable.Repeat("接続が切断されました。", 20)));
            VisualElement joinRoot = null;
            yield return OpenJoin(r => joinRoot = r);
            var label = joinRoot.Q<Label>("join-status-label");

            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return TextLayoutProbe.ResizeRoot(joinRoot, viewport);
                PhraseWrappedText.SetText(label, reason);
                label.style.display = DisplayStyle.Flex;
                yield return null;
                yield return null;

                Assert.IsFalse(label.enableRichText, "PhraseWrappedText で設定した表示欄はタグを解釈しないはず。");
                Assert.AreEqual(reason, PhraseWrappedText.GetSourceText(label));
                Assert.That(label.text.Split('\n').Length, Is.GreaterThan(1), $"{viewport}: 上限の長さの理由は複数行になるはず。");
                AssertBreaksOnlyAtPhraseBoundaries(label, reason, $"{viewport} / 切断理由");
            }
        }

        /// <summary>
        /// issue の再現手順 3: 理由別の見出し（「…対応してい / ない可能性があります。」）と案内文（「…再試行してく / ださい。」）。
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator 読み上げ状態パネルの見出しと案内文は区切りの位置で改行する()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);
            Button statusButton = null;
            yield return WaitForElement<Button>(panelRoot, "tts-status-button", b => statusButton = b);
            yield return SimulateClickRoutine(statusButton);
            VisualElement overlay = null;
            yield return WaitForElement<VisualElement>(panelRoot, "tts-status-overlay", o => overlay = o);
            var headline = overlay.Q<Label>("tts-status-headline");
            var guidance = overlay.Q<Label>("tts-status-guidance");

            foreach (var viewport in TextLayoutProbe.LogicalViewports)
            {
                yield return ResizeOverlay(overlay, viewport);
                foreach (TtsUnavailableReason reason in Enum.GetValues(typeof(TtsUnavailableReason)))
                {
                    var message = TtsStatusMessages.For(reason);
                    PhraseWrappedText.SetText(headline, message.Headline);
                    PhraseWrappedText.SetText(guidance, message.Guidance);
                    yield return null;
                    yield return null;

                    AssertBreaksOnlyAtPhraseBoundaries(headline, message.Headline, $"{viewport} / {reason} / 見出し");
                    AssertBreaksOnlyAtPhraseBoundaries(guidance, message.Guidance, $"{viewport} / {reason} / 案内文");
                }
            }
        }

        /// <summary>
        /// #199 レビュー M-2: 英単語の間の空白（「ONNX |Runtime」）で改行すると、<see cref="PhraseLineComposer"/> は行末の空白を
        /// 削る。削った空白を除けば元の文言と一致し、改行が区切りの位置にあると判定できること。あわせて、ラベルの幅が
        /// 変わると元の文言から組み直すこと（<see cref="GeometryChangedEvent"/>）も確かめる。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator 英単語の間の空白で改行しても区切りの位置として判定し幅が戻れば組み直す()
        {
            const string text = "ONNX Runtime のバージョン";
            VisualElement joinRoot = null;
            yield return OpenJoin(r => joinRoot = r);
            yield return TextLayoutProbe.ResizeRoot(joinRoot, PanelScaleProbe.ViewportMinimumWindow);
            var label = joinRoot.Q<Label>("join-code-error-label");
            label.style.display = DisplayStyle.Flex;
            PhraseWrappedText.SetText(label, text);
            yield return null;
            yield return null;
            Assert.AreEqual(text, label.text, "列の幅（360px）では 1 行に収まるはずです。");

            // 「ONNX Runtime の」が収まらず、各片は収まる幅に狭める。
            var narrow = TextLayoutProbe.SingleLineWidth(label, "ONNX Runtime の") * 0.9f;
            Assert.That(TextLayoutProbe.SingleLineWidth(label, "Runtime の"), Is.LessThan(narrow - PhraseLineComposer.FitTolerance));
            Assert.That(TextLayoutProbe.SingleLineWidth(label, "バージョン"), Is.LessThan(narrow - PhraseLineComposer.FitTolerance));
            label.style.width = narrow + label.resolvedStyle.paddingLeft + label.resolvedStyle.paddingRight
                                + label.resolvedStyle.borderLeftWidth + label.resolvedStyle.borderRightWidth;
            label.style.flexGrow = 0;
            label.style.alignSelf = Align.FlexStart;
            yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual("ONNX\nRuntime の\nバージョン", label.text, "空白の位置で改行し、行末の空白を削ること。");
            AssertBreaksOnlyAtPhraseBoundaries(label, text, "狭い幅");

            label.style.width = StyleKeyword.Null;
            label.style.alignSelf = StyleKeyword.Null;
            yield return null;
            yield return null;
            yield return null;
            Assert.AreEqual(text, label.text, "幅が戻ったら元の文言から組み直すこと。");
        }

        /// <summary>
        /// 描画された各行が明示改行の位置でだけ区切られ（自動の折り返しが無い）、明示改行はすべて
        /// <see cref="PhraseSegmenter"/> の区切りの位置にあり、改行と、改行の直前で削った空白（<see cref="PhraseLineComposer"/>
        /// は行末の空白を削る）を除けば元の文言と一致すること。
        /// </summary>
        private static void AssertBreaksOnlyAtPhraseBoundaries(TextElement label, string message, string context)
        {
            var lines = TextLayoutProbe.VisualLines(label);
            var described = string.Join(" / ", lines.Select(l => l.TrimEnd('\n')));
            Debug.Log($"[DynamicTextWrapping] {context}: width={label.contentRect.width:0.0} lines={lines.Count}: {described}");

            var breakOffsets = AlignWithSource(label.text, message, context);
            var autoWrapped = TextLayoutProbe.FindAutoWrappedVisualLines(label).ToList();
            Assert.IsEmpty(autoWrapped,
                $"{context}: 区切りの位置ではなく自動で折り返した行があります（語の途中で改行しうる）: {described}");

            var boundaries = PhraseBoundaries(message);
            foreach (var offset in breakOffsets)
            {
                Assert.IsTrue(boundaries.Contains(offset), $"{context}: 区切りではない位置（{offset} 文字目）で改行しています: {described}");
            }
        }

        /// <summary>
        /// 表示中の文字列（改行を入れたもの）を元の文言と 1 文字ずつ突き合わせ、入れた改行の位置を元の文言の添字で返す。
        /// 改行の直前で削られた空白は元の文言の側で読み飛ばす（改行の位置は空白の後ろ = 次の片の先頭になる）。
        /// 元の文言にもともとある改行は、入れた改行として数えない。
        /// </summary>
        private static List<int> AlignWithSource(string composed, string message, string context)
        {
            var offsets = new List<int>();
            var m = 0;
            foreach (var c in composed)
            {
                if (c == '\n')
                {
                    if (m < message.Length && message[m] == '\n')
                    {
                        m++;
                        continue;
                    }

                    while (m < message.Length && message[m] != '\n' && char.IsWhiteSpace(message[m]))
                    {
                        m++;
                    }

                    offsets.Add(m);
                    continue;
                }

                Assert.IsTrue(m < message.Length && message[m] == c,
                    $"{context}: 改行と行末の空白以外の文字が変わっています（元の {m} 文字目。元「{message}」/ 表示「{composed}」）。");
                m++;
            }

            Assert.AreEqual(message.Length, m, $"{context}: 元の文言の末尾が欠けています（元「{message}」/ 表示「{composed}」）。");
            return offsets;
        }

        private static HashSet<int> PhraseBoundaries(string message)
        {
            var boundaries = new HashSet<int>();
            var position = 0;
            foreach (var segment in PhraseSegmenter.Segment(message))
            {
                position += segment.Length;
                boundaries.Add(position);
            }

            return boundaries;
        }

        /// <summary>Join 画面に出る文言（定義元の定数と、拒否理由の全種類）。</summary>
        private static IEnumerable<string> JoinMessages()
        {
            var messages = new List<string>
            {
                JoinCodeErrorMessages.InvalidLength,
                JoinCodeErrorMessages.InvalidCharacter,
                JoinCodeErrorMessages.InvalidChecksum,
                JoinStatusMessages.Connecting,
                JoinStatusMessages.Timeout,
                JoinStatusMessages.TransportFailure,
                JoinStatusMessages.DisconnectedWithoutReason,
                JoinStatusMessages.NetworkServiceUnavailable,
            };
            foreach (ConnectionRejectionReason reason in Enum.GetValues(typeof(ConnectionRejectionReason)))
            {
                messages.Add(ConnectionRejectionMessages.Create(reason, 65535, 65535));
            }

            // 承認後の切断の文言と、NGO の理由から対応づける文言（#208）。Join 画面・ロビーに出る。
            messages.AddRange(DisconnectReasonMessages.All);

            return messages.Where(m => !string.IsNullOrEmpty(m)).Distinct();
        }

        private static IEnumerator OpenJoin(Action<VisualElement> onRoot)
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);
            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            router.ShowView(ViewNames.Join);

            VisualElement joinRoot = null;
            yield return WaitForElement<VisualElement>(panelRoot, "join-root", r => joinRoot = r);
            yield return WaitForElement<Label>(panelRoot, "join-status-label", _ => { });
            onRoot(joinRoot);
        }

        /// <summary>読み上げ状態パネルのオーバーレイを論理ビューポートの大きさに固定する（TextWrappingLayoutSceneTests と同じ）。</summary>
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
    }
}
