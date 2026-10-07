using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 得点表から順位を計算する（Result View、#20、docs/architecture.md §2）。
    /// 同点は同順位とし、その次の順位は同点者の人数ぶん飛ばす（いわゆる competition ranking。
    /// 例: 100, 100, 80 点なら 1 位, 1 位, 3 位）。
    /// </summary>
    public readonly struct RankedScore
    {
        /// <summary>順位（1 始まり）。同点は同順位。</summary>
        public int Rank { get; }

        /// <summary>クライアント ID。</summary>
        public ulong ClientId { get; }

        /// <summary>得点。</summary>
        public int Score { get; }

        /// <summary>
        /// 値を指定して生成する。
        /// </summary>
        /// <param name="rank">順位（1 始まり）。</param>
        /// <param name="clientId">クライアント ID。</param>
        /// <param name="score">得点。</param>
        public RankedScore(int rank, ulong clientId, int score)
        {
            Rank = rank;
            ClientId = clientId;
            Score = score;
        }
    }

    /// <summary>
    /// <see cref="RankedScore"/> の一覧を計算する純ロジック。Unity API に依存しない。
    /// </summary>
    public static class ResultRanking
    {
        /// <summary>
        /// 得点の高い順に並べ替え、同点には同じ順位を割り当てる。
        /// 切断中かどうか等の表示情報は持たない（クライアント ID から呼び出し側が解決する）。
        /// </summary>
        /// <param name="entries">クライアント ID と得点の組。</param>
        /// <returns>得点降順・同点同順位で並んだ一覧。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entries"/> が null のとき。</exception>
        public static IReadOnlyList<RankedScore> Compute(IReadOnlyList<(ulong ClientId, int Score)> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var sorted = new List<(ulong ClientId, int Score)>(entries);

            // 得点の降順。同点内の並びは元の並び順を保つ（安定ソート、List<T>.Sort は不安定なので
            // インデックスを絡めて安定化する）。
            var indexed = new List<(int Index, ulong ClientId, int Score)>(sorted.Count);
            for (var i = 0; i < sorted.Count; i++)
            {
                indexed.Add((i, sorted[i].ClientId, sorted[i].Score));
            }

            indexed.Sort((a, b) =>
            {
                var byScore = b.Score.CompareTo(a.Score);
                return byScore != 0 ? byScore : a.Index.CompareTo(b.Index);
            });

            var result = new List<RankedScore>(indexed.Count);
            var rank = 0;
            var hasPreviousScore = false;
            var previousScore = 0;

            for (var i = 0; i < indexed.Count; i++)
            {
                var current = indexed[i];
                if (!hasPreviousScore || current.Score != previousScore)
                {
                    rank = i + 1;
                    previousScore = current.Score;
                    hasPreviousScore = true;
                }

                result.Add(new RankedScore(rank, current.ClientId, current.Score));
            }

            return result;
        }
    }
}
