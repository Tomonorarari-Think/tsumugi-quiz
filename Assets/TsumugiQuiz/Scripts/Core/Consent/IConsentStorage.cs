using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="ConsentRecord"/> の永続化を担う抽象。実体（JSON ファイル読み書き等）は UI 層から注入する。
    /// <see cref="ConsentStore"/> 自体は Unity API に依存しないため、テストでは純 C# のフェイク実装を渡せる。
    /// </summary>
    public interface IConsentStorage
    {
        /// <summary>
        /// 保存されている同意記録をすべて読み込む。
        /// 記録が無い場合・読み込みに失敗した場合（壊れた JSON 等）は、例外を投げず空のコレクションを返すこと。
        /// 破損データを「同意なし」として安全側に倒すのは実装側の責務とする。
        /// </summary>
        IReadOnlyList<ConsentRecord> Load();

        /// <summary>
        /// 同意記録を上書き保存する。空のコレクションを渡すと同意なしの状態になる（<see cref="ConsentStore.Revoke"/> 参照）。
        /// </summary>
        void Save(IReadOnlyList<ConsentRecord> records);
    }
}
