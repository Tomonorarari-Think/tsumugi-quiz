using TsumugiQuiz.Core.Audio;
using UnityEngine;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// <see cref="TtsService"/> のうち、ディスクキャッシュ（<see cref="TtsCache"/>）を介した
    /// 読み込み・全消しをまとめた部分（#140）。キャッシュの実体・保存先仕様は
    /// <see cref="TtsCache"/>（docs/tts.md §7）を参照。
    /// </summary>
    public sealed partial class TtsService
    {
        /// <summary>キャッシュを全消しする（設定画面の「キャッシュをクリア」、docs/tts.md §7.3）。</summary>
        public void ClearCache() => Cache?.Clear();

        /// <summary>キャッシュから読んで解析する。読めない・壊れている場合は null（合成にフォールバック）。</summary>
        private WavData? TryLoadFromCache(string key)
        {
            if (!Cache.TryGet(key, out var wav, out _)) return null;

            try
            {
                return WavParser.ParseWav(wav);
            }
            catch (WavFormatException e)
            {
                Debug.LogWarning($"[TtsService] キャッシュの WAV が壊れていました（key={key}）。作り直します: {e.Message}");
                Cache.Invalidate(key);
                return null;
            }
        }
    }
}
