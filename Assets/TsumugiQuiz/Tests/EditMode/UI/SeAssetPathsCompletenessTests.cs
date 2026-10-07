using System;
using NUnit.Framework;
using TsumugiQuiz.UI;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// <see cref="SeAssetPaths.AllKinds"/> は <c>Enum.GetValues(typeof(SeKind))</c> から導出しているため、
    /// <see cref="SeKind"/> に値を追加した場合でも自動的に列挙対象になる。
    /// 一方でファイル名の対応表（<see cref="SeAssetPaths.GetFileName"/> が内部で参照する辞書）は
    /// 手動で追記が必要なので、追加し忘れを検出するための完全性テスト。
    /// </summary>
    public class SeAssetPathsCompletenessTests
    {
        [Test]
        public void AllEnumValues_HaveFileNameMapping()
        {
            foreach (SeKind kind in Enum.GetValues(typeof(SeKind)))
            {
                Assert.DoesNotThrow(
                    () => SeAssetPaths.GetFileName(kind),
                    $"SeKind.{kind} に対応するファイル名が SeAssetPaths に定義されていません。");
            }
        }

        [Test]
        public void AllKinds_ContainsEveryEnumValue()
        {
            var enumValues = (SeKind[])Enum.GetValues(typeof(SeKind));

            Assert.AreEqual(enumValues.Length, SeAssetPaths.AllKinds.Count,
                "SeAssetPaths.AllKinds の件数が SeKind の全値と一致しません。");

            foreach (var kind in enumValues)
            {
                CollectionAssert.Contains((System.Collections.ICollection)SeAssetPaths.AllKinds, kind,
                    $"SeAssetPaths.AllKinds に SeKind.{kind} が含まれていません。");
            }
        }
    }
}
