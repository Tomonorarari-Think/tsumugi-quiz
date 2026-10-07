using System;
using System.Collections.Generic;
using System.Linq;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tts;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// <see cref="SettingsView"/> のうち「アプリ設定」タブ（docs/room-settings.md §0/§2）。
    /// <c>player.name</c> / <c>upnp.*</c> / <c>network.ipLookupUrls</c> / <c>tts.*</c> /
    /// <c>character.enabled</c> に加え、詳細設定として <c>network.port</c> /
    /// <c>question.prefetchCount</c> も扱う（<see cref="SettingsView"/> 独自の判断。issue 本文の例示には
    /// 挙げられていないが、AppSettings の全項目を編集可能にするため対象に含めた）。
    /// <c>network.tickRate</c> は設定項目から外した（統括判断、PR #92 Phase 2。docs/room-settings.md §2/§7）。
    /// </summary>
    public sealed partial class SettingsView
    {
        /// <summary>
        /// 使う <see cref="AppSettingsStore"/> の生成方法。既定は実ファイル（<c>app-settings.json</c>）を使う実装。
        /// PlayMode テストではこのプロパティにテスト用パスを積んだファクトリを差し替えられる。
        /// </summary>
        internal Func<AppSettingsStore> AppSettingsStoreFactory { get; set; } = () => new AppSettingsStore();

        private AppSettingsStore _appSettingsStore;
        private AppSettings _currentAppSettings;

        private TextField _appPlayerNameField;

        private Toggle _appUpnpEnabledField;
        private IntegerField _appUpnpDiscoveryTimeoutField;
        private IntegerField _appUpnpMappingLifetimeField;
        private IntegerField _appUpnpRenewIntervalField;
        private TextField _appIpLookupUrlsField;

        private TextField _appTtsSpeakerNameField;
        private TextField _appTtsStyleNameField;
        private TextField _appTtsCacheMaxBytesField;
        private IntegerField _appTtsCacheMaxEntriesField;
        private TextField _appTtsAssetPathOverrideField;

        private Toggle _appCharacterEnabledField;

        private IntegerField _appNetworkPortField;
        private IntegerField _appQuestionPrefetchCountField;

        private VisualElement _appWarningsContainer;
        private Label _appStatusLabel;
        private Button _appSaveButton;

        private void OnShowAppTab(VisualElement root)
        {
            var allFound = true;

            _appPlayerNameField = SettingsFieldBinder.Require<TextField>(root, "app-player-name-field", ref allFound);

            _appUpnpEnabledField = SettingsFieldBinder.Require<Toggle>(root, "app-upnp-enabled-field", ref allFound);
            _appUpnpDiscoveryTimeoutField = SettingsFieldBinder.Require<IntegerField>(root, "app-upnp-discovery-timeout-field", ref allFound);
            _appUpnpMappingLifetimeField = SettingsFieldBinder.Require<IntegerField>(root, "app-upnp-mapping-lifetime-field", ref allFound);
            _appUpnpRenewIntervalField = SettingsFieldBinder.Require<IntegerField>(root, "app-upnp-renew-interval-field", ref allFound);
            _appIpLookupUrlsField = SettingsFieldBinder.Require<TextField>(root, "app-ip-lookup-urls-field", ref allFound);

            _appTtsSpeakerNameField = SettingsFieldBinder.Require<TextField>(root, "app-tts-speaker-name-field", ref allFound);
            _appTtsStyleNameField = SettingsFieldBinder.Require<TextField>(root, "app-tts-style-name-field", ref allFound);
            _appTtsCacheMaxBytesField = SettingsFieldBinder.Require<TextField>(root, "app-tts-cache-max-bytes-field", ref allFound);
            _appTtsCacheMaxEntriesField = SettingsFieldBinder.Require<IntegerField>(root, "app-tts-cache-max-entries-field", ref allFound);
            _appTtsAssetPathOverrideField = SettingsFieldBinder.Require<TextField>(root, "app-tts-asset-path-override-field", ref allFound);

            _appCharacterEnabledField = SettingsFieldBinder.Require<Toggle>(root, "app-character-enabled-field", ref allFound);

            _appNetworkPortField = SettingsFieldBinder.Require<IntegerField>(root, "app-network-port-field", ref allFound);
            _appQuestionPrefetchCountField = SettingsFieldBinder.Require<IntegerField>(root, "app-question-prefetch-count-field", ref allFound);

            _appWarningsContainer = SettingsFieldBinder.Require<VisualElement>(root, "app-settings-warnings", ref allFound);
            _appStatusLabel = SettingsFieldBinder.Require<Label>(root, "app-settings-status-label", ref allFound);
            _appSaveButton = SettingsFieldBinder.Require<Button>(root, "app-settings-save-button", ref allFound);

            if (!allFound)
            {
                // L6: 部分的に配線された状態を残さない。
                OnHideAppTab();
                return;
            }

            AppSettingsLoadResult loadResult;
            try
            {
                _appSettingsStore = AppSettingsStoreFactory();
                loadResult = _appSettingsStore.Load();
            }
            catch (InvalidOperationException ex)
            {
                // M4: AppSettingsStore の既定コンストラクタは AppPaths 未設定だと例外を投げる。
                Debug.LogWarning($"[SettingsView] app-settings.json を読み込めませんでした。既定値を使用します: {ex.Message}");
                _appSettingsStore = null;
                loadResult = new AppSettingsLoadResult(AppSettings.Default, Array.Empty<string>());
            }

            _currentAppSettings = loadResult.Settings;
            PopulateAppFields(_currentAppSettings);
            ShowAppWarnings(loadResult.Warnings);
            ShowAppStatus(string.Empty);

            _appSaveButton.clicked += OnAppSaveClicked;
        }

        private void OnHideAppTab()
        {
            if (_appSaveButton != null)
            {
                _appSaveButton.clicked -= OnAppSaveClicked;
            }

            _appSettingsStore = null;
            _appPlayerNameField = null;
            _appUpnpEnabledField = null;
            _appUpnpDiscoveryTimeoutField = null;
            _appUpnpMappingLifetimeField = null;
            _appUpnpRenewIntervalField = null;
            _appIpLookupUrlsField = null;
            _appTtsSpeakerNameField = null;
            _appTtsStyleNameField = null;
            _appTtsCacheMaxBytesField = null;
            _appTtsCacheMaxEntriesField = null;
            _appTtsAssetPathOverrideField = null;
            _appCharacterEnabledField = null;
            _appNetworkPortField = null;
            _appQuestionPrefetchCountField = null;
            _appWarningsContainer = null;
            _appStatusLabel = null;
            _appSaveButton = null;
        }

        private void PopulateAppFields(AppSettings settings)
        {
            _appPlayerNameField.value = settings.PlayerName;

            _appUpnpEnabledField.value = settings.UpnpEnabled;
            _appUpnpDiscoveryTimeoutField.value = settings.UpnpDiscoveryTimeoutMs;
            _appUpnpMappingLifetimeField.value = settings.UpnpMappingLifetimeSec;
            _appUpnpRenewIntervalField.value = settings.UpnpRenewIntervalMs;
            _appIpLookupUrlsField.value = string.Join("\n", settings.IpLookupUrls);

            _appTtsSpeakerNameField.value = settings.TtsSpeakerName;
            _appTtsStyleNameField.value = settings.TtsStyleName;
            _appTtsCacheMaxBytesField.value = settings.TtsCacheMaxBytes.ToString();
            _appTtsCacheMaxEntriesField.value = settings.TtsCacheMaxEntries;
            _appTtsAssetPathOverrideField.value = settings.TtsAssetPathOverride;

            _appCharacterEnabledField.value = settings.CharacterEnabled;

            _appNetworkPortField.value = settings.NetworkPort;
            _appQuestionPrefetchCountField.value = settings.QuestionPrefetchCount;
        }

        /// <summary>
        /// <see cref="CollectAppInput"/> の直前呼び出しで <c>tts.cacheMaxBytes</c> の数値解析に失敗したか
        /// （H3。<c>TextField</c> は自由入力のため、数値以外を入力できてしまう）。
        /// </summary>
        private bool _appTtsCacheMaxBytesParseFailed;

        private AppSettingsInput CollectAppInput()
        {
            _appTtsCacheMaxBytesParseFailed = false;
            var cacheMaxBytesText = _appTtsCacheMaxBytesField.value;
            long? cacheMaxBytes = null;
            if (!string.IsNullOrWhiteSpace(cacheMaxBytesText))
            {
                if (long.TryParse(cacheMaxBytesText, out var parsed))
                {
                    cacheMaxBytes = parsed;
                }
                else
                {
                    _appTtsCacheMaxBytesParseFailed = true;
                }
            }

            return new AppSettingsInput
            {
                PlayerName = _appPlayerNameField.value,

                UpnpEnabled = _appUpnpEnabledField.value,
                UpnpDiscoveryTimeoutMs = _appUpnpDiscoveryTimeoutField.value,
                UpnpMappingLifetimeSec = _appUpnpMappingLifetimeField.value,
                UpnpRenewIntervalMs = _appUpnpRenewIntervalField.value,
                NetworkIpLookupUrls = SplitLines(_appIpLookupUrlsField.value),

                TtsSpeakerName = _appTtsSpeakerNameField.value,
                TtsStyleName = _appTtsStyleNameField.value,
                TtsCacheMaxBytes = cacheMaxBytes,
                TtsCacheMaxEntries = _appTtsCacheMaxEntriesField.value,
                TtsAssetPathOverride = _appTtsAssetPathOverrideField.value,

                CharacterEnabled = _appCharacterEnabledField.value,

                NetworkPort = _appNetworkPortField.value,
                QuestionPrefetchCount = _appQuestionPrefetchCountField.value,
            };
        }

        /// <summary>
        /// アプリ設定タブが編集しないキーを、保存済みのファイルから引き継ぐ（PR #92 再レビュー H-1）。
        /// 現状の対象は <c>room.lastApplied</c>（<see cref="RoomSettingsDraft"/> が書く「編集中のルーム設定」）と
        /// <c>host.role</c>（issue #155。HostSetup View / <c>HostRolePreference</c> が書く「次にホストを
        /// 開始するときの初期値」）。新しいキーを <see cref="AppSettings"/> に足して UI を作らない場合は、
        /// ここにも足すこと（<see cref="TsumugiQuiz.Tests.EditMode.Room.AppSettingsPropertyCanaryTests"/> が
        /// プロパティ追加を検知する）。
        /// </summary>
        /// <param name="store">読み直しに使うストア（保存に使うものと同じインスタンス）。</param>
        /// <param name="edited">この画面で編集した内容。</param>
        /// <returns>引き継ぎ後の設定。</returns>
        internal static AppSettings PreserveKeysNotEditedHere(AppSettingsStore store, AppSettings edited)
        {
            var saved = store.Load().Settings;

            var result = edited;
            if (saved.RoomLastApplied != result.RoomLastApplied)
            {
                result = result.WithRoomLastApplied(saved.RoomLastApplied);
            }

            if (saved.HostRole != result.HostRole)
            {
                result = result.WithHostRole(saved.HostRole);
            }

            return result;
        }

        private void OnAppSaveClicked()
        {
            var input = CollectAppInput();
            var validation = AppSettingsValidator.Validate(input);

            var warnings = new List<string>(validation.Warnings);
            if (_appTtsCacheMaxBytesParseFailed)
            {
                // H3: TextField は自由入力のため、数値として解釈できない値を入力できてしまう。
                // AppSettingsValidator からは検出できない（未指定として扱われるだけ）ため、ここで警告する。
                warnings.Add(
                    $"tts.cacheMaxBytes の値 \"{_appTtsCacheMaxBytesField.value}\" は数値として解釈できません。既定値を使用しました。");
            }

            var settingsToSave = validation.Settings;

            AppSettingsSaveResult saveResult;
            try
            {
                var store = _appSettingsStore ?? AppSettingsStoreFactory();

                // H-1: この画面が UI に持っていないキー（現状は room.lastApplied）は、保存で失わせない。
                // AppSettingsStore.Save はファイル全体を書き直すため、保存直前に読み直して引き継ぐ
                // （「UI が持たないキーは保存で失わない」不変条件）。
                settingsToSave = PreserveKeysNotEditedHere(store, settingsToSave);

                saveResult = store.Save(settingsToSave);
            }
            catch (InvalidOperationException ex)
            {
                // M4: AppSettingsStore の既定コンストラクタは AppPaths 未設定だと例外を投げる。
                saveResult = AppSettingsSaveResult.Failed($"app-settings.json の保存先を解決できませんでした: {ex.Message}");
            }

            _currentAppSettings = settingsToSave;
            PopulateAppFields(_currentAppSettings);

            warnings.AddRange(saveResult.Warnings);
            ShowAppWarnings(warnings);

            var ttsReloadOutcome = TtsReloadOutcome.NotNeeded;

            if (saveResult.Success)
            {
                // M-4: character.enabled は GameView がプロセス単位でキャッシュしているので、
                // 保存したら次に Game 画面を開くときに読み直させる。
                Game.GameView.InvalidateCharacterEnabledCache();

                // ネットワーク層への反映（issue #28 H5/M1/Phase 2）: NAT（UPnP・グローバル IP）のファクトリ
                // 差し替えと question.prefetchCount の反映をまとめて NetworkBootstrap に委ねる。
                // NAT は実行中のホストには影響せず次回のホスト開始から、先読み数はスポーン済みなら即時有効。
                if (NetworkBootstrap.Instance != null)
                {
                    NetworkBootstrap.Instance.ApplyAppSettings(_currentAppSettings);
                }

                // #138: tts.* は TtsService の初期化時に一度だけ読まれ、話者・スタイル・配置は
                // 合成エンジンの生成時に固定される。初期化済みなら再初期化して次の読み上げから効かせる
                // （未初期化・未同意・変更なしのときは何もしない。docs/tts.md §6.5）。
                // #138 レビュー M-3: ここを最後に置くのは、再初期化がいちばん重い後処理であり、
                // 軽いキャッシュ無効化・ネットワーク層への反映を巻き添えで遅らせないため。
                ttsReloadOutcome = TtsAppSettingsReloader.ReloadIfNeeded(TtsService.Instance, _currentAppSettings);
            }

            ShowAppStatus(saveResult.Success
                ? DescribeAppSaveSucceeded(ttsReloadOutcome)
                : "アプリ設定の保存に失敗しました。");
        }

        /// <summary>
        /// 保存成功時のメッセージ。読み上げの再初期化まで走ったのか、初期化完了まで持ち越したのかを
        /// 伝える（#138 レビュー M-2。話者を変えたのに何も起きていないように見えるのを防ぐ）。
        /// </summary>
        private static string DescribeAppSaveSucceeded(TtsReloadOutcome ttsReloadOutcome)
        {
            switch (ttsReloadOutcome)
            {
                case TtsReloadOutcome.Started:
                    return "アプリ設定を保存しました。読み上げを初期化し直しています…";
                case TtsReloadOutcome.Deferred:
                    return "アプリ設定を保存しました。読み上げは初期化完了後に反映します。";
                default:
                    return "アプリ設定を保存しました。";
            }
        }

        private void ShowAppWarnings(IReadOnlyList<string> warnings)
        {
            _appWarningsContainer.Clear();
            if (warnings == null)
            {
                return;
            }

            foreach (var warning in warnings)
            {
                var label = new Label(warning);
                label.AddToClassList("body-text");
                label.AddToClassList("host-setup-warning");
                _appWarningsContainer.Add(label);
            }
        }

        private void ShowAppStatus(string message)
        {
            _appStatusLabel.text = message ?? string.Empty;
            _appStatusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static List<string> SplitLines(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
    }
}
