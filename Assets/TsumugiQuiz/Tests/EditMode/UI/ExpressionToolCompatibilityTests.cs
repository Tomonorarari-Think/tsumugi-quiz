using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// 配布 zip の表情生成ツール（scripts/tsumugi_app_consent.py、#219 H1）が、アプリと「まったく同じ条件」で
    /// 同意の有無を判定することを、Python と C# で同じテスト入力（scripts/tests/fixtures/）を使って確かめる。
    /// Python 側は scripts/tests/test_tsumugi_app_consent.py が同じ入力で同じ期待値を確かめる（両方向の照合）。
    /// </summary>
    public class ExpressionToolCompatibilityTests
    {
        private static string FixturesDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "scripts", "tests", "fixtures"));

        private static string TermsResourceDirectory =>
            Path.Combine(Application.dataPath, "TsumugiQuiz", "Resources", "Terms");

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void TermsHashVectors_MatchPythonPort()
        {
            var vectors = JArray.Parse(File.ReadAllText(Path.Combine(FixturesDirectory, "terms-hash-vectors.json"), Encoding.UTF8));
            Assert.That(vectors.Count, Is.GreaterThan(0));
            foreach (var vector in vectors)
            {
                var actual = TermsHasher.ComputeSha256HexForTermsBody((string)vector["text"]);
                Assert.AreEqual((string)vector["sha256"], actual, $"ベクタ {(string)vector["name"]} のハッシュが Python の移植と違います。");
            }
        }

        [Test]
        public void ConsentCases_SameVerdictAsPythonPort()
        {
            var consentDirectory = Path.Combine(FixturesDirectory, "consent");
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(consentDirectory, "cases.json"), Encoding.UTF8));
            var required = manifest["requiredTerms"]
                .Select(t => new TermsDefinition((string)t["termsId"], (string)t["sha256"]))
                .ToList();

            // 壊れた入力ではアプリが LogError / LogWarning を出す（それ自体が想定どおりの動き）。
            LogAssert.ignoreFailingMessages = true;
            foreach (var testCase in (JArray)manifest["cases"])
            {
                var fileName = (string)testCase["file"];
                var path = fileName == null
                    ? Path.Combine(consentDirectory, "does-not-exist", "consent.json")
                    : Path.Combine(consentDirectory, fileName);
                var store = new ConsentStore(new JsonConsentStorage(path));
                Assert.AreEqual((bool)testCase["accepted"], store.HasAcceptedAll(required),
                    $"ケース {(string)testCase["name"]} の判定が Python の移植と違います。");
            }
        }

        [Test]
        public void TermsCatalogEntries_MatchTermsFiles_CopiedIntoReleaseZip()
        {
            // package-release.ps1 は Resources/Terms/*.txt をすべて zip の terms/ にコピーし、ツールはその全件を
            // 「必要な規約」として扱う。アプリの必要な規約（TermsCatalog.Entries）と一致していなければならない。
            var fileIds = Directory.GetFiles(TermsResourceDirectory, "*.txt")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(id => id)
                .ToList();
            var entryIds = TermsCatalog.Entries.Select(e => e.TermsId).OrderBy(id => id).ToList();
            CollectionAssert.AreEqual(entryIds, fileIds);
        }

        [Test]
        public void TermsResourceHash_EqualsHashOfFileRead()
        {
            // アプリは TextAsset（Resources.Load）の文字列を、ツールはファイルを UTF-8 で読んだ文字列をハッシュにする。
            // 両者が同じハッシュになることを確かめる。
            var required = TermsCatalog.LoadRequiredTerms();
            foreach (var definition in required)
            {
                var path = Path.Combine(TermsResourceDirectory, definition.TermsId + ".txt");
                var fromFile = TermsHasher.ComputeSha256HexForTermsBody(File.ReadAllText(path, Encoding.UTF8));
                Assert.AreEqual(definition.Sha256Hash, fromFile, $"{definition.TermsId} のハッシュが違います。");
            }
        }
    }
}
