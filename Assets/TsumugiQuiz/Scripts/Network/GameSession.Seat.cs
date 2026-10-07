using TsumugiQuiz.Core.Network;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、再接続で席（名簿エントリ）のクライアント ID が変わったときの
    /// 引き継ぎをまとめた部分（#84、docs/network.md §2.4「再接続で引き継ぐもの / 引き継がないもの」）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 「同じ人か」の判定は名簿（<see cref="LobbyState"/> → <c>LobbyRoster</c> の席
    /// <c>LobbyPlayer.SeatId</c> と再接続トークン #69）が行い、その結果を
    /// <see cref="SeatTransfer"/> として受け取る。本ファイルはそれをサーバー側の進行状態
    /// （<c>QuizStateMachine</c>）と同期用の得点表（<c>NetworkList&lt;ScoreEntry&gt;</c>）へ反映するだけで、
    /// 本人性の判断は一切しない。
    /// </para>
    /// <para>
    /// すべてサーバー（ホスト）専用。クライアントは <c>NetworkVariable</c> / <c>NetworkList</c> の
    /// 同期で結果だけを受け取る（新しいクライアント ID の行に得点が載って届く）。
    /// </para>
    /// </remarks>
    public sealed partial class GameSession
    {
        /// <summary>
        /// 再接続した席の進行状態を、古いクライアント ID から新しいクライアント ID へ移し替える
        /// （サーバーのみ、#84）。
        /// </summary>
        /// <remarks>
        /// 移し替える対象は <c>QuizStateMachine.TransferClient</c> を参照。
        /// レート制限（#52）と棄却ログの間引き（#72）は接続そのものに紐づく状態なので引き継がず、
        /// 古いクライアント ID の分を捨てるだけにする（新しい接続はまっさらな枠から始める）。
        /// </remarks>
        /// <param name="transfer">名簿が報告した付け替え（旧クライアント ID → 新クライアント ID）。</param>
        /// <returns>移し替えたものが 1 つでもあれば true。</returns>
        public bool TransferSeat(SeatTransfer transfer)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[GameSession] 席の引き継ぎはサーバー（ホスト）でのみ行えます。");
                return false;
            }

            if (!transfer.HasValue)
            {
                return false;
            }

            var previousClientId = transfer.PreviousClientId;
            var clientId = transfer.ClientId;

            // 接続に紐づく状態は引き継がない（古い分だけ捨てる）。
            _rejectLogger?.Forget(previousClientId);
            _rpcRateGuard?.Forget(previousClientId);

            var moved = _machine != null && _machine.TransferClient(previousClientId, clientId);

            // 得点表（NetworkList）の行も同じタイミングで付け替える。PublishScores は行を
            // 追加・更新するだけで削除しないため、ここで古い行を消さないと二重に見えてしまう。
            var movedScoreRow = MoveScoreRow(previousClientId, clientId);

            if (moved)
            {
                // ロック保持者（NetworkVariable）など、付け替えた値を同期へ反映する。
                PublishState();
            }

            if (moved || movedScoreRow)
            {
                Debug.Log(
                    $"[GameSession] 再接続した席の進行状態を引き継ぎました（{transfer}、"
                    + $"得点 {GetServerScore(clientId)}）。");
            }

            return moved || movedScoreRow;
        }

        /// <summary>
        /// 席が名簿から消えた（保持期間切れ・ホストによる手動削除）クライアントの
        /// ペナルティ・誤答済みを捨てる（サーバーのみ、#84）。
        /// </summary>
        /// <remarks>
        /// 切断しただけでは捨てない。席が残っている間は再接続で戻ってくる可能性があり、
        /// そこで捨ててしまうと「切断すればお手つきの罰から逃れられる」抜け道になるため
        /// （docs/network.md §2.4 の引き継ぎ表）。得点は結果表示に使うので消さない。
        /// </remarks>
        /// <param name="clientId">席が無くなったクライアント ID。</param>
        /// <returns>サーバーで処理したら true。</returns>
        public bool ForgetSeat(ulong clientId)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[GameSession] 席の破棄はサーバー（ホスト）でのみ行えます。");
                return false;
            }

            _machine?.ForgetClient(clientId);

            // #194: 参加者パネルの進行状態からも消えた席を外す（フェーズ遷移を伴わないので明示的に反映する）。
            PublishQuestionProgress();
            return true;
        }

        /// <summary>
        /// サーバー側の進行で、指定クライアントが現在の早押し受付から棄却される
        /// （お手つきで次問休み・現在の問題で誤答済み）か（診断・テスト用）。
        /// クライアントでは常に false。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>棄却されるなら true。</returns>
        public bool IsServerPenalized(ulong clientId) => _machine?.IsPenalized(clientId) ?? false;
    }
}
