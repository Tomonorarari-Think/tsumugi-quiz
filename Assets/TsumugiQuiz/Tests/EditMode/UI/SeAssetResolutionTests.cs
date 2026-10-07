using NUnit.Framework;
using TsumugiQuiz.UI;
using UnityEditor;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.UI
{
    /// <summary>
    /// scripts/gen-se.py が生成した wav（Assets/TsumugiQuiz/Audio/SE/）が、
    /// <see cref="SeAssetPaths"/> の定義するパスで実際に AudioClip としてロードできることを確認する。
    /// （EditMode テストは Editor 上でのみ実行されるため <c>UnityEditor.AssetDatabase</c> を直接使える）
    /// </summary>
    public class SeAssetResolutionTests
    {
        // scripts/gen-se.py の MIN_DURATION/MAX_DURATION（0.2秒/1.0秒）と一致させる。
        // サンプル数を丸める際の誤差を吸収する程度の小さな許容差のみ加える。
        private const float MinDurationSeconds = 0.2f;
        private const float MaxDurationSeconds = 1.0f;
        private const float DurationToleranceSeconds = 0.01f;

        [Test]
        public void AllSeKinds_HaveResolvableAudioClip()
        {
            foreach (var kind in SeAssetPaths.AllKinds)
            {
                var path = SeAssetPaths.GetAssetPath(kind);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

                Assert.IsNotNull(
                    clip,
                    $"'{kind}' に対応する AudioClip が '{path}' から読み込めません。" +
                    "scripts/gen-se.py を実行して wav を生成・コミットしてください。");
            }
        }

        [Test]
        public void AllSeKinds_HaveNonZeroLength()
        {
            foreach (var kind in SeAssetPaths.AllKinds)
            {
                var path = SeAssetPaths.GetAssetPath(kind);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assume.That(clip, Is.Not.Null);

                Assert.GreaterOrEqual(clip.length, MinDurationSeconds - DurationToleranceSeconds,
                    $"'{kind}' ({path}) の AudioClip が短すぎます（下限 {MinDurationSeconds}秒）。");
                Assert.LessOrEqual(clip.length, MaxDurationSeconds + DurationToleranceSeconds,
                    $"'{kind}' ({path}) の AudioClip が長すぎます（上限 {MaxDurationSeconds}秒）。");
            }
        }
    }
}
