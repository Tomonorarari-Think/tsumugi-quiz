using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// クライアント → サーバーの RPC 受信の先頭で呼ぶ、レート制限・警告ログの間引き・強制切断をまとめた
    /// plain クラス（<c>NetworkBehaviour</c> ではない。docs/network.md §9、#52）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GameSession"/> / <see cref="QuestionDistributor"/> / <see cref="TtsSyncCoordinator"/> は、
    /// それぞれ自分の <c>[Rpc(SendTo.Server)]</c> メソッドの先頭 1 行で <see cref="Allow"/> を呼ぶだけでよい
    /// （<c>if (!guard.Allow(senderId, serverNow)) return;</c>）。実体はここに集約し、
    /// 進行中の他 issue（#7 / #16 / #19 / #23）が触っている <c>GameSession*.cs</c> /
    /// <c>QuestionDistributor*.cs</c> 側の変更を最小に保つ。
    /// </para>
    /// <para>
    /// レート判定そのものは <see cref="RateLimiter"/>（<c>TsumugiQuiz.Core</c>、毎秒 20 / バースト 60）に委譲する。
    /// 「超過回数」を数えるのはこのクラスだけ（<see cref="OverageWindow"/>）で、
    /// <see cref="RateLimiter"/> 側は累計カウントを持たない（2 箇所で似た集計を持つと
    /// どちらが正か曖昧になるため、#52 のレビューで 1 箇所に寄せた）。
    /// 直近 <see cref="DisconnectWindowSeconds"/> 秒間の超過回数が <see cref="DisconnectThreshold"/> を
    /// 超えたクライアントは警告ログを残したうえで <c>NetworkManager.DisconnectClient</c> により切断する
    /// （DoS 対策、docs/network.md §9）。
    /// </para>
    /// <para>
    /// 1 つの <see cref="GameSession"/> と <see cref="QuestionDistributor"/> は同じ <c>NetworkObject</c> 上の
    /// 別 <c>NetworkBehaviour</c> なので、それぞれが自分専用のインスタンスを持つ。
    /// つまりレートは「コンポーネントごと」に数える（<c>BuzzRpc</c> / <c>SubmitAnswerRpc</c> は
    /// <see cref="GameSession"/> 側のインスタンス、<c>QuestionReceivedRpc</c> は
    /// <see cref="QuestionDistributor"/> 側のインスタンス、<c>TtsReadyRpc</c> は
    /// <see cref="TtsSyncCoordinator"/> 側のインスタンスで別々に管理する。1 クライアントが複数の RPC 経路に
    /// 分散させれば合計の許容量はコンポーネント数倍まで増えうるという既知の制約がある）。
    /// </para>
    /// <para>
    /// 切断の決定と実際の切断は非同期（<c>NetworkManager.DisconnectClient</c> を呼んでも、
    /// その呼び出し中に積まれていた同じバッチの RPC が即座に届かなくなるとは限らない）。
    /// そのため <see cref="HandleExceeded"/> は切断を決めた時点でこのクライアントの状態を
    /// <see cref="Forget"/> しない。代わりに <see cref="_pendingDisconnectClientIds"/>
    /// （切断済み集合）に入れておき、<see cref="Allow"/> の先頭でこの集合に入っているクライアントは
    /// 即座に false を返す。実際の状態の掃除（<see cref="Forget"/>）は、NGO の切断コールバック経由で
    /// 呼ばれる各コンポーネントの <c>HandleClientDisconnected</c> に任せる。
    /// こうしないと、切断決定の直後に同じクライアントからの新しい RPC が届いたとき
    /// <see cref="RateLimiter"/> がまっさらな新規クライアントとして扱ってしまい、
    /// 切断待ちの間にもう一度バーストぶんだけ処理してしまう（#52 レビュー H2）。
    /// </para>
    /// </remarks>
    public sealed class RpcRateGuard
    {
        /// <summary>docs/network.md §9 の既定レート（毎秒 20 メッセージ）。</summary>
        public const double RatePerSecond = RateLimiter.DefaultRatePerSecond;

        /// <summary>docs/network.md §9 の既定バースト（60 メッセージ）。</summary>
        public const double BurstCapacity = RateLimiter.DefaultBurstCapacity;

        /// <summary>
        /// 切断可否を判定する集計窓の長さ（秒）。
        /// バーストを使い切った直後にレート上限（毎秒 20 件）ちょうどで送り続けても、
        /// 5 秒あれば 100 件（20 × 5）は正当な通信としてあり得るため、閾値
        /// （<see cref="DisconnectThreshold"/> = 120）をわずかに上回るまでの猶予として 5 秒を選んだ。
        /// 短すぎると回線の一時的な遅延・再送の集中を誤検知しやすく、長すぎると被害が広がるまで
        /// 切断が遅れるため、docs/network.md §9 の基準値（20/60）から 1 桁大きい程度の秒数にした。
        /// </summary>
        public const double DisconnectWindowSeconds = 5.0;

        /// <summary>
        /// 集計窓内での超過回数がこれを超えたら切断する。
        /// レート上限（毎秒 20 件）で <see cref="DisconnectWindowSeconds"/>（5 秒）分の超過が
        /// 起こり得る量（20 × 5 = 100）に安全マージンを乗せた値。
        /// </summary>
        public const int DisconnectThreshold = 120;

        /// <summary>警告ログを残す最短間隔（秒）。クライアントごとに 1 秒 1 回へ間引く（docs/network.md §9）。</summary>
        private const double WarnLogIntervalSeconds = 1.0;

        /// <summary>
        /// 切断理由（クライアントの <c>DisconnectReason</c> に残る定型文。内部情報は含めない）。
        /// #208 で英語（"rate limit exceeded"）から日本語にした。受信側は旧版の英語も同じ文言に対応づける。
        /// </summary>
        private const string DisconnectReasonMessage = DisconnectReasonMessages.RateLimitExceeded;

        private readonly NetworkManager _networkManager;
        private readonly RateLimiter _limiter;
        private readonly RejectLogThrottle _warnLogThrottle = new RejectLogThrottle(WarnLogIntervalSeconds);
        private readonly Dictionary<ulong, OverageWindow> _overageWindows = new Dictionary<ulong, OverageWindow>();

        /// <summary>
        /// 切断を決定したクライアント（実際の切断コールバックが来て <see cref="Forget"/> されるまでの間、
        /// バッチに残っている RPC を無条件で無視するための集合。docs/network.md §9、#52 レビュー H2）。
        /// </summary>
        private readonly HashSet<ulong> _pendingDisconnectClientIds = new HashSet<ulong>();

        private readonly string _logTag;

        /// <summary>
        /// ガードを作る。
        /// </summary>
        /// <param name="networkManager">切断（<see cref="NetworkManager.DisconnectClient"/>）に使う。</param>
        /// <param name="logTag">ログの先頭に付ける区分（例: "GameSession"）。</param>
        /// <param name="limiter">差し替え用のレート制限器（省略時は既定のレート・バーストで新規作成）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="networkManager"/> が null。</exception>
        public RpcRateGuard(NetworkManager networkManager, string logTag, RateLimiter limiter = null)
        {
            _networkManager = networkManager != null
                ? networkManager
                : throw new ArgumentNullException(nameof(networkManager));
            _logTag = string.IsNullOrEmpty(logTag) ? nameof(RpcRateGuard) : logTag;
            _limiter = limiter ?? new RateLimiter(RatePerSecond, BurstCapacity);
        }

        /// <summary>
        /// このクライアントからの 1 件の RPC を受理してよいか判定する。
        /// </summary>
        /// <param name="clientId"><c>rpcParams.Receive.SenderClientId</c> から取った送信元 ID（詐称防止）。</param>
        /// <param name="nowSeconds">サーバー時刻（<c>NetworkManager.ServerTime.Time</c>）。</param>
        /// <returns>
        /// 受理してよいなら true。false のとき、呼び出し側は RPC 本来の処理を行わずに戻ること
        /// （レート制限の超過は棄却する。docs/network.md §9）。
        /// </returns>
        public bool Allow(ulong clientId, double nowSeconds)
        {
            // ホスト自身の呼び出し（サーバー兼クライアントとしてのローカル RPC）は常に許可する。
            // ホストはネットワーク越しに連打されているわけではなく、レート制限は他クライアントの
            // DoS 対策が目的なので、ホスト自身の正当な操作を誤って弾かないようにする（#52 レビュー M3）。
            if (clientId == NetworkManager.ServerClientId)
            {
                return true;
            }

            // 切断を決めた相手からの RPC は、実際に切断が反映されるまでの間もすべて無視する（H2）。
            if (_pendingDisconnectClientIds.Contains(clientId))
            {
                return false;
            }

            if (_limiter.Allow(clientId, nowSeconds))
            {
                return true;
            }

            HandleExceeded(clientId, nowSeconds);
            return false;
        }

        /// <summary>
        /// このクライアントの状態を破棄する（NGO の切断コールバック経由で、各コンポーネントの
        /// <c>HandleClientDisconnected</c> から呼ぶ。辞書を無制限に伸ばさないため、docs/network.md §9）。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        public void Forget(ulong clientId)
        {
            _limiter.Forget(clientId);
            _overageWindows.Remove(clientId);
            _warnLogThrottle.Forget(clientId);
            _pendingDisconnectClientIds.Remove(clientId);
        }

        private void HandleExceeded(ulong clientId, double nowSeconds)
        {
            var window = _overageWindows.TryGetValue(clientId, out var existing)
                ? existing
                : new OverageWindow(nowSeconds, 0);

            var elapsed = nowSeconds - window.WindowStartSeconds;
            if (elapsed >= DisconnectWindowSeconds)
            {
                // 集計窓を過ぎているので、この超過を新しい窓の 1 件目として数え直す。
                window = new OverageWindow(nowSeconds, 0);
            }
            else if (elapsed < 0)
            {
                // 時刻が逆行した場合は基準時刻だけ最新の観測値に合わせ、件数は維持する
                // （RateLimiter の Refill と同じ方針。docs: 1 箇所の異常な時刻で集計が壊れないようにする、L9）。
                window = new OverageWindow(nowSeconds, window.Count);
            }

            window = window.WithIncrement();

            if (window.Count > DisconnectThreshold)
            {
                Debug.LogWarning(
                    $"[{_logTag}] クライアント {clientId} を切断します（直近 {DisconnectWindowSeconds:0}秒間の"
                    + $"RPC レート制限超過が {window.Count} 回に達したため。docs/network.md §9）。");

                // Forget はまだ呼ばない（H2）。切断済み集合に入れて以後の Allow を即座に弾くだけにし、
                // 実体の掃除は NGO の切断コールバックが来た HandleClientDisconnected に任せる。
                _overageWindows.Remove(clientId);
                _pendingDisconnectClientIds.Add(clientId);
                _networkManager.DisconnectClient(clientId, DisconnectReasonMessage);
                return;
            }

            _overageWindows[clientId] = window;

            var logDecision = _warnLogThrottle.Evaluate(clientId, nowSeconds);
            if (logDecision.ShouldLog)
            {
                Debug.LogWarning(
                    $"[{_logTag}] クライアント {clientId} からの RPC がレート制限"
                    + $"（毎秒 {RatePerSecond:0} 件、バースト {BurstCapacity:0} 件）を超えたため無視しました。"
                    + logDecision.FormatSuppressedSuffix());
            }
        }

        /// <summary>集計窓の開始時刻と、その窓内での超過回数（不変値）。</summary>
        private readonly struct OverageWindow
        {
            public OverageWindow(double windowStartSeconds, int count)
            {
                WindowStartSeconds = windowStartSeconds;
                Count = count;
            }

            public double WindowStartSeconds { get; }

            public int Count { get; }

            public OverageWindow WithIncrement() => new OverageWindow(WindowStartSeconds, Count + 1);
        }
    }
}
