using System;
using TsumugiQuiz.Core;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// 選択式（<c>choice</c>）の選択肢の表示順を決める純 C# ロジック（issue #17、
    /// docs/question-data.md §6「シャッフルは各クライアントの表示上のみで行い、サーバーは常に元の
    /// <c>choices</c> 配列インデックスで判定する」）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BuildDisplayOrder"/> が返す配列は「表示位置 → 元 <c>choices</c> インデックス」の
    /// 対応表そのもの。<see cref="GameView"/> はボタン生成時にこの対応表をボタンごとに 1 つずつ
    /// 割り当てて保持し、クリック時に対応する元インデックスをサーバーへ送る。
    /// </para>
    /// <para>
    /// シードは <see cref="BuildSeed"/>（問題インデックス + クライアント ID）で決定的に作る。
    /// 同じ問題・同じクライアントなら毎回同じ表示順になり、クライアントごとには異なる並びになりうる
    /// （<c>choices.shuffleDisplay</c> 既定 true。本 issue では定数、ルーム設定との接続は #26）。
    /// </para>
    /// </remarks>
    public static class ChoiceShuffle
    {
        /// <summary>本 issue（#17）における <c>choices.shuffleDisplay</c> の既定値（定数、#26 で設定接続）。</summary>
        public const bool DefaultShuffleDisplay = true;

        /// <summary>
        /// 決定的なシードを組み立てる（問題インデックス + クライアント ID）。
        /// </summary>
        /// <remarks>
        /// 単純な加算だと <c>(questionIndex, clientId)</c> の組み合わせが違っても同じ値になりやすい
        /// （例: <c>(1, 0)</c> と <c>(0, 1)</c>）ため、固定の乗数・XOR で混ぜる（レビュー L1）。
        /// <see cref="HashCode.Combine{T1, T2}"/> は使わない。.NET のプロセスごとにランダム化された
        /// 内部シードを使うため、同じ入力でもプロセスをまたぐと異なる値を返しうる（レビュー R-1）。
        /// 本メソッドは同じ実行バイナリである限り、どのプロセス・どの実行でも同じ入力に対して
        /// 常に同じシードを返す必要がある（<c>unchecked</c> の乗算・XOR は環境やプロセスに依存しない）。
        /// </remarks>
        /// <param name="questionIndex">問題インデックス（0 以上）。</param>
        /// <param name="clientId">自分のクライアント ID。</param>
        /// <returns>シード値。</returns>
        public static int BuildSeed(int questionIndex, ulong clientId) =>
            unchecked(questionIndex * 397 ^ (int)clientId);

        /// <summary>
        /// 表示順（表示位置 → 元 <c>choices</c> インデックス）を作る。
        /// </summary>
        /// <param name="choiceCount">選択肢件数（0 以上）。</param>
        /// <param name="shuffle">シャッフルするか。false なら元の順序のまま（<c>[0, 1, 2, ...]</c>）。</param>
        /// <param name="seed">シャッフルのシード（<see cref="BuildSeed"/>）。同じシードなら同じ並びになる。</param>
        /// <returns>長さ <paramref name="choiceCount"/> の配列。<c>result[表示位置] == 元インデックス</c>。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="choiceCount"/> が負のとき。</exception>
        public static int[] BuildDisplayOrder(int choiceCount, bool shuffle, int seed)
        {
            if (choiceCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(choiceCount), choiceCount, "choiceCount は 0 以上である必要があります。");
            }

            var order = new int[choiceCount];
            for (var i = 0; i < choiceCount; i++)
            {
                order[i] = i;
            }

            if (!shuffle || choiceCount < 2)
            {
                return order;
            }

            var random = new SeededRandom(seed);
            for (var i = choiceCount - 1; i > 0; i--)
            {
                var j = random.NextInt(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return order;
        }
    }
}
