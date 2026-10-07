namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// 読み上げ速度（ルーム設定 <c>tts.speed</c>、docs/room-settings.md §1・docs/tts.md §6.4）の範囲。
    ///
    /// キャッシュキーには<b>クランプ後</b>の値を使う（範囲外の値で別キーが生まれるのを防ぐため）。
    /// <c>AudioSource.pitch</c> で速度を変えてはいけない（声の高さが変わり、durationSec が全員でずれる）。
    /// </summary>
    public static class TtsSpeed
    {
        /// <summary>既定値。</summary>
        public const float Default = 1.0f;

        /// <summary>下限。</summary>
        public const float Min = 0.5f;

        /// <summary>上限。</summary>
        public const float Max = 2.0f;

        /// <summary>
        /// 一括合成（<c>voicevox_synthesizer_tts</c>）で済ませられるとみなす許容差。
        /// これを超える場合は AudioQuery 経由の 2 段系統を使う（docs/tts.md §6.4）。
        /// </summary>
        public const float DefaultTolerance = 0.001f;

        /// <summary>範囲内に丸める。NaN は既定値にする。</summary>
        public static float Clamp(float speed)
        {
            if (float.IsNaN(speed)) return Default;
            if (speed < Min) return Min;
            if (speed > Max) return Max;
            return speed;
        }

        /// <summary>速度が 1.0 とみなせるか（一括合成のショートカット判定）。</summary>
        public static bool IsDefault(float speed) => System.Math.Abs(speed - Default) <= DefaultTolerance;
    }
}
