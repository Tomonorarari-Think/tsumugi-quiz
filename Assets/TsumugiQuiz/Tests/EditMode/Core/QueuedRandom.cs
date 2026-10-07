using System;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// あらかじめ与えた値を順に返す決定的な <see cref="IRandom"/> スタブ。
    /// 同着抽選の分岐を決定的に検証するために使う。
    /// </summary>
    public sealed class QueuedRandom : IRandom
    {
        private readonly int[] _values;
        private int _index;

        public QueuedRandom(params int[] values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        /// <summary>最後に要求された上限（抽選対象数の検証用）。</summary>
        public int LastExclusiveMax { get; private set; } = -1;

        /// <summary>呼び出し回数。</summary>
        public int CallCount { get; private set; }

        public int NextInt(int exclusiveMax)
        {
            LastExclusiveMax = exclusiveMax;
            CallCount++;

            if (_index >= _values.Length)
            {
                throw new InvalidOperationException("QueuedRandom に用意した値が尽きました。");
            }

            return _values[_index++];
        }
    }
}
