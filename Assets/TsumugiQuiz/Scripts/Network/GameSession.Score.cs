using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、得点の同期（<c>NetworkList&lt;ScoreEntry&gt;</c>）と
    /// 誤答後の受付再開放の通知をまとめた部分（#18、docs/room-settings.md §1「得点」）。
    /// 進行そのものは <c>QuizStateMachine</c>（<c>TsumugiQuiz.Core</c>）が持つ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 得点の権威はサーバー側の <c>QuizStateMachine.Scores</c>（不変な <c>ScoreBoard</c>）で、
    /// 本ファイルはそれを <c>NetworkList</c> に写してクライアントの表示用に配るだけ。
    /// <c>NetworkList</c> を使うのは、途中参加・再接続でも現在の得点表がそのまま届くため
    /// （RPC だけだと参加前の増減を受け取れない）。
    /// </para>
    /// <para>
    /// <c>NetworkList</c> そのものは公開せず、読み取り（<see cref="ScoreCount"/> /
    /// <see cref="GetScore"/>）と変更通知（<see cref="ScoreTableChanged"/> /
    /// <see cref="ScoreChanged"/>）だけを再公開する（外から行を書き換えられないようにするため）。
    /// </para>
    /// </remarks>
    public sealed partial class GameSession
    {
        /// <summary>
        /// 得点表（サーバー書き込み・クライアント読み取り専用）。
        /// 並びはクライアント ID の昇順で、得点 0 の行も載る。
        /// </summary>
        private readonly NetworkList<ScoreEntry> _scores = new NetworkList<ScoreEntry>();

        /// <summary>
        /// 得点表の <c>NetworkVariable</c>（書き込み権限の確認・診断用）。
        /// 値の読み取りは <see cref="GetScore"/> / <see cref="ScoreCount"/> を使う。
        /// </summary>
        public NetworkVariableBase Scores => _scores;

        /// <summary>得点表に載っている人数。</summary>
        public int ScoreCount => _scores.Count;

        /// <summary>UI へ渡す読み取り用のバッファ（毎フレーム確保しないよう使い回す）。</summary>
        private readonly List<ScoreEntry> _scoreSnapshot = new List<ScoreEntry>();

        /// <summary>
        /// 得点表（<c>NetworkList</c>）の現在の内容のコピーを返す（Result View、#20）。
        /// 返されるリストは次の呼び出しで再利用されるため、呼び出し側で保持しないこと
        /// （<see cref="LobbyState.GetPlayersSnapshot"/> と同じ作法）。
        /// </summary>
        public IReadOnlyList<ScoreEntry> GetScoreSnapshot()
        {
            _scoreSnapshot.Clear();

            if (!IsSpawned)
            {
                return _scoreSnapshot;
            }

            foreach (var entry in _scores)
            {
                _scoreSnapshot.Add(entry);
            }

            return _scoreSnapshot;
        }

        /// <summary>
        /// 得点が動いたとき（クライアント ID・増減・累計）。全ピアで発火する。
        /// 増減が ±0 の場合（既定設定での誤答など）も発火する。
        /// </summary>
        /// <remarks>
        /// 累計の表示にはこの第 3 引数（<c>total</c>）を使うこと。
        /// <see cref="GetScore"/> は <c>NetworkList</c> の同期を待つため、
        /// 本イベントの時点ではまだ 1 tick 前の値を返すことがある。
        /// </remarks>
        public event Action<ulong, int, int> ScoreChanged;

        /// <summary>
        /// 得点表（<c>NetworkList</c>）の内容が変わったとき。行の追加・更新・クリアで発火する。
        /// 途中参加時の初期同期でも発火するので、UI はこれを購読して
        /// <see cref="GetScore"/> で読み直せばよい。
        /// </summary>
        public event Action ScoreTableChanged;

        /// <summary>
        /// 誤答・お手つきのあと早押し受付が再開放されたとき（誤答者・据え置きの T0）。
        /// <c>buzz.reopenAfterWrongAnswer</c> が true のときだけ発火する（docs/network.md §6.6）。
        /// </summary>
        public event Action<ulong, double> BuzzReopened;

        /// <summary>
        /// 同期済みの得点を取得する（クライアントでも使える）。未登録なら 0。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>累計得点。</returns>
        public int GetScore(ulong clientId)
        {
            var index = IndexOfScore(clientId);
            return index < 0 ? 0 : _scores[index].Score;
        }

        /// <summary>状態機械の得点表を <c>NetworkList</c> へ反映する（サーバーのみ）。</summary>
        private void PublishScores()
        {
            if (_machine == null || !IsSpawned || !IsServer)
            {
                return;
            }

            var board = _machine.Scores;
            var clientIds = board.ClientIds;
            for (var i = 0; i < clientIds.Count; i++)
            {
                var entry = new ScoreEntry(clientIds[i], board.GetScore(clientIds[i]));
                var index = IndexOfScore(entry.ClientId);
                if (index < 0)
                {
                    _scores.Add(entry);
                }
                else if (_scores[index].Score != entry.Score)
                {
                    _scores[index] = entry;
                }
            }
        }

        /// <summary>
        /// 得点表を空にする（新しい進行を始めるとき・デスポーンするとき）。
        /// </summary>
        /// <remarks>
        /// 書けるのはサーバーだけで、クライアントで呼ぶと NGO が権限エラーをログに出す。
        /// 判定に <c>IsSpawned</c> を使えないのは、デスポーン処理の中（<c>OnNetworkDespawn</c>）では
        /// 既に <c>IsSpawned == false</c> になっているのに <c>NetworkManager</c> は残っていて、
        /// NGO の権限チェックが働くため。<c>NetworkManager</c> が未設定（一度もスポーンしていない）なら
        /// 権限チェックが働かないので、そのままローカルの内容を初期化してよい。
        /// クライアント側の内容は再スポーン時にサーバーから全量が届くので消さなくてよい。
        /// </remarks>
        private void ClearScores()
        {
            if (NetworkManager != null && !IsServer)
            {
                return;
            }

            if (_scores.Count == 0)
            {
                return;
            }

            _scores.Clear();
        }

        /// <summary>直近の判定で得点が動いていれば、増減を全員へ通知する（サーバーのみ）。</summary>
        private void NotifyScoreChanged()
        {
            if (_machine == null)
            {
                return;
            }

            var clientId = _machine.LastScoredClientId;
            if (clientId == NoClientId)
            {
                return;
            }

            ScoreChangedRpc(clientId, _machine.LastScoreDelta, _machine.GetScore(clientId));
        }

        /// <summary>
        /// 誤答後の受付再開放を全員へ通知する（サーバーのみ）。
        /// 得点の増減（お手つきの減点）も同時に配る。
        /// </summary>
        private void NotifyBuzzReopened()
        {
            if (_machine == null)
            {
                return;
            }

            NotifyScoreChanged();
            BuzzReopenedRpc(_machine.LastScoredClientId, _machine.BuzzOpenServerTime);
        }

        /// <summary>
        /// 得点表を書き直している最中か（<see cref="MoveScoreRow"/>、#84）。
        /// true の間は <see cref="ScoreTableChanged"/> を発火しない。
        /// </summary>
        private bool _isRewritingScores;

        /// <summary><c>NetworkList</c> の変更を <see cref="ScoreTableChanged"/> として再公開する。</summary>
        private void HandleScoreListChanged(NetworkListEvent<ScoreEntry> listEvent)
        {
            if (_isRewritingScores)
            {
                // 書き直し（Clear → 全件 Add）の途中では、得点表が空に見えるなど中途半端な状態になる。
                // 購読側（UI）に見せる必要はないので、終わったあと 1 回だけ通知する。
                return;
            }

            ScoreTableChanged?.Invoke();
        }

        /// <summary>
        /// 得点表（<c>NetworkList</c>）の行のキーを付け替える（サーバーのみ、#84 の再接続の引き継ぎ）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 権威は <c>QuizStateMachine.Scores</c> 側で、ここはその結果を同期用の表へ写すだけ。
        /// <see cref="PublishScores"/> は行の追加・更新しかしないため、古い行の削除はここで行う。
        /// </para>
        /// <para>
        /// 付け替えは <c>RemoveAt</c> + <c>Add</c> ではなく、<b><c>Clear()</c> してから全件を
        /// <c>Add()</c> し直す</b>（<c>LobbyState.SyncRosterToNetworkList</c> と同じ作法）。
        /// 付け替えが起きるのは「クライアントが再接続した直後」＝その tick に
        /// <c>NetworkObject</c> のスポーン同期（全状態の <c>WriteField</c>）と差分（<c>WriteDelta</c>）の
        /// 両方が同じクライアントへ届く瞬間で、差分が「削除 + 追加」だと適用結果が受信側の元の内容に
        /// 依存してしまう（実測: 復帰したクライアントにだけ古い行が残り 2 行になった）。
        /// <c>Clear</c> から始めれば、受信側が何を持っていても同じ結果に収束する。
        /// </para>
        /// <para>
        /// ホスト側の <see cref="ScoreTableChanged"/> は書き直しが終わってから 1 回だけ発火させる
        /// （レビュー L-1）。クライアント側は届いた差分ごとに発火するので、購読側は名簿
        /// （<c>LobbyState.RosterChanged</c>）と同じく「1 フレーム分をまとめてから読み直す」こと。
        /// </para>
        /// </remarks>
        /// <param name="fromClientId">切断時に使っていた古いクライアント ID。</param>
        /// <param name="toClientId">復帰後の新しいクライアント ID。</param>
        /// <returns>付け替える行があったら true。</returns>
        private bool MoveScoreRow(ulong fromClientId, ulong toClientId)
        {
            if (!IsSpawned || !IsServer || fromClientId == toClientId)
            {
                return false;
            }

            if (IndexOfScore(fromClientId) < 0)
            {
                return false;
            }

            var score = 0;
            var rows = new List<ScoreEntry>(_scores.Count);
            foreach (var entry in _scores)
            {
                if (entry.ClientId == fromClientId)
                {
                    score = entry.Score;
                    continue;
                }

                rows.Add(entry);
            }

            var toIndex = rows.FindIndex(row => row.ClientId == toClientId);
            if (toIndex < 0)
            {
                // NGO はクライアント ID を使い回さないので、復帰後の ID は既存のどれより大きい。
                // 末尾に足せばクライアント ID の昇順も保たれる。
                rows.Add(new ScoreEntry(toClientId, score));
            }
            else
            {
                // 復帰直後のクライアントに行があることは通常ないが、あれば席が持っていた得点を権威とする。
                rows[toIndex] = new ScoreEntry(toClientId, score);
            }

            _isRewritingScores = true;
            try
            {
                _scores.Clear();
                for (var i = 0; i < rows.Count; i++)
                {
                    _scores.Add(rows[i]);
                }
            }
            finally
            {
                _isRewritingScores = false;

                // 中途半端な状態を見せないよう、書き直しが終わってから 1 回だけ通知する（レビュー L-1）。
                // 途中で例外が出た場合も、抑止したぶんの通知は必ず流す（レビュー L-6）。
                ScoreTableChanged?.Invoke();
            }

            return true;
        }

        /// <summary>得点表の中から指定クライアントの行を探す（最大 12 人なので線形探索で足りる）。</summary>
        private int IndexOfScore(ulong clientId)
        {
            for (var i = 0; i < _scores.Count; i++)
            {
                if (_scores[i].ClientId == clientId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
