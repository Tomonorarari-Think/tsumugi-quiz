using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// theme.uss のデザイントークン（issue #132）を静的に検証する。
    ///
    /// - 必須トークンが :root に定義されていること
    /// - 主要な「文字色 / 地色」の組み合わせが WCAG 2.1 AA のコントラスト比を満たすこと
    ///   （通常サイズ 4.5:1、大きい文字・非文字 UI 部品 3:1）
    /// - theme.uss を読み込む UXML が画面別の theme-views-*.uss 4 本もすべて読み込んでいること
    ///   （#132 でスタイルシートを theme.uss + 画面ドメイン別 4 本に分割したため、一部だけ
    ///   読み込む UXML があると UI Builder のプレビューと実行時で見た目が食い違う）
    /// - Styles/ 配下のすべての .uss が 800 行以内であること
    ///
    /// 実行時の見た目そのもの（既定テーマとの詳細度勝負の結果）は PlayMode 側
    /// （MainSceneUiTests / ViewContainerSizingTests など）で確認する。
    /// </summary>
    public sealed class ThemePaletteTests
    {
        private const string StylesRelativeDirectory = "TsumugiQuiz/UI/Styles";
        private const string ThemeUssRelativePath = StylesRelativeDirectory + "/theme.uss";
        private const string ThemeTssRelativePath = StylesRelativeDirectory + "/tsumugi-theme.tss";

        /// <summary>1 スタイルシートあたりの上限行数（CLAUDE.md「コーディング規約の要点」）。</summary>
        private const int MaxStyleSheetLines = 800;

        /// <summary>
        /// theme.uss から分割したスタイルシート（読み込み順）。入力系コントロールの theme-controls.uss
        /// （#189 レビュー M-2）と、画面ドメインごとの theme-views-*.uss（#132 レビュー M-4）。
        /// tsumugi-theme.tss の @import 順・各 UXML の &lt;Style&gt; の並びと一致させること。
        /// </summary>
        private static readonly string[] ThemeViewsUssFileNames =
        {
            "theme-controls.uss",
            "theme-views-misc.uss",
            "theme-views-game.uss",
            "theme-views-lobby.uss",
            "theme-views-editor.uss",
        };

        /// <summary>WCAG 2.1 AA: 通常サイズの文字。</summary>
        private const double NormalTextMinRatio = 4.5;

        /// <summary>WCAG 2.1 AA: 大きい文字（18.66px 以上の太字 / 24px 以上）と非文字 UI 部品。</summary>
        private const double LargeTextMinRatio = 3.0;

        private static readonly string[] RequiredTokens =
        {
            "--color-background",
            "--color-surface",
            "--color-surface-alt",
            "--color-surface-hover",
            "--color-input",
            "--color-input-focus",
            "--color-surface-dim",
            "--color-border",
            "--color-divider",
            "--color-primary",
            "--color-primary-hover",
            "--color-primary-active",
            "--color-on-primary",
            "--color-primary-text",
            "--color-text",
            "--color-text-muted",
            "--color-accent",
            "--color-success",
            "--color-on-success",
            "--color-error",
            "--color-error-hover",
            "--color-error-active",
            "--color-on-error",
            "--color-error-text",
            "--color-warning",
            "--color-disabled",
            "--color-on-disabled",
            "--color-focus-ring",
            "--color-focus-ring-on-primary",
            "--color-selection",
            // 立ち絵（#192 ティント / #191 カードの地）。USS のセレクタ側にリテラルの色を書かないためのトークン。
            "--color-character-tint-none",
            "--color-character-tint-correct",
            "--color-character-tint-wrong",
            "--color-character-frame",
            "--font-size-title",
            "--font-size-heading",
            "--font-size-subheading",
            "--font-size-base",
            "--font-size-small",
            "--spacing-xs",
            "--spacing-s",
            "--spacing-m",
            "--spacing-l",
            "--radius-s",
            "--radius-m",
            "--radius-l",
        };

        /// <summary>
        /// 「この文字色をこの地色の上に置く」という USS 上の実際の組み合わせ。
        /// theme.uss / theme-views.uss のルールと対応させること。
        /// </summary>
        private static readonly ContrastCase[] ContrastCases =
        {
            // 本文・見出し
            new("--color-text", "--color-background", NormalTextMinRatio, ".screen-root 上の本文・見出し"),
            new("--color-text", "--color-surface", NormalTextMinRatio, "パネル上の本文"),
            new("--color-text", "--color-surface-alt", NormalTextMinRatio, "インセット・一覧行の本文"),
            new("--color-text", "--color-surface-hover", NormalTextMinRatio, "ホバー中のボタン・行の文字"),
            new("--color-text", "--color-input", NormalTextMinRatio, "入力欄の文字"),
            new("--color-text", "--color-input-focus", NormalTextMinRatio, "フォーカス中の入力欄の文字"),

            // 補助テキスト（.small-text / .body-text）
            new("--color-text-muted", "--color-background", NormalTextMinRatio, ".body-text / .small-text（地）"),
            new("--color-text-muted", "--color-surface", NormalTextMinRatio, ".small-text（パネル上）"),
            new("--color-text-muted", "--color-surface-alt", NormalTextMinRatio, ".lobby-player-state 等"),
            new("--color-text-muted", "--color-surface-dim", NormalTextMinRatio, "切断中の行の文字（#132 M-3）"),
            new("--color-text", "--color-surface-dim", NormalTextMinRatio, "切断中の行に残る通常色の文字"),

            // ブランド色の上の文字（.button-primary / .game-buzz-button / タブ選択中 / 選択行）
            new("--color-on-primary", "--color-primary", NormalTextMinRatio, ".button-primary の文字"),
            new("--color-on-primary", "--color-primary-hover", NormalTextMinRatio, ".button-primary:hover の文字"),
            new("--color-on-primary", "--color-primary-active", NormalTextMinRatio, ".button-primary:active の文字"),

            // ブランド色を文字として使う箇所（.join-code-label / .result-rank-badge）
            new("--color-primary-text", "--color-surface", NormalTextMinRatio, ".join-code-label（パネル上）"),
            new("--color-primary-text", "--color-surface-alt", NormalTextMinRatio, ".result-rank-badge（行の上）"),
            new("--color-primary-text", "--color-background", NormalTextMinRatio, "地の上のブランド色の文字"),

            // 状態色
            new("--color-error-text", "--color-surface", NormalTextMinRatio, ".error-text（パネル上）"),
            new("--color-error-text", "--color-surface-alt", NormalTextMinRatio, ".error-text（インセット上）"),
            new("--color-error-text", "--color-background", NormalTextMinRatio, ".error-text（地の上）"),
            new("--color-warning", "--color-surface", NormalTextMinRatio, ".host-setup-warning / .lobby-notice"),
            new("--color-warning", "--color-surface-alt", NormalTextMinRatio, "インセット上の警告文"),
            new("--color-warning", "--color-background", NormalTextMinRatio, "地の上の警告文"),
            new("--color-accent", "--color-surface", NormalTextMinRatio, ".status-text（パネル上）"),
            new("--color-accent", "--color-background", NormalTextMinRatio, ".status-text（地の上）"),
            new("--color-accent", "--color-surface-alt", NormalTextMinRatio, ".status-text（インセット上）"),
            new("--color-on-error", "--color-error", NormalTextMinRatio, ".button-danger / --wrong の文字"),
            new("--color-on-error", "--color-error-hover", NormalTextMinRatio, ".button-danger:hover の文字"),
            new("--color-on-error", "--color-error-active", NormalTextMinRatio, ".button-danger:active の文字"),
            new("--color-on-success", "--color-success", NormalTextMinRatio, ".game-choice-button--correct の文字"),

            // 非文字コントラスト（WCAG 1.4.11）: 枠線・フォーカスリング
            new("--color-border", "--color-input", LargeTextMinRatio, "入力欄の枠線"),
            new("--color-border", "--color-surface", LargeTextMinRatio, "パネル上の入力欄の枠線"),
            new("--color-border", "--color-background", LargeTextMinRatio, "地の上の中立ボタンの枠線"),
            new("--color-border", "--color-surface-alt", LargeTextMinRatio, "中立ボタン自身の地との境界"),
            new("--color-primary-text", "--color-divider", LargeTextMinRatio, "残り時間バーの塗りと地（#132 H2-1）"),
            new("--color-text", "--color-divider", NormalTextMinRatio, "切断中の行のバッジ文字（#132 M2-1）"),
            new("--color-focus-ring", "--color-surface-alt", LargeTextMinRatio, "中立ボタンのフォーカスリング"),
            new("--color-focus-ring", "--color-input", LargeTextMinRatio, "入力欄のフォーカスリング"),
            new("--color-focus-ring", "--color-input-focus", LargeTextMinRatio, "フォーカス中・メニューを開いている間の入力欄の枠（#132 レビュー L5-1）"),
            new("--color-focus-ring-on-primary", "--color-primary", LargeTextMinRatio, "金のボタンのフォーカスリング"),

            // フォーカス前後の枠色の差（WCAG 2.2 2.4.13 Focus Appearance を参考にした目標水準、#174。
            // ユーザー承認済み・2026-09-30）。通常時の枠はどのコントロールも --color-border で共通
            // （主要ボタンの通常時の枠も #174 で --color-border に統一した）。フォーカス用トークンだけ
            // 2 種類ある: 主要ボタン・早押しボタン・選択中タブは地が常に金のため、基調に関わらず暗い値に
            // 固定した --color-focus-ring-on-primary を使い、それ以外は基調で明暗が反転する
            // --color-focus-ring を使う。
            new("--color-border", "--color-focus-ring", LargeTextMinRatio, "通常時の枠とフォーカス時の枠の差（中立・危険ボタン、入力欄、ドロップダウン、トグル、#174）"),
            new("--color-border", "--color-focus-ring-on-primary", LargeTextMinRatio, "通常時の枠とフォーカス時の枠の差（主要ボタン、#174）"),

            // 無効化（AA の対象外だが、判読はできる水準を保つ）
            new("--color-on-disabled", "--color-disabled", LargeTextMinRatio, "無効化されたボタンの文字"),
        };

        private static Dictionary<string, string> _tokens;

        [OneTimeSetUp]
        public void LoadTokens()
        {
            _tokens = ParseRootTokens(ReadStyleSheet(ThemeUssRelativePath));
        }

        [Test]
        public void 必須のデザイントークンがrootに定義されている()
        {
            foreach (var token in RequiredTokens)
            {
                Assert.That(_tokens.ContainsKey(token), Is.True, $"theme.uss の :root に {token} がありません。");
            }
        }

        [Test]
        public void 色トークンは不透明なHEXか明示的なrgbaで書かれている()
        {
            foreach (var pair in _tokens)
            {
                if (!pair.Key.StartsWith("--color-", StringComparison.Ordinal))
                {
                    continue;
                }

                // 半透明が必要なトークン（テキスト選択の反転色など）だけ rgba() を許す。
                // その場合は「地の色が透けて見える」ため、コントラスト検証の対象外にする。
                if (pair.Value.StartsWith("rgba(", StringComparison.Ordinal))
                {
                    Assert.That(
                        Regex.IsMatch(pair.Value, @"^rgba\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*[0-9.]+\s*\)$"),
                        Is.True,
                        $"{pair.Key} の値 '{pair.Value}' は rgba(r, g, b, a) の形式ではありません。");
                    continue;
                }

                Assert.That(
                    TryParseHexColor(pair.Value, out var color),
                    Is.True,
                    $"{pair.Key} の値 '{pair.Value}' は #rrggbb 形式ではありません（コントラスト検証ができません）。");
                Assert.That(color.a, Is.EqualTo(1f).Within(0.001f), $"{pair.Key} は不透明であるべきです。");
            }
        }

        [Test]
        public void 主要な文字と地のコントラスト比がWCAG_AAを満たす()
        {
            var failures = new List<string>();

            foreach (var testCase in ContrastCases)
            {
                var foreground = GetColor(testCase.ForegroundToken);
                var background = GetColor(testCase.BackgroundToken);
                var ratio = ContrastRatio(foreground, background);

                if (ratio + 0.005 < testCase.MinRatio)
                {
                    failures.Add(
                        $"{testCase.Description}: {testCase.ForegroundToken} on {testCase.BackgroundToken} = "
                        + $"{ratio:F2}:1（必要 {testCase.MinRatio:F1}:1）");
                }
            }

            Assert.That(failures, Is.Empty, "WCAG 2.1 AA を満たさない配色があります:\n" + string.Join("\n", failures));
        }

        [Test]
        public void 本文の最小フォントサイズが15px以上ある()
        {
            Assert.That(ParsePixels("--font-size-small"), Is.GreaterThanOrEqualTo(15));
            Assert.That(ParsePixels("--font-size-base"), Is.GreaterThanOrEqualTo(16));
        }

        [Test]
        public void テーマtssが既定テーマとtheme_ussと画面別ussをこの順で読み込む()
        {
            var tss = ReadStyleSheet(ThemeTssRelativePath);

            // コメント中にも "theme.uss" という文字列が出てくるため、@import の並びだけを見る。
            var imports = new List<string>();
            foreach (Match match in Regex.Matches(tss, @"@import\s+url\(""(?<target>[^""]+)""\)"))
            {
                imports.Add(match.Groups["target"].Value);
            }

            var expected = new List<string> { "unity-theme://default", "theme.uss" };
            expected.AddRange(ThemeViewsUssFileNames);

            Assert.That(
                imports,
                Is.EqualTo(expected),
                "tsumugi-theme.tss の @import は「既定テーマ → theme.uss → theme-controls.uss → 画面別 theme-views-*.uss」の順であること"
                + $"（実際: {string.Join(", ", imports)}）。");
        }

        [Test]
        public void theme_ussを読み込むUXMLは画面別ussもすべて読み込む()
        {
            var uxmlFiles = Directory.GetFiles(Application.dataPath, "*.uxml", SearchOption.AllDirectories);
            var missing = new List<string>();

            foreach (var file in uxmlFiles)
            {
                var content = File.ReadAllText(file);
                if (!content.Contains("theme.uss"))
                {
                    continue;
                }

                // #189 レビュー M-2: 抜けだけでなく、tsumugi-theme.tss と同じ並び（theme.uss → 分割ファイル）であることも見る。
                var previousIndex = content.IndexOf("theme.uss", StringComparison.Ordinal);
                foreach (var viewsFileName in ThemeViewsUssFileNames)
                {
                    var index = content.IndexOf(viewsFileName, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        missing.Add($"{file} : {viewsFileName}");
                    }
                    else if (index < previousIndex)
                    {
                        missing.Add($"{file} : {viewsFileName}（読み込み順が tsumugi-theme.tss と異なる）");
                    }

                    previousIndex = Math.Max(previousIndex, index);
                }
            }

            Assert.That(missing, Is.Empty, "分割した uss の <Style> が抜けている、または順序が違う UXML:\n" + string.Join("\n", missing));
        }

        [Test]
        public void Styles配下のすべてのussが800行以内である()
        {
            var stylesDirectory = Path.Combine(Application.dataPath, StylesRelativeDirectory);
            Assert.That(Directory.Exists(stylesDirectory), Is.True, $"スタイルフォルダがありません: {stylesDirectory}");

            var tooLong = new List<string>();
            foreach (var file in Directory.GetFiles(stylesDirectory, "*.uss", SearchOption.AllDirectories))
            {
                var lineCount = File.ReadAllText(file).Split('\n').Length;
                if (lineCount > MaxStyleSheetLines)
                {
                    tooLong.Add($"{Path.GetFileName(file)}（{lineCount} 行）");
                }
            }

            Assert.That(
                tooLong,
                Is.Empty,
                $"{MaxStyleSheetLines} 行を超えたスタイルシート: {string.Join(", ", tooLong)}");
        }

        [Test]
        public void 分割した画面別ussがすべて存在する()
        {
            foreach (var fileName in ThemeViewsUssFileNames)
            {
                var path = Path.Combine(Application.dataPath, StylesRelativeDirectory, fileName);
                Assert.That(File.Exists(path), Is.True, $"画面別スタイルシートがありません: {path}");
            }
        }

        // ------------------------------------------------------------------
        // ヘルパー
        // ------------------------------------------------------------------

        private static string ReadStyleSheet(string relativePath)
        {
            var path = Path.Combine(Application.dataPath, relativePath);
            Assert.That(File.Exists(path), Is.True, $"スタイルシートが見つかりません: {path}");
            return File.ReadAllText(path);
        }

        /// <summary>
        /// <c>:root { ... }</c> ブロックから <c>--name: value;</c> を抜き出す。
        /// コメント（<c>/* ... */</c>）は先に取り除く。
        /// </summary>
        private static Dictionary<string, string> ParseRootTokens(string uss)
        {
            var withoutComments = Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var rootMatch = Regex.Match(withoutComments, @":root\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
            Assert.That(rootMatch.Success, Is.True, "theme.uss に :root ブロックが見つかりません。");

            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match declaration in Regex.Matches(rootMatch.Groups["body"].Value, @"(?<name>--[a-z0-9-]+)\s*:\s*(?<value>[^;]+);"))
            {
                tokens[declaration.Groups["name"].Value] = declaration.Groups["value"].Value.Trim();
            }

            return tokens;
        }

        private static Color GetColor(string token)
        {
            Assert.That(_tokens.ContainsKey(token), Is.True, $"トークン {token} がありません。");
            Assert.That(TryParseHexColor(_tokens[token], out var color), Is.True, $"{token} の値をパースできません: {_tokens[token]}");
            return color;
        }

        private static bool TryParseHexColor(string value, out Color color) =>
            ColorUtility.TryParseHtmlString(value.Trim(), out color);

        private static int ParsePixels(string token)
        {
            Assert.That(_tokens.ContainsKey(token), Is.True, $"トークン {token} がありません。");
            var value = _tokens[token].Trim();
            Assert.That(value.EndsWith("px", StringComparison.Ordinal), Is.True, $"{token} は px 指定であるべきです: {value}");
            return int.Parse(value.Substring(0, value.Length - 2), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// WCAG 2.1 の相対輝度。同じ式は <c>scripts/extract-palette.ps1</c> の
        /// <c>Get-RelativeLuminance</c> にもある（あちらは素材の調査用、こちらが theme.uss を
        /// 検証する本番のゲート）。片方を直したらもう片方も合わせること（#132 レビュー L-9）。
        /// </summary>
        private static double RelativeLuminance(Color color)
        {
            static double Channel(float raw)
            {
                double v = raw;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(color.r)) + (0.7152 * Channel(color.g)) + (0.0722 * Channel(color.b));
        }

        private static double ContrastRatio(Color a, Color b)
        {
            var la = RelativeLuminance(a);
            var lb = RelativeLuminance(b);
            var lighter = Math.Max(la, lb);
            var darker = Math.Min(la, lb);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private readonly struct ContrastCase
        {
            public ContrastCase(string foregroundToken, string backgroundToken, double minRatio, string description)
            {
                ForegroundToken = foregroundToken;
                BackgroundToken = backgroundToken;
                MinRatio = minRatio;
                Description = description;
            }

            public string ForegroundToken { get; }

            public string BackgroundToken { get; }

            public double MinRatio { get; }

            public string Description { get; }
        }
    }
}
