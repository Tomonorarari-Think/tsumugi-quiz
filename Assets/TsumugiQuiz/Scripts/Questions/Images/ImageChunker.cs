using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace TsumugiQuiz.Questions.Images
{
    /// <summary>
    /// 問題画像のバイト列を固定長のチャンクへ分割し、SHA-256 で完全性を検証するための純 C# ロジック
    /// （docs/network.md §8.3、#16）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 送信側（サーバー）は <see cref="ComputeHash"/> と <see cref="GetChunkCount"/> で
    /// メタ情報を作り、<see cref="CopyChunk"/> で 1 チャンクずつ取り出して送る。
    /// 受信側（クライアント）は <see cref="ImageAssembler"/> が組み立てとハッシュ照合を行う。
    /// </para>
    /// <para>
    /// 本クラスの引数検証は「呼び出し側の不具合」を早く見つけるためのもので、例外を投げる。
    /// ネットワークから届いた値の検証（敵対的な入力）は例外ではなく
    /// <see cref="ImageFailureReason"/> を返す <see cref="ImageAssembler"/> 側で行う。
    /// </para>
    /// </remarks>
    public static class ImageChunker
    {
        /// <summary>SHA-256 のバイト長。</summary>
        public const int Sha256ByteLength = 32;

        /// <summary>扱える画像の最大バイト数（docs/question-data.md §2 と同じ 2MB）。</summary>
        public const int MaxTotalBytes = QuestionLimits.MaxImageSizeBytes;

        private static readonly char[] HexDigits = "0123456789abcdef".ToCharArray();

        /// <summary>
        /// 指定した総バイト数を何チャンクに分割するかを返す。
        /// </summary>
        /// <param name="totalBytes">総バイト数（1 以上 <see cref="MaxTotalBytes"/> 以下）。</param>
        /// <param name="chunkSizeBytes">1 チャンクのバイト数（1 以上）。</param>
        /// <returns>チャンク数。</returns>
        /// <exception cref="ArgumentOutOfRangeException">引数が範囲外のとき。</exception>
        public static int GetChunkCount(int totalBytes, int chunkSizeBytes)
        {
            ValidateTotalBytes(totalBytes);
            ValidateChunkSize(chunkSizeBytes);

            // 切り上げ除算。totalBytes は MaxTotalBytes 以下なので加算で溢れない。
            return (totalBytes + chunkSizeBytes - 1) / chunkSizeBytes;
        }

        /// <summary>
        /// 指定したチャンクの長さ（末尾チャンクは端数）を返す。
        /// </summary>
        /// <param name="totalBytes">総バイト数。</param>
        /// <param name="chunkIndex">チャンク番号（0 始まり）。</param>
        /// <param name="chunkSizeBytes">1 チャンクのバイト数。</param>
        /// <returns>そのチャンクのバイト数。</returns>
        /// <exception cref="ArgumentOutOfRangeException">引数が範囲外のとき。</exception>
        public static int GetChunkLength(int totalBytes, int chunkIndex, int chunkSizeBytes)
        {
            var chunkCount = GetChunkCount(totalBytes, chunkSizeBytes);
            if (chunkIndex < 0 || chunkIndex >= chunkCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkIndex), chunkIndex, $"チャンク番号は 0 以上 {chunkCount} 未満である必要があります。");
            }

            var offset = (long)chunkIndex * chunkSizeBytes;
            var remaining = totalBytes - offset;
            return (int)Math.Min(chunkSizeBytes, remaining);
        }

        /// <summary>
        /// 指定したチャンクを新しい配列へ複製して返す（元の配列は変更しない）。
        /// </summary>
        /// <param name="source">画像のバイト列。</param>
        /// <param name="chunkIndex">チャンク番号（0 始まり）。</param>
        /// <param name="chunkSizeBytes">1 チャンクのバイト数。</param>
        /// <returns>チャンクのバイト列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> が null のとき。</exception>
        /// <exception cref="ArgumentOutOfRangeException">引数が範囲外のとき。</exception>
        public static byte[] CopyChunk(byte[] source, int chunkIndex, int chunkSizeBytes)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var length = GetChunkLength(source.Length, chunkIndex, chunkSizeBytes);
            var chunk = new byte[length];
            Buffer.BlockCopy(source, chunkIndex * chunkSizeBytes, chunk, 0, length);
            return chunk;
        }

        /// <summary>
        /// 指定したチャンクを、呼び出し側が用意したバッファへ複製する
        /// （1 チャンクごとに配列を作らないための版。送信経路で使う）。
        /// </summary>
        /// <param name="source">画像のバイト列。</param>
        /// <param name="chunkIndex">チャンク番号（0 始まり）。</param>
        /// <param name="chunkSizeBytes">1 チャンクのバイト数。</param>
        /// <param name="destination">
        /// 書き込み先。長さはそのチャンクの長さ（<see cref="GetChunkLength"/>）と一致している必要がある。
        /// </param>
        /// <exception cref="ArgumentNullException">引数が null のとき。</exception>
        /// <exception cref="ArgumentException"><paramref name="destination"/> の長さが合わないとき。</exception>
        /// <exception cref="ArgumentOutOfRangeException">引数が範囲外のとき。</exception>
        public static void CopyChunkInto(byte[] source, int chunkIndex, int chunkSizeBytes, byte[] destination)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            var length = GetChunkLength(source.Length, chunkIndex, chunkSizeBytes);
            if (destination.Length != length)
            {
                throw new ArgumentException(
                    $"書き込み先の長さがチャンク {chunkIndex} の長さ（{length}）と一致しません（実際: {destination.Length}）。",
                    nameof(destination));
            }

            Buffer.BlockCopy(source, chunkIndex * chunkSizeBytes, destination, 0, length);
        }

        /// <summary>
        /// 画像のバイト列を全チャンクへ分割する（テスト・小さな画像向け。
        /// 送信経路では <see cref="CopyChunkInto"/> を 1 チャンクずつ使い、複製を溜めない）。
        /// </summary>
        /// <param name="source">画像のバイト列。</param>
        /// <param name="chunkSizeBytes">1 チャンクのバイト数。</param>
        /// <returns>チャンクの一覧。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> が null のとき。</exception>
        /// <exception cref="ArgumentOutOfRangeException">引数が範囲外のとき。</exception>
        public static IReadOnlyList<byte[]> Split(byte[] source, int chunkSizeBytes)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var chunkCount = GetChunkCount(source.Length, chunkSizeBytes);
            var chunks = new byte[chunkCount][];
            for (var i = 0; i < chunkCount; i++)
            {
                chunks[i] = CopyChunk(source, i, chunkSizeBytes);
            }

            return chunks;
        }

        /// <summary>
        /// SHA-256 を計算する。
        /// </summary>
        /// <param name="source">対象のバイト列。</param>
        /// <returns>32 バイトのハッシュ。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> が null のとき。</exception>
        public static byte[] ComputeHash(byte[] source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(source);
            }
        }

        /// <summary>
        /// ハッシュ同士を比較する（null・長さ違いは不一致）。
        /// </summary>
        /// <param name="left">比較対象。</param>
        /// <param name="right">比較対象。</param>
        /// <returns>一致したら true。</returns>
        public static bool HashEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var diff = 0;
            for (var i = 0; i < left.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }

            return diff == 0;
        }

        /// <summary>ログ用に 16 進表記へ変換する（null は空文字）。</summary>
        /// <param name="hash">ハッシュ。</param>
        /// <returns>16 進表記。</returns>
        public static string ToHex(byte[] hash)
        {
            if (hash == null || hash.Length == 0)
            {
                return string.Empty;
            }

            var chars = new char[hash.Length * 2];
            for (var i = 0; i < hash.Length; i++)
            {
                chars[i * 2] = HexDigits[hash[i] >> 4];
                chars[(i * 2) + 1] = HexDigits[hash[i] & 0x0F];
            }

            return new string(chars);
        }

        private static void ValidateTotalBytes(int totalBytes)
        {
            if (totalBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalBytes), totalBytes, "画像のバイト数は 1 以上である必要があります。");
            }

            if (totalBytes > MaxTotalBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalBytes), totalBytes, $"画像のバイト数は {MaxTotalBytes} 以下である必要があります。");
            }
        }

        private static void ValidateChunkSize(int chunkSizeBytes)
        {
            if (chunkSizeBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkSizeBytes), chunkSizeBytes, "チャンクサイズは 1 以上である必要があります。");
            }
        }
    }
}
