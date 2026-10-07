using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 問題データの配信を担う <see cref="NetworkBehaviour"/>（docs/network.md §8、
    /// docs/architecture.md §4）。<see cref="GameSession"/> と同じ <c>NetworkObject</c> に載せて使う。
    /// </summary>
    /// <remarks>
    /// <para>
    /// サーバーは出題直前に、現在問の <see cref="QuestionDto"/>（正解を含まない）と
    /// <see cref="PrefetchCount"/> 件分の次問を配信する。クライアントは受け取った DTO を
    /// 問題インデックスで保持し、現在問の受信を <see cref="QuestionReceivedRpc"/> で Ack する。
    /// サーバーは全員分の Ack が揃うか <see cref="AckTimeoutSec"/> 秒経過するまで待ち、
    /// その間セッションは Reading フェーズに留まる（docs/network.md §8.4 / §8.6）。
    /// </para>
    /// <para>
    /// 先読みを 1 件に抑えるのは、メモリダンプで先の問題が見えてしまうのを防ぐため（docs/network.md §8.1）。
    /// </para>
    /// <para>
    /// 画像（<c>imageData</c>）の分割送信は #16。本クラスはテキストのみを扱う。
    /// </para>
    /// <para>
    /// <see cref="GameSession"/> とは別の <see cref="NetworkBehaviour"/> なので、
    /// 同じ <c>NetworkObject</c> 上のコンポーネント順（＝ tick 購読順）に依存しないように作る。
    /// 受付開始の保留時刻は <see cref="HoldUntilServerTime"/>（Ack 期限 + <see cref="AckHoldMarginSec"/>）で、
    /// Ack のタイムアウト判定より必ず後になる。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class QuestionDistributor : NetworkBehaviour
    {
        /// <summary>
        /// 先読みする問題数の既定値（docs/network.md §8.1、<see cref="SettingsDefaults.QuestionPrefetchCount"/>）。
        /// </summary>
        public const int DefaultPrefetchCount = SettingsDefaults.QuestionPrefetchCount;

        /// <summary>
        /// 先読みする問題数の上限（<see cref="AppSettings.MaxQuestionPrefetchCount"/>、docs/room-settings.md §2）。
        /// </summary>
        public const int MaxPrefetchCount = AppSettings.MaxQuestionPrefetchCount;

        /// <summary>先読みする問題数（サーバー側。<see cref="PrefetchCount"/> 経由で設定する）。</summary>
        private int _prefetchCount = DefaultPrefetchCount;

        /// <summary>
        /// 現在問の Ack を待つ上限（秒）。これを超えたら、届いていないクライアントを置いて出題へ進む
        /// （docs/network.md §8.4「3 回失敗したら画像なしで出題する」と同じ考え方で、進行を止めない）。
        /// 早押し判定の <c>tieEpsilonSec</c> と同じく、ユーザーが変える意味のない値なので設定項目にしない
        /// （docs/room-settings.md §2 の備考、統括判断 2026-09-13）。
        /// </summary>
        public const double AckTimeoutSec = 3.0;

        /// <summary>
        /// Ack 期限に足すマージン（秒）。<see cref="GameSession"/> は
        /// 「Ack 期限 + このマージン」まで Reading に留まるため、
        /// タイムアウト判定（本クラスの tick）と受付開始（状態機械の tick）が同値にならず、
        /// 同じ <c>NetworkObject</c> 上のコンポーネント順に依存しない。
        /// </summary>
        public const double AckHoldMarginSec = 0.1;

        /// <summary>
        /// クライアントが保持する DTO の最大件数（現在問 + 先読み）。
        /// 出題ごとに現在問より古い問題を捨てるため、これ以上は溜まらない。
        /// 先読み数（<see cref="PrefetchCount"/>）はホストのアプリ設定なのでクライアントは知らない。
        /// そのため上限値（<see cref="MaxPrefetchCount"/>）で余裕を持たせ、
        /// ホストが先読みを増やしても受け取った DTO を取りこぼさないようにする（#27）。
        /// </summary>
        private const int MaxCachedQuestions = MaxPrefetchCount + 1;

        /// <summary>Ack 待ちが無いことを表す問題インデックス。</summary>
        private const int NoQuestionIndex = -1;

        /// <summary>サーバー側: 送信予定の DTO（<see cref="TryPrepareDistribution"/> で組み立てる）。</summary>
        private readonly List<PreparedQuestion> _prepared = new List<PreparedQuestion>();

        /// <summary>サーバー側: 現在問の Ack がまだ届いていないクライアント。</summary>
        private readonly HashSet<ulong> _pendingAckClientIds = new HashSet<ulong>();

        /// <summary>サーバー側: 現在問の Ack を既に受理したクライアント（重複と待ち対象外を区別するため）。</summary>
        private readonly HashSet<ulong> _ackedClientIds = new HashSet<ulong>();

        /// <summary>サーバー側: 直近の配信で送った問題インデックス（Ack の範囲検証に使う）。</summary>
        private readonly HashSet<int> _distributedIndices = new HashSet<int>();

        /// <summary>クライアント側: 受信済みの DTO（問題インデックス → DTO）。</summary>
        private readonly Dictionary<int, QuestionDto> _receivedQuestions = new Dictionary<int, QuestionDto>();

        private IQuestionSource _questionSource;
        private int _preparedQuestionIndex = NoQuestionIndex;
        private int _awaitingQuestionIndex = NoQuestionIndex;
        private double _ackDeadlineServerTime;
        private NetworkTickSystem _subscribedTickSystem;
        private bool _subscribedClientDisconnect;

        /// <summary>
        /// 送信ループ中に Ack 完了を確定させないためのガード（入れ子にできるようカウンタで持つ）。
        /// ホスト自身への RPC はローカルで同期実行されるため、先読み分や画像チャンクを送り終える前に
        /// 「全員 Ack 完了」になってしまうのを防ぐ。
        /// </summary>
        private int _completionSuppressionDepth;

        /// <summary>Ack 完了の確定を抑えている最中か。</summary>
        private bool IsCompletionSuppressed => _completionSuppressionDepth > 0;

        /// <summary>
        /// 現在問の配信が完了したとき（サーバーのみ）。引数は（問題インデックス, タイムアウトしたか）。
        /// <see cref="GameSession"/> がこれを受けて出題（提示）へ進む。
        /// </summary>
        internal event Action<int, bool> DistributionCompleted;

        /// <summary>DTO を受信したとき（クライアント・ホスト双方で発火）。引数は（問題インデックス, DTO）。</summary>
        public event Action<int, QuestionDto> QuestionDataReceived;

        /// <summary>Ack 待ち中の問題インデックス。待っていなければ -1。</summary>
        internal int AwaitingQuestionIndex => _awaitingQuestionIndex;

        /// <summary>現在問の Ack を待っているか（サーバーのみ意味を持つ）。</summary>
        internal bool IsAwaitingAck => _awaitingQuestionIndex != NoQuestionIndex;

        /// <summary>Ack がまだ届いていないクライアント数（サーバーのみ意味を持つ）。</summary>
        internal int PendingAckCount => _pendingAckClientIds.Count;

        /// <summary>
        /// 現在問の受信確認を返したクライアント（サーバーのみ意味を持つ）。
        /// 次の配信（<see cref="DistributePrepared"/>）まで保持する。
        /// 読み上げ同期（#23、<see cref="TtsSyncCoordinator"/>）が
        /// 「Ready を待つ相手」をここに合わせる: 問題データが届いていないクライアントは
        /// 出題（提示）を処理できないので、読み上げの準備完了も返せない。
        /// </summary>
        internal IReadOnlyCollection<ulong> AckedClientIds => _ackedClientIds;

        /// <summary>
        /// 現在問の Ack 期限（サーバー時刻の秒）。<see cref="TryPrepareDistribution"/> で確定する。
        /// </summary>
        internal double AckDeadlineServerTime => _ackDeadlineServerTime;

        /// <summary>
        /// 受付開始（T0）を保留しておくサーバー時刻。Ack 期限より <see cref="AckHoldMarginSec"/> 秒だけ後ろで、
        /// <see cref="GameSession"/> はこの時刻を状態機械の T0 として使う（docs/network.md §8.6）。
        /// </summary>
        internal double HoldUntilServerTime => _ackDeadlineServerTime + AckHoldMarginSec;

        /// <summary>
        /// 先読みする問題数（<c>question.prefetchCount</c>、docs/network.md §8.1）。
        /// 範囲外の値は <c>0</c>〜<see cref="MaxPrefetchCount"/> に丸める。
        /// </summary>
        /// <remarks>
        /// docs/room-settings.md §2 のとおり <c>question.prefetchCount</c> は
        /// <b>アプリ設定</b>（ホスト機のローカルな技術的挙動）であり、ルーム設定として
        /// クライアントへ同期する値ではない。したがって <see cref="RoomSettingsSync"/> ではなく、
        /// ホストが <c>TsumugiQuiz.Room.AppSettings.QuestionPrefetchCount</c> を読んでここへ設定する
        /// （<c>app-settings.json</c> を実際に読み込む配線は Settings View の #28。
        /// それまでは <see cref="DefaultPrefetchCount"/> のまま）。
        /// </remarks>
        public int PrefetchCount
        {
            get => _prefetchCount;
            set => _prefetchCount = Math.Clamp(value, 0, MaxPrefetchCount);
        }

        /// <summary>
        /// アプリ設定（<c>question.prefetchCount</c>、docs/room-settings.md §2）を
        /// <see cref="PrefetchCount"/> へ反映する（issue #28 Phase 2）。
        /// Boot を経由していない構成（PlayMode の単体テスト等）では <see cref="NetworkBootstrap.Instance"/> が
        /// null なので、既定値（<see cref="DefaultPrefetchCount"/>）のまま何もしない。
        /// 起動後にアプリ設定が保存された場合は <c>NetworkBootstrap.ApplyAppSettings</c> が
        /// スポーン済みの本コンポーネントへ直接書き込む。
        /// </summary>
        private void ApplyAppSettingsPrefetchCount()
        {
            var bootstrap = NetworkBootstrap.Instance;
            if (bootstrap == null)
            {
                return;
            }

            PrefetchCount = bootstrap.CurrentAppSettings.QuestionPrefetchCount;
        }

        /// <summary>
        /// 問題の供給元を設定する（サーバーのみ）。<see cref="GameSession.Configure"/> から渡される。
        /// </summary>
        /// <param name="questionSource">問題の供給元。</param>
        /// <exception cref="ArgumentNullException"><paramref name="questionSource"/> が null のとき。</exception>
        internal void SetQuestionSource(IQuestionSource questionSource)
        {
            _questionSource = questionSource ?? throw new ArgumentNullException(nameof(questionSource));
        }

        /// <inheritdoc />
        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                return;
            }

            ApplyAppSettingsPrefetchCount();

            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _subscribedClientDisconnect = true;

            _subscribedTickSystem = NetworkManager.NetworkTickSystem;
            if (_subscribedTickSystem != null)
            {
                _subscribedTickSystem.Tick += HandleServerTick;
            }
        }

        /// <inheritdoc />
        public override void OnNetworkDespawn()
        {
            UnsubscribeClientDisconnect();
            UnsubscribeTick();
            ResetState();
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            UnsubscribeClientDisconnect();
            UnsubscribeTick();

            // 復号済みのテクスチャは GC の対象にならないので、破棄時に必ず解放する（#16）。
            ResetImageCache();
            base.OnDestroy();
        }

        /// <summary>
        /// 現在問と先読み分の DTO を組み立て、サイズ上限を検証する（サーバーのみ、送信はまだ行わない）。
        /// 検証で落ちた場合は何も送らず false を返すので、呼び出し側は出題を中止できる。
        /// Ack 期限（<see cref="AckDeadlineServerTime"/> / <see cref="HoldUntilServerTime"/>）も
        /// ここで確定するので、呼び出し側は送信前に「いつまで Reading に留めるか」を決められる。
        /// </summary>
        /// <param name="questionIndex">現在問の問題インデックス。</param>
        /// <param name="error">失敗理由（ログ用）。成功時は null。</param>
        /// <returns>配信内容を用意できたら true。</returns>
        internal bool TryPrepareDistribution(int questionIndex, out string error)
        {
            CancelPrepared();

            if (!IsSpawned || !IsServer)
            {
                error = "問題の配信はサーバーでのみ行えます。";
                return false;
            }

            if (_questionSource == null)
            {
                error = "問題の供給元が設定されていません。";
                return false;
            }

            if (!_questionSource.TryGetQuestion(questionIndex, out var question))
            {
                error = $"問題インデックス {questionIndex} は範囲外です（問題数 {_questionSource.Count}）。";
                return false;
            }

            if (!question.Type.HasValue)
            {
                error = $"出題形式（type）が設定されていない問題は配信できません（id: {question.Id}）。";
                return false;
            }

            var dto = QuestionDto.From(question, out var tagsTruncated);
            if (tagsTruncated)
            {
                // tags は参考表示用なので、上限超過は切り詰めて配信し、出題は止めない（統括判断 2026-09-13）。
                Debug.LogWarning(
                    $"[QuestionDistributor] 問題 {questionIndex}（id: {question.Id}）の tags が上限"
                    + $"（{QuestionDto.MaxTagCount} 件 × {QuestionDto.MaxTagLength} 文字）を超えていたため切り詰めて配信します。");
            }

            if (!dto.TryValidate(out var reason))
            {
                error = $"問題 {questionIndex}（id: {question.Id}）は配信できません: {reason}";
                return false;
            }

            _prepared.Add(new PreparedQuestion(questionIndex, dto));
            AppendPrefetch(questionIndex);

            _preparedQuestionIndex = questionIndex;

            // 画像付きの問題は、チャンク送信に要する時間ぶん Ack 期限を延ばす
            // （3 秒 + サイズ / 推定帯域、上限 15 秒。docs/network.md §8.4、#16）。
            var imageAckExtensionSec = PrepareImages(questionIndex);
            _ackDeadlineServerTime = NetworkManager.ServerTime.Time + AckTimeoutSec + imageAckExtensionSec;
            error = null;
            return true;
        }

        /// <summary>用意した配信内容を破棄する（出題が始められなかったときに呼ぶ）。</summary>
        internal void CancelPrepared()
        {
            _prepared.Clear();
            _preparedQuestionIndex = NoQuestionIndex;
        }

        /// <summary>
        /// <see cref="TryPrepareDistribution"/> で用意した DTO を配信し、現在問の Ack 待ちを始める（サーバーのみ）。
        /// </summary>
        /// <exception cref="InvalidOperationException">配信内容が用意されていないとき。</exception>
        internal void DistributePrepared()
        {
            if (_preparedQuestionIndex == NoQuestionIndex)
            {
                throw new InvalidOperationException(
                    "配信内容が用意されていません。先に TryPrepareDistribution を成功させてください。");
            }

            // Ack 待ちの登録は送信より先に済ませる。ホスト自身への RPC はローカルで同期実行されるため、
            // 送信後に登録すると「ホストの Ack が先に届いて誰も待っていない」状態になる。
            _pendingAckClientIds.Clear();
            _ackedClientIds.Clear();
            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                _pendingAckClientIds.Add(clientId);
            }

            _distributedIndices.Clear();
            for (var i = 0; i < _prepared.Count; i++)
            {
                _distributedIndices.Add(_prepared[i].Index);
            }

            _awaitingQuestionIndex = _preparedQuestionIndex;

            // 画像の Ack 待ちの登録も送信より先に済ませる（#16）。チャンクの送信自体は tick で平準化する。
            BeginImageDistribution(_awaitingQuestionIndex);

            BeginCompletionSuppression();
            try
            {
                for (var i = 0; i < _prepared.Count; i++)
                {
                    var entry = _prepared[i];
                    QuestionDataRpc(entry.Index, entry.Index == _awaitingQuestionIndex, entry.Dto);
                }
            }
            finally
            {
                EndCompletionSuppression();
                CancelPrepared();
            }

            if (IsAckComplete)
            {
                // ホストしか居ない場合など、送信ループ中に全員の Ack が揃っているケース。
                // 画像付きの問題では画像 Ack が残るので、ここでは完了しない（tick の送信で揃う）。
                CompleteDistribution(timedOut: false);
            }
        }

        /// <summary>先読み分（末尾を超える分は無し）の DTO を積む。検証に落ちた先読みは黙って捨てず警告する。</summary>
        private void AppendPrefetch(int questionIndex)
        {
            for (var offset = 1; offset <= _prefetchCount; offset++)
            {
                var nextIndex = questionIndex + offset;
                if (!_questionSource.TryGetQuestion(nextIndex, out var next))
                {
                    return;
                }

                if (!next.Type.HasValue)
                {
                    Debug.LogWarning(
                        $"[QuestionDistributor] 先読み（問題 {nextIndex}、id: {next.Id}）は出題形式が未設定のため配信しません。");
                    continue;
                }

                var nextDto = QuestionDto.From(next, out _);
                if (!nextDto.TryValidate(out var reason))
                {
                    // 先読みが配信できなくても現在問の進行は続けられるため、警告だけ残して落とす。
                    Debug.LogWarning(
                        $"[QuestionDistributor] 先読み（問題 {nextIndex}、id: {next.Id}）を配信できません: {reason}");
                    continue;
                }

                _prepared.Add(new PreparedQuestion(nextIndex, nextDto));
            }
        }

        /// <summary>
        /// 画像チャンクを平準化して送りつつ、Ack のタイムアウトを監視する（サーバーのみ）。
        /// 画像の送信は Ack 待ちが終わったあと（先読み分）も続くため、待ちの有無より先に行う。
        /// </summary>
        private void HandleServerTick()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            DrainImageSends();

            if (_awaitingQuestionIndex == NoQuestionIndex)
            {
                return;
            }

            if (NetworkManager.ServerTime.Time < _ackDeadlineServerTime)
            {
                return;
            }

            CompleteDistribution(timedOut: true);
        }

        /// <summary>現在問の Ack 待ちを終える。タイムアウト時は誰が届いていないかをログに残す。</summary>
        private void CompleteDistribution(bool timedOut)
        {
            var questionIndex = _awaitingQuestionIndex;
            _awaitingQuestionIndex = NoQuestionIndex;

            if (timedOut && _pendingAckClientIds.Count > 0)
            {
                Debug.LogWarning(
                    $"[QuestionDistributor] 問題 {questionIndex} の受信確認が {AckTimeoutSec} 秒で揃いませんでした"
                    + $"（未受信: {string.Join(", ", _pendingAckClientIds)}）。出題へ進みます。");
            }

            _pendingAckClientIds.Clear();

            // 画像 Ack の待ちも同じタイミングで解く（届かなかったクライアントは画像なしで進行、#16）。
            CompleteImageDistribution(questionIndex, timedOut);

            DistributionCompleted?.Invoke(questionIndex, timedOut);
        }

        /// <summary>切断したクライアントの Ack は待たず、棄却ログの記録も捨てる（辞書を伸ばさないため）。</summary>
        private void HandleClientDisconnected(ulong clientId)
        {
            _rejectLogger?.Forget(clientId); // #72: 棄却ログの間引き記録も切断時に捨てる。
            _rpcRateGuard?.Forget(clientId); // #52: レート制限の状態も切断時に捨てる（docs/network.md §9）。
            _ackedClientIds.Remove(clientId);

            // 画像の Ack 待ち・再送記録からも外す（#16）。
            HandleImageClientDisconnected(clientId);

            if (_awaitingQuestionIndex == NoQuestionIndex)
            {
                _pendingAckClientIds.Remove(clientId);
                return;
            }

            _pendingAckClientIds.Remove(clientId);

            if (IsAckComplete && !IsCompletionSuppressed)
            {
                CompleteDistribution(timedOut: false);
            }
        }

        private void ResetState()
        {
            CancelPrepared();
            _pendingAckClientIds.Clear();
            _ackedClientIds.Clear();
            _distributedIndices.Clear();
            _rejectLogger = null; // #72: 次のスポーンで作り直す（RpcRateGuard と同じ遅延生成の方針）。
            _rpcRateGuard = null; // #52: 次のスポーンで新しい NetworkManager に対して作り直す。
            ResetImageDistribution();
            ResetCache();
            _awaitingQuestionIndex = NoQuestionIndex;
            _ackDeadlineServerTime = 0.0;
            _completionSuppressionDepth = 0;
        }

        /// <summary>Ack 完了の確定を一時的に止める（入れ子可）。</summary>
        private void BeginCompletionSuppression()
        {
            _completionSuppressionDepth++;
        }

        /// <summary>Ack 完了の確定の抑止を 1 段解く。</summary>
        private void EndCompletionSuppression()
        {
            if (_completionSuppressionDepth > 0)
            {
                _completionSuppressionDepth--;
            }
        }

        private void UnsubscribeTick()
        {
            if (_subscribedTickSystem == null)
            {
                return;
            }

            _subscribedTickSystem.Tick -= HandleServerTick;
            _subscribedTickSystem = null;
        }

        private void UnsubscribeClientDisconnect()
        {
            if (!_subscribedClientDisconnect)
            {
                return;
            }

            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            _subscribedClientDisconnect = false;
        }

        /// <summary>送信予定の 1 件（問題インデックスと DTO の対）。</summary>
        private readonly struct PreparedQuestion
        {
            public PreparedQuestion(int index, QuestionDto dto)
            {
                Index = index;
                Dto = dto;
            }

            public int Index { get; }

            public QuestionDto Dto { get; }
        }
    }
}
