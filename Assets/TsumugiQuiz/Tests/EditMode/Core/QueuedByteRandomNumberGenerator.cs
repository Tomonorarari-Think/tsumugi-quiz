using System;
using System.Security.Cryptography;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// あらかじめ与えた 32bit 値を順にバイト列として返す <see cref="RandomNumberGenerator"/> スタブ。
    /// <c>CryptoRandom</c> の棄却サンプリング経路を決定的に検証するために使う。
    /// </summary>
    public sealed class QueuedByteRandomNumberGenerator : RandomNumberGenerator
    {
        private readonly uint[] _values;
        private int _index;

        public QueuedByteRandomNumberGenerator(params uint[] values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        /// <summary>バイト列を要求された回数。</summary>
        public int CallCount { get; private set; }

        public override void GetBytes(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            CallCount++;

            if (_index >= _values.Length)
            {
                throw new InvalidOperationException("QueuedByteRandomNumberGenerator に用意した値が尽きました。");
            }

            var bytes = BitConverter.GetBytes(_values[_index++]);
            if (data.Length != bytes.Length)
            {
                throw new InvalidOperationException(
                    $"想定外のバッファ長です: {data.Length}（{bytes.Length} を期待）。");
            }

            Array.Copy(bytes, data, bytes.Length);
        }
    }
}
