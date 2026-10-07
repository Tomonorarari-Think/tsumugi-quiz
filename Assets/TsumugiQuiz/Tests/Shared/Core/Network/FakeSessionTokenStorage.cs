using System;
using System.Collections.Generic;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.Shared.Core.Network
{
    /// <summary>
    /// メモリ上だけで動く <see cref="ISessionTokenStorage"/>（issue #69 のテスト用）。
    /// 実ファイル（<c>session-token.json</c>）に触れずに <see cref="SessionTokenStore"/> を検証する。
    /// EditMode（<c>SessionTokenStoreTests</c>）と PlayMode（<c>NetworkServiceSessionTokenTests</c>、#85）の
    /// 両方から使うため、共有アセンブリ（<c>TsumugiQuiz.Tests.Shared</c>）に置いている。
    /// 置き場は差し替える対象（<see cref="ISessionTokenStorage"/> = <c>TsumugiQuiz.Core.Network</c>）に合わせて
    /// <c>Tests/Shared/Core/Network/</c>（<c>Tests/Shared/Network/</c> は <c>TsumugiQuiz.Network</c> のフェイク置き場）。
    /// </summary>
    public sealed class FakeSessionTokenStorage : ISessionTokenStorage
    {
        private List<SessionTokenRecord> _records = new List<SessionTokenRecord>();

        /// <summary>保存が呼ばれた回数。</summary>
        public int SaveCount { get; private set; }

        /// <summary>保存されている件数。</summary>
        public int Count => _records.Count;

        /// <summary>検証用に、任意のレコードを直接置く。</summary>
        public void Seed(params SessionTokenRecord[] records)
        {
            _records = new List<SessionTokenRecord>(records ?? Array.Empty<SessionTokenRecord>());
        }

        /// <inheritdoc />
        public IReadOnlyList<SessionTokenRecord> Load() => _records.ToArray();

        /// <inheritdoc />
        public void Save(IReadOnlyList<SessionTokenRecord> records)
        {
            SaveCount++;
            _records = new List<SessionTokenRecord>(records ?? Array.Empty<SessionTokenRecord>());
        }
    }
}
