using System;
using System.Collections.Generic;
using TsumugiQuiz.UI;
using UnityEngine;

namespace TsumugiQuiz.UI.Credits
{
    /// <summary>
    /// クレジット画面（<see cref="Views.CreditsView"/>）に表示する各項目のメタデータと、
    /// 同梱ライセンス全文（Assets/TsumugiQuiz/Resources/Licenses/*.txt）の読み込みをまとめる
    /// （issue #33、docs/licenses.md §12 のクレジット画面構成順序に対応）。
    /// <see cref="TermsCatalog"/> の設計（Resources 経由での読み込み・エントリ一覧）に倣う。
    /// </summary>
    public static class CreditsLicenseCatalog
    {
        /// <summary>docs/licenses.md §12 が定めるクレジット画面のセクション順序。</summary>
        public enum Section
        {
            /// <summary>1. キャラクター（春日部つむぎ本体・立ち絵・ロゴ）。</summary>
            Character,

            /// <summary>2. 音声（VOICEVOX / voicevox_core / vvm / ONNX Runtime / Open JTalk・辞書）。</summary>
            Voice,

            /// <summary>3. OSS（Unity パッケージ群、Mono.Nat、Noto Sans JP）。</summary>
            Oss,
        }

        /// <summary>ライセンス・規約の出典ページへのリンク（見出しと URL の組）。</summary>
        public sealed class SourceLink
        {
            public string Label { get; }
            public string Url { get; }

            public SourceLink(string label, string url)
            {
                Label = label;
                Url = url;
            }
        }

        /// <summary>1件のクレジット項目に関する表示用メタデータ。</summary>
        public sealed class Entry
        {
            /// <summary>項目 ID（ファイル名のベースと対応）。</summary>
            public string Id { get; }

            /// <summary>どのセクション（1.キャラクター/2.音声/3.OSS）に表示するか。</summary>
            public Section Section { get; }

            /// <summary>画面に表示する見出し。</summary>
            public string DisplayName { get; }

            /// <summary>docs/licenses.md で確定した、常時表示するクレジット文言（原文どおり）。</summary>
            public string CreditLine { get; }

            /// <summary>Resources.Load で読み込む際のパス（拡張子なし。Resources/ からの相対パス）。</summary>
            public string ResourcePath { get; }

            /// <summary>ライセンス・規約の出典 URL 一覧。0件ならボタンを表示しない。</summary>
            public IReadOnlyList<SourceLink> SourceLinks { get; }

            /// <summary>
            /// null 以外の場合、画面の Foldout にはこの要約テキストを表示し、
            /// <see cref="ResourcePath"/> の全文は読み込まない（H-3）。
            /// ONNX Runtime の third-party-notices（約400KB）のように、画面に全文を載せると
            /// 実用的でないほど長いファイル向け。全文は同梱ファイル（Resources/Licenses、
            /// 配布 zip の THIRD-PARTY-NOTICES.txt）側で担保する。
            /// </summary>
            public string ScreenSummary { get; }

            public Entry(
                string id,
                Section section,
                string displayName,
                string creditLine,
                string resourcePath,
                IReadOnlyList<SourceLink> sourceLinks,
                string screenSummary = null)
            {
                Id = id;
                Section = section;
                DisplayName = displayName;
                CreditLine = creditLine;
                ResourcePath = resourcePath;
                SourceLinks = sourceLinks ?? Array.Empty<SourceLink>();
                ScreenSummary = screenSummary;
            }
        }

        /// <summary>
        /// Unity Editor Software Terms（https://unity.com/legal/editor-terms-of-service/software ）
        /// Section 2.12「Proprietary Notices and Attribution」が定める帰属定型文、および
        /// Unity's Trademark Guidelines（https://unity.com/legal/branding-trademarks 、v.4.2。
        /// Section 2.12 原文中の「Unity's Trademark Guidelines」リンクの実際の遷移先。
        /// レビュー H-1、確認日 2026-09-17、curl で取得した HTML の Next.js RSC ペイロード中の
        /// markDefs.href を根拠に確認）の「TRADEMARK NOTICE AND ATTRIBUTION STATEMENT」を、
        /// プロジェクト名（<see cref="Application.productName"/>）と現在年（ローカル時刻。
        /// レビュー L-1: 配布ビルドはユーザーのローカル PC で起動するため UTC ではなくローカル時刻を使う）
        /// を差し込んで生成する（issue #100、docs/licenses.md §11 の該当部分抜粋・確認日 2026-09-17）。
        /// クレジット画面はこの条項が言う「credits and attributions」に該当するため Section 2.12 の
        /// 定型文が必須表示。加えて、Unity's Trademark Guidelines の
        /// "Permitted uses: Wordmarks" 条項が、published materials で Wordmark（ロゴ画像ではなく
        /// テキストとしての「Unity」表記）を使う場合に "a proper Trademark Notice and Attribution
        /// Statement" の提示を求めているため、その定型文（原文の
        /// "[This website/these materials/(product/service or company name)] [is/are] not
        /// sponsored by or affiliated with Unity Technologies or its affiliates. [Unity
        /// Trademark(s)] [is a/are] trademark[s] or registered trademark[s] of Unity Technologies
        /// or its affiliates in the U.S. and elsewhere." に、プロジェクト名と単数形の
        /// Trademark（Unity）を当てはめたもの）も続けて表示する。
        /// テストから同じ文言を再計算できるよう internal にしている（EditMode への
        /// InternalsVisibleTo は AssemblyInfo.cs 参照。ただしレビュー M-2 により、EditMode テストは
        /// このメソッドを直接呼ばずリテラルなテンプレートで期待値を組み立てて検証する）。
        /// </summary>
        internal static string BuildUnityAttributionCreditLine()
        {
            var productName = Application.productName;
            var currentYear = DateTime.Now.Year;
            return
                $"{productName} was made with Unity®. Unity is a trademark or registered trademark of Unity Technologies\n" +
                $"Copyright © 2005-{currentYear} Unity Technologies. All rights reserved.\n" +
                $"{productName} is not sponsored by or affiliated with Unity Technologies or its affiliates. " +
                "Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.";
        }

        /// <summary>
        /// docs/licenses.md §12 の順序（1.キャラクター 2.音声 3.OSS）で並んだ全項目。
        /// 追加・削除する場合は Resources/Licenses/*.txt の追加・削除とセットで行うこと。
        /// </summary>
        public static readonly IReadOnlyList<Entry> Entries = new[]
        {
            // 1. キャラクター
            // TODO(#34, licenses.md §3): 立ち絵クレジットの「春日部つくし」表記は、公式サイト規約
            // （rule2）が権利主体を「春日部つむぎ運営」と表記している点との差異があるため、
            // 配布前に公式サイトで最新のクレジット表記を再確認すること。
            // TODO(#34, licenses.md §4): 春日部つむぎロゴ（tsumugi_logo.png）専用の追加条件の有無は
            // 未確認（ロゴ自体は本画面では使用していない）。ロゴを画面上で使う場合は配布前に
            // 公式サイトで追加条件の有無を再確認すること。
            new Entry(
                "tsumugi-character-credit",
                Section.Character,
                "春日部つむぎ（音声・立ち絵）",
                "VOICEVOX:春日部つむぎ\n春日部つむぎ立ち絵 (C) 春日部つくし",
                "Licenses/tsumugi-character-credit",
                new[]
                {
                    new SourceLink("VOICEVOX 利用規約", "https://voicevox.hiroshiba.jp/term/"),
                    new SourceLink("春日部つむぎ 利用規約", "https://tsumugi-official.studio.site/rule2"),
                }),

            // 2. 音声
            new Entry(
                "voicevox-core",
                Section.Voice,
                "VOICEVOX CORE",
                "VOICEVOX CORE (C) 2021 Hiroshiba Kazuyuki (MIT License)",
                "Licenses/voicevox-core-license",
                new[] { new SourceLink("voicevox_core LICENSE", "https://github.com/VOICEVOX/voicevox_core/blob/0.17.0/LICENSE") }),
            new Entry(
                "onnxruntime",
                Section.Voice,
                "ONNX Runtime",
                "ONNX Runtime (C) Microsoft Corporation (MIT License)",
                "Licenses/onnxruntime-license",
                new[] { new SourceLink("ONNX Runtime LICENSE", "https://github.com/microsoft/onnxruntime/blob/main/LICENSE") }),
            new Entry(
                "voicevox-onnxruntime",
                Section.Voice,
                "voicevox_onnxruntime",
                "voicevox_onnxruntime (C) 2021 VOICEVOX (MIT License)",
                "Licenses/voicevox-onnxruntime-license",
                new[] { new SourceLink("onnxruntime-builder LICENSE", "https://github.com/VOICEVOX/onnxruntime-builder/blob/main/LICENSE") }),
            new Entry(
                "onnxruntime-third-party-notices",
                Section.Voice,
                "ONNX Runtime 同梱コンポーネントの通知",
                "ONNX Runtime が同梱する第三者コンポーネント（Apache-2.0 / MIT / Unicode License 等）の通知。",
                "Licenses/onnxruntime-third-party-notices",
                Array.Empty<SourceLink>(),
                screenSummary:
                    "ONNX Runtime は、Apache License 2.0（19クレート）/ MIT License（3クレート）/ " +
                    "Unicode License Agreement - Data Files and Software（1クレート）の第三者コンポーネント" +
                    "（Rust クレート群など）を同梱しています。\n" +
                    "全文（約400KB）は画面上には表示していません。全文は同梱ファイル " +
                    "Assets/TsumugiQuiz/Resources/Licenses/onnxruntime-third-party-notices.txt、および " +
                    "配布物同梱の THIRD-PARTY-NOTICES.txt を参照してください。"),
            new Entry(
                "open-jtalk",
                Section.Voice,
                "Open JTalk / 辞書",
                "Open JTalk (Modified BSD License)\n" +
                "open_jtalk_dic_utf_8 (C) Nara Institute of Science and Technology / " +
                "The UniDic Consortium / Nagoya Institute of Technology (Modified BSD License)",
                "Licenses/open-jtalk-dict-copying",
                new[] { new SourceLink("Open JTalk 公式サイト", "http://open-jtalk.sourceforge.net/") }),

            // 3. OSS
            // TODO(#34, licenses.md §11): Unity Personal のスプラッシュ画面義務の正式な条項文言は
            // 一次ドキュメントで確認できていない。配布前に Unity Hub / Project Settings 上のライセンス
            // 種別表示、および最新の Unity 利用規約で Personal エディションのスプラッシュ画面義務を
            // 再確認すること。
            // issue #100, docs/licenses.md §11: Unity Editor Software Terms Section 2.12 が要求する
            // 帰属定型文（Made with Unity）と、Unity's Trademark Guidelines の Trademark Notice and
            // Attribution Statement を、既存の Unity パッケージ群クレジットの先頭に追加する。
            // レビュー H-1: Section 2.12 原文の「Unity's Trademark Guidelines」リンクは
            // https://unity.com/legal/branding-trademarks （実際のガイドライン本文）であり、
            // https://unity.com/legal/trademarks は同ガイドライン内で個別に参照される
            // 「Unity Trademark List」（商標一覧）なので、別リンクとして両方を掲載する。
            new Entry(
                "unity-packages",
                Section.Oss,
                "Unity パッケージ群",
                BuildUnityAttributionCreditLine() +
                    "\n\nUnity と各パッケージ (C) Unity Technologies (Unity Companion License)",
                "Licenses/unity-packages-notices",
                new[]
                {
                    new SourceLink("Unity Editor Software Terms (2.12)", "https://unity.com/legal/editor-terms-of-service/software"),
                    new SourceLink("Unity's Trademark Guidelines", "https://unity.com/legal/branding-trademarks"),
                    new SourceLink("Unity Trademark List", "https://unity.com/legal/trademarks"),
                }),
            new Entry(
                "unity-companion-license",
                Section.Oss,
                "Unity Companion License 全文",
                "上記 Unity パッケージ群に適用されるライセンスの全文。",
                "Licenses/unity-companion-license",
                new[] { new SourceLink("Unity Companion License", "https://unity.com/legal/licenses/unity-companion-license") }),
            new Entry(
                "mono-nat",
                Section.Oss,
                "Mono.Nat",
                "Mono.Nat (C) 2006 Alan McGovern, 2007 Ben Motmans, 2013 Nicholas Terry (MIT License)",
                "Licenses/mono-nat-license",
                new[] { new SourceLink("Mono.Nat LICENSE.md", "https://github.com/alanmcgovern/Mono.Nat/blob/master/LICENSE.md") }),
            new Entry(
                "noto-sans-jp",
                Section.Oss,
                "Noto Sans JP",
                "Noto Sans JP — © 2014-2021 Adobe (SIL Open Font License 1.1)",
                "Licenses/noto-sans-jp-ofl",
                new[] { new SourceLink("Noto CJK リリース", "https://github.com/notofonts/noto-cjk") }),
        };

        /// <summary><paramref name="section"/> に属する項目を、<see cref="Entries"/> の並び順のまま返す。</summary>
        public static IEnumerable<Entry> EntriesForSection(Section section)
        {
            foreach (var entry in Entries)
            {
                if (entry.Section == section)
                {
                    yield return entry;
                }
            }
        }

        /// <summary>
        /// <paramref name="entry"/> のライセンス全文を Resources から読み込む。
        /// 見つからない場合は例外を投げる（同梱必須のリソースが欠けているのはアプリの構成不備のため、
        /// 握りつぶさずに fail-fast する。<see cref="TermsCatalog.LoadText"/> と同じ方針）。
        /// </summary>
        public static string LoadText(Entry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            var asset = Resources.Load<TextAsset>(entry.ResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"[CreditsLicenseCatalog] ライセンステキストが見つかりません: Resources/{entry.ResourcePath}.txt");
            }

            return asset.text;
        }
    }
}
