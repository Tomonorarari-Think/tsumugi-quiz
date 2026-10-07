using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// 設定画面のコントローラ（issue #28、docs/room-settings.md §1〜§3、docs/architecture.md §2）。
    /// 「ルーム設定」「プリセット」「アプリ設定」「音声合成」の 4 タブで構成する。
    /// タブごとの処理は同じ partial class の別ファイル（<c>SettingsView.RoomTab.cs</c> /
    /// <c>SettingsView.PresetsTab.cs</c> / <c>SettingsView.AppTab.cs</c> / <c>SettingsView.TtsTab.cs</c>）にある。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>#27（<c>RoomSettingsSync</c>）との結合点</b>: 「ルーム設定」タブの「適用」だけが結合点で、
    /// <c>SettingsView.RoomSync.cs</c> の <see cref="PushToRoomSettingsSync"/> に閉じている
    /// （ホストかつ未ロックのときだけ <c>RoomSettingsSync.TrySetSettings</c> を呼ぶ）。
    /// <c>host.role</c> の権威も <c>RoomSettingsSync</c> に一本化してあり、本 View から
    /// <c>HostRolePreference</c>（PlayerPrefs）や <c>LobbyState.Role</c> へ直接書き込むことはしない。
    /// 「アプリ設定」タブの保存は <c>NetworkBootstrap.ApplyAppSettings</c>（NAT・先読み数）へ渡す。
    /// </para>
    /// </remarks>
    public sealed partial class SettingsView : IView
    {
        private const string RoomTabName = "room";
        private const string PresetsTabName = "presets";
        private const string AppTabName = "app";

        private ViewRouter _router;
        private VisualElement _root;

        private Button _tabRoomButton;
        private Button _tabPresetsButton;
        private Button _tabAppButton;
        private Button _tabTtsButton;

        private VisualElement _roomSection;
        private VisualElement _presetsSection;
        private VisualElement _appSection;

        private Button _termsButton;
        private Button _backButton;

        private string _activeTab = RoomTabName;

        /// <inheritdoc />
        public void OnShow(ViewContext context)
        {
            _router = context.Router;
            _root = context.Root;
            var root = context.Root;

            var allFound = true;

            _tabRoomButton = SettingsFieldBinder.Require<Button>(root, "settings-tab-room-button", ref allFound);
            _tabPresetsButton = SettingsFieldBinder.Require<Button>(root, "settings-tab-presets-button", ref allFound);
            _tabAppButton = SettingsFieldBinder.Require<Button>(root, "settings-tab-app-button", ref allFound);
            _tabTtsButton = SettingsFieldBinder.Require<Button>(root, "settings-tab-tts-button", ref allFound);

            _roomSection = SettingsFieldBinder.Require<VisualElement>(root, "room-settings-section", ref allFound);
            _presetsSection = SettingsFieldBinder.Require<VisualElement>(root, "presets-section", ref allFound);
            _appSection = SettingsFieldBinder.Require<VisualElement>(root, "app-settings-section", ref allFound);

            _termsButton = SettingsFieldBinder.Require<Button>(root, "terms-button", ref allFound);
            _backButton = SettingsFieldBinder.Require<Button>(root, "back-button", ref allFound);

            if (!allFound)
            {
                // L6: 部分的に配線された状態を残さない。
                OnHide();
                return;
            }

            _tabRoomButton.clicked += ShowRoomTab;
            _tabPresetsButton.clicked += ShowPresetsTab;
            _tabAppButton.clicked += ShowAppTab;
            _tabTtsButton.clicked += OnTtsStatusButtonClicked;

            _termsButton.clicked += OnTermsClicked;
            _backButton.clicked += OnBackClicked;
            _backButton.SetEnabled(_router.CanGoBack);

            OnShowRoomTab(root);
            OnShowPresetsTab(root);
            OnShowAppTab(root);

            _activeTab = RoomTabName;
            ApplyActiveTab();
        }

        /// <inheritdoc />
        public void OnHide()
        {
            if (_tabRoomButton != null) _tabRoomButton.clicked -= ShowRoomTab;
            if (_tabPresetsButton != null) _tabPresetsButton.clicked -= ShowPresetsTab;
            if (_tabAppButton != null) _tabAppButton.clicked -= ShowAppTab;
            if (_tabTtsButton != null) _tabTtsButton.clicked -= OnTtsStatusButtonClicked;
            if (_termsButton != null) _termsButton.clicked -= OnTermsClicked;
            if (_backButton != null) _backButton.clicked -= OnBackClicked;

            OnHideRoomTab();
            OnHidePresetsTab();
            OnHideAppTab();
            OnHideTtsTab();

            _tabRoomButton = null;
            _tabPresetsButton = null;
            _tabAppButton = null;
            _tabTtsButton = null;
            _roomSection = null;
            _presetsSection = null;
            _appSection = null;
            _termsButton = null;
            _backButton = null;
            _router = null;
            _root = null;
        }

        private void ShowRoomTab() => SetActiveTab(RoomTabName);

        private void ShowPresetsTab() => SetActiveTab(PresetsTabName);

        private void ShowAppTab() => SetActiveTab(AppTabName);

        private void SetActiveTab(string tabName)
        {
            _activeTab = tabName;
            ApplyActiveTab();
        }

        private void ApplyActiveTab()
        {
            SetDisplay(_roomSection, _activeTab == RoomTabName);
            SetDisplay(_presetsSection, _activeTab == PresetsTabName);
            SetDisplay(_appSection, _activeTab == AppTabName);

            // L1: 現在のタブが見た目でも分かるようにする。
            SetSelected(_tabRoomButton, _activeTab == RoomTabName);
            SetSelected(_tabPresetsButton, _activeTab == PresetsTabName);
            SetSelected(_tabAppButton, _activeTab == AppTabName);
        }

        private static void SetSelected(VisualElement element, bool selected)
        {
            element?.EnableInClassList("settings-tab-button--selected", selected);
        }

        private static void SetDisplay(VisualElement element, bool visible)
        {
            if (element != null)
            {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void OnTermsClicked() => _router.ShowView(ViewNames.Terms);

        private void OnBackClicked()
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
