using System;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// シードから決定的に値を返す <see cref="IRandom"/>（SplitMix64）。
    /// 出題順シャッフル（#19 の <c>questions.shuffleOrder</c>）のように
    /// 「同じシードなら必ず同じ並び」を保証したい場面で使う。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 早押しの同着抽選には使わない（そちらは予測されると不公平になるため
    /// <see cref="CryptoRandom"/>）。本クラスは再現性が目的で、暗号学的な強度は無い。
    /// </para>
    /// <para>
    /// アルゴリズムは SplitMix64（状態 64bit の加算 + 混合）。<see cref="Random"/> を使わないのは、
    /// .NET / Unity のバージョンで内部アルゴリズムが変わりうるため（同じシードでも並びが変わりうる）。
    /// スレッドセーフではない。
    /// </para>
    /// </remarks>
    public sealed class SeededRandom : IRandom
    {
        private const ulong Gamma = 0x9E3779B97F4A7C15UL;
        private const ulong Mix1 = 0xBF58476D1CE4E5B9UL;
        private const ulong Mix2 = 0x94D049BB133111EBUL;

        private ulong _state;

        /// <summary>
        /// シードを指定して生成する。同じシードなら必ず同じ列を返す。
        /// </summary>
        /// <param name="seed">シード。負の値も使える。</param>
        public SeededRandom(int seed)
        {
            // 符号付き 32bit をそのまま 64bit 状態へ広げると上位が偏るので、Gamma で撹拌してから始める。
            _state = unchecked((ulong)(uint)seed * Gamma);
        }

        /// <inheritdoc />
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveMax), exclusiveMax, "exclusiveMax は 1 以上である必要があります。");
            }

            var bound = (ulong)exclusiveMax;

            // 剰余の偏り（modulo bias）を避けるため、2^64 を bound で割った余り未満の値は棄却する。
            var threshold = unchecked(0UL - bound) % bound;

            ulong value;
            do
            {
                value = NextUInt64();
            }
            while (value < threshold);

            return (int)(value % bound);
        }

        /// <summary>次の 64bit 値を返す（SplitMix64）。</summary>
        private ulong NextUInt64()
        {
            _state = unchecked(_state + Gamma);

            var z = _state;
            z = unchecked((z ^ (z >> 30)) * Mix1);
            z = unchecked((z ^ (z >> 27)) * Mix2);
            return z ^ (z >> 31);
        }
    }
}
