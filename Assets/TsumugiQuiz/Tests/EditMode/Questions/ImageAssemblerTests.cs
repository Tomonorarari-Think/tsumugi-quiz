using System;
using NUnit.Framework;
using TsumugiQuiz.Questions.Images;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="ImageAssembler"/> の組み立て・欠落・改竄の検出（docs/network.md §8.4 / §9、#16）。
    /// </summary>
    public class ImageAssemblerTests
    {
        private const int ChunkSize = 256;
        private const int TotalBytes = (ChunkSize * 3) + 10;

        [Test]
        public void TryCreate_WithValidMeta_Succeeds()
        {
            var image = CreateImage(TotalBytes);

            Assert.IsTrue(
                ImageAssembler.TryCreate(
                    image.Length, 4, ChunkSize, ImageChunker.ComputeHash(image), out var assembler, out var reason));

            Assert.AreEqual(ImageFailureReason.None, reason);
            Assert.AreEqual(4, assembler.ChunkCount);
            Assert.AreEqual(TotalBytes, assembler.TotalBytes);
            Assert.AreEqual(0, assembler.ReceivedChunkCount);
            Assert.IsFalse(assembler.IsComplete);
        }

        [Test]
        public void TryCreate_WithNonPositiveTotalBytes_IsInvalidMeta()
        {
            Assert.IsFalse(ImageAssembler.TryCreate(0, 1, ChunkSize, new byte[32], out _, out var reason));
            Assert.AreEqual(ImageFailureReason.InvalidMeta, reason);

            Assert.IsFalse(ImageAssembler.TryCreate(-5, 1, ChunkSize, new byte[32], out _, out reason));
            Assert.AreEqual(ImageFailureReason.InvalidMeta, reason);
        }

        [Test]
        public void TryCreate_WithTotalBytesOverLimit_IsSizeExceeded()
        {
            // 上限超過はバッファを確保する前に弾く（docs/network.md §9）。
            var tooLarge = ImageChunker.MaxTotalBytes + 1;
            var chunkCount = (tooLarge + ChunkSize - 1) / ChunkSize;

            Assert.IsFalse(
                ImageAssembler.TryCreate(tooLarge, chunkCount, ChunkSize, new byte[32], out var assembler, out var reason));
            Assert.AreEqual(ImageFailureReason.SizeExceeded, reason);
            Assert.IsNull(assembler);
        }

        [Test]
        public void TryCreate_WithWrongHashLengthOrChunkCount_IsInvalidMeta()
        {
            Assert.IsFalse(ImageAssembler.TryCreate(TotalBytes, 4, ChunkSize, null, out _, out var reason));
            Assert.AreEqual(ImageFailureReason.InvalidMeta, reason);

            Assert.IsFalse(ImageAssembler.TryCreate(TotalBytes, 4, ChunkSize, new byte[16], out _, out reason));
            Assert.AreEqual(ImageFailureReason.InvalidMeta, reason);

            // 総バイト数から算出されるチャンク数（4）と違う値は受け付けない。
            Assert.IsFalse(ImageAssembler.TryCreate(TotalBytes, 3, ChunkSize, new byte[32], out _, out reason));
            Assert.AreEqual(ImageFailureReason.InvalidMeta, reason);

            Assert.IsFalse(ImageAssembler.TryCreate(TotalBytes, 4, 0, new byte[32], out _, out reason));
            Assert.AreEqual(ImageFailureReason.InvalidMeta, reason);
        }

        [Test]
        public void Accept_OutOfOrder_CompletesAndRestoresImage()
        {
            var image = CreateImage(TotalBytes);
            var assembler = Create(image);
            var chunks = ImageChunker.Split(image, ChunkSize);

            // 逆順で受け取っても組み立てられる（順序保証に依存しない）。
            for (var i = chunks.Count - 1; i >= 0; i--)
            {
                Assert.AreEqual(ImageFailureReason.None, assembler.Accept(i, chunks[i]));
            }

            Assert.IsTrue(assembler.IsComplete);
            Assert.AreEqual(chunks.Count, assembler.ReceivedChunkCount);
            Assert.IsTrue(assembler.TryGetImage(out var restored, out var reason));
            Assert.AreEqual(ImageFailureReason.None, reason);
            CollectionAssert.AreEqual(image, restored);
        }

        [Test]
        public void TryGetImage_ReturnsCopy_NotTheInternalBuffer()
        {
            var image = CreateImage(TotalBytes);
            var assembler = Create(image);
            AcceptAll(assembler, image);

            Assert.IsTrue(assembler.TryGetImage(out var first, out _));
            first[0] = (byte)(first[0] ^ 0xFF);

            Assert.IsTrue(assembler.TryGetImage(out var second, out _));
            Assert.AreEqual(image[0], second[0], "取り出したバイト列を書き換えても内部バッファは変わらないはず。");
        }

        [Test]
        public void Accept_WithIndexOutOfRange_IsInvalidChunk()
        {
            var image = CreateImage(TotalBytes);
            var assembler = Create(image);
            var chunks = ImageChunker.Split(image, ChunkSize);

            Assert.AreEqual(ImageFailureReason.InvalidChunk, assembler.Accept(-1, chunks[0]));
            Assert.AreEqual(ImageFailureReason.InvalidChunk, assembler.Accept(chunks.Count, chunks[0]));
            Assert.AreEqual(0, assembler.ReceivedChunkCount);
        }

        [Test]
        public void Accept_WithWrongLength_IsInvalidChunk()
        {
            var image = CreateImage(TotalBytes);
            var assembler = Create(image);

            Assert.AreEqual(ImageFailureReason.InvalidChunk, assembler.Accept(0, new byte[ChunkSize - 1]));
            Assert.AreEqual(ImageFailureReason.InvalidChunk, assembler.Accept(0, new byte[ChunkSize + 1]));
            Assert.AreEqual(ImageFailureReason.InvalidChunk, assembler.Accept(0, Array.Empty<byte>()));
            Assert.AreEqual(ImageFailureReason.InvalidChunk, assembler.Accept(0, null));

            // 末尾チャンクは端数（10 バイト）でなければならない。
            Assert.AreEqual(ImageFailureReason.InvalidChunk, assembler.Accept(3, new byte[ChunkSize]));
            Assert.AreEqual(0, assembler.ReceivedChunkCount);
        }

        [Test]
        public void Accept_DuplicateWithSameContent_IsIgnoredWithoutError()
        {
            var image = CreateImage(TotalBytes);
            var assembler = Create(image);
            var chunks = ImageChunker.Split(image, ChunkSize);

            Assert.AreEqual(ImageFailureReason.None, assembler.Accept(0, chunks[0]));
            Assert.AreEqual(
                ImageFailureReason.None,
                assembler.Accept(0, chunks[0]),
                "同じ内容の重複は（再送が重なったときに起きるため）無害として受け流す。");
            Assert.AreEqual(1, assembler.ReceivedChunkCount, "進捗は二重に数えない。");
        }

        [Test]
        public void Accept_DuplicateWithDifferentContent_IsDuplicateChunk()
        {
            var image = CreateImage(TotalBytes);
            var assembler = Create(image);
            var chunks = ImageChunker.Split(image, ChunkSize);

            Assert.AreEqual(ImageFailureReason.None, assembler.Accept(1, chunks[1]));

            var different = (byte[])chunks[1].Clone();
            different[0] = (byte)(different[0] ^ 0xFF);

            Assert.AreEqual(ImageFailureReason.DuplicateChunk, assembler.Accept(1, different));
        }

        [Test]
        public void TryGetImage_WithTamperedChunk_IsHashMismatch()
        {
            var image = CreateImage(TotalBytes);
            var assembler = Create(image);
            var chunks = ImageChunker.Split(image, ChunkSize);

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (i == 2)
                {
                    chunk = (byte[])chunk.Clone();
                    chunk[5] = (byte)(chunk[5] ^ 0xFF);
                }

                Assert.AreEqual(ImageFailureReason.None, assembler.Accept(i, chunk));
            }

            Assert.IsTrue(assembler.IsComplete, "チャンク数は揃っている（改竄は長さを変えない）。");
            Assert.IsFalse(assembler.TryGetImage(out var restored, out var reason));
            Assert.AreEqual(ImageFailureReason.HashMismatch, reason);
            Assert.IsNull(restored);
        }

        [Test]
        public void TryGetImage_WithMissingChunk_IsMissingChunk()
        {
            var image = CreateImage(TotalBytes);
            var assembler = Create(image);
            var chunks = ImageChunker.Split(image, ChunkSize);

            assembler.Accept(0, chunks[0]);
            assembler.Accept(3, chunks[3]);

            Assert.IsFalse(assembler.IsComplete);
            Assert.IsFalse(assembler.TryGetImage(out _, out var reason));
            Assert.AreEqual(ImageFailureReason.MissingChunk, reason);
            CollectionAssert.AreEqual(new[] { 1, 2 }, assembler.GetMissingChunks());
        }

        [Test]
        public void GetMissingChunks_RespectsMaxCount()
        {
            var image = CreateImage(TotalBytes);
            var assembler = Create(image);

            CollectionAssert.AreEqual(new[] { 0 }, assembler.GetMissingChunks(1));
            CollectionAssert.IsEmpty(assembler.GetMissingChunks(0));
            Assert.AreEqual(4, assembler.GetMissingChunks().Count, "既定では全件（上限 32 件まで）返す。");
        }

        private static ImageAssembler Create(byte[] image)
        {
            Assert.IsTrue(
                ImageAssembler.TryCreate(
                    image.Length,
                    ImageChunker.GetChunkCount(image.Length, ChunkSize),
                    ChunkSize,
                    ImageChunker.ComputeHash(image),
                    out var assembler,
                    out _),
                "テストの前提としてメタ情報は妥当であるはず。");
            return assembler;
        }

        private static void AcceptAll(ImageAssembler assembler, byte[] image)
        {
            var chunks = ImageChunker.Split(image, ChunkSize);
            for (var i = 0; i < chunks.Count; i++)
            {
                Assert.AreEqual(ImageFailureReason.None, assembler.Accept(i, chunks[i]));
            }
        }

        private static byte[] CreateImage(int length)
        {
            var bytes = new byte[length];
            var random = new Random(length);
            random.NextBytes(bytes);
            return bytes;
        }
    }
}
