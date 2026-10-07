using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// <see cref="SePlayer"/> が実際に AudioSource.PlayOneShot を呼び出せることを確認する PlayMode テスト。
    /// 実アセット（wav）の読み込みは EditMode 側（SeAssetResolutionTests）で検証済みのため、
    /// ここでは合成した AudioClip を注入し、Play が例外なく完了することのみを確認する。
    /// </summary>
    public class SePlayerPlaybackTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
            }
        }

        [Test]
        public void Play_WithRegisteredClip_DoesNotThrow()
        {
            var player = CreateSePlayerWithClip(SeKind.Buzz, CreateShortSilentClip());

            Assert.DoesNotThrow(() => player.Play(SeKind.Buzz));
        }

        [Test]
        public void Play_WithUnregisteredKind_LogsErrorButDoesNotThrow()
        {
            var player = CreateSePlayerWithClip(SeKind.Buzz, CreateShortSilentClip());

            // Correct 用のクリップは登録していないため、警告ログを許容しつつ例外が出ないことを確認する。
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*Correct.*"));
            Assert.DoesNotThrow(() => player.Play(SeKind.Correct));
        }

        private SePlayer CreateSePlayerWithClip(SeKind kind, AudioClip clip)
        {
            _gameObject = new GameObject(nameof(SePlayerPlaybackTests));
            _gameObject.AddComponent<AudioSource>();
            var player = _gameObject.AddComponent<SePlayer>();
            player.SetClips(new List<SeClipEntry> { new(kind, clip) });
            return player;
        }

        private static AudioClip CreateShortSilentClip()
        {
            const int sampleRate = 44100;
            const int lengthSamples = 100;
            return AudioClip.Create("test-clip", lengthSamples, 1, sampleRate, false);
        }
    }
}
