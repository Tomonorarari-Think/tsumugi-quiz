namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 押下タイムスタンプに入った丸め補正の種類（docs/network.md §6.4）。
    /// </summary>
    public enum BuzzClamp
    {
        /// <summary>補正なし（報告された時刻をそのまま使った）。</summary>
        None = 0,

        /// <summary>受付開始より前の時刻だったため T0 に丸めた。司会画面に「⚠ 時刻補正あり」を表示する対象。</summary>
        ToT0 = 1,

        /// <summary>サーバー受信時刻より後の時刻だったため serverNow に丸めた（通常運用でも起こりうる）。</summary>
        ToServerNow = 2,
    }
}
