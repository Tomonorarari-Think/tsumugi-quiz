namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 乱数抽選の抽象。早押しの同着抽選（docs/network.md §6.3）で使う。
    /// 本番では <see cref="CryptoRandom"/>、テストでは決定的なスタブを差し替える。
    /// </summary>
    public interface IRandom
    {
        /// <summary>
        /// 0 以上 <paramref name="exclusiveMax"/> 未満の整数を一様乱数で返す。
        /// </summary>
        /// <param name="exclusiveMax">上限（この値自体は含まない）。1 以上であること。</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// <paramref name="exclusiveMax"/> が 1 未満のとき。
        /// </exception>
        int NextInt(int exclusiveMax);
    }
}
