namespace TsumugiQuiz.Questions.Images
{
    /// <summary>
    /// バイト列の先頭から画像の形式と解像度を読み取る（デコードはしない、#16）。
    /// </summary>
    /// <remarks>
    /// 受信したバイト列を <c>Texture2D.LoadImage</c> に渡す**前**に、
    /// PNG / JPG であることと解像度が上限（<see cref="QuestionLimits.MaxImageDimension"/>）以内であることを
    /// 確かめるために使う。圧縮率の高い巨大画像でメモリを食い潰されないための入力検証
    /// （docs/network.md §9）であり、拡張子（送信側の検証）とは独立に中身で判断する。
    /// </remarks>
    public static class ImageFormatProbe
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>PNG の IHDR（幅・高さ）が始まるオフセット。</summary>
        private const int PngWidthOffset = 16;

        /// <summary>PNG のヘッダを読むのに最低限必要なバイト数（署名 + IHDR の幅・高さまで）。</summary>
        private const int PngMinimumLength = 24;

        /// <summary>
        /// 形式と解像度を読み取る。
        /// </summary>
        /// <param name="data">画像のバイト列。</param>
        /// <param name="format">判定した形式。</param>
        /// <param name="width">幅（ピクセル）。判定できなければ 0。</param>
        /// <param name="height">高さ（ピクセル）。判定できなければ 0。</param>
        /// <returns>形式と解像度を読み取れたら true。</returns>
        public static bool TryProbe(byte[] data, out ImageFormat format, out int width, out int height)
        {
            format = ImageFormat.Unknown;
            width = 0;
            height = 0;

            if (data == null)
            {
                return false;
            }

            if (StartsWithPngSignature(data))
            {
                format = ImageFormat.Png;
                return TryReadPngSize(data, out width, out height);
            }

            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                format = ImageFormat.Jpeg;
                return TryReadJpegSize(data, out width, out height);
            }

            return false;
        }

        /// <summary>
        /// 受信した画像として受け入れてよいかを判定する。
        /// </summary>
        /// <param name="data">画像のバイト列。</param>
        /// <param name="reason">
        /// 受け入れられない理由。受け入れられるときは <see cref="ImageFailureReason.None"/>。
        /// </param>
        /// <returns>PNG / JPG かつ解像度が上限以内なら true。</returns>
        public static bool TryValidate(byte[] data, out ImageFailureReason reason)
        {
            if (data == null || data.Length == 0)
            {
                reason = ImageFailureReason.DecodeFailed;
                return false;
            }

            if (data.Length > QuestionLimits.MaxImageSizeBytes)
            {
                reason = ImageFailureReason.SizeExceeded;
                return false;
            }

            if (!TryProbe(data, out _, out var width, out var height))
            {
                reason = ImageFailureReason.DecodeFailed;
                return false;
            }

            if (width <= 0
                || height <= 0
                || width > QuestionLimits.MaxImageDimension
                || height > QuestionLimits.MaxImageDimension)
            {
                reason = ImageFailureReason.DecodeFailed;
                return false;
            }

            reason = ImageFailureReason.None;
            return true;
        }

        private static bool StartsWithPngSignature(byte[] data)
        {
            if (data.Length < PngSignature.Length)
            {
                return false;
            }

            for (var i = 0; i < PngSignature.Length; i++)
            {
                if (data[i] != PngSignature[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadPngSize(byte[] data, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (data.Length < PngMinimumLength)
            {
                return false;
            }

            // 8..11 = IHDR チャンクの長さ、12..15 = "IHDR"、16..19 = 幅、20..23 = 高さ（いずれもビッグエンディアン）。
            if (data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R')
            {
                return false;
            }

            var rawWidth = ReadUInt32BigEndian(data, PngWidthOffset);
            var rawHeight = ReadUInt32BigEndian(data, PngWidthOffset + 4);

            // int で扱えない値（2^31 以上）は不正として扱う。
            if (rawWidth > int.MaxValue || rawHeight > int.MaxValue)
            {
                return false;
            }

            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
        }

        /// <summary>
        /// JPEG のマーカーを順に辿り、SOFn セグメントから解像度を読む。
        /// </summary>
        private static bool TryReadJpegSize(byte[] data, out int width, out int height)
        {
            width = 0;
            height = 0;

            // SOI（FFD8）の直後から走査する。
            var offset = 2;
            while (offset + 3 < data.Length)
            {
                if (data[offset] != 0xFF)
                {
                    // マーカー境界を見失った（壊れたデータ）。
                    return false;
                }

                var marker = data[offset + 1];
                offset += 2;

                // フィルバイト（FF の連続）は読み飛ばす。
                if (marker == 0xFF)
                {
                    offset--;
                    continue;
                }

                // 長さを持たないマーカー（RSTn / SOI / EOI / TEM）。
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
                {
                    continue;
                }

                if (offset + 1 >= data.Length)
                {
                    return false;
                }

                var segmentLength = (data[offset] << 8) | data[offset + 1];
                if (segmentLength < 2 || offset + segmentLength > data.Length)
                {
                    return false;
                }

                if (IsStartOfFrame(marker))
                {
                    // セグメント: 長さ(2) 精度(1) 高さ(2) 幅(2)
                    if (segmentLength < 7)
                    {
                        return false;
                    }

                    height = (data[offset + 3] << 8) | data[offset + 4];
                    width = (data[offset + 5] << 8) | data[offset + 6];
                    return true;
                }

                if (marker == 0xDA)
                {
                    // SOS 以降は圧縮データなので、ここまでで SOFn が無ければ諦める。
                    return false;
                }

                offset += segmentLength;
            }

            return false;
        }

        /// <summary>SOF0〜SOF15（DHT / JPG / DAC を除く）か。</summary>
        private static bool IsStartOfFrame(byte marker)
        {
            if (marker < 0xC0 || marker > 0xCF)
            {
                return false;
            }

            // C4 = DHT、C8 = JPG（予約）、CC = DAC はフレームヘッダではない。
            return marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
        }

        private static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24)
                | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8)
                | data[offset + 3];
        }
    }
}
