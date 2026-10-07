using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 集計窓が閉じた時点の早押し裁定結果（docs/network.md §6.3）。
    /// </summary>
    public readonly struct BuzzResolution
    {
        /// <summary>勝者のクライアント ID。</summary>
        public ulong WinnerClientId { get; }

        /// <summary>勝者の T0 からの経過秒。</summary>
        public double WinnerDt { get; }

        /// <summary>同着（差 &lt; tieEpsilon）により抽選になったか。</summary>
        public bool WasTie { get; }

        /// <summary>同着として抽選対象になった候補数（抽選でないときは 1）。</summary>
        public int TiedCount { get; }

        /// <summary>
        /// 勝者のタイムスタンプに入った丸め補正。
        /// <see cref="BuzzClamp.ToT0"/> のときだけ司会画面に「⚠ 時刻補正あり」を表示する（§6.4）。
        /// </summary>
        public BuzzClamp WinnerClamp { get; }

        /// <summary>集計窓に積まれた候補の総数。</summary>
        public int CandidateCount { get; }

        /// <summary>
        /// 集計窓の中で受理した全候補の順位（#194）。先頭が勝者で、以降は <see cref="BuzzRankEntry.Dt"/> の昇順
        /// （同じ Dt なら受理した順）。同着の抽選で負けた候補も勝者のすぐ後に並ぶ。null にはならない。
        /// </summary>
        public IReadOnlyList<BuzzRankEntry> Ranking => _ranking ?? Array.Empty<BuzzRankEntry>();

        private readonly IReadOnlyList<BuzzRankEntry> _ranking;

        public BuzzResolution(
            ulong winnerClientId,
            double winnerDt,
            bool wasTie,
            int tiedCount,
            BuzzClamp winnerClamp,
            int candidateCount,
            IReadOnlyList<BuzzRankEntry> ranking = null)
        {
            _ranking = ranking;
            WinnerClientId = winnerClientId;
            WinnerDt = winnerDt;
            WasTie = wasTie;
            TiedCount = tiedCount;
            WinnerClamp = winnerClamp;
            CandidateCount = candidateCount;
        }
    }
}
