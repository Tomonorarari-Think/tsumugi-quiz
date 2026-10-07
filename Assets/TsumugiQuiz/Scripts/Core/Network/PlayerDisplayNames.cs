using System;
using System.Collections.Generic;
using System.Globalization;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 同名のプレイヤーを画面上で区別するための表示名を決める純 C# ロジック
    /// （issue #85、docs/network.md §2.3）。
    ///
    /// 名簿の <c>name</c> は書き換えず、<b>表示の段でだけ</b>連番（<c>つむぎ #1</c> / <c>つむぎ #2</c>）を付ける。
    /// 名前はサーバー権威のデータで、切断中エントリはクライアントへ伏せ字で配っている
    /// （<c>TsumugiQuiz.Network.PlayerEntry.DisconnectedNameMask</c>）都合もあるため。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同名の同時接続は本来拒否しているが（統括判断 #7 Q2）、再接続経路では
    /// 「A が切断 → 別人 B が同じ名前で新規参加 → A がトークンで復帰」の順をたどると
    /// 同名の接続中エントリが 2 つ並ぶ（#69。ここで A を弾くと本人が自分の席に戻れないため、
    /// 席・得点の保護を名前の一意性より優先している）。その唯一の例外に対する表示側の対処。
    /// </para>
    /// <para>
    /// 番号は「渡された並び順」（= 名簿の追加順 = 参加順）で 1 から振る。名簿の再接続は
    /// エントリをその場で差し替える（<see cref="LobbyRoster.TryApply"/>）ので、
    /// 復帰しても並び順は変わらず、同じ人には同じ番号が付き続ける。
    /// </para>
    /// <para>
    /// 連番の対象は呼び出し側が選べる（<see cref="Resolve(IReadOnlyList{string}, IReadOnlyList{bool})"/>）。
    /// 実際の表示では「接続中のエントリだけ」を対象にする: 切断中のエントリはクライアントには
    /// 全件が同じ伏せ字で届くため、対象に含めると伏せ字同士が重複扱いになってしまう。
    /// </para>
    /// <para>
    /// プレイヤー名には任意の文字を入れられる（1〜16 文字・制御文字以外なら何でも通る。
    /// <see cref="PlayerNameValidator"/>）ので、<c>つむぎ #1</c> と名乗ることもできる。
    /// そのため生成した表示名が他の行の表示名と衝突しないところまで番号を進める。
    /// <b>連番を付けた表示名は、他のどの行の表示名とも衝突しない</b>（番号が 1 から始まらないことはある）。
    /// ただし「全行の表示名が互いに異なる」わけではない: 連番の対象外にした行どうし
    /// —— 実際の使い方では切断中エントリの伏せ字（<c>（切断中）</c>）—— は元々同じ文字列で、
    /// そこは連番では区別しない（行のグレー表示と「切断中」ラベルで区別する）。
    /// </para>
    /// </remarks>
    public static class PlayerDisplayNames
    {
        /// <summary>連番の区切り（<c>つむぎ #2</c> の <c>" #"</c> の部分）。</summary>
        public const string OrdinalSeparator = " #";

        /// <summary>名前と連番から表示名を組み立てる。</summary>
        /// <param name="name">元の名前。空なら連番だけを返す。</param>
        /// <param name="ordinal">1 から始まる連番。1 未満は 1 に丸める。</param>
        public static string WithOrdinal(string name, int ordinal)
        {
            var effective = ordinal < 1 ? 1 : ordinal;
            var suffix = effective.ToString(CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(name)
                ? OrdinalSeparator.TrimStart() + suffix
                : name + OrdinalSeparator + suffix;
        }

        /// <summary>
        /// 表示名を決める（全件を連番の対象にする）。同名が 1 件だけの名前はそのまま、
        /// 2 件以上ある名前には並び順で 1 から連番を付ける
        /// （<c>つむぎ</c> が 2 人なら <c>つむぎ #1</c> / <c>つむぎ #2</c>）。
        /// </summary>
        /// <param name="names">
        /// 表示する順に並べた名前。null 要素は空文字として扱う（名簿の外から来た値も落とさない）。
        /// </param>
        /// <returns>表示名と重複していた名前。<paramref name="names"/> が空なら <see cref="PlayerDisplayNameSet.Empty"/>。</returns>
        public static PlayerDisplayNameSet Resolve(IReadOnlyList<string> names) => Resolve(names, null);

        /// <summary>
        /// 表示名を決める。<paramref name="numberingTargets"/> が false の位置は連番の対象外で、
        /// 重複の数え上げからも外れる（名前をそのまま表示する）。
        /// </summary>
        /// <param name="names">表示する順に並べた名前。null 要素は空文字として扱う。</param>
        /// <param name="numberingTargets">
        /// 各位置を連番の対象にするか。null なら全件が対象。指定する場合は
        /// <paramref name="names"/> と同じ件数でなければならない。
        /// </param>
        /// <returns>表示名と重複していた名前。</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="numberingTargets"/> の件数が <paramref name="names"/> と一致しない。
        /// </exception>
        public static PlayerDisplayNameSet Resolve(
            IReadOnlyList<string> names, IReadOnlyList<bool> numberingTargets)
        {
            if (numberingTargets != null && names != null && numberingTargets.Count != names.Count)
            {
                throw new ArgumentException(
                    $"連番の対象指定は名前と同じ件数である必要があります（名前 {names.Count} 件 / 対象指定 {numberingTargets.Count} 件）。",
                    nameof(numberingTargets));
            }

            if (names == null || names.Count == 0)
            {
                return PlayerDisplayNameSet.Empty;
            }

            var counts = CountByName(names, numberingTargets);

            var labels = new string[names.Count];
            var needsOrdinal = new bool[names.Count];

            // 1 周目: 連番を付けない行を確定させ、その表示名を「使用済み」として押さえる。
            // 先に押さえておかないと、`つむぎ` が 2 人 + `つむぎ #1` という名前の 3 人目が居た場合に
            // 生成した連番付きの表示名が 3 人目の名前と衝突してしまう（名前は任意の文字を含められる）。
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i] ?? string.Empty;
                needsOrdinal[i] = IsNumberingTarget(numberingTargets, i) && counts[name] > 1;
                if (needsOrdinal[i])
                {
                    continue;
                }

                labels[i] = name;
                used.Add(name);
            }

            // 2 周目: 同名の行へ並び順で連番を振る。既に使われている表示名は飛ばすので、
            // 連番を付けた表示名は他のどの行の表示名とも衝突しない（番号が 1 から始まらないことはある）。
            // 連番の対象外にした行どうし（切断中の伏せ字など）は元々同じ文字列なので、
            // 全行の表示名が互いに異なるわけではない。
            var nextOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> duplicated = null;
            List<string> numberedLabels = null;

            for (var i = 0; i < names.Count; i++)
            {
                if (!needsOrdinal[i])
                {
                    continue;
                }

                var name = names[i] ?? string.Empty;
                if (!nextOrdinals.TryGetValue(name, out var ordinal))
                {
                    ordinal = 1;
                    duplicated ??= new List<string>();
                    duplicated.Add(name);
                }

                var label = WithOrdinal(name, ordinal);
                while (!used.Add(label))
                {
                    ordinal++;
                    label = WithOrdinal(name, ordinal);
                }

                labels[i] = label;
                nextOrdinals[name] = ordinal + 1;

                numberedLabels ??= new List<string>();
                numberedLabels.Add(label);
            }

            return new PlayerDisplayNameSet(labels, duplicated?.ToArray(), numberedLabels?.ToArray());
        }

        /// <summary>
        /// 名前ごとの件数を数える（完全一致・<see cref="StringComparison.Ordinal"/>）。
        /// 連番の対象外（<paramref name="numberingTargets"/> が false）の位置は数えない。
        /// </summary>
        private static Dictionary<string, int> CountByName(
            IReadOnlyList<string> names, IReadOnlyList<bool> numberingTargets)
        {
            var counts = new Dictionary<string, int>(names.Count, StringComparer.Ordinal);

            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i] ?? string.Empty;
                if (!IsNumberingTarget(numberingTargets, i))
                {
                    // 対象外でも辞書には 0 件で載せておく（Resolve 側の counts[name] が例外にならないように）。
                    if (!counts.ContainsKey(name))
                    {
                        counts[name] = 0;
                    }

                    continue;
                }

                counts[name] = counts.TryGetValue(name, out var count) ? count + 1 : 1;
            }

            return counts;
        }

        private static bool IsNumberingTarget(IReadOnlyList<bool> numberingTargets, int index)
            => numberingTargets == null || numberingTargets[index];
    }
}
