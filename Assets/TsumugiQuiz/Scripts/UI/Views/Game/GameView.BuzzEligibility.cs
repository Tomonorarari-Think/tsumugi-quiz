using TsumugiQuiz.Core.Participants;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、早押しボタンを押せるかを同期された進行状態（#194）からも決める部分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 以前は「自分が誤答した」ことを <c>BuzzReopenedRpc</c> を受けたときのローカルの印（<c>_isExcludedFromBuzzing</c>）
    /// だけで覚えていたため、次の 2 つの場面でボタンが押せるのにサーバーが黙って棄却していた。
    /// 参加者パネルに「×」「休み」が出ているのにボタンが押せる食い違いを残さないよう、同期値からも決める（統括判断 #194）。
    /// </para>
    /// <list type="bullet">
    /// <item><description>お手つきの「次問休み」（<c>score.penaltyType = "skipNext"</c>）の問題。ローカルの印は次の問題でクリアされる</description></item>
    /// <item><description>誤答した後に再接続・途中参加した場合。ローカルの印は RPC を受けたプロセスにしか残らない</description></item>
    /// </list>
    /// <para>
    /// ローカルの印は残す。誤答の RPC は同期値（次の tick の差分）より先に届くので、その間も押せないようにするため。
    /// 正しさの判断はこれまでどおりサーバー（<c>QuizStateMachine.IsPenalized</c>）が行い、ここは体感のための事前判定。
    /// </para>
    /// </remarks>
    public sealed partial class GameView
    {
        /// <summary>
        /// 自分がこの問題で早押しできない（誤答済み・次問休み）か。ローカルの印と同期された進行状態のどちらかが立っていれば true。
        /// </summary>
        private bool IsExcludedFromBuzzing => _isExcludedFromBuzzing || IsExcludedBySyncedProgress();

        /// <summary>同期された進行状態（現在の問題のもの）で、自分が誤答済み・次問休みか。</summary>
        private bool IsExcludedBySyncedProgress()
        {
            if (_session == null || !TryGetLocalClientId(out var localClientId))
            {
                return false;
            }

            var progress = _session.GetQuestionProgress();
            return progress.QuestionIndex != QuestionProgress.NoQuestion
                && progress.QuestionIndex == _session.QuestionIndex.Value
                && progress.IsExcludedFromBuzzing(localClientId);
        }

        /// <summary>進行状態が変わったら早押しボタンの可否を更新する（<see cref="SubscribeSessionEvents"/> で購読）。</summary>
        private void HandleQuestionProgressChangedForBuzz()
        {
            if (_buzzButton != null)
            {
                UpdateBuzzButtonEnabled();
            }
        }
    }
}
