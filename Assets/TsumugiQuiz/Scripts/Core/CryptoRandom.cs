using System;
using System.Security.Cryptography;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="RandomNumberGenerator"/> による暗号論的乱数の <see cref="IRandom"/> 実装（本番用）。
    /// docs/network.md §6.3 の「同着（差 &lt; 1ms）はサーバー側で暗号論的乱数による抽選」に対応する。
    /// 剰余バイアスを避けるため、採用区間を 2^32 の切り捨て境界までに限定した棄却サンプリングを行う。
    /// </summary>
    public sealed class CryptoRandom : IRandom, IDisposable
    {
        private const ulong UInt32Range = 0x1_0000_0000UL;

        private readonly RandomNumberGenerator _rng;
        private bool _disposed;

        /// <summary>既定の <see cref="RandomNumberGenerator"/> を使って生成する。</summary>
        public CryptoRandom()
            : this(RandomNumberGenerator.Create())
        {
        }

        /// <summary>乱数源を指定して生成する（このインスタンスが所有権を持ち、Dispose で破棄する）。</summary>
        public CryptoRandom(RandomNumberGenerator rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <inheritdoc />
        public int NextInt(int exclusiveMax)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CryptoRandom));
            }

            if (exclusiveMax < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveMax), exclusiveMax, "exclusiveMax は 1 以上である必要があります。");
            }

            if (exclusiveMax == 1)
            {
                return 0;
            }

            var range = (uint)exclusiveMax;

            // 2^32 を range で割った余りの分だけ上位を棄却すると、剰余を取っても一様になる。
            var remainder = (uint)(UInt32Range % range);
            var acceptableUpperBound = UInt32Range - remainder;

            var buffer = new byte[sizeof(uint)];
            while (true)
            {
                _rng.GetBytes(buffer);
                var value = BitConverter.ToUInt32(buffer, 0);
                if (value < acceptableUpperBound)
                {
                    return (int)(value % range);
                }
            }
        }

        /// <summary>乱数源を破棄する。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _rng.Dispose();
        }
    }
}
