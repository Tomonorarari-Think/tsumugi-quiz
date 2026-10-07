using System.Collections.Generic;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 再接続トークンの保存先（issue #69）。実際の読み書き
    /// （<c>Application.persistentDataPath/session-token.json</c> 等）は Network 層が実装し、
    /// この層（Core）は Unity API に依存しない（<c>TsumugiQuiz.Core.IConsentStorage</c> と同じ方針）。
    /// </summary>
    public interface ISessionTokenStorage
    {
        /// <summary>
        /// 保存済みのレコードをすべて読む。ファイルが無い・壊れている場合は空を返す
        /// （例外を投げず、詳細は実装側でログに残す）。
        /// </summary>
        IReadOnlyList<SessionTokenRecord> Load();

        /// <summary>レコードを保存する（全件置き換え）。</summary>
        void Save(IReadOnlyList<SessionTokenRecord> records);
    }
}
