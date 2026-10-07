using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TsumugiQuiz.Tts.Native
{
    /// <summary>
    /// ネイティブ API に渡す UTF-8 文字列のヘルパ（docs/tts-native-api.md §2.1）。
    /// DllImport の既定マーシャリングは ANSI なので、日本語を渡すには必ずここを経由する。
    /// </summary>
    internal static class Utf8
    {
        /// <summary>UTF-8 のヌル終端バイト列に変換する（P/Invoke の in 文字列用）。</summary>
        /// <exception cref="ArgumentNullException"><paramref name="s"/> が null</exception>
        /// <exception cref="ArgumentException">
        /// 埋め込み NUL（U+0000）を含むとき。ネイティブ側では途中で文字列が切れてしまい、
        /// 意図しないテキストが合成されるため、境界で弾く。
        /// </exception>
        public static byte[] ToNullTerminated(string s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("文字列に NUL 文字（U+0000）を含められません。", nameof(s));
            }

            var len = Encoding.UTF8.GetByteCount(s);
            var bytes = new byte[len + 1];
            if (len > 0)
            {
                Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
            }
            bytes[len] = 0;
            return bytes;
        }

        /// <summary>ネイティブの const char*（UTF-8 ヌル終端）を string にする。NULL なら空文字列。</summary>
        public static string FromPtr(IntPtr p)
        {
            if (p == IntPtr.Zero) return string.Empty;

            var len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            if (len == 0) return string.Empty;

            var buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }
    }
}
