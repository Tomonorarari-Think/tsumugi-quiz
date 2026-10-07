using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.HostSetup;
using TsumugiQuiz.UI.Views.Settings;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI.Settings
{
    /// <summary>
    /// Settings View（issue #28）の最小限の PlayMode テスト。
    /// 「画面が開く」「プリセット選択で値が変わる」「戻る」の 3 点のみを検証する
    /// （実データ保存の検証は #26 の <c>RoomPresetStoreTests</c> / <c>AppSettingsStoreTests</c>（EditMode）で
    /// 済んでいるため、ここでは <see cref="RoomPresetStore"/> / <see cref="AppSettingsStore"/> をテスト用の
    /// 一時フォルダへ差し替え、実際の <c>Documents</c> フォルダ・<c>app-settings.json</c> には一切触れない）。
    /// </summary>
    public class SettingsSceneTests
    {
        private MainSceneTestHelpers.ConsentFileScope _consentScope;
        private string _tempRoot;

        /// <summary>L-3: 「クライアントの sync」を模すための未スポーンの <see cref="RoomSettingsSync"/>。</summary>
        private GameObject _fakeSyncObject;

        [SetUp]
        public void SeedConsentedStateAndTempPaths()
        {
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            var requiredTerms = TermsCatalog.LoadRequiredTerms();
            store.RecordConsent(requiredTerms, Application.version, DateTime.UtcNow);

            _tempRoot = Path.Combine(Path.GetTempPath(), "tsumugi-settings-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            // RoomSettingsDraft / PlayerNamePreferences / HostSetupPreferences は静的な既定ファクトリで
            // 実際の app-settings.json を参照するため、テスト用の一時パスへ差し替える
            // （SettingsView 自身の RoomPresetStoreFactory / AppSettingsStoreFactory とは別物）。
            var appSettingsFile = Path.Combine(_tempRoot, "app-settings.json");
            RoomSettingsDraft.AppSettingsStoreFactory = () => new AppSettingsStore(appSettingsFile);
            PlayerNamePreferences.AppSettingsStoreFactory = () => new AppSettingsStore(appSettingsFile);
            HostSetupPreferences.AppSettingsStoreFactory = () => new AppSettingsStore(appSettingsFile);
            RoomSettingsDraft.ResetCacheForTesting();

            // #28 Phase 2: RoomSettingsSync（#27）はホストを開始しないと存在しない。
            // テストでは実ネットワークに触れないよう「同期先なし」を明示する。
            SettingsView.RoomSettingsSyncLocator = () => null;
        }

        [TearDown]
        public void RestoreConsentFileAndTempPaths()
        {
            _consentScope?.Restore();

            RoomSettingsDraft.AppSettingsStoreFactory = () => new AppSettingsStore();
            PlayerNamePreferences.AppSettingsStoreFactory = () => new AppSettingsStore();
            HostSetupPreferences.AppSettingsStoreFactory = () => new AppSettingsStore();
            RoomSettingsDraft.ResetCacheForTesting();
            SettingsView.RoomSettingsSyncLocator = null;

            if (_fakeSyncObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_fakeSyncObject);
                _fakeSyncObject = null;
            }

            if (_tempRoot != null && Directory.Exists(_tempRoot))
            {
                try
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();
        }

        /// <summary>Settings View 用に、実ファイルへ触れないテスト用ストアを登録する。</summary>
        private void RegisterTestSettingsView(ViewRouter router)
        {
            var presetsFolder = Path.Combine(_tempRoot, "Presets");
            var appSettingsFile = Path.Combine(_tempRoot, "app-settings.json");

            router.RegisterController(ViewNames.Settings, () => new SettingsView
            {
                RoomPresetStoreFactory = () => new RoomPresetStore(presetsFolder),
                AppSettingsStoreFactory = () => new AppSettingsStore(appSettingsFile),
            });
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TitleView_SettingsButton_ShowsSettingsView()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            Assert.IsNotNull(router, "Main シーンに ViewRouter が見つかりません。");
            RegisterTestSettingsView(router);

            Button settingsButton = null;
            yield return WaitForElement<Button>(panelRoot, "settings-button", found => settingsButton = found);
            yield return SimulateClickRoutine(settingsButton);

            Label heading = null;
            yield return WaitForElement<Label>(panelRoot, "settings-heading", found => heading = found);
            Assert.IsNotNull(heading, "settings-button クリック後に Settings View（settings-heading）へ遷移していません。");

            Assert.IsNotNull(panelRoot.Q<Button>("room-settings-apply-button"), "ルーム設定タブが初期表示されていません。");
            // #221: 効果のない「回答入力を1回のみに制限する」は設定画面に出さない。
            Assert.IsNull(panelRoot.Q<Toggle>("room-answer-single-attempt-only-field"), "回答1回制限のトグルが設定画面に残っています。");
            Assert.IsNotNull(panelRoot.Q<Toggle>("room-buzz-reopen-after-wrong-answer-field"), "隣のトグルは残っているはず。");
        }

        /// <summary>
        /// #221: 設定画面から外した answer.singleAttemptOnly の値（false）は、
        /// プリセット読み込み後に「適用」しても true へ戻らず保たれる。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator RoomApplyButton_PreservesSingleAttemptOnlyLoadedFromPreset()
        {
            const string presetName = "single-off";
            var parsed = RoomPresetJson.Parse(
                "{ \"schemaVersion\": 1, \"name\": \"" + presetName + "\", \"settings\": { \"answer.singleAttemptOnly\": false } }",
                presetName);
            Assert.IsFalse(parsed.Preset.Settings.Scoring.SingleAttemptOnly, "前提: プリセットの値は false。");
            new RoomPresetStore(Path.Combine(_tempRoot, "Presets")).Save(parsed.Preset);

            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            RegisterTestSettingsView(router);
            router.ShowView(ViewNames.Settings);

            Button presetsTabButton = null;
            yield return WaitForElement<Button>(panelRoot, "settings-tab-presets-button", found => presetsTabButton = found);
            yield return SimulateClickRoutine(presetsTabButton);

            DropdownField presetSelectField = null;
            yield return WaitForElement<DropdownField>(panelRoot, "preset-select-field", found => presetSelectField = found);
            presetSelectField.value = presetName;
            yield return SimulateClickRoutine(panelRoot.Q<Button>("preset-load-button"));
            Assert.IsFalse(RoomSettingsDraft.Current.Scoring.SingleAttemptOnly, "読み込み直後は false。");

            yield return SimulateClickRoutine(panelRoot.Q<Button>("settings-tab-room-button"));
            var applyButton = panelRoot.Q<Button>("room-settings-apply-button");
            Assert.IsNotNull(applyButton, "room-settings-apply-button が見つかりません。");
            yield return SimulateClickRoutine(applyButton);

            Assert.IsFalse(
                RoomSettingsDraft.Current.Scoring.SingleAttemptOnly,
                "適用しても answer.singleAttemptOnly の値が保たれていません。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator PresetSelectField_SelectingBuzzFocused_ChangesRoomFields()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            RegisterTestSettingsView(router);
            router.ShowView(ViewNames.Settings);

            Button presetsTabButton = null;
            yield return WaitForElement<Button>(panelRoot, "settings-tab-presets-button", found => presetsTabButton = found);
            yield return SimulateClickRoutine(presetsTabButton);

            DropdownField presetSelectField = null;
            yield return WaitForElement<DropdownField>(panelRoot, "preset-select-field", found => presetSelectField = found);
            presetSelectField.value = RoomPreset.BuzzFocusedName;

            Button presetLoadButton = panelRoot.Q<Button>("preset-load-button");
            Assert.IsNotNull(presetLoadButton, "preset-load-button が見つかりません。");
            yield return SimulateClickRoutine(presetLoadButton);

            Button roomTabButton = panelRoot.Q<Button>("settings-tab-room-button");
            Assert.IsNotNull(roomTabButton, "settings-tab-room-button が見つかりません。");
            yield return SimulateClickRoutine(roomTabButton);

            IntegerField buzzTimeLimitField = null;
            yield return WaitForElement<IntegerField>(panelRoot, "room-buzz-time-limit-field", found => buzzTimeLimitField = found);

            yield return WaitUntil(
                () => buzzTimeLimitField.value == 6,
                $"「早押し重視」プリセット読み込み後、buzz.timeLimitSec が 6 秒になっていません（実際: {buzzTimeLimitField.value}）。");
        }

        /// <summary>
        /// #28 Phase 2: ホストを開始していない（<c>RoomSettingsSync</c> が無い）状態で「適用」すると、
        /// ローカル保存だけ行ったことが状態ラベルに出て、<see cref="RoomSettingsDraft"/> にも反映される。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator RoomApplyButton_WithoutHost_SavesLocallyAndReportsIt()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            RegisterTestSettingsView(router);
            router.ShowView(ViewNames.Settings);

            IntegerField maxPlayersField = null;
            yield return WaitForElement<IntegerField>(panelRoot, "room-max-players-field", found => maxPlayersField = found);
            maxPlayersField.value = 5;

            var applyButton = panelRoot.Q<Button>("room-settings-apply-button");
            Assert.IsNotNull(applyButton, "room-settings-apply-button が見つかりません。");
            yield return SimulateClickRoutine(applyButton);

            var statusLabel = panelRoot.Q<Label>("room-settings-status-label");
            Assert.IsNotNull(statusLabel, "room-settings-status-label が見つかりません。");
            StringAssert.Contains(
                "この PC 内にのみ保存されます",
                statusLabel.text,
                "ホスト未開始のときは「ローカルにのみ保存した」旨を表示すること。");

            Assert.AreEqual(5, RoomSettingsDraft.Current.MaxPlayers, "適用した値が下書きへ保存されていません。");
        }

        /// <summary>
        /// PR #92 再レビュー L-3 / M-1: クライアント（ホストでない）として接続中は、ルーム設定タブの
        /// 入力群と「適用」が無効化され、<see cref="RoomSettingsDraft"/> も書き換わらない。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator RoomTab_WhenClient_IsReadOnlyAndKeepsDraft()
        {
            // 下書きに「この PC の値」を入れておく（ホストの設定で潰されないことを見る）。
            var draft = new RoomSettings(maxPlayers: 4);
            Assert.IsTrue(RoomSettingsDraft.Set(draft, out _), "下書きの保存に成功するはず。");

            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            // 未スポーンの RoomSettingsSync は IsServer == false / IsLocked == false なので、
            // 「クライアントとして接続中」と同じ判定になる（実接続は RoomSettingsDraftSpawnTests で検証済み）。
            // Main シーンの読み込み（Single モード）で破棄されないよう、読み込み後に作る。
            _fakeSyncObject = new GameObject("FakeRoomSettingsSync");
            _fakeSyncObject.SetActive(false);
            _fakeSyncObject.AddComponent<NetworkObject>();
            var fakeSync = _fakeSyncObject.AddComponent<RoomSettingsSync>();
            SettingsView.RoomSettingsSyncLocator = () => fakeSync;

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            RegisterTestSettingsView(router);
            router.ShowView(ViewNames.Settings);

            VisualElement fields = null;
            yield return WaitForElement<VisualElement>(panelRoot, "room-settings-fields", found => fields = found);

            Assert.IsFalse(fields.enabledSelf, "クライアントではルーム設定の入力群が無効化されるはず。");
            Assert.IsFalse(
                panelRoot.Q<VisualElement>("room-settings-advanced-fields").enabledSelf,
                "詳細設定の入力も無効化されるはず。");

            var applyButton = panelRoot.Q<Button>("room-settings-apply-button");
            Assert.IsNotNull(applyButton, "room-settings-apply-button が見つかりません。");
            Assert.IsFalse(applyButton.enabledSelf, "クライアントでは「適用」も無効化されるはず。");

            // L-2: 理由の文言と警告欄は器の外にあるので、閲覧モードでも有効なまま。
            var statusLabel = panelRoot.Q<Label>("room-settings-status-label");
            Assert.IsNotNull(statusLabel, "room-settings-status-label が見つかりません。");
            Assert.IsTrue(statusLabel.enabledSelf, "状態ラベルは無効化しない（理由が読めるようにする）。");
            StringAssert.Contains("ホストのみ変更できます", statusLabel.text);

            yield return SimulateClickRoutine(applyButton);
            yield return null;

            Assert.AreEqual(
                4,
                RoomSettingsDraft.Current.MaxPlayers,
                "クライアントでは下書きを書き換えないはず（ホストの設定で潰さない）。");
        }

        /// <summary>
        /// #120: 長文の注記 Label（<c>app-question-prefetch-count-note-label</c>）は
        /// <c>small-text-wrap</c> を併用しており、<c>resolvedStyle.whiteSpace</c> が
        /// <see cref="WhiteSpace.Normal"/>（折り返しあり）になっていることを確認する。
        /// 対照として、<c>small-text-wrap</c> を併用していない <c>app-settings-status-label</c>
        /// （<c>small-text</c> 単体）が既定どおり <see cref="WhiteSpace.NoWrap"/>（横に切れる）の
        /// ままであることも合わせて確認し、<c>small-text-wrap</c> 併用の効果が実際に効いていることを
        /// ピン留めする（PR #126 レビュー M-1）。
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AppTab_QuestionPrefetchCountNoteLabel_WhiteSpaceIsNormal()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            RegisterTestSettingsView(router);
            router.ShowView(ViewNames.Settings);

            Button appTabButton = null;
            yield return WaitForElement<Button>(panelRoot, "settings-tab-app-button", found => appTabButton = found);
            yield return SimulateClickRoutine(appTabButton);

            Foldout advancedFoldout = null;
            yield return WaitForElement<Foldout>(panelRoot, "app-advanced-foldout", found => advancedFoldout = found);
            advancedFoldout.value = true;

            Label noteLabel = null;
            yield return WaitForElement<Label>(
                panelRoot, "app-question-prefetch-count-note-label", found => noteLabel = found);

            Label appStatusLabel = null;
            yield return WaitForElement<Label>(panelRoot, "app-settings-status-label", found => appStatusLabel = found);

            yield return null;

            Assert.AreEqual(
                WhiteSpace.Normal,
                noteLabel.resolvedStyle.whiteSpace,
                "small-text-wrap を併用した注記 Label は折り返し（WhiteSpace.Normal）になっているはずです。");

            Assert.AreEqual(
                WhiteSpace.NoWrap,
                appStatusLabel.resolvedStyle.whiteSpace,
                "small-text-wrap を併用していない small-text 単体の Label は既定どおり折り返さない（WhiteSpace.NoWrap）はずです。");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator BackButton_ReturnsToTitle()
        {
            VisualElement panelRoot = null;
            yield return LoadMainSceneAndGetRoot(r => panelRoot = r);

            var router = UnityEngine.Object.FindAnyObjectByType<ViewRouter>();
            RegisterTestSettingsView(router);

            Button settingsButton = null;
            yield return WaitForElement<Button>(panelRoot, "settings-button", found => settingsButton = found);
            yield return SimulateClickRoutine(settingsButton);

            Button backButton = null;
            yield return WaitForElement<Button>(panelRoot, "back-button", found => backButton = found);
            Assert.IsTrue(backButton.enabledSelf, "Title から遷移した直後は戻るボタンが有効になっているはずです。");
            yield return SimulateClickRoutine(backButton);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            Assert.IsNotNull(hostButton, "戻るボタンのクリック後に Title View（host-button）へ戻っていません。");
            Assert.IsNull(panelRoot.Q<Label>("settings-heading"), "戻った後も Settings View の要素が残っています。");
        }
    }
}
