using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Core.TextLayout;
using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="PhraseSegmenter"/>（動的な文言の改行してよい位置、issue #199）を検証する。
    /// </summary>
    public class PhraseSegmenterTests
    {
        private const string ClosingOrPunctuation = "）」』】〕］｝〉》〙〗)]}’”、。，．！？.,!?:;…：％・";

        /// <summary>行頭に置かない記号（#199 レビュー L-1 で追加したもの）。</summary>
        private static readonly string[] NoBreakBeforeSymbols = { ".", ",", "!", "?", ":", ";", "…", "：", "％", "・" };

        [TestCase(null)]
        [TestCase("")]
        public void Segment_NullOrEmpty_ReturnsEmpty(string text)
        {
            Assert.That(PhraseSegmenter.Segment(text), Is.Empty);
        }

        // issue #199 の例。修正前は「12 / 文字」「参加コード / や」「対応していな / い」で折り返していた。
        // 「対応していない可能性」のように、い・る などで終わる語の後ろは区切らない（「使い方」「言い換え」を分けないため）。
        [TestCase(JoinCodeErrorMessages.InvalidLength, "参加コードの|長さが|違います|（ハイフンを|除いて|12文字です）。")]
        [TestCase(JoinStatusMessages.Timeout, "接続が|タイムアウトしました。|参加コードや|接続先を|確認してください。")]
        [TestCase("ONNX Runtime のバージョンが対応していない可能性があります。", "ONNX |Runtime の|バージョンが|対応していない可能性があります。")]
        [TestCase("「配置手順を表示」から手順を確認し、配置したら再試行してください。", "「配置手順を|表示」から|手順を|確認し、|配置したら|再試行してください。")]
        [TestCase(ConnectionRejectionMessages.SeatReserved, "切断したプレイヤーの|席を|確保中です|（最大 60 秒）。|しばらく|待ってからもう一度お試しください。")]
        public void Segment_SplitsAtPhraseBoundaries(string text, string expected)
        {
            Assert.That(string.Join("|", PhraseSegmenter.Segment(text)), Is.EqualTo(expected));
        }

        // 送り仮名・空白・長音符・繰り返し記号の前後では区切らない。
        [TestCase("読み上げ用の辞書", "読み上げ用の|辞書")]
        [TestCase("アプリを起動し直してください。", "アプリを|起動し直してください。")]
        [TestCase("確保中です（最大 60 秒）。", "確保中です|（最大 60 秒）。")]
        [TestCase("ルームの人々", "ルームの|人々")]
        [TestCase("１２文字", "１２文字")]
        public void Segment_KeepsWordsTogether(string text, string expected)
        {
            Assert.That(string.Join("|", PhraseSegmenter.Segment(text)), Is.EqualTo(expected));
        }

        [Test]
        public void Segment_Concatenation_RestoresOriginal_AndNoSegmentStartsWithClosingOrPunctuation()
        {
            foreach (var message in CatalogMessages())
            {
                var segments = PhraseSegmenter.Segment(message);
                Assert.That(string.Concat(segments), Is.EqualTo(message), message);
                foreach (var segment in segments.Skip(1))
                {
                    Assert.That(ClosingOrPunctuation.IndexOf(segment[0]), Is.EqualTo(-1),
                        $"「{segment}」が閉じ括弧・句読点で始まっています（{message}）。");
                    Assert.That(segment[0], Is.Not.EqualTo(' '), $"「{segment}」が空白で始まっています（{message}）。");
                }
            }
        }

        // #199 レビュー L-1: 英数字・URL・IP アドレス・参加コード・サロゲートペアは途中で区切らない。
        [TestCase("Version 1.2.3 を使います。", "Version 1.2.3 を|使います。")]
        [TestCase("https://example.com/path?a=1&b=2 を開いてください。", "https://example.com/path?a=1&b=2 を|開いてください。")]
        [TestCase("192.168.0.1:7777 に接続します。", "192.168.0.1:7777 に|接続します。")]
        [TestCase("参加コード ABCD-EFGH-JKMN を入力してください。", "参加コード |ABCD-EFGH-JKMN を|入力してください。")]
        [TestCase("\U00020BB7野家で食べる。", "\U00020BB7野家で|食べる。")]
        [TestCase("です。\U00020BB7野家", "です。|\U00020BB7野家")]
        [TestCase("回答1人目・× 不正解", "回答1人目・× 不正解")]
        [TestCase("辞書・モデルの探索パス", "辞書・モデルの|探索パス")]
        [TestCase("正答率は50％です。", "正答率は|50％です。")]
        public void Segment_KeepsAlphanumericsAndSymbolsTogether(string text, string expected)
        {
            Assert.That(string.Join("|", PhraseSegmenter.Segment(text)), Is.EqualTo(expected));
        }

        [Test]
        public void Segment_NeverSplitsSurrogatePair()
        {
            // 「つちよし」（U+20BB7）は UTF-16 で 2 文字。句読点・閉じ括弧・空白・助詞のどの後に置いても、ペアの間では区切らない。
            const string kichi = "\U00020BB7";
            foreach (var before in new[] { "。", "）", " ", "の", "漢", "A" })
            {
                foreach (var segment in PhraseSegmenter.Segment("文" + before + kichi + kichi + "字"))
                {
                    Assert.That(char.IsLowSurrogate(segment[0]), Is.False, $"「{before}」の後でサロゲートペアの間を区切っています。");
                    Assert.That(char.IsHighSurrogate(segment[segment.Length - 1]), Is.False, $"「{before}」の後でサロゲートペアの間を区切っています。");
                }
            }
        }

        [Test]
        public void Segment_NeverStartsSegmentWithNoBreakBeforeSymbol()
        {
            // 区切りになりうる位置（句点・閉じ括弧・助詞・空白・漢字の後）の直後に記号を置いても、記号の前では区切らない。
            foreach (var symbol in NoBreakBeforeSymbols)
            {
                foreach (var before in new[] { "終わり。", "（注）", "ホストの", "abc ", "漢字" })
                {
                    var text = before + symbol + "次の文章";
                    var segments = PhraseSegmenter.Segment(text);
                    Assert.That(string.Concat(segments), Is.EqualTo(text));
                    Assert.That(segments.Any(seg => seg.StartsWith(symbol, StringComparison.Ordinal)), Is.False,
                        $"「{symbol}」で始まる片があります: {string.Join("|", segments)}");
                }
            }
        }

        [Test]
        public void Segment_KeepsExistingNewlineInsideSegment()
        {
            Assert.That(string.Concat(PhraseSegmenter.Segment("一行目\n二行目")), Is.EqualTo("一行目\n二行目"));
        }

        /// <summary>UI に出る動的な文言（Join 画面・読み上げ状態パネル）の定義元すべて。</summary>
        internal static IEnumerable<string> CatalogMessages()
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
                messages.Add(ConnectionRejectionMessages.Create(reason, 3, 2));
            }

            // 承認後の切断の文言と、NGO の理由から対応づける文言（#208）。Join 画面・ロビーに出る。
            messages.AddRange(DisconnectReasonMessages.All);

            foreach (TtsUnavailableReason reason in Enum.GetValues(typeof(TtsUnavailableReason)))
            {
                var message = TtsStatusMessages.For(reason);
                messages.Add(message.Headline);
                messages.Add(message.Guidance);
            }

            return messages.Where(m => !string.IsNullOrEmpty(m)).Distinct();
        }
    }
}
