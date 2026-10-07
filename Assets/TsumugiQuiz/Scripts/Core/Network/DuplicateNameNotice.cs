using System.Collections.Generic;
using System.Text;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 同名のプレイヤーが同時に接続しているときの注意文を組み立てる（issue #85）。
    /// 文言だけを持つ純 C#（<see cref="ConnectionRejectionMessages"/> / <see cref="JoinStatusMessages"/> と同じ方針）で、
    /// 表示するかどうかは UI 層（<c>LobbyView</c>）が決める。
    /// </summary>
    /// <remarks>
    /// 例示する番号は <b>実際に割り当てた表示名</b>（<see cref="PlayerDisplayNameSet.NumberedLabels"/>）から埋める。
    /// 連番は既存の名前と衝突する番号を飛ばすため <c>#1</c> から始まるとは限らず、
    /// 固定の文言（「#1 / #2 で区別しています」）だと画面の見た目とずれることがあるため。
    /// </remarks>
    public static class DuplicateNameNotice
    {
        /// <summary>名前・表示名を並べるときの区切り。</summary>
        public const string NameSeparator = "、";

        /// <summary>
        /// ホストにだけ出す対処の案内。
        /// </summary>
        /// <remarks>
        /// この状態になった時点では、名前を先取りされた本人が既に復帰しているため
        /// 「切断中のエントリ」は残っていないことが多い（＝削除ボタンでは解消しない）。
        /// そこで「今できること」と「次から起こりにくくする方法」を分けて書く。
        /// 席・得点は名簿のエントリ（トークン）に紐づいているので取り違えは起きない（#69）。
        /// </remarks>
        public const string HostAdvice =
            "席と得点は名前ではなく席ごとに記録しているので取り違えは起きません。"
            + "表示を戻したいときは、どちらかに名前を変えて入り直してもらってください。"
            + "切断中のエントリを早めに削除しておくと、名前を先取りされにくくなります。";

        /// <summary>割り当てた表示名が分からない場合の言い回し（通常は使われない）。</summary>
        private const string GenericNumberingNote = "一覧では名前の末尾に番号を付けて区別しています。";

        /// <summary>
        /// 注意文を組み立てる。
        /// </summary>
        /// <param name="displayNames">
        /// 表示名の解決結果（<see cref="PlayerDisplayNames.Resolve(IReadOnlyList{string})"/> の戻り値）。
        /// </param>
        /// <param name="includeHostAdvice">ホスト向けの対処案内（<see cref="HostAdvice"/>）を添えるか。</param>
        /// <returns>注意文。重複が無ければ（<paramref name="displayNames"/> が null の場合も）空文字。</returns>
        public static string Build(PlayerDisplayNameSet displayNames, bool includeHostAdvice)
        {
            if (displayNames == null)
            {
                return string.Empty;
            }

            var names = Join(displayNames.DuplicatedNames);
            if (names.Length == 0)
            {
                return string.Empty;
            }

            var message = new StringBuilder();
            message.Append("同じ名前の参加者が同時に接続しています（").Append(names).Append("）。");

            var numbered = Join(displayNames.NumberedLabels);
            message.Append(numbered.Length == 0
                ? GenericNumberingNote
                : "一覧では " + numbered + " のように末尾の番号で区別しています。");

            if (includeHostAdvice)
            {
                message.Append(HostAdvice);
            }

            return message.ToString();
        }

        /// <summary>
        /// ログ（ホストのプレイヤーログ）へ残す 1 行。UI に出ていない状況でも後から追えるようにする。
        /// </summary>
        /// <param name="playerName">重複した名前。</param>
        /// <param name="connectedCount">その名前で接続中のエントリ数。</param>
        public static string BuildLogLine(string playerName, int connectedCount)
            => $"同名のプレイヤーが同時に接続しています name='{playerName}' 接続中={connectedCount} 件"
               + "（再接続で名前を先取りされた場合に起こります。表示は末尾の番号で区別します）";

        /// <summary>文字列を読める形に並べる（空の要素はそのまま省く）。</summary>
        private static string Join(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(NameSeparator);
                }

                builder.Append(value);
            }

            return builder.ToString();
        }
    }
}
