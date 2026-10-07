namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="NetworkService"/> のうち、ゲーム進行・問題配信の器
    /// （<see cref="GameSession"/> + <see cref="QuestionDistributor"/>）の生成に関わる部分
    /// （#13、docs/network.md §8.6）。実際の探索・スポーンは <see cref="GameSessionSpawner"/> が行う。
    /// </summary>
    public sealed partial class NetworkService
    {
        private readonly GameSessionSpawner _gameSessionSpawner;

        /// <summary>
        /// ホスト開始時にスポーンした <see cref="GameSession"/>。
        /// クライアントとして接続している場合や、プレハブが登録されていない場合は null。
        /// 出題を始める前に <see cref="GameSession.Configure"/> を呼ぶのは呼び出し側（UI）の責務で、
        /// 本番経路ではロビーの「ゲーム開始」（<c>TsumugiQuiz.UI.Views.Lobby.LobbyView</c>、#95）が呼ぶ。
        /// </summary>
        public GameSession ActiveGameSession => _gameSessionSpawner.ActiveGameSession;

        /// <summary>
        /// スポーン済みの <see cref="GameSession"/> を探す（#14、docs/network.md §1.2
        /// 「実装済みの <c>NetworkVariable</c>」）。ホストなら <see cref="ActiveGameSession"/> と同じものを返す。クライアントは
        /// <see cref="Spawn"/> を呼ばない（ホストのみが呼ぶ API）ため、NGO の同期でスポーンされた
        /// <see cref="Unity.Netcode.NetworkObject"/> の一覧から探す。UI 層（<c>GameView</c>）は
        /// ホスト・クライアントのどちらでも同じ呼び出しで自分の <see cref="GameSession"/> を得られる。
        /// </summary>
        /// <returns>
        /// 見つかった <see cref="GameSession"/>。まだスポーンされていない場合と、本サービスが
        /// <see cref="Dispose"/> 済みの場合は null。
        /// </returns>
        /// <remarks>
        /// #101: 本メソッドだけは破棄済みでも例外を投げず null を返す（他の API は
        /// <see cref="System.ObjectDisposedException"/> のまま）。UI 層（<c>GameView</c> /
        /// <c>LobbyView</c> / <c>SettingsView</c>）がセッションを見つけるまで毎フレーム呼ぶ「探索」用の
        /// API であり、ホスト停止・アプリ終了・シーンアンロードで本サービスが破棄された直後にも
        /// 呼ばれうるため。そこで例外を投げると、呼び出し側すべてに try/catch を強いるうえ、
        /// 捕捉漏れが毎フレームの例外ログになる（実際に PlayMode テストが連鎖失敗した）。
        /// 「見つからない」も「もう探せない」も、呼び出し側から見れば同じ「セッション無し」として扱える。
        /// </remarks>
        public GameSession FindActiveGameSession()
        {
            // IsDisposed（#116）を見る。Dispose() 実行中（_disposing）も含めて null にすることで、
            // Stop()/Dispose() の中で発火するイベントのハンドラから呼ばれた場合も一貫して
            // 「セッション無し」を返す。
            if (IsDisposed)
            {
                return null;
            }

            return _gameSessionSpawner.FindGameSession();
        }

        /// <summary>
        /// <see cref="GameSession"/>（<see cref="QuestionDistributor"/> を含むネットワークプレハブ）を
        /// ホスト側でスポーンする。<see cref="StartHost"/> の成功時に自動で呼ばれるため、
        /// 通常は外から呼ぶ必要はない（冪等）。
        /// </summary>
        /// <returns>スポーンした <see cref="GameSession"/>。できなかった場合は null。</returns>
        internal GameSession SpawnGameSession()
        {
            ThrowIfDisposed();
            return _gameSessionSpawner.Spawn();
        }

        /// <summary>
        /// スポーン済みの <see cref="GameSession"/> を Despawn する（<see cref="Stop"/> から呼ばれる）。
        /// </summary>
        internal void DespawnGameSession()
        {
            ThrowIfDisposed();
            _gameSessionSpawner.Despawn();
        }
    }
}
