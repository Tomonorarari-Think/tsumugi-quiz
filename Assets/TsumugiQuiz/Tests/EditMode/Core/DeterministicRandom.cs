using System;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// 線形合同法によるシード固定の <see cref="IRandom"/> スタブ。
    /// 公平性テスト（勝率の収束）を再現可能な形で回すために使う。本番コードでは使わない。
    /// </summary>
    public sealed class DeterministicRandom : IRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed)
        {
            _state = seed;
        }

        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveMax), exclusiveMax, "exclusiveMax は 1 以上である必要があります。");
            }

            // Knuth / MMIX の定数。上位 32bit を使うことで下位ビットの周期の短さを避ける。
            _state = unchecked((_state * 6364136223846793005UL) + 1442695040888963407UL);
            var value = (uint)(_state >> 32);
            return (int)(value % (uint)exclusiveMax);
        }
    }
}
