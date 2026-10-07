using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Questions.Images
{
    /// <summary>
    /// 受信したチャンクを組み立て、SHA-256 で完全性を確かめる（docs/network.md §8.4、#16）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ネットワークから届く値だけを相手にするため、**例外を投げず** 不正は
    /// <see cref="ImageFailureReason"/> として返す（docs/network.md §9）。
    /// 呼び出し側はその理由をそのまま NAK（<c>ImageFailedRpc</c>）に載せる。
    /// </para>
    /// <para>
    /// 受信の途中経過を持つ都合上、本クラスだけは可変（1 枚の画像 = 1 インスタンス）。
    /// 取り出せるのは完成したバイト列の複製だけで、内部バッファは公開しない。
    /// </para>
    /// </remarks>
    public sealed class ImageAssembler
    {
        /// <summary>欠落チャンク番号を報告する最大件数（docs/network.md §8.4）。</summary>
        public const int MaxReportedMissingChunks = 32;

        private readonly byte[] _buffer;
        private readonly bool[] _received;
        private readonly byte[] _expectedHash;
        private readonly int _chunkSizeBytes;

        private int _receivedChunkCount;

        private ImageAssembler(int totalBytes, int chunkCount, int chunkSizeBytes, byte[] expectedHash)
        {
            _buffer = new byte[totalBytes];
            _received = new bool[chunkCount];
            _chunkSizeBytes = chunkSizeBytes;
            _expectedHash = (byte[])expectedHash.Clone();
            TotalBytes = totalBytes;
            ChunkCount = chunkCount;
        }

        /// <summary>画像の総バイト数。</summary>
        public int TotalBytes { get; }

        /// <summary>チャンク数。</summary>
        public int ChunkCount { get; }

        /// <summary>受信済みのチャンク数。</summary>
        public int ReceivedChunkCount => _receivedChunkCount;

        /// <summary>全チャンクが揃ったか（ハッシュ照合はまだ）。</summary>
        public bool IsComplete => _receivedChunkCount == ChunkCount;

        /// <summary>
        /// メタ情報を検証して組み立て器を作る。
        /// </summary>
        /// <param name="totalBytes">総バイト数。</param>
        /// <param name="chunkCount">チャンク数。</param>
        /// <param name="chunkSizeBytes">1 チャンクのバイト数（送受信で共通の定数）。</param>
        /// <param name="expectedHash">期待する SHA-256（32 バイト）。</param>
        /// <param name="assembler">作成した組み立て器。失敗時は null。</param>
        /// <param name="reason">失敗理由。成功時は <see cref="ImageFailureReason.None"/>。</param>
        /// <returns>作成できたら true。</returns>
        public static bool TryCreate(
            int totalBytes,
            int chunkCount,
            int chunkSizeBytes,
            byte[] expectedHash,
            out ImageAssembler assembler,
            out ImageFailureReason reason)
        {
            assembler = null;

            if (chunkSizeBytes <= 0)
            {
                reason = ImageFailureReason.InvalidMeta;
                return false;
            }

            if (totalBytes <= 0)
            {
                reason = ImageFailureReason.InvalidMeta;
                return false;
            }

            if (totalBytes > ImageChunker.MaxTotalBytes)
            {
                // 上限を超える総バイト数では 1 バイトも確保しない（docs/network.md §9）。
                reason = ImageFailureReason.SizeExceeded;
                return false;
            }

            if (expectedHash == null || expectedHash.Length != ImageChunker.Sha256ByteLength)
            {
                reason = ImageFailureReason.InvalidMeta;
                return false;
            }

            if (chunkCount != ImageChunker.GetChunkCount(totalBytes, chunkSizeBytes))
            {
                reason = ImageFailureReason.InvalidMeta;
                return false;
            }

            assembler = new ImageAssembler(totalBytes, chunkCount, chunkSizeBytes, expectedHash);
            reason = ImageFailureReason.None;
            return true;
        }

        /// <summary>
        /// チャンクを受け入れる。
        /// </summary>
        /// <param name="chunkIndex">チャンク番号（0 始まり）。</param>
        /// <param name="data">チャンクのバイト列。</param>
        /// <returns>
        /// 受け入れたら <see cref="ImageFailureReason.None"/>。
        /// 同じ内容の重複も（進捗は増やさずに）<see cref="ImageFailureReason.None"/> を返す。
        /// </returns>
        public ImageFailureReason Accept(int chunkIndex, byte[] data)
        {
            if (chunkIndex < 0 || chunkIndex >= ChunkCount)
            {
                return ImageFailureReason.InvalidChunk;
            }

            if (data == null || data.Length == 0)
            {
                return ImageFailureReason.InvalidChunk;
            }

            var expectedLength = ImageChunker.GetChunkLength(TotalBytes, chunkIndex, _chunkSizeBytes);
            if (data.Length != expectedLength)
            {
                return ImageFailureReason.InvalidChunk;
            }

            var offset = chunkIndex * _chunkSizeBytes;

            if (_received[chunkIndex])
            {
                // 再送が重なった場合など、同じ内容の重複は無害なので受け流す（NAK の連鎖を避ける）。
                // 内容が違う重複は改竄・実装不整合なので不正として返す。
                return SegmentEquals(offset, data) ? ImageFailureReason.None : ImageFailureReason.DuplicateChunk;
            }

            Buffer.BlockCopy(data, 0, _buffer, offset, data.Length);
            _received[chunkIndex] = true;
            _receivedChunkCount++;
            return ImageFailureReason.None;
        }

        /// <summary>
        /// 完成した画像のバイト列を取り出す（複製を返す）。
        /// </summary>
        /// <param name="image">画像のバイト列。失敗時は null。</param>
        /// <param name="reason">失敗理由。成功時は <see cref="ImageFailureReason.None"/>。</param>
        /// <returns>全チャンクが揃い、ハッシュも一致したら true。</returns>
        public bool TryGetImage(out byte[] image, out ImageFailureReason reason)
        {
            image = null;

            if (!IsComplete)
            {
                reason = ImageFailureReason.MissingChunk;
                return false;
            }

            if (!ImageChunker.HashEquals(ImageChunker.ComputeHash(_buffer), _expectedHash))
            {
                reason = ImageFailureReason.HashMismatch;
                return false;
            }

            image = (byte[])_buffer.Clone();
            reason = ImageFailureReason.None;
            return true;
        }

        /// <summary>
        /// まだ届いていないチャンク番号を返す（先頭から最大 <paramref name="maxCount"/> 件）。
        /// </summary>
        /// <param name="maxCount">返す最大件数。既定は <see cref="MaxReportedMissingChunks"/>。</param>
        /// <returns>欠落しているチャンク番号。</returns>
        public IReadOnlyList<int> GetMissingChunks(int maxCount = MaxReportedMissingChunks)
        {
            if (maxCount <= 0)
            {
                return Array.Empty<int>();
            }

            var missing = new List<int>();
            for (var i = 0; i < _received.Length && missing.Count < maxCount; i++)
            {
                if (!_received[i])
                {
                    missing.Add(i);
                }
            }

            return missing;
        }

        private bool SegmentEquals(int offset, byte[] data)
        {
            for (var i = 0; i < data.Length; i++)
            {
                if (_buffer[offset + i] != data[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
