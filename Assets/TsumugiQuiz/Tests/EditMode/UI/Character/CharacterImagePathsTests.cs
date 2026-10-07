using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.UI;

namespace TsumugiQuiz.Tests.EditMode.UI.Character
{
    /// <summary>
    /// <see cref="CharacterImagePaths"/> のパス解決を検証する（issue #24、レビュー H1: AppPaths ベースに変更）。
    /// </summary>
    public class CharacterImagePathsTests
    {
        [Test]
        public void ResolveImagePath_IsUnderAppPathsDataRoot()
        {
            // AppPaths は静的な状態を持つため、明示設定と後始末(Reset)をこのテスト内で完結させる
            // （TsumugiQuiz.Tests.EditMode.UI.JsonConsentStorageTests と同じ方針、#71）。
            var explicitRoot = Path.Combine(Path.GetTempPath(), "TsumugiQuizCharacterImagePathsTest_" + System.Guid.NewGuid().ToString("N"));
            AppPaths.Configure(explicitRoot);
            try
            {
                var path = CharacterImagePaths.ResolveImagePath();

                Assert.IsTrue(path.StartsWith(explicitRoot));
                Assert.IsTrue(path.EndsWith(Path.Combine("tsumugi", "tsumugi_v2.png")));
            }
            finally
            {
                AppPaths.Reset();
            }
        }

        [Test]
        public void ResolveImagePath_DataRootNotConfigured_Throws()
        {
            AppPaths.Reset();

            // 環境変数 TSUMUGI_DATA_ROOT が設定されている実行環境（scripts/verify.ps1）では
            // 例外にならないため、その場合はテストの前提が成り立たないとしてスキップする。
            if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable)))
            {
                Assert.Ignore($"環境変数 {AppPaths.DataRootEnvironmentVariable} が設定されているため、このテストの前提が成り立ちません。");
            }

            Assert.Throws<System.InvalidOperationException>(() => CharacterImagePaths.ResolveImagePath());
        }

        // ---- 表情差分（issue #86） --------------------------------------------------

        private static IEnumerable<TestCaseData> ExpressionFileNameCases()
        {
            yield return new TestCaseData(CharacterState.Idle, CharacterImagePaths.IdleFileName);
            yield return new TestCaseData(CharacterState.Reading, CharacterImagePaths.ReadingFileName);
            yield return new TestCaseData(CharacterState.Correct, CharacterImagePaths.CorrectFileName);
            yield return new TestCaseData(CharacterState.Wrong, CharacterImagePaths.WrongFileName);

            // #212: 場面ごとの表情
            yield return new TestCaseData(CharacterState.BuzzSelf, CharacterImagePaths.BuzzSelfFileName);
            yield return new TestCaseData(CharacterState.BuzzOther, CharacterImagePaths.BuzzOtherFileName);
            yield return new TestCaseData(CharacterState.WrongMoment, CharacterImagePaths.WrongMomentFileName);
            yield return new TestCaseData(CharacterState.TimedOut, CharacterImagePaths.TimedOutFileName);
            yield return new TestCaseData(CharacterState.NoEligibleBuzzers, CharacterImagePaths.NoEligibleBuzzersFileName);
        }

        [TestCaseSource(nameof(ExpressionFileNameCases))]
        public void GetFileName_ReturnsExpressionFileForEachState(CharacterState state, string expected)
        {
            Assert.AreEqual(expected, CharacterImagePaths.GetFileName(state));
        }

        [Test]
        public void ExpressionFileNames_AreAllDistinct()
        {
            var names = new[]
            {
                CharacterImagePaths.FileName,
                CharacterImagePaths.IdleFileName,
                CharacterImagePaths.ReadingFileName,
                CharacterImagePaths.CorrectFileName,
                CharacterImagePaths.WrongFileName,
                CharacterImagePaths.BuzzSelfFileName,
                CharacterImagePaths.BuzzOtherFileName,
                CharacterImagePaths.WrongMomentFileName,
                CharacterImagePaths.TimedOutFileName,
                CharacterImagePaths.NoEligibleBuzzersFileName,
            };

            CollectionAssert.AllItemsAreUnique(names);
            // scripts/generate_tsumugi_expressions.py の fileName 検証と同じ制約
            // （ディレクトリを含まない .png であること）。
            Assert.IsTrue(names.All(n => n == Path.GetFileName(n) && n.EndsWith(".png")));
        }

        [Test]
        public void GetFileNameCandidates_Idle_FallsBackToLegacyWholeBodyPng()
        {
            CollectionAssert.AreEqual(
                new[] { CharacterImagePaths.IdleFileName, CharacterImagePaths.FileName },
                CharacterImagePaths.GetFileNameCandidates(CharacterState.Idle));
        }

        [TestCase(CharacterState.Reading)]
        [TestCase(CharacterState.Correct)]
        [TestCase(CharacterState.Wrong)]
        [TestCase(CharacterState.BuzzSelf)]
        [TestCase(CharacterState.BuzzOther)]
        public void GetFileNameCandidates_NonIdle_FallsBackToIdleThenLegacy(CharacterState state)
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    CharacterImagePaths.GetFileName(state),
                    CharacterImagePaths.IdleFileName,
                    CharacterImagePaths.FileName,
                },
                CharacterImagePaths.GetFileNameCandidates(state));
        }

        /// <summary>
        /// #212: 増やした表情は、意味の近い既存の表情（#86 の 4 枚）を経由してフォールバックする。
        /// 既存の 4 枚しか生成していない利用者でも、誤答・時間切れでは不正解の顔が出る。
        /// </summary>
        private static IEnumerable<TestCaseData> NegativeFallbackCases()
        {
            yield return new TestCaseData(
                CharacterState.WrongMoment,
                new[]
                {
                    CharacterImagePaths.WrongMomentFileName, CharacterImagePaths.WrongFileName,
                    CharacterImagePaths.IdleFileName, CharacterImagePaths.FileName,
                });
            yield return new TestCaseData(
                CharacterState.TimedOut,
                new[]
                {
                    CharacterImagePaths.TimedOutFileName, CharacterImagePaths.WrongFileName,
                    CharacterImagePaths.IdleFileName, CharacterImagePaths.FileName,
                });
            yield return new TestCaseData(
                CharacterState.NoEligibleBuzzers,
                new[]
                {
                    CharacterImagePaths.NoEligibleBuzzersFileName, CharacterImagePaths.TimedOutFileName,
                    CharacterImagePaths.WrongFileName, CharacterImagePaths.IdleFileName, CharacterImagePaths.FileName,
                });
        }

        [TestCaseSource(nameof(NegativeFallbackCases))]
        public void GetFileNameCandidates_NegativeScenes_FallBackThroughWrong(CharacterState state, string[] expected)
        {
            CollectionAssert.AreEqual(expected, CharacterImagePaths.GetFileNameCandidates(state));
        }

        [Test]
        public void GetFileNameCandidates_EveryState_EndsWithIdleThenLegacy()
        {
            foreach (CharacterState state in System.Enum.GetValues(typeof(CharacterState)))
            {
                var candidates = CharacterImagePaths.GetFileNameCandidates(state);
                Assert.AreEqual(CharacterImagePaths.FileName, candidates[candidates.Count - 1], $"{state}");
                if (state != CharacterState.Idle)
                {
                    Assert.AreEqual(CharacterImagePaths.IdleFileName, candidates[candidates.Count - 2], $"{state}");
                }

                CollectionAssert.AllItemsAreUnique(candidates, $"{state}");
            }
        }

        /// <summary>
        /// #212: 生成スクリプトの設定（docs/tsumugi-expressions.sample.json）が書き出すファイル名と、
        /// アプリが読む状態ごとのファイル名が一致していること（片方だけ増やして読まれない表情を防ぐ）。
        /// </summary>
        [Test]
        public void SampleExpressionConfig_FileNames_MatchEveryStateDedicatedFile()
        {
            var samplePath = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "docs", "tsumugi-expressions.sample.json"));
            Assert.IsTrue(File.Exists(samplePath), $"{samplePath} が見つかりません。");

            var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(samplePath));
            var fileNames = root["expressions"]
                .Select(expression => (string)expression["fileName"])
                .ToArray();

            var expected = System.Enum.GetValues(typeof(CharacterState))
                .Cast<CharacterState>()
                .Select(CharacterImagePaths.GetFileName)
                .ToArray();

            CollectionAssert.AreEquivalent(expected, fileNames);
        }

        [Test]
        public void GetFileNameCandidates_UnknownState_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => CharacterImagePaths.GetFileNameCandidates((CharacterState)999));
        }

        [Test]
        public void ResolveImagePathCandidates_AreUnderAppPathsDataRoot_InFallbackOrder()
        {
            var explicitRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizCharacterExpressionPathsTest_" + System.Guid.NewGuid().ToString("N"));
            AppPaths.Configure(explicitRoot);
            try
            {
                var paths = CharacterImagePaths.ResolveImagePathCandidates(CharacterState.Correct);

                Assert.AreEqual(3, paths.Count);
                CollectionAssert.AreEqual(
                    new[]
                    {
                        Path.Combine(explicitRoot, "tsumugi", CharacterImagePaths.CorrectFileName),
                        Path.Combine(explicitRoot, "tsumugi", CharacterImagePaths.IdleFileName),
                        Path.Combine(explicitRoot, "tsumugi", CharacterImagePaths.FileName),
                    },
                    paths);
            }
            finally
            {
                AppPaths.Reset();
            }
        }
    }
}
