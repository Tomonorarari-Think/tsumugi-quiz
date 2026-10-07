using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Tests.EditMode.Room
{
    /// <summary>
    /// <see cref="AppSettings"/> の public プロパティ名の集合を固定するカナリアテスト
    /// （PR #92 再レビュー L-1）。
    /// </summary>
    /// <remarks>
    /// <see cref="AppSettingsStore.Save"/> は <c>app-settings.json</c> を丸ごと書き直すため、
    /// 「アプリ設定タブが UI に持たないキー」は保存前に読み直して引き継ぐ必要がある
    /// （<c>SettingsView.AppTab.cs</c> の <c>PreserveKeysNotEditedHere</c>。再レビュー H-1）。
    /// プロパティを足したときにその対応を忘れると値が黙って消えるので、ここで気付けるようにする。
    /// </remarks>
    public class AppSettingsPropertyCanaryTests
    {
        /// <summary>現在の <see cref="AppSettings"/> の public プロパティ名（アルファベット順）。</summary>
        private static readonly string[] ExpectedPropertyNames =
        {
            nameof(AppSettings.CharacterEnabled),
            nameof(AppSettings.HostRole),
            nameof(AppSettings.IpLookupUrls),
            nameof(AppSettings.NetworkPort),
            nameof(AppSettings.PlayerName),
            nameof(AppSettings.QuestionPrefetchCount),
            nameof(AppSettings.RoomLastApplied),
            nameof(AppSettings.TtsAssetPathOverride),
            nameof(AppSettings.TtsCacheMaxBytes),
            nameof(AppSettings.TtsCacheMaxEntries),
            nameof(AppSettings.TtsSpeakerName),
            nameof(AppSettings.TtsStyleName),
            nameof(AppSettings.UpnpDiscoveryTimeoutMs),
            nameof(AppSettings.UpnpEnabled),
            nameof(AppSettings.UpnpMappingLifetimeSec),
            nameof(AppSettings.UpnpRenewIntervalMs),
        };

        [Test]
        public void AppSettings_PublicProperties_MatchExpectedSet()
        {
            var actual = typeof(AppSettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.Name)
                .OrderBy(name => name, System.StringComparer.Ordinal)
                .ToArray();

            var expected = ExpectedPropertyNames
                .OrderBy(name => name, System.StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                expected,
                actual,
                BuildFailureMessage(expected, actual));
        }

        private static string BuildFailureMessage(IEnumerable<string> expected, IEnumerable<string> actual)
            => "AppSettings の public プロパティが変わりました。"
               + "プロパティを足したら、アプリ設定タブが編集しないキーを保存で失わないよう "
               + "SettingsView.AppTab.cs の PreserveKeysNotEditedHere と CollectAppInput を見直すこと"
               + "（どちらにも無いキーは『保存』のたびに既定値へ戻ります）。"
               + $" 期待: [{string.Join(", ", expected)}] / 実際: [{string.Join(", ", actual)}]";
    }
}
