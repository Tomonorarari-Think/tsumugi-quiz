using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// 合成結果キャッシュのキー生成（docs/tts.md §7.1、仮決め K16）。
    ///
    /// <code>
    /// key = SHA-256( readingText + "\0" + styleName + "\0" + speakerName
    ///                + "\0" + speed.ToString("F2", InvariantCulture)
    ///                + "\0" + coreVersion + "\0" + modelsVersion )
    ///       の先頭 16 バイトを小文字 hex（32 文字）
    /// </code>
    ///
    /// 注意点:
    /// <list type="bullet">
    ///   <item><description>区切りに NUL を入れる。区切りなしでは ("あい","う") と ("あ","いう") が同じキーになる</description></item>
    ///   <item><description>スタイル ID ではなく話者名・スタイル名を使う（ID は VVM のバージョンで変わる）</description></item>
    ///   <item><description>coreVersion / modelsVersion を含める（更新すると同じ入力でも波形が変わる）</description></item>
    ///   <item><description>連結順は readingText → styleName → speakerName → speed → coreVersion → modelsVersion で固定</description></item>
    /// </list>
    ///
    /// Unity API に依存しない純 C# なので EditMode テストで検証できる。
    /// </summary>
    public static class TtsCacheKey
    {
        /// <summary>キーの hex 文字数。</summary>
        public const int HexLength = 32;

        /// <summary>ハッシュから使う先頭バイト数（16 バイト = 128bit）。</summary>
        public const int UsedHashBytes = HexLength / 2;

        /// <summary>連結の区切り文字（NUL）。</summary>
        public const char Separator = '\0';

        /// <summary>キーのサブディレクトリに使う先頭文字数（docs/tts.md §7.2）。</summary>
        public const int ShardLength = 2;

        /// <summary>速度の書式。丸めによって 1.005 と 1.0049 が同じキーになるのは許容する（合成結果もほぼ同じ）。</summary>
        private const string SpeedFormat = "F2";

        /// <summary>
        /// キャッシュキーを求める。
        /// </summary>
        /// <param name="readingText">読み上げテキスト（空なら問題文を使うのは呼び出し側の責務）</param>
        /// <param name="styleName">スタイル名（解決後の実際の値を渡すこと）</param>
        /// <param name="speakerName">話者名（解決後の実際の値を渡すこと）</param>
        /// <param name="speed">読み上げ速度（0.5〜2.0）</param>
        /// <param name="coreVersion"><c>voicevox_get_version()</c> の値</param>
        /// <param name="modelsVersion">音声モデルのバージョン</param>
        /// <exception cref="ArgumentNullException"><paramref name="readingText"/> が null</exception>
        /// <exception cref="ArgumentException"><paramref name="speakerName"/> / <paramref name="styleName"/> が空</exception>
        public static string Compute(
            string readingText, string styleName, string speakerName,
            float speed, string coreVersion, string modelsVersion)
        {
            if (readingText == null) throw new ArgumentNullException(nameof(readingText));
            if (string.IsNullOrEmpty(styleName)) throw new ArgumentException("スタイル名が空です。", nameof(styleName));
            if (string.IsNullOrEmpty(speakerName)) throw new ArgumentException("話者名が空です。", nameof(speakerName));

            var builder = new StringBuilder(readingText.Length + 64);
            builder.Append(readingText).Append(Separator)
                   .Append(styleName).Append(Separator)
                   .Append(speakerName).Append(Separator)
                   .Append(speed.ToString(SpeedFormat, CultureInfo.InvariantCulture)).Append(Separator)
                   .Append(coreVersion ?? string.Empty).Append(Separator)
                   .Append(modelsVersion ?? string.Empty);

            var source = Encoding.UTF8.GetBytes(builder.ToString());
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(source);
            }

            return ToLowerHex(hash, UsedHashBytes);
        }

        /// <summary>キーとして妥当な形（32 文字の小文字 hex）かどうか。走査で拾ったファイル名の検証に使う。</summary>
        public static bool IsValid(string key)
        {
            if (key == null || key.Length != HexLength) return false;
            foreach (var c in key)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!isHex) return false;
            }
            return true;
        }

        /// <summary>キーのサブディレクトリ名（先頭 2 文字、docs/tts.md §7.2）。</summary>
        /// <exception cref="ArgumentException">キーが妥当でないとき</exception>
        public static string GetShard(string key)
        {
            if (!IsValid(key)) throw new ArgumentException($"キャッシュキーの形式が不正です: {key}", nameof(key));
            return key.Substring(0, ShardLength);
        }

        private static string ToLowerHex(byte[] bytes, int count)
        {
            const string Digits = "0123456789abcdef";
            var chars = new char[count * 2];
            for (var i = 0; i < count; i++)
            {
                chars[i * 2] = Digits[bytes[i] >> 4];
                chars[(i * 2) + 1] = Digits[bytes[i] & 0x0F];
            }
            return new string(chars);
        }
    }
}
