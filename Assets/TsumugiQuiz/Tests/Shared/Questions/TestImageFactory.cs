using UnityEngine;

namespace TsumugiQuiz.Tests.Shared.Questions
{
    /// <summary>
    /// 画像関連のテストで使う PNG / JPG のバイト列を実際に生成するヘルパー（#16）。
    /// EditMode / PlayMode の両方から使うため <c>Tests/Shared</c> に置く
    /// （docs/tasks/impl-rules.md「コード配置規約」、PR #93 レビュー M5）。
    /// </summary>
    /// <remarks>
    /// 固定シードのノイズを詰めるのは、圧縮が効きすぎて意図したバイト数にならないのを避けるため。
    /// </remarks>
    internal static class TestImageFactory
    {
        /// <summary>PNG のバイト列を作る。</summary>
        /// <param name="width">幅（ピクセル）。</param>
        /// <param name="height">高さ（ピクセル）。</param>
        /// <param name="seed">ノイズのシード（同じ値なら同じ画像）。</param>
        /// <returns>PNG のバイト列。</returns>
        public static byte[] CreatePng(int width, int height, int seed = 1234)
        {
            var texture = CreateNoiseTexture(width, height, seed);
            try
            {
                return texture.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>JPG のバイト列を作る。</summary>
        /// <param name="width">幅（ピクセル）。</param>
        /// <param name="height">高さ（ピクセル）。</param>
        /// <param name="seed">ノイズのシード。</param>
        /// <returns>JPG のバイト列。</returns>
        public static byte[] CreateJpg(int width, int height, int seed = 1234)
        {
            var texture = CreateNoiseTexture(width, height, seed);
            try
            {
                return texture.EncodeToJPG(85);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// 指定した解像度を主張する PNG のヘッダだけを組み立てる（実体としては不正な PNG）。
        /// 解像度の上限検証を、巨大な画像を実際に作らずに確かめるために使う。
        /// </summary>
        /// <param name="width">IHDR に書き込む幅。</param>
        /// <param name="height">IHDR に書き込む高さ。</param>
        /// <returns>署名 + IHDR の幅・高さまでを含む 24 バイト。</returns>
        public static byte[] CreatePngHeaderOnly(int width, int height)
        {
            var bytes = new byte[24];
            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            signature.CopyTo(bytes, 0);

            // 8..11 = IHDR の長さ（13）
            bytes[11] = 13;

            bytes[12] = (byte)'I';
            bytes[13] = (byte)'H';
            bytes[14] = (byte)'D';
            bytes[15] = (byte)'R';

            WriteBigEndian(bytes, 16, width);
            WriteBigEndian(bytes, 20, height);
            return bytes;
        }

        private static Texture2D CreateNoiseTexture(int width, int height, int seed)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
            var random = new System.Random(seed);
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(
                    (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), byte.MaxValue);
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);
            return texture;
        }

        private static void WriteBigEndian(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)((value >> 24) & 0xFF);
            bytes[offset + 1] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 2] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 3] = (byte)(value & 0xFF);
        }
    }
}
