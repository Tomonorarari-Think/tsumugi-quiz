using System;
using System.Collections.Generic;
using System.Linq;
using TsumugiQuiz.Room;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// <see cref="SettingsView"/> のうち「プリセット」タブ（docs/room-settings.md §3）。
    /// 組み込み3種（<see cref="RoomPreset.BuiltIns"/>）とユーザー保存分の選択・保存・読込・削除を扱う。
    /// </summary>
    public sealed partial class SettingsView
    {
        /// <summary>
        /// 使う <see cref="RoomPresetStore"/> の生成方法。既定は実ファイルシステムを使う実装。
        /// PlayMode テストではこのプロパティにフェイク・テスト用フォルダを積んだファクトリを差し替えることで、
        /// 実際の <c>Documents</c> フォルダに触れずに検証できる。
        /// </summary>
        internal Func<RoomPresetStore> RoomPresetStoreFactory { get; set; } = () => new RoomPresetStore();

        private RoomPresetStore _presetStore;

        private DropdownField _presetSelectField;
        private Button _presetLoadButton;
        private Button _presetDeleteButton;
        private TextField _presetNameField;
        private Button _presetSaveButton;
        private VisualElement _presetWarningsContainer;
        private Label _presetStatusLabel;

        /// <summary>削除確認待ちのプリセット名（L2）。null なら確認待ちではない。</summary>
        private string _pendingDeleteName;

        private void OnShowPresetsTab(VisualElement root)
        {
            var allFound = true;

            _presetSelectField = SettingsFieldBinder.Require<DropdownField>(root, "preset-select-field", ref allFound);
            _presetLoadButton = SettingsFieldBinder.Require<Button>(root, "preset-load-button", ref allFound);
            _presetDeleteButton = SettingsFieldBinder.Require<Button>(root, "preset-delete-button", ref allFound);
            _presetNameField = SettingsFieldBinder.Require<TextField>(root, "preset-name-field", ref allFound);
            _presetSaveButton = SettingsFieldBinder.Require<Button>(root, "preset-save-button", ref allFound);
            _presetWarningsContainer = SettingsFieldBinder.Require<VisualElement>(root, "preset-warnings", ref allFound);
            _presetStatusLabel = SettingsFieldBinder.Require<Label>(root, "preset-status-label", ref allFound);

            if (!allFound)
            {
                // L6: 部分的に配線された状態を残さない。
                OnHidePresetsTab();
                return;
            }

            _pendingDeleteName = null;

            try
            {
                _presetStore = RoomPresetStoreFactory();
            }
            catch (InvalidOperationException ex)
            {
                // M-2: RoomPresetStore の既定コンストラクタは保存先（Documents 配下）を解決できないと
                // InvalidOperationException を投げる。アプリ設定タブ（M4）と同じく、プリセット機能だけを
                // 無効化して理由を表示する（画面全体は開けるようにする）。
                _presetStore = null;
                ShowPresetWarnings(new[] { $"プリセットの保存先を解決できませんでした: {ex.Message}" });
                ShowPresetStatus("プリセット機能は利用できません。");
                SetPresetControlsEnabled(false);
                return;
            }

            SetPresetControlsEnabled(true);
            RefreshPresetChoices(RoomPreset.StandardName);
            ShowPresetStatus(string.Empty);

            _presetLoadButton.clicked += OnPresetLoadClicked;
            _presetSaveButton.clicked += OnPresetSaveClicked;
            _presetDeleteButton.clicked += OnPresetDeleteClicked;
        }

        /// <summary>プリセットタブの操作要素をまとめて有効・無効にする（M-2）。</summary>
        private void SetPresetControlsEnabled(bool enabled)
        {
            _presetSelectField.SetEnabled(enabled);
            _presetLoadButton.SetEnabled(enabled);
            _presetDeleteButton.SetEnabled(enabled);
            _presetNameField.SetEnabled(enabled);
            _presetSaveButton.SetEnabled(enabled);
        }

        private void OnHidePresetsTab()
        {
            if (_presetLoadButton != null) _presetLoadButton.clicked -= OnPresetLoadClicked;
            if (_presetSaveButton != null) _presetSaveButton.clicked -= OnPresetSaveClicked;
            if (_presetDeleteButton != null) _presetDeleteButton.clicked -= OnPresetDeleteClicked;

            _presetStore = null;
            _pendingDeleteName = null;
            _presetSelectField = null;
            _presetLoadButton = null;
            _presetDeleteButton = null;
            _presetNameField = null;
            _presetSaveButton = null;
            _presetWarningsContainer = null;
            _presetStatusLabel = null;
        }

        /// <summary>
        /// プリセット一覧を再読み込みする。I/O 失敗時は <see cref="RoomPresetStore.ListUserPresetNamesWithWarnings"/>
        /// の警告を表示する（M8）。
        /// </summary>
        private void RefreshPresetChoices(string selectedName)
        {
            var userNames = _presetStore.ListUserPresetNamesWithWarnings(out var listWarnings);
            ShowPresetWarnings(listWarnings.Count > 0 ? listWarnings : null);

            var choices = SettingsPresetCatalog.BuildDisplayList(userNames);
            _presetSelectField.choices = new List<string>(choices);

            _presetSelectField.value = choices.Count > 0 && choices.Contains(selectedName)
                ? selectedName
                : (choices.Count > 0 ? choices[0] : string.Empty);
        }

        private void OnPresetLoadClicked()
        {
            _pendingDeleteName = null;

            var name = _presetSelectField.value;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (SettingsPresetCatalog.IsBuiltIn(name))
            {
                // L-2: IsBuiltIn が true でも FindBuiltInSettings が null を返す状況
                // （組み込み一覧の定義と名前判定がずれた場合）に備えて防御する。
                var builtIn = SettingsPresetCatalog.FindBuiltInSettings(name);
                if (builtIn == null)
                {
                    ShowPresetStatus($"組み込みプリセット「{name}」の内容を取得できませんでした。");
                    return;
                }

                ApplyLoadedRoomSettings(builtIn, name, warnings: null);
                return;
            }

            try
            {
                var parseResult = _presetStore.Load(name);
                if (!parseResult.Found)
                {
                    ShowPresetStatus($"プリセット「{name}」が見つかりませんでした。");
                    return;
                }

                ApplyLoadedRoomSettings(parseResult.Preset.Settings, name, parseResult.Warnings);
            }
            catch (ArgumentException ex)
            {
                // M2: ファイル名として使えない名前等（通常はドロップダウンの選択肢に出てこないはずだが防御的に捕捉する）。
                ShowPresetStatus($"プリセット「{name}」を読み込めませんでした: {ex.Message}");
            }
        }

        private void ApplyLoadedRoomSettings(RoomSettings settings, string name, IReadOnlyList<string> warnings)
        {
            _currentRoomSettings = settings;
            PopulateRoomFields(_currentRoomSettings);
            ShowRoomWarnings(null);

            var presetWarnings = new List<string>();
            if (warnings != null)
            {
                presetWarnings.AddRange(warnings);
            }

            SaveRoomDraft(_currentRoomSettings, presetWarnings);
            ShowPresetWarnings(presetWarnings.Count > 0 ? presetWarnings : null);
            ShowPresetStatus(
                $"プリセット「{name}」を読み込みました。「ルーム設定」タブで内容を確認し、"
                + "「適用」を押すと参加者へ反映されます（#28 Phase 2）。");
        }

        private void OnPresetSaveClicked()
        {
            _pendingDeleteName = null;

            var name = _presetNameField.value?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ShowPresetStatus("保存する名前を入力してください。");
                return;
            }

            if (SettingsPresetCatalog.IsBuiltIn(name))
            {
                ShowPresetStatus($"「{name}」は組み込みプリセットの名前のため使用できません。別の名前を指定してください。");
                return;
            }

            // M3: ウィジェットの現在値を Validator に通してから保存する（「適用」を押していない未検証の値を
            // そのまま保存しないため）。クランプ結果はルーム設定タブにも反映する。
            var input = CollectRoomInput();
            var validation = RoomSettingsValidator.Validate(input);
            _currentRoomSettings = validation.Settings;
            PopulateRoomFields(_currentRoomSettings);
            ShowRoomWarnings(validation.Warnings);

            var collectedWarnings = new List<string>(validation.Warnings);
            SaveRoomDraft(_currentRoomSettings, collectedWarnings);

            RoomPresetSaveResult saveResult;
            try
            {
                saveResult = _presetStore.Save(new RoomPreset(name, _currentRoomSettings));
            }
            catch (ArgumentException ex)
            {
                ShowPresetStatus($"この名前では保存できません: {ex.Message}");
                return;
            }

            if (!saveResult.Success)
            {
                ShowPresetWarnings(saveResult.Warnings);
                ShowPresetStatus("プリセットの保存に失敗しました。");
                return;
            }

            RefreshPresetChoices(name);
            var savedWarnings = collectedWarnings.Count > 0 ? collectedWarnings : null;
            ShowPresetWarnings(savedWarnings);
            ShowPresetStatus(savedWarnings == null
                ? $"現在の設定をプリセット「{name}」として保存しました。"
                : $"現在の設定をプリセット「{name}」として保存しました（{savedWarnings.Count} 件の警告があります）。");
        }

        private void OnPresetDeleteClicked()
        {
            var name = _presetSelectField.value;
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (SettingsPresetCatalog.IsBuiltIn(name))
            {
                _pendingDeleteName = null;
                ShowPresetStatus("組み込みプリセットは削除できません。");
                return;
            }

            // L2: 誤操作防止のため、同じプリセットへの 2 回目のクリックで初めて削除する。
            if (_pendingDeleteName != name)
            {
                _pendingDeleteName = name;
                ShowPresetStatus($"プリセット「{name}」を削除します。もう一度「削除」を押すと確定します。");
                return;
            }

            _pendingDeleteName = null;

            try
            {
                var deleted = _presetStore.Delete(name);
                RefreshPresetChoices(RoomPreset.StandardName);
                ShowPresetStatus(deleted ? $"プリセット「{name}」を削除しました。" : $"プリセット「{name}」を削除できませんでした。");
            }
            catch (ArgumentException ex)
            {
                // M2: ファイル名として使えない名前等（通常はドロップダウンの選択肢に出てこないはずだが防御的に捕捉する）。
                ShowPresetStatus($"プリセット「{name}」を削除できませんでした: {ex.Message}");
            }
        }

        private void ShowPresetWarnings(IReadOnlyList<string> warnings)
        {
            _presetWarningsContainer.Clear();
            if (warnings == null)
            {
                return;
            }

            foreach (var warning in warnings)
            {
                var label = new Label(warning);
                label.AddToClassList("body-text");
                label.AddToClassList("host-setup-warning");
                _presetWarningsContainer.Add(label);
            }
        }

        private void ShowPresetStatus(string message)
        {
            _presetStatusLabel.text = message ?? string.Empty;
            _presetStatusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
