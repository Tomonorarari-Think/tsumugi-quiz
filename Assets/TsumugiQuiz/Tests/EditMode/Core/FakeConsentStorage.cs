using System.Collections.Generic;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="ConsentStore"/> のロジックをファイル I/O なしで検証するためのインメモリ実装。
    /// 実際のストレージ（JsonConsentStorage 等）はシリアライズを介するため、呼び出し側が
    /// Save に渡したリストへの参照をその後も持ち続けていても、保存済みの内容には影響しない。
    /// このフェイクも同じ独立性を再現するため、Load/Save の双方でコピーを保持・返却する（L-7）。
    /// </summary>
    internal sealed class FakeConsentStorage : IConsentStorage
    {
        private List<ConsentRecord> _storedRecords = new List<ConsentRecord>();

        public int SaveCallCount { get; private set; }

        public IReadOnlyList<ConsentRecord> Load()
        {
            return new List<ConsentRecord>(_storedRecords);
        }

        public void Save(IReadOnlyList<ConsentRecord> records)
        {
            SaveCallCount++;
            _storedRecords = records == null ? new List<ConsentRecord>() : new List<ConsentRecord>(records);
        }
    }
}
