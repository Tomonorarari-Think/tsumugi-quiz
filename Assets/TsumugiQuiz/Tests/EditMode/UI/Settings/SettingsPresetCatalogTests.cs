using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Room;
using TsumugiQuiz.UI.Views.Settings;

namespace TsumugiQuiz.Tests.EditMode.UI.Settings
{
    /// <summary>
    /// <see cref="SettingsPresetCatalog"/>（issue #28）の組み込み/ユーザー保存プリセットの一覧組み立てを検証する。
    /// </summary>
    public class SettingsPresetCatalogTests
    {
        [Test]
        public void IsBuiltIn_BuiltInNames_ReturnsTrue()
        {
            Assert.IsTrue(SettingsPresetCatalog.IsBuiltIn(RoomPreset.StandardName));
            Assert.IsTrue(SettingsPresetCatalog.IsBuiltIn(RoomPreset.BuzzFocusedName));
            Assert.IsTrue(SettingsPresetCatalog.IsBuiltIn(RoomPreset.RelaxedName));
        }

        [Test]
        public void IsBuiltIn_UserPresetName_ReturnsFalse()
        {
            Assert.IsFalse(SettingsPresetCatalog.IsBuiltIn("マイプリセット"));
        }

        [Test]
        public void BuildDisplayList_NullUserNames_ReturnsBuiltInsOnly()
        {
            var list = SettingsPresetCatalog.BuildDisplayList(null);

            CollectionAssert.AreEqual(SettingsPresetCatalog.BuiltInNames, list);
        }

        [Test]
        public void BuildDisplayList_UserNames_AppendedSortedAfterBuiltIns()
        {
            var list = SettingsPresetCatalog.BuildDisplayList(new[] { "ぜっと", "あるふぁ" });

            Assert.AreEqual(RoomPreset.StandardName, list[0]);
            Assert.AreEqual(RoomPreset.BuzzFocusedName, list[1]);
            Assert.AreEqual(RoomPreset.RelaxedName, list[2]);
            Assert.AreEqual("あるふぁ", list[3]);
            Assert.AreEqual("ぜっと", list[4]);
        }

        [Test]
        public void BuildDisplayList_DuplicatesAndBlanks_AreRemoved()
        {
            var list = SettingsPresetCatalog.BuildDisplayList(
                new[] { "マイ設定", "マイ設定", " ", "", RoomPreset.StandardName });

            var expected = new List<string>(SettingsPresetCatalog.BuiltInNames) { "マイ設定" };
            CollectionAssert.AreEqual(expected, list);
        }

        [Test]
        public void FindBuiltInSettings_KnownName_ReturnsMatchingSettings()
        {
            var settings = SettingsPresetCatalog.FindBuiltInSettings(RoomPreset.BuzzFocusedName);

            Assert.IsNotNull(settings);
            Assert.AreSame(RoomPreset.BuzzFocused.Settings, settings);
        }

        [Test]
        public void FindBuiltInSettings_UnknownName_ReturnsNull()
        {
            Assert.IsNull(SettingsPresetCatalog.FindBuiltInSettings("存在しないプリセット"));
        }
    }
}
