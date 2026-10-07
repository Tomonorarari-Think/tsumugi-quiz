using TsumugiQuiz.Questions.Images;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、問題画像の配信（#16）に関わる設定 API をまとめた部分。
    /// </summary>
    /// <remarks>
    /// 画像の解決には「問題セットファイルの配置先」が必要で、<c>IQuestionSource</c> は
    /// それを持たない（問題を平坦な配列として供給する）。そのため
    /// <see cref="Configure"/> とは別の入口を用意し、画像を配る構成のときだけ呼ぶ形にしている。
    /// ホスト側の本番の配線（問題フォルダのパスを渡す）は、ロビーの「ゲーム開始」
    /// （<c>TsumugiQuiz.UI.Views.Lobby.LobbyView.TryStartSession</c>）が <see cref="Configure"/> の直後に行う（#185）。
    /// 受信した画像の表示は <c>TsumugiQuiz.UI.Views.Game.GameView</c>（<c>GameView.QuestionImage.cs</c>）が
    /// <see cref="QuestionDistributor.TryGetImage"/> で行う。
    /// </remarks>
    public sealed partial class GameSession
    {
        /// <summary>
        /// 問題画像の供給元を設定する（サーバーのみ。<see cref="Configure"/> の後に呼ぶ）。
        /// </summary>
        /// <param name="imageSource">
        /// 画像の供給元（通常は <see cref="QuestionImageSource"/>）。
        /// null を渡すと画像を配信しない（テキストのみの進行に戻す）。
        /// </param>
        /// <returns>設定できたら true。配信器が無い構成では false。</returns>
        public bool ConfigureImages(IQuestionImageSource imageSource)
        {
            if (IsSpawned && !IsServer)
            {
                Debug.LogWarning("[GameSession] 画像の供給元はサーバーでのみ設定できます。");
                return false;
            }

            if (_distributor == null)
            {
                Debug.LogWarning("[GameSession] QuestionDistributor が無いため画像を配信できません。");
                return false;
            }

            _distributor.SetImageSource(imageSource);
            return true;
        }
    }
}
