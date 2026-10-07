using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Credits;
using TsumugiQuiz.UI.Views;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// <see cref="CreditsLicenseCatalog"/> による同梱ライセンス全文（Resources/Licenses/*.txt）の
    /// 読み込みと、必須文言の含有を検証する（issue #33 の受け入れ条件、レビュー M-4）。
    /// </summary>
    public class CreditsLicenseCatalogTests
    {
        private static readonly IReadOnlyList<string> ExpectedIds = new[]
        {
            "tsumugi-character-credit",
            "voicevox-core",
            "onnxruntime",
            "voicevox-onnxruntime",
            "onnxruntime-third-party-notices",
            "open-jtalk",
            "unity-packages",
            "unity-companion-license",
            "mono-nat",
            "noto-sans-jp",
        };

        /// <summary>
        /// issue #100 レビュー M-2: "unity-packages" の期待クレジット文言を、実装の
        /// <see cref="CreditsLicenseCatalog.BuildUnityAttributionCreditLine"/> を呼び出さずに
        /// 独立したリテラルテンプレート（<see cref="Application.productName"/> と現在年だけを
        /// 差し込む）で組み立てる。実装を直接呼ぶと実装のバグをそのまま期待値として複製してしまい
        /// テストが無意味になるため、docs/licenses.md §11 の原文（Section 2.12 の定型文 +
        /// Trademark Notice and Attribution Statement）を独自に書き下ろしている。
        /// </summary>
        private static string BuildExpectedUnityAttributionCreditLine()
        {
            var productName = Application.productName;
            var year = System.DateTime.Now.Year; // レビュー L-1: ローカル時刻を使う
            return
                $"{productName} was made with Unity®. Unity is a trademark or registered trademark of Unity Technologies\n" +
                $"Copyright © 2005-{year} Unity Technologies. All rights reserved.\n" +
                $"{productName} is not sponsored by or affiliated with Unity Technologies or its affiliates. " +
                "Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.";
        }

        // M-4: docs/licenses.md 確定文言と一字一句一致することを検証する（id 順は ExpectedIds と対応）。
        private static readonly IReadOnlyDictionary<string, string> ExpectedCreditLines =
            new Dictionary<string, string>
            {
                ["tsumugi-character-credit"] =
                    "VOICEVOX:春日部つむぎ\n春日部つむぎ立ち絵 (C) 春日部つくし",
                ["voicevox-core"] =
                    "VOICEVOX CORE (C) 2021 Hiroshiba Kazuyuki (MIT License)",
                ["onnxruntime"] =
                    "ONNX Runtime (C) Microsoft Corporation (MIT License)",
                ["voicevox-onnxruntime"] =
                    "voicevox_onnxruntime (C) 2021 VOICEVOX (MIT License)",
                ["onnxruntime-third-party-notices"] =
                    "ONNX Runtime が同梱する第三者コンポーネント（Apache-2.0 / MIT / Unicode License 等）の通知。",
                ["open-jtalk"] =
                    "Open JTalk (Modified BSD License)\n" +
                    "open_jtalk_dic_utf_8 (C) Nara Institute of Science and Technology / " +
                    "The UniDic Consortium / Nagoya Institute of Technology (Modified BSD License)",
                ["unity-packages"] =
                    BuildExpectedUnityAttributionCreditLine() +
                        "\n\nUnity と各パッケージ (C) Unity Technologies (Unity Companion License)",
                ["unity-companion-license"] =
                    "上記 Unity パッケージ群に適用されるライセンスの全文。",
                ["mono-nat"] =
                    "Mono.Nat (C) 2006 Alan McGovern, 2007 Ben Motmans, 2013 Nicholas Terry (MIT License)",
                ["noto-sans-jp"] =
                    "Noto Sans JP — © 2014-2021 Adobe (SIL Open Font License 1.1)",
            };

        [Test]
        public void Entries_ContainsExactlyTheExpectedIds()
        {
            var ids = CreditsLicenseCatalog.Entries.Select(e => e.Id).ToList();

            CollectionAssert.AreEquivalent(ExpectedIds, ids);
        }

        [Test]
        public void Entries_AreOrderedByLicensesMdSection12_CharacterThenVoiceThenOss()
        {
            var sections = CreditsLicenseCatalog.Entries.Select(e => e.Section).ToList();

            // docs/licenses.md §12: 1.キャラクター 2.音声 3.OSS の順であること
            // （Character の後に Voice、Voice の後に Oss だけが現れる＝一度切り替わったら戻らない）。
            var lastSeen = CreditsLicenseCatalog.Section.Character;
            foreach (var section in sections)
            {
                Assert.GreaterOrEqual((int)section, (int)lastSeen,
                    "セクションの並び順が docs/licenses.md §12（キャラクター→音声→OSS）に反しています。");
                lastSeen = section;
            }
        }

        [Test]
        public void CreditLine_EachEntry_MatchesLicensesMdVerbatim()
        {
            foreach (var entry in CreditsLicenseCatalog.Entries)
            {
                Assert.IsTrue(ExpectedCreditLines.TryGetValue(entry.Id, out var expected),
                    $"{entry.Id} の期待値が ExpectedCreditLines に定義されていません。");
                Assert.AreEqual(expected, entry.CreditLine,
                    $"{entry.Id} の CreditLine が docs/licenses.md の確定文言と一致しません。");
            }
        }

        [Test]
        public void LoadText_EachEntry_ReturnsNonEmptyText()
        {
            foreach (var entry in CreditsLicenseCatalog.Entries)
            {
                var text = CreditsLicenseCatalog.LoadText(entry);

                Assert.IsFalse(string.IsNullOrWhiteSpace(text),
                    $"{entry.Id} のテキストが空です。Resources/{entry.ResourcePath}.txt を確認してください。");
            }
        }

        [Test]
        public void RequiredPhrases_AppearSomewhereAcrossAllLicenseFiles()
        {
            var combined = string.Join("\n", CreditsLicenseCatalog.Entries.Select(CreditsLicenseCatalog.LoadText));

            Assert.IsTrue(combined.Contains("VOICEVOX:春日部つむぎ"),
                "「VOICEVOX:春日部つむぎ」が Resources/Licenses のいずれのファイルにも見つかりません。");
            Assert.IsTrue(combined.Contains("Copyright (c) 2021 Hiroshiba Kazuyuki"),
                "「Copyright (c) 2021 Hiroshiba Kazuyuki」が Resources/Licenses のいずれのファイルにも見つかりません。");
            Assert.IsTrue(combined.Contains("SIL OPEN FONT LICENSE"),
                "「SIL OPEN FONT LICENSE」が Resources/Licenses のいずれのファイルにも見つかりません。");
            Assert.IsTrue(combined.Contains("Unity Companion License"),
                "「Unity Companion License」が Resources/Licenses のいずれのファイルにも見つかりません。");
            // M-1: Noto Sans JP のヘッダに追記した著作権・商標表示。
            Assert.IsTrue(combined.Contains("Copyright © 2014-2021 Adobe"),
                "「Copyright © 2014-2021 Adobe」が Resources/Licenses のいずれのファイルにも見つかりません。");
            Assert.IsTrue(combined.Contains("Noto is a trademark of Google Inc."),
                "「Noto is a trademark of Google Inc.」が Resources/Licenses のいずれのファイルにも見つかりません。");
            // issue #100: Unity Editor Software Terms Section 2.12 の帰属定型文と、
            // Trademark Notice and Attribution Statement（unity-packages-notices.txt に存在するはず）。
            Assert.IsTrue(combined.Contains("was made with Unity®"),
                "「was made with Unity®」が Resources/Licenses のいずれのファイルにも見つかりません。");
            Assert.IsTrue(combined.Contains("is not sponsored by or affiliated with Unity Technologies"),
                "Trademark Notice and Attribution Statement が Resources/Licenses のいずれのファイルにも見つかりません。");
        }

        [Test]
        public void EntriesForSection_ReturnsOnlyThatSection_InDeclaredOrder()
        {
            foreach (CreditsLicenseCatalog.Section section in System.Enum.GetValues(typeof(CreditsLicenseCatalog.Section)))
            {
                var filtered = CreditsLicenseCatalog.EntriesForSection(section).ToList();

                Assert.IsTrue(filtered.All(e => e.Section == section));
                Assert.IsTrue(filtered.Count > 0, $"{section} に属する項目がありません。");
            }
        }

        [Test]
        public void UnityPackagesEntry_CreditLine_ContainsUnityEditorSoftwareTermsSection212Attribution()
        {
            // issue #100, docs/licenses.md §11: Unity Editor Software Terms Section 2.12 が要求する
            // 帰属定型文、および Unity's Trademark Guidelines（branding-trademarks v.4.2）の
            // Trademark Notice and Attribution Statement が「3. OSS」節の unity-packages エントリの
            // クレジット行に含まれることを検証する（レビュー H-3・L-1）。
            var entry = CreditsLicenseCatalog.Entries.Single(e => e.Id == "unity-packages");

            StringAssert.Contains("was made with Unity®", entry.CreditLine);
            StringAssert.Contains(
                "Unity is a trademark or registered trademark of Unity Technologies", entry.CreditLine);
            StringAssert.Contains(
                $"Copyright © 2005-{System.DateTime.Now.Year} Unity Technologies. All rights reserved.",
                entry.CreditLine);
            StringAssert.Contains(
                "is not sponsored by or affiliated with Unity Technologies or its affiliates.", entry.CreditLine);
            StringAssert.Contains(
                "Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.",
                entry.CreditLine);
            StringAssert.StartsWith(Application.productName, entry.CreditLine);

            // レビュー H-1: Section 2.12 原文の "Unity's Trademark Guidelines" リンク先
            // （branding-trademarks）と、そこから個別参照される商標一覧（Unity Trademark List）を
            // 別リンクとして両方掲載していることを検証する。
            Assert.IsTrue(entry.SourceLinks.Any(l => l.Url == "https://unity.com/legal/branding-trademarks"),
                "Unity's Trademark Guidelines（branding-trademarks）への出典リンクが見つかりません。");
            Assert.IsTrue(entry.SourceLinks.Any(l => l.Url == "https://unity.com/legal/trademarks"),
                "Unity Trademark List（trademarks）への出典リンクが見つかりません。");
        }

        [Test]
        public void ResolveDisplayText_EntryWithScreenSummary_ReturnsSummary_NotFullResourceText()
        {
            // H-3: ONNX Runtime の third-party-notices は画面には要約のみを表示する。
            var entry = CreditsLicenseCatalog.Entries.Single(e => e.Id == "onnxruntime-third-party-notices");

            var displayText = CreditsView.ResolveDisplayText(entry);

            Assert.AreEqual(entry.ScreenSummary, displayText);
            StringAssert.Contains("THIRD-PARTY-NOTICES.txt", displayText);
        }

        [Test]
        public void ResolveDisplayText_MissingResource_ReturnsFallbackMessage()
        {
            // M-4: LoadText が失敗した場合のフォールバック表示を検証する。
            var bogusEntry = new CreditsLicenseCatalog.Entry(
                id: "bogus-entry-for-test",
                section: CreditsLicenseCatalog.Section.Oss,
                displayName: "存在しないエントリ",
                creditLine: "テスト用",
                resourcePath: "Licenses/does-not-exist-for-testing",
                sourceLinks: null);

            LogAssert.Expect(LogType.Error, new Regex(@"\[CreditsLicenseCatalog\].*does-not-exist-for-testing"));
            var displayText = CreditsView.ResolveDisplayText(bogusEntry);

            Assert.AreEqual(CreditsView.LicenseLoadFailureMessage, displayText);
        }
    }
}
