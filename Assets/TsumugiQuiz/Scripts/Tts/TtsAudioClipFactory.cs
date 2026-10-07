using System;
using TsumugiQuiz.Core.Audio;
using UnityEngine;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// 解析済み WAV から <see cref="AudioClip"/> を作る（docs/tts.md §6.3）。
    ///
    /// <b><c>AudioClip.Create</c> と <c>SetData</c> はメインスレッドでしか呼べない。</b>
    /// 合成とパースはワーカースレッドで行い、ここだけメインスレッドに戻して呼ぶこと。
    /// </summary>
    public static class TtsAudioClipFactory
    {
        /// <summary>生成する <see cref="AudioClip"/> の既定名。</summary>
        public const string DefaultClipName = "tts";

        /// <summary><see cref="WavData"/> から <see cref="AudioClip"/> を作る。</summary>
        /// <exception cref="ArgumentException">サンプルが空のとき</exception>
        /// <exception cref="InvalidOperationException"><c>SetData</c> が失敗したとき</exception>
        public static AudioClip Create(WavData wav, string name = DefaultClipName)
        {
            var frameCount = wav.FrameCount;
            if (frameCount <= 0)
            {
                throw new ArgumentException("サンプルが空の WAV からは AudioClip を作れません。", nameof(wav));
            }

            var clip = AudioClip.Create(
                string.IsNullOrEmpty(name) ? DefaultClipName : name,
                frameCount, wav.Channels, wav.SampleRate, stream: false);

            if (!clip.SetData(wav.Samples, 0))
            {
                // 生成に失敗したクリップを残すとリークになるので破棄する。
                UnityEngine.Object.Destroy(clip);
                throw new InvalidOperationException(
                    $"AudioClip.SetData に失敗しました（frames={frameCount} channels={wav.Channels} rate={wav.SampleRate}）。");
            }

            return clip;
        }
    }
}
