using System;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Images;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="ImageChunker"/> の分割・復元・ハッシュ（docs/network.md §8.3、#16）。
    /// </summary>
    public class ImageChunkerTests
    {
        private const int ChunkSize = 1024;

        /// <summary>SHA-256 の既知のテストベクタ（空のバイト列）。</summary>
        private const string EmptySha256Hex =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        [Test]
        public void GetChunkCount_AtChunkBoundaries_RoundsUp()
        {
            Assert.AreEqual(1, ImageChunker.GetChunkCount(1, ChunkSize), "1 バイトでも 1 チャンク。");
            Assert.AreEqual(1, ImageChunker.GetChunkCount(ChunkSize, ChunkSize), "ちょうど 1 チャンク。");
            Assert.AreEqual(2, ImageChunker.GetChunkCount(ChunkSize + 1, ChunkSize), "1 バイト超えたら 2 チャンク。");
            Assert.AreEqual(3, ImageChunker.GetChunkCount((ChunkSize * 2) + 1, ChunkSize));
        }

        [Test]
        public void GetChunkCount_For2MbWith16KbChunks_Is128()
        {
            // docs/network.md §8.3: 2MB の画像は 16KB チャンクで 128 チャンクになる。
            Assert.AreEqual(128, ImageChunker.GetChunkCount(QuestionLimits.MaxImageSizeBytes, 16 * 1024));
        }

        [Test]
        public void GetChunkCount_WithInvalidArguments_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageChunker.GetChunkCount(0, ChunkSize));
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageChunker.GetChunkCount(-1, ChunkSize));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ImageChunker.GetChunkCount(ImageChunker.MaxTotalBytes + 1, ChunkSize),
                "2MB を超える画像は扱わない（docs/question-data.md §2）。");
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageChunker.GetChunkCount(100, 0));
        }

        [Test]
        public void GetChunkLength_LastChunk_IsRemainder()
        {
            Assert.AreEqual(ChunkSize, ImageChunker.GetChunkLength((ChunkSize * 2) + 5, 0, ChunkSize));
            Assert.AreEqual(ChunkSize, ImageChunker.GetChunkLength((ChunkSize * 2) + 5, 1, ChunkSize));
            Assert.AreEqual(5, ImageChunker.GetChunkLength((ChunkSize * 2) + 5, 2, ChunkSize));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ImageChunker.GetChunkLength((ChunkSize * 2) + 5, 3, ChunkSize));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ImageChunker.GetChunkLength((ChunkSize * 2) + 5, -1, ChunkSize));
        }

        [TestCase(1)]
        [TestCase(ChunkSize - 1)]
        [TestCase(ChunkSize)]
        [TestCase(ChunkSize + 1)]
        [TestCase((ChunkSize * 3) + 7)]
        public void Split_ThenConcatenate_RestoresOriginal(int totalBytes)
        {
            var original = CreateBytes(totalBytes);

            var chunks = ImageChunker.Split(original, ChunkSize);

            Assert.AreEqual(ImageChunker.GetChunkCount(totalBytes, ChunkSize), chunks.Count);

            var restored = new byte[totalBytes];
            var offset = 0;
            for (var i = 0; i < chunks.Count; i++)
            {
                Assert.AreEqual(
                    ImageChunker.GetChunkLength(totalBytes, i, ChunkSize),
                    chunks[i].Length,
                    $"チャンク {i} の長さが期待と違う。");
                Buffer.BlockCopy(chunks[i], 0, restored, offset, chunks[i].Length);
                offset += chunks[i].Length;
            }

            Assert.AreEqual(totalBytes, offset, "チャンクの合計長が元のバイト数と一致するはず。");
            CollectionAssert.AreEqual(original, restored);
            Assert.IsTrue(
                ImageChunker.HashEquals(ImageChunker.ComputeHash(original), ImageChunker.ComputeHash(restored)));
        }

        [Test]
        public void CopyChunk_ReturnsCopy_NotAViewIntoTheSource()
        {
            var original = CreateBytes(ChunkSize * 2);

            var chunk = ImageChunker.CopyChunk(original, 0, ChunkSize);
            chunk[0] = (byte)(chunk[0] ^ 0xFF);

            Assert.AreNotEqual(chunk[0], original[0], "取り出したチャンクを書き換えても元の配列は変わらないはず。");
        }

        [Test]
        public void CopyChunkInto_FillsCallerBuffer_AndRejectsWrongLength()
        {
            var original = CreateBytes(ChunkSize + 7);
            var full = new byte[ChunkSize];
            var tail = new byte[7];

            ImageChunker.CopyChunkInto(original, 0, ChunkSize, full);
            ImageChunker.CopyChunkInto(original, 1, ChunkSize, tail);

            CollectionAssert.AreEqual(ImageChunker.CopyChunk(original, 0, ChunkSize), full);
            CollectionAssert.AreEqual(ImageChunker.CopyChunk(original, 1, ChunkSize), tail);

            // 長さが合わないバッファは受け付けない（使い回しバッファの取り違えを早く見つけるため）。
            Assert.Throws<ArgumentException>(
                () => ImageChunker.CopyChunkInto(original, 1, ChunkSize, new byte[ChunkSize]));
            Assert.Throws<ArgumentNullException>(
                () => ImageChunker.CopyChunkInto(original, 0, ChunkSize, null));
            Assert.Throws<ArgumentNullException>(
                () => ImageChunker.CopyChunkInto(null, 0, ChunkSize, full));
        }

        [Test]
        public void CopyChunk_WithNullSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ImageChunker.CopyChunk(null, 0, ChunkSize));
            Assert.Throws<ArgumentNullException>(() => ImageChunker.Split(null, ChunkSize));
            Assert.Throws<ArgumentNullException>(() => ImageChunker.ComputeHash(null));
        }

        [Test]
        public void ComputeHash_IsDeterministic_And32Bytes()
        {
            var bytes = CreateBytes(5000);

            var first = ImageChunker.ComputeHash(bytes);
            var second = ImageChunker.ComputeHash(bytes);

            Assert.AreEqual(ImageChunker.Sha256ByteLength, first.Length);
            Assert.IsTrue(ImageChunker.HashEquals(first, second));
        }

        [Test]
        public void ComputeHash_OfEmptyArray_MatchesKnownVector()
        {
            // 実装（SHA-256）そのものが期待どおりであることを既知のテストベクタで固定する。
            Assert.AreEqual(EmptySha256Hex, ImageChunker.ToHex(ImageChunker.ComputeHash(Array.Empty<byte>())));
        }

        [Test]
        public void ComputeHash_DetectsSingleBitTampering()
        {
            var bytes = CreateBytes(4096);
            var hash = ImageChunker.ComputeHash(bytes);

            var tampered = (byte[])bytes.Clone();
            tampered[2048] = (byte)(tampered[2048] ^ 0x01);

            Assert.IsFalse(
                ImageChunker.HashEquals(hash, ImageChunker.ComputeHash(tampered)),
                "1 ビットの改竄でもハッシュは変わるはず。");
        }

        [Test]
        public void HashEquals_WithNullOrDifferentLength_IsFalse()
        {
            var hash = ImageChunker.ComputeHash(CreateBytes(16));

            Assert.IsFalse(ImageChunker.HashEquals(hash, null));
            Assert.IsFalse(ImageChunker.HashEquals(null, hash));
            Assert.IsFalse(ImageChunker.HashEquals(null, null));
            Assert.IsFalse(ImageChunker.HashEquals(hash, new byte[] { 1, 2, 3 }));
            Assert.IsTrue(ImageChunker.HashEquals(hash, (byte[])hash.Clone()));
        }

        [Test]
        public void ToHex_WithNullOrEmpty_IsEmptyString()
        {
            Assert.AreEqual(string.Empty, ImageChunker.ToHex(null));
            Assert.AreEqual(string.Empty, ImageChunker.ToHex(Array.Empty<byte>()));
            Assert.AreEqual("00ff10", ImageChunker.ToHex(new byte[] { 0x00, 0xFF, 0x10 }));
        }

        /// <summary>決定的な擬似ランダム列（同じ長さなら常に同じ内容）。</summary>
        private static byte[] CreateBytes(int length)
        {
            var bytes = new byte[length];
            var random = new Random(length);
            random.NextBytes(bytes);
            return bytes;
        }
    }
}
