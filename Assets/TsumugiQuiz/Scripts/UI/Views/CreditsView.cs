using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.UI.Credits;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views
{
    /// <summary>
    /// クレジット/ライセンス画面のコントローラ(issue #33)。docs/licenses.md §12 が定める順序
    /// (1.キャラクター 2.音声合成 3.OSS)で、<see cref="CreditsLicenseCatalog"/> に登録された
    /// 確定クレジット文言と同梱ライセンス全文(Resources/Licenses/*.txt)を折りたたみ表示する。
    /// 続けて利用規約の同意状況(<see cref="ConsentStore"/>、#37)とアプリバージョンを表示する。
    /// 同意の再表示・撤回は architecture.md の設計どおり <see cref="TermsView"/> の責務のままとし、
    /// このView自体は同意記録を書き換えない(レビュー H-1)。
    /// </summary>
    public sealed class CreditsView : IView
    {
        /// <summary>ライセンス全文の読み込みに失敗した場合に Foldout 本文へ表示するメッセージ。</summary>
        internal const string LicenseLoadFailureMessage = "（ライセンステキストの読み込みに失敗しました）";

        /// <summary>同意状況の判定・読み込みに失敗した場合に表示するメッセージ。</summary>
        private const string ConsentStatusLoadFailureMessage =
            "同意状況の確認に失敗しました。「利用規約の確認・撤回」から確認してください。";

        private ViewRouter _router;

        private Button _backButton;
        private Action _backHandler;

        private Button _termsButton;
        private Action _termsHandler;

        private Label _consentStatusLabel;

        private ConsentStore _store;

        public void OnShow(ViewContext context)
        {
            _router = context.Router;
            var root = context.Root;

            var characterSection = root.Q<VisualElement>("credits-character-section");
            var voiceSection = root.Q<VisualElement>("credits-voice-section");
            var ossSection = root.Q<VisualElement>("credits-oss-section");
            _consentStatusLabel = root.Q<Label>("credits-consent-status");
            var appVersionLabel = root.Q<Label>("credits-app-version");

            _backButton = root.Q<Button>("back-button");
            _termsButton = root.Q<Button>("terms-button");

            if (characterSection == null || voiceSection == null || ossSection == null
                || _consentStatusLabel == null || appVersionLabel == null
                || _backButton == null || _termsButton == null)
            {
                Debug.LogError("[CreditsView] 必要な UI 要素が見つかりません。credits-view.uxml を確認してください。");
                return;
            }

            BuildSection(characterSection, CreditsLicenseCatalog.Section.Character);
            BuildSection(voiceSection, CreditsLicenseCatalog.Section.Voice);
            BuildSection(ossSection, CreditsLicenseCatalog.Section.Oss);

            _store = ConsentGate.CreateDefaultStore();
            RefreshConsentStatus();

            // ビルド番号は、ビルドの違う相手を拒否したとき（#204）の「バージョンが異なります（ホスト: x / あなた: y）」の番号と同じ。
            appVersionLabel.text = $"バージョン: {Application.version}（ビルド番号 {LocalBuildIdentity.DisplayNumber}）";

            _backHandler = NavigateBack;
            _backButton.SetEnabled(_router.CanGoBack);
            _backButton.clicked += _backHandler;

            _termsHandler = () => _router.ShowView(ViewNames.Terms);
            _termsButton.clicked += _termsHandler;
        }

        public void OnHide()
        {
            if (_backButton != null && _backHandler != null)
            {
                _backButton.clicked -= _backHandler;
            }

            if (_termsButton != null && _termsHandler != null)
            {
                _termsButton.clicked -= _termsHandler;
            }

            _router = null;
            _backButton = null;
            _backHandler = null;
            _termsButton = null;
            _termsHandler = null;
            _consentStatusLabel = null;
            _store = null;
        }

        /// <summary>
        /// 指定セクションに属する <see cref="CreditsLicenseCatalog.Entry"/> をすべて、
        /// 見出し・クレジット文言・出典リンク・ライセンス全文（Foldout で折りたたみ）として追加する。
        /// </summary>
        private static void BuildSection(VisualElement container, CreditsLicenseCatalog.Section section)
        {
            container.Clear();

            foreach (var entry in CreditsLicenseCatalog.EntriesForSection(section))
            {
                var item = new VisualElement();
                item.AddToClassList("credits-item");

                var heading = new Label(entry.DisplayName);
                heading.AddToClassList("credits-item-heading");
                item.Add(heading);

                var creditLabel = new Label(entry.CreditLine) { name = $"credits-credit-line-{entry.Id}" };
                creditLabel.AddToClassList("body-text");
                creditLabel.AddToClassList("credits-credit-line");
                creditLabel.style.whiteSpace = WhiteSpace.Normal;
                item.Add(creditLabel);

                foreach (var link in entry.SourceLinks)
                {
                    var url = link.Url;
                    var linkButton = new Button(() => Application.OpenURL(url)) { text = link.Label };
                    linkButton.AddToClassList("terms-link-button");
                    item.Add(linkButton);
                }

                item.Add(BuildLicenseFoldout(entry));

                container.Add(item);
            }
        }

        /// <summary>
        /// ライセンス全文（長文）を折りたたみ表示する <see cref="Foldout"/> を生成する。
        /// 受け入れ条件（スクロール可能なテキスト表示）を満たすため、内側に
        /// <c>max-height</c> 付きの <see cref="ScrollView"/> を持たせる。
        /// 本文（<see cref="ResolveDisplayText"/>）は初回展開時まで生成しない（H-3。
        /// 一覧に並ぶ全項目分を毎回読み込まないことで、特に ONNX Runtime の
        /// third-party-notices のような大きなファイルの無駄な読み込みを避ける）。
        /// </summary>
        private static Foldout BuildLicenseFoldout(CreditsLicenseCatalog.Entry entry)
        {
            var foldout = new Foldout { text = "ライセンス全文を表示", value = false };
            foldout.name = $"credits-license-foldout-{entry.Id}";
            foldout.AddToClassList("credits-license-foldout");

            var innerScroll = new ScrollView(ScrollViewMode.Vertical);
            innerScroll.AddToClassList("credits-license-scroll");
            foldout.Add(innerScroll);

            var built = false;
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue || built)
                {
                    return;
                }

                built = true;

                var bodyLabel = new Label(ResolveDisplayText(entry)) { name = $"credits-license-body-{entry.Id}" };
                bodyLabel.AddToClassList("small-text");
                bodyLabel.AddToClassList("terms-section-body");
                bodyLabel.AddToClassList("credits-license-body");
                bodyLabel.style.whiteSpace = WhiteSpace.Normal;
                innerScroll.Add(bodyLabel);
            });

            return foldout;
        }

        /// <summary>
        /// Foldout の本文に表示するテキストを決定する。
        /// <see cref="CreditsLicenseCatalog.Entry.ScreenSummary"/> が指定されていればそれを使い（H-3）、
        /// なければ <see cref="CreditsLicenseCatalog.LoadText"/> でライセンス全文を読み込む。
        /// 読み込みに失敗した場合は例外を握りつぶさずログに残し、<see cref="LicenseLoadFailureMessage"/>
        /// をユーザー向けに表示する（画面自体は壊さない）。
        /// </summary>
        internal static string ResolveDisplayText(CreditsLicenseCatalog.Entry entry)
        {
            if (entry.ScreenSummary != null)
            {
                return entry.ScreenSummary;
            }

            try
            {
                return CreditsLicenseCatalog.LoadText(entry);
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogError($"[CreditsView] {ex.Message}");
                return LicenseLoadFailureMessage;
            }
        }

        /// <summary>
        /// 同意状況（<see cref="ConsentStore.LoadRecords"/>）を表示する。文言は
        /// <see cref="TermsView"/> の同意済み表示（<c>BuildConsentStatusText</c>）と揃えている（H-1）。
        /// 撤回そのものはこの View では行わず、「利用規約の確認・撤回」ボタンで <see cref="TermsView"/>
        /// に遷移してから行う（architecture.md の設計どおり）。
        /// </summary>
        private void RefreshConsentStatus()
        {
            IReadOnlyList<TermsDefinition> requiredTerms;
            bool hasAcceptedAll;
            try
            {
                requiredTerms = TermsCatalog.LoadRequiredTerms();
                hasAcceptedAll = _store.HasAcceptedAll(requiredTerms);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CreditsView] 同意状況の判定に失敗しました: {ex}");
                _consentStatusLabel.text = ConsentStatusLoadFailureMessage;
                return;
            }

            if (!hasAcceptedAll)
            {
                IReadOnlyList<ConsentRecord> records;
                try
                {
                    records = _store.LoadRecords();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CreditsView] 同意記録の読み込みに失敗しました: {ex}");
                    _consentStatusLabel.text = ConsentStatusLoadFailureMessage;
                    return;
                }

                _consentStatusLabel.text = records.Count == 0
                    ? "同意記録がありません。「利用規約の確認・撤回」から同意手続きを行ってください。"
                    : "一部の規約が更新されているため、再同意が必要です。「利用規約の確認・撤回」から確認してください。";
                return;
            }

            // TermsView.BuildConsentStatusText と同じ書式（同意日時・ハッシュ先頭8桁・アプリバージョン）で揃える。
            var lines = new List<string> { "既に同意済みです。撤回すると次回起動時に再度同意が必要になります。" };
            var recordsForDisplay = _store.LoadRecords();
            foreach (var entry in TermsCatalog.Entries)
            {
                ConsentRecord matched = null;
                foreach (var record in recordsForDisplay)
                {
                    if (record.TermsId == entry.TermsId)
                    {
                        matched = record;
                        break;
                    }
                }

                if (matched == null)
                {
                    continue;
                }

                var localAcceptedAt = matched.AcceptedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                var hashPrefix = matched.Sha256Hash.Length >= 8 ? matched.Sha256Hash.Substring(0, 8) : matched.Sha256Hash;
                lines.Add($"・{entry.DisplayName}: {localAcceptedAt} に同意（ハッシュ {hashPrefix}…、v{matched.AppVersion}）");
            }

            _consentStatusLabel.text = string.Join("\n", lines);
        }

        private void NavigateBack()
        {
            if (_router.CanGoBack)
            {
                _router.GoBack();
            }
            else
            {
                _router.ShowView(ViewNames.Title);
            }
        }
    }
}
