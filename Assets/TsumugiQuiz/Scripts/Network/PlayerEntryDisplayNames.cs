using System.Collections.Generic;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 名簿のエントリ（<see cref="PlayerEntry"/>）から、画面に出す表示名を決める（issue #85）。
    /// 連番の付け方そのものは純 C# の <see cref="PlayerDisplayNames"/>（Core）にあり、
    /// ここは「どのエントリを連番の対象にするか」という名簿側の約束だけを持つ。
    /// </summary>
    /// <remarks>
    /// 連番の対象は<b>接続中のエントリだけ</b>にする。切断中のエントリはクライアントへ
    /// 全件が同じ伏せ字（<see cref="PlayerEntry.DisconnectedNameMask"/>）で届くため、
    /// 対象に含めると伏せ字同士が「同名」と見なされてしまう（ホスト側は実名が見えるが、
    /// 画面の見え方をホストとクライアントで揃えるために規則は同じにする）。
    /// また、区別が要るのは「同時に接続している同名」であって、切断中の行は
    /// グレー表示と「切断中」ラベルで既に区別できる。
    ///
    /// 呼び出し側（<c>LobbyView.Roster.cs</c> の一覧、<c>GameView.Network.cs</c> の回答者表示）が
    /// 同じ規則を使うことで、ロビーとゲーム画面で同じ人に同じ表示名が出る。
    /// </remarks>
    public static class PlayerEntryDisplayNames
    {
        /// <summary>
        /// 表示名を決める。
        /// </summary>
        /// <param name="entries">表示する順に並べた名簿のエントリ（<c>GetPlayersSnapshot()</c> の順）。</param>
        /// <returns>
        /// 入力と同じ並び順の表示名。<paramref name="entries"/> が null / 空なら
        /// <see cref="PlayerDisplayNameSet.Empty"/>。
        /// </returns>
        public static PlayerDisplayNameSet Resolve(IReadOnlyList<PlayerEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return PlayerDisplayNameSet.Empty;
            }

            var names = new string[entries.Count];
            var numberingTargets = new bool[entries.Count];

            for (var i = 0; i < entries.Count; i++)
            {
                // PlayerEntry.GetDisplayName() は string を返すので、呼び出し側は
                // Unity.Collections（FixedString64Bytes の定義元）を参照せずに済む。
                // 表示用に整えた名前（#206）で連番を決める。整えた結果が同じ名前は、画面上で区別できるよう同名として扱う。
                names[i] = entries[i].GetDisplayName();
                numberingTargets[i] = entries[i].IsConnected;
            }

            return PlayerDisplayNames.Resolve(names, numberingTargets);
        }
    }
}
