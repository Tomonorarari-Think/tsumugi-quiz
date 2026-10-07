using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views
{
    /// <summary>
    /// 利用規約同意画面のコントローラ(requirements.md FR-71〜FR-76)。
    /// <see cref="TermsCatalog"/> の全規約を本文表示し、本文の最下部までスクロールし、
    /// かつ「同意します」チェックが入るまで同意ボタンを無効化する（FR-72・M-3）。
    /// 同意すると <see cref="ConsentStore"/> に記録して Title へ進む（FR-73）。
    /// 既に同意済みの状態でこの View を再表示した場合（Settings/Credits からの「再表示」導線）は、
    /// 主ボタンが「戻る」に切り替わり、各規約の同意日時・ハッシュ先頭8桁・アプリバージョンを表示する。
    /// この場合は <see cref="ConsentStore.RecordConsent"/> を呼び直さない（H-3）。
    /// 「同意を撤回」ボタンで撤回できる（FR-75）。
    /// </summary>
    public sealed class TermsView : IView
    {
        private const float ScrollBottomTolerance = 1f;

        private ViewRouter _router;
        private ScrollView _scrollView;
        private Toggle _consentToggle;
        private Button _agreeButton;
        private Button _revokeButton;
        private Label _statusLabel;

        private ConsentStore _store;
        private IReadOnlyList<TermsDefinition> _requiredTerms;
        private bool _hasConsented;
        private bool _hasScrolledToBottom;

        private EventCallback<ChangeEvent<bool>> _toggleChangedHandler;
        private EventCallback<GeometryChangedEvent> _geometryChangedHandler;
        private Action<float> _scrollValueChangedHandler;
        private Action _primaryButtonClickedHandler;
        private Action _revokeClickedHandler;

        public void OnShow(ViewContext context)
        {
            _router = context.Router;
            var root = context.Root;

            var sectionsContainer = root.Q<VisualElement>("terms-sections");
            _scrollView = root.Q<ScrollView>("terms-scroll-view");
            _consentToggle = root.Q<Toggle>("consent-toggle");
            _agreeButton = root.Q<Button>("agree-button");
            _revokeButton = root.Q<Button>("revoke-button");
            _statusLabel = root.Q<Label>("terms-status-label");

            if (sectionsContainer == null || _scrollView == null || _consentToggle == null || _agreeButton == null)
            {
                Debug.LogError("[TermsView] 必要な UI 要素が見つかりません。terms-view.uxml を確認してください。");
                return;
            }

            _store = ConsentGate.CreateDefaultStore();
            _requiredTerms = TermsCatalog.LoadRequiredTerms();
            _hasConsented = _store.HasAcceptedAll(_requiredTerms);
            _hasScrolledToBottom = false;

            BuildTermsSections(sectionsContainer);

            _geometryChangedHandler = OnScrollViewGeometryChanged;
            _scrollView.RegisterCallback(_geometryChangedHandler);
            _scrollValueChangedHandler = OnScrollValueChanged;
            _scrollView.verticalScroller.valueChanged += _scrollValueChangedHandler;

            _toggleChangedHandler = OnToggleChanged;
            _consentToggle.RegisterValueChangedCallback(_toggleChangedHandler);

            _primaryButtonClickedHandler = OnPrimaryButtonClicked;
            _agreeButton.clicked += _primaryButtonClickedHandler;

            if (_revokeButton != null)
            {
                _revokeClickedHandler = OnRevokeClicked;
                _revokeButton.clicked += _revokeClickedHandler;
            }

            RefreshUiState();
        }

        public void OnHide()
        {
            if (_scrollView != null)
            {
                if (_geometryChangedHandler != null)
                {
                    _scrollView.UnregisterCallback(_geometryChangedHandler);
                }

                if (_scrollValueChangedHandler != null)
                {
                    _scrollView.verticalScroller.valueChanged -= _scrollValueChangedHandler;
                }
            }

            if (_consentToggle != null && _toggleChangedHandler != null)
            {
                _consentToggle.UnregisterValueChangedCallback(_toggleChangedHandler);
            }

            if (_agreeButton != null && _primaryButtonClickedHandler != null)
            {
                _agreeButton.clicked -= _primaryButtonClickedHandler;
            }

            if (_revokeButton != null && _revokeClickedHandler != null)
            {
                _revokeButton.clicked -= _revokeClickedHandler;
            }

            _router = null;
            _scrollView = null;
            _consentToggle = null;
            _agreeButton = null;
            _revokeButton = null;
            _statusLabel = null;
            _store = null;
            _requiredTerms = null;
            _toggleChangedHandler = null;
            _geometryChangedHandler = null;
            _scrollValueChangedHandler = null;
            _primaryButtonClickedHandler = null;
            _revokeClickedHandler = null;
        }

        private void BuildTermsSections(VisualElement container)
        {
            container.Clear();

            foreach (var entry in TermsCatalog.Entries)
            {
                var section = new VisualElement();
                section.AddToClassList("terms-section");

                var heading = new Label(entry.DisplayName);
                heading.AddToClassList("terms-section-heading");
                section.Add(heading);

                string text;
                try
                {
                    text = TermsCatalog.LoadText(entry);
                }
                catch (InvalidOperationException ex)
                {
                    Debug.LogError($"[TermsView] {ex.Message}");
                    text = "（規約テキストの読み込みに失敗しました）";
                }

                var bodyLabel = new Label(text);
                bodyLabel.AddToClassList("body-text");
                bodyLabel.AddToClassList("terms-section-body");
                bodyLabel.style.whiteSpace = WhiteSpace.Normal;
                section.Add(bodyLabel);

                // M-3: 本文はここではスクロールさせない（入れ子 ScrollView を廃止）。
                // 外側の terms-scroll-view 1本だけをスクロール対象にする。

                foreach (var link in entry.SourceLinks)
                {
                    var url = link.Url;
                    var linkButton = new Button(() => Application.OpenURL(url)) { text = link.Label };
                    linkButton.AddToClassList("terms-link-button");
                    section.Add(linkButton);
                }

                container.Add(section);
            }
        }

        private void OnScrollViewGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateScrollGateState();
        }

        private void OnScrollValueChanged(float value)
        {
            UpdateScrollGateState();
        }

        /// <summary>
        /// 本文の最下部までスクロールしたかどうかを判定する（M-3）。スクロールバーの可動域
        /// （<see cref="Scroller.highValue"/>）が 0 以下＝そもそもスクロールが不要な短い内容の場合は、
        /// 即座に「最下部まで読んだ」扱いにする。
        /// </summary>
        private void UpdateScrollGateState()
        {
            if (_hasConsented || _hasScrolledToBottom || _scrollView == null)
            {
                return;
            }

            var scroller = _scrollView.verticalScroller;
            var reachedBottom = scroller.highValue <= 0f || scroller.value >= scroller.highValue - ScrollBottomTolerance;
            if (!reachedBottom)
            {
                return;
            }

            _hasScrolledToBottom = true;
            RefreshUiState();
        }

        private void OnToggleChanged(ChangeEvent<bool> evt)
        {
            if (_hasConsented)
            {
                return;
            }

            _agreeButton.SetEnabled(_hasScrolledToBottom && evt.newValue);
        }

        private void OnPrimaryButtonClicked()
        {
            if (_hasConsented)
            {
                NavigateBack();
                return;
            }

            // FR-72・M-3: チェック前・最下部未到達では無効化しているが、念のため二重にガードする。
            if (!_hasScrolledToBottom || _consentToggle == null || !_consentToggle.value)
            {
                return;
            }

            try
            {
                _store.RecordConsent(_requiredTerms, Application.version, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                // H-4: 保存失敗はユーザーに分かる形で伝え、詳細はログに残す。ここで例外を投げっぱなしに
                // すると、同意が実際には記録されていないのに Title へ進めてしまう恐れがある。
                Debug.LogError($"[TermsView] 同意記録の保存に失敗しました: {ex}");
                ShowStatusMessage("同意の保存に失敗しました。時間をおいて再度お試しください。");
                return;
            }

            _router.ShowView(ViewNames.Title);
        }

        private void OnRevokeClicked()
        {
            try
            {
                _store.Revoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TermsView] 同意の撤回に失敗しました: {ex}");
                ShowStatusMessage("同意の撤回に失敗しました。時間をおいて再度お試しください。");
                return;
            }

            _hasConsented = false;
            _hasScrolledToBottom = false;
            RefreshUiState();
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

        private void RefreshUiState()
        {
            if (_hasConsented)
            {
                _consentToggle.SetValueWithoutNotify(true);
                _consentToggle.SetEnabled(false);

                _agreeButton.text = "戻る";
                _agreeButton.SetEnabled(true);

                if (_revokeButton != null)
                {
                    _revokeButton.style.display = DisplayStyle.Flex;
                }

                ShowStatusMessage(BuildConsentStatusText());
                return;
            }

            _consentToggle.SetEnabled(_hasScrolledToBottom);
            if (!_hasScrolledToBottom)
            {
                _consentToggle.SetValueWithoutNotify(false);
            }

            _agreeButton.text = "同意して進む";
            _agreeButton.SetEnabled(_hasScrolledToBottom && _consentToggle.value);

            if (_revokeButton != null)
            {
                _revokeButton.style.display = DisplayStyle.None;
            }

            ShowStatusMessage(_hasScrolledToBottom
                ? string.Empty
                : "本文の最後までスクロールすると同意のチェックが有効になります。");
        }

        private void ShowStatusMessage(string message)
        {
            if (_statusLabel == null)
            {
                return;
            }

            _statusLabel.text = message;
            _statusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// 同意済み状態のステータス表示（H-3）。各規約ごとに、同意日時（ローカル時刻）・
        /// ハッシュ先頭8桁・アプリバージョンを1行ずつ表示する。
        /// </summary>
        private string BuildConsentStatusText()
        {
            var records = _store.LoadRecords();
            var lines = new List<string> { "既に同意済みです。撤回すると次回起動時に再度同意が必要になります。" };

            foreach (var entry in TermsCatalog.Entries)
            {
                ConsentRecord matched = null;
                foreach (var record in records)
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

            return string.Join("\n", lines);
        }
    }
}
