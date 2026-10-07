using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// issue #29（PR #42 統括申し送り M4）: docs/samples/sample-questions.json と、
    /// <see cref="TsumugiQuiz.Questions.QuestionLibrary"/> がビルドに同梱して起動時に書き出す
    /// Resources 版（Assets/TsumugiQuiz/Resources/Questions/sample-questions.json）
    /// が内容として乖離していないことを保証する回帰テスト。
    /// Resources フォルダの実体はビルドに含める必要があるため物理ファイルとして複製せざるを得ないが、
    /// 少なくとも内容の同一性はテストで検証し、ドリフトを防ぐ。
    /// </summary>
    public class SampleQuestionsResourceSourceTests
    {
        [Test]
        public void ResourcesCopy_MatchesDocsSampleContentExactly()
        {
            var docsSamplePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "samples", "sample-questions.json"));
            Assert.IsTrue(File.Exists(docsSamplePath), $"docs/samples/sample-questions.json が見つかりません: {docsSamplePath}");

            var resourceAsset = Resources.Load<TextAsset>("Questions/sample-questions");
            Assert.IsNotNull(resourceAsset, "Resources/Questions/sample-questions.json が見つかりません");

            var docsContent = File.ReadAllText(docsSamplePath);
            Assert.AreEqual(docsContent, resourceAsset.text,
                "Resources 版のサンプルは docs/samples/sample-questions.json と内容が一致していること（単一ソースの複製として同期を保つ）");
        }
    }
}
