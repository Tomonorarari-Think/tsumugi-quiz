namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession.QuestionShown"/> がどの経路で発火したか（#109、docs/network.md §2.4）。
    /// </summary>
    /// <remarks>
    /// 途中参加・再接続の再同期（<see cref="GameSession.ResyncClient"/>）は「いま出題中の問題」を
    /// 後から 1 人へ送り直すもので、**通常の提示とは意味が違う**（進行は既に先へ進んでおり、
    /// 読み上げ（#23）も途中まで再生済みか、これから鳴る保証がない）。
    /// 表示の復元だけを行いたい購読者と、通常の提示でしか動いてはいけない購読者
    /// （<see cref="TtsSyncCoordinator"/>・ゲーム開始ジングル）を区別するために渡す。
    /// </remarks>
    public enum QuestionShownSource
    {
        /// <summary>通常の出題（配信 → 受信確認 → 提示の合図）。全ピアで同時に発火する。</summary>
        Distribution = 0,

        /// <summary>
        /// 途中参加・再接続したクライアントへの再同期（<see cref="GameSession.ResyncClient"/>）。
        /// そのクライアント 1 人だけで発火する。
        /// </summary>
        Resync = 1,
    }
}
