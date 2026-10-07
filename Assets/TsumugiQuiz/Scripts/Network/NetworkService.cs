using System;
using TsumugiQuiz.Core.Network;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="NetworkManager"/> / <see cref="UnityTransport"/> を薄く包み、
    /// ホスト開始・クライアント接続・切断通知だけを提供するサービス（docs/network.md §2.1 / §2.2 / §2.4）。
    /// UI からは本クラスだけを触り、NGO の API を直接呼ばない。
    ///
    /// NGO の <c>Shutdown()</c> はその場では止まらず、フレーム終端で実際の停止処理が走る。
    /// そのため停止直後に開始し直す場合は <see cref="StartHostWhenReady"/> /
    /// <see cref="StartClientWhenReady"/>（停止完了を待つコルーチン）を使う。
    /// </summary>
    public sealed partial class NetworkService : IDisposable
    {
        private readonly NetworkManager _networkManager;
        private readonly UnityTransport _transport;
        private readonly ConnectionApprovalHandler _approvalHandler;

        /// <summary>
        /// ポート再試行中は true。NGO はバインド失敗時に <c>OnTransportFailure</c> を発火してから
        /// <c>StartHost()</c> が false を返す（<c>NetworkManager.cs</c> L1474-1479）ため、
        /// 再試行の途中で外部へ「Transport 失敗」を通知しないよう抑止する。
        /// </summary>
        private bool _startingHost;

        private bool _disposed;

        /// <summary>
        /// <see cref="Dispose"/> の実行中（<see cref="Stop"/> 呼び出しから <see cref="_disposed"/> を
        /// 立てるまでの間）は true。<see cref="Stopped"/> は <see cref="Stop"/> の中で発火するため、
        /// そのハンドラから見ても <see cref="IsDisposed"/> が true になるようにする（#116）。
        /// </summary>
        private bool _disposing;

        /// <summary>
        /// クライアント専用ピアとして動いている間に <see cref="Stop"/> が呼ばれたら true（#208）。
        /// NGO は自分で停止したときもクライアント側の切断コールバックを呼ぶ（Transport の停止で切断イベントが出る）ため、
        /// これを見て <see cref="DisconnectedFromHost"/> を出さないようにする。<see cref="StartClient"/> で下ろす。
        /// </summary>
        private bool _localClientStopRequested;

        /// <summary>
        /// サービスを生成する。<see cref="NetworkManager"/> には <see cref="UnityTransport"/> が
        /// 設定されている必要がある。
        /// </summary>
        /// <param name="networkManager">対象の <see cref="NetworkManager"/>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="networkManager"/> が null。</exception>
        /// <exception cref="InvalidOperationException">Transport が <see cref="UnityTransport"/> でない。</exception>
        public NetworkService(NetworkManager networkManager)
        {
            _networkManager = networkManager != null
                ? networkManager
                : throw new ArgumentNullException(nameof(networkManager));

            _transport = _networkManager.NetworkConfig?.NetworkTransport as UnityTransport;
            if (_transport == null)
            {
                throw new InvalidOperationException(
                    "NetworkManager に UnityTransport が設定されていません。Boot シーンの設定を確認してください。");
            }

            // 画像チャンク（16KB）を送れるように既定の 6144 から引き上げる（K14 改訂）。
            // ホスト・クライアントで同じ値になっている必要があるため、接続前に必ず適用する。
            _transport.MaxPayloadSize = NetworkConstants.MaxPayloadSizeBytes;

            _approvalHandler = new ConnectionApprovalHandler(_networkManager, ConnectionApprovalPolicy.Default);
            _gameSessionSpawner = new GameSessionSpawner(_networkManager);

            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _networkManager.OnTransportFailure += HandleTransportFailure;
        }

        /// <summary>接続承認ハンドラ。ルーム設定の確定時に <c>SetPolicy</c> で条件を差し替える。</summary>
        public ConnectionApprovalHandler ApprovalHandler => _approvalHandler;

        /// <summary>
        /// 実際に待ち受け・接続に使ったポート。未開始なら 0。
        /// ホスト開始時にポート 0（OS に選ばせる）を指定した場合も、実際にバインドされたポートが入る。
        /// </summary>
        public ushort ActivePort { get; private set; }

        /// <summary>
        /// 自分のプレイヤー名（正規化済み）。ホスト開始・クライアント接続のときに確定する。
        /// ロビーのプレイヤー一覧（#7）で使う。
        /// </summary>
        public string LocalPlayerName { get; private set; } = string.Empty;

        /// <summary>ホストとして動作中か。</summary>
        public bool IsHost => _networkManager.IsHost;

        /// <summary>クライアント（ホスト兼用を含む）として動作中か。</summary>
        public bool IsClient => _networkManager.IsClient;

        /// <summary>サーバーが待ち受け中か。</summary>
        public bool IsListening => _networkManager.IsListening;

        /// <summary>停止処理中（<c>Shutdown()</c> 済みで、まだ実際の停止が終わっていない）か。</summary>
        public bool IsShutdownInProgress => _networkManager.ShutdownInProgress;

        /// <summary>
        /// <see cref="Dispose"/> 済み、または実行中か（#101 / #116）。破棄後は二度と使えるように
        /// ならないため、毎フレーム本サービスを参照する UI（<c>GameView</c> の再探索ループなど）は、
        /// これが true になったら参照そのものをやめる。<see cref="Dispose"/> 実行中（<see cref="Stop"/> の
        /// 呼び出しから完了までの間、<see cref="Stopped"/> ハンドラの中を含む）も true になる。
        /// </summary>
        public bool IsDisposed => _disposed || _disposing;

        /// <summary>クライアントが接続したとき（サーバー側でもホスト自身の接続でも発火する）。</summary>
        public event Action<ulong> ClientConnected;

        /// <summary>クライアントが切断したとき（サーバー視点。切断したクライアント ID を渡す）。</summary>
        public event Action<ulong> ClientDisconnected;

        /// <summary>
        /// 自分（クライアント）がホストから切断されたとき。引数は画面に出す日本語の文言で、空にならない。
        /// ConnectionApproval で拒否された場合もここに届く（docs/network.md §2.2 の 5）。
        /// NGO の <c>DisconnectReason</c> を <see cref="DisconnectReasonLocalizer"/> で対応づけたもので、このアプリの
        /// 日本語の理由はそのまま、NGO の英語の理由は日本語の文言に、未知の理由は汎用の文言になる（#208、docs/network.md §2.4）。
        /// 元の理由は詳細ログに残す。表示先はリッチテキストを解釈させないこと（#206）。
        /// 自分で <see cref="Stop"/> したことによる切断では発火しない（#208）。
        /// </summary>
        public event Action<string> DisconnectedFromHost;

        /// <summary>
        /// Transport が失敗したとき。ポート再試行の途中で起きる一時的な失敗は通知せず、
        /// 全ポートで開始できなかった場合と、稼働中に起きた失敗だけを通知する。
        /// </summary>
        public event Action TransportFailed;

        /// <summary>
        /// <see cref="Stop"/> が呼ばれるたびに（実際に稼働していたかに関わらず）発火する。
        /// <see cref="NetworkBootstrap"/> はこれを購読し、保持している
        /// <c>HostConnectivityService</c> のポートマッピング解放・破棄を行う（#5 C-1）。
        /// </summary>
        public event Action Stopped;

        /// <summary>
        /// ホストを開始する。指定ポートが使用中なら +1 しながら最大
        /// <see cref="NetworkConstants.PortRetryCount"/> 回まで試す（docs/network.md §2.1）。
        /// </summary>
        /// <param name="startPort">最初に試すポート。0 を渡すと OS が空きポートを選ぶ。</param>
        /// <param name="maxPlayers">参加人数の上限。null なら制限なし。</param>
        /// <param name="hostPlayerName">ホスト自身のプレイヤー名。指定した場合のみ書式を検証する。</param>
        /// <param name="spawnGameSession">
        /// 開始成功時に <see cref="GameSession"/>（#12 / #13）をスポーンするか。
        /// 接続だけを試すテストなどでは false にできる。
        /// </param>
        /// <returns>開始結果。成功時は実際に使ったポートが入る。</returns>
        public NetworkStartResult StartHost(
            ushort startPort = NetworkConstants.DefaultPort,
            int? maxPlayers = null,
            string hostPlayerName = null,
            bool spawnGameSession = true)
        {
            ThrowIfDisposed();

            if (_networkManager.ShutdownInProgress)
            {
                return NetworkStartResult.Fail(ShutdownInProgressMessage);
            }

            if (_networkManager.IsListening || _networkManager.IsClient)
            {
                return NetworkStartResult.Fail("すでにネットワークを開始しています。");
            }

            if (hostPlayerName != null)
            {
                if (!PlayerNameValidator.TryNormalize(hostPlayerName, out var normalizedHostName))
                {
                    return NetworkStartResult.Fail(ConnectionRejectionMessages.InvalidPlayerName);
                }

                LocalPlayerName = normalizedHostName;
            }

            // ビルドの違うクライアントは承認で拒否する（#204、docs/network.md §2.3「バージョンとビルドの一致」）。
            _approvalHandler.SetPolicy(ConnectionApprovalPolicy.Default
                .WithMaxPlayers(maxPlayers)
                .WithExpectedClientBuildHash(LocalBuildIdentity.BuildHash));
            _approvalHandler.Register();

            // ホスト自身の承認ペイロードは使われない（ホストは拒否できない）が、
            // 前回クライアントとして接続したときの残骸を持ち越さないようにクリアしておく。
            _networkManager.NetworkConfig.ConnectionData = Array.Empty<byte>();

            var portRangeExceeded = false;
            _startingHost = true;
            try
            {
                for (var attempt = 0; attempt < NetworkConstants.PortRetryCount; attempt++)
                {
                    var candidate = startPort + attempt;
                    if (candidate > ushort.MaxValue)
                    {
                        portRangeExceeded = true;
                        break;
                    }

                    var port = (ushort)candidate;

                    // NGO / UTP に渡す前に自前で空きを確認する。UnityTransport はバインド失敗時に
                    // Debug.LogError を出すため、使用中とわかっているポートを渡すとログが赤く汚れる
                    // （ホストのプレイヤーログにも、verify.ps1 のログ検査にも出てしまう）。
                    if (!NetworkPortProbe.IsUdpPortAvailable(port))
                    {
                        Debug.LogWarning($"[NetworkService] ポート {port} は使用中です。次のポートを試します。");
                        continue;
                    }

                    // listenAddress に 0.0.0.0 を明示して全 NIC で待ち受ける（docs/network.md §2.1）。
                    // forceOverrideCommandLineArgs: true にしているのは、UnityTransport 自身が持つ
                    // -port / -ip の解析を無効にするため。有効のままだと再試行しても同じポートに固定される。
                    _transport.SetConnectionData(true, NetworkConstants.AnyAddress, port, NetworkConstants.AnyAddress);

                    if (_networkManager.StartHost())
                    {
                        ActivePort = NetworkPortProbe.ResolveBoundPort(_transport, port);

                        if (spawnGameSession)
                        {
                            // ゲーム進行・問題配信の器をホスト側でスポーンする（#12 / #13、docs/network.md §8.6）。
                            // クライアントには NGO のスポーン同期で届く。
                            SpawnGameSession();
                        }

                        return NetworkStartResult.Ok(ActivePort);
                    }

                    // StartHost() が false を返した時点で NGO 側は内部シャットダウン済みなので、
                    // 次のポートでそのまま再試行してよい。
                    Debug.LogWarning($"[NetworkService] ポート {port} で待ち受けを開始できませんでした。次のポートを試します。");
                }
            }
            finally
            {
                _startingHost = false;
            }

            _approvalHandler.Unregister();

            // ここまで来たら 1 つも開始できていない。この時点で初めて Transport 失敗として通知する。
            TransportFailed?.Invoke();

            return NetworkStartResult.Fail(portRangeExceeded
                ? $"ポート番号が上限（{ushort.MaxValue}）を超えました。開始ポートを小さくしてください。"
                : $"ポート {startPort} から {NetworkConstants.PortRetryCount} 個のポートがすべて使用中です。他のアプリを終了するか、ポートを変更してください。");
        }

        /// <summary>
        /// クライアントとして接続する。承認ペイロードを組み立ててから <c>StartClient()</c> を呼ぶ
        /// （docs/network.md §2.2）。
        /// </summary>
        /// <param name="address">ホストの IP アドレス。</param>
        /// <param name="port">ホストのポート。</param>
        /// <param name="playerName">プレイヤー名（1〜16 文字）。</param>
        /// <param name="clientBuildHash">
        /// 承認ペイロードに載せるビルドの識別子。null（省略）なら自分のビルドの識別子（<see cref="LocalBuildIdentity.BuildHash"/>）。
        /// ホストはこれが自分の識別子と完全一致しなければ拒否する（#204）。null 以外を渡すのは、別のビルドを再現するテストだけ。
        /// </param>
        /// <returns>開始結果。接続の成否そのものは非同期で、承認結果は各イベントで届く。</returns>
        public NetworkStartResult StartClient(string address, ushort port, string playerName, string clientBuildHash = null)
        {
            ThrowIfDisposed();

            if (_networkManager.ShutdownInProgress)
            {
                return NetworkStartResult.Fail(ShutdownInProgressMessage);
            }

            if (_networkManager.IsListening || _networkManager.IsClient)
            {
                return NetworkStartResult.Fail("すでにネットワークを開始しています。");
            }

            // NGO はクライアントの NetworkConfig.ConnectionApproval が false だと承認ペイロードを
            // 送らない（NetworkConnectionManager.SendConnectionRequest の ShouldSendConnectionData）。
            // さらに NetworkConfig.GetConfig() のハッシュにもこの値が入るため、ホストと一致していないと
            // 設定不一致で弾かれる。開始してから謎の拒否になるより前に落とす。
            if (!_networkManager.NetworkConfig.ConnectionApproval)
            {
                Debug.LogError(
                    "[NetworkService] NetworkConfig.ConnectionApproval が false です。承認ペイロードが送信されないため接続を開始しません。");
                return NetworkStartResult.Fail(
                    "接続設定が正しくありません（承認が無効）。Boot シーンの NetworkManager 設定を確認してください。");
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                return NetworkStartResult.Fail("接続先のアドレスが指定されていません。");
            }

            if (port == 0)
            {
                return NetworkStartResult.Fail("接続先のポートが指定されていません。");
            }

            if (!PlayerNameValidator.TryNormalize(playerName, out var normalizedName))
            {
                return NetworkStartResult.Fail(ConnectionRejectionMessages.InvalidPlayerName);
            }

            // 同じホストに入り直す場合は、前回もらった再接続トークンを載せる（#69）。
            // 名前だけでは席・得点へ復帰できず、トークンが一致したときだけ元のエントリへ戻る。
            var reconnectToken = LoadReconnectToken(address, port);
            var payload = new ConnectionPayload(
                NetworkConstants.ProtocolVersion,
                normalizedName,
                clientBuildHash ?? LocalBuildIdentity.BuildHash,
                reconnectToken);
            if (!ConnectionPayloadCodec.TrySerialize(payload, out var payloadBytes, out var serializeReason))
            {
                return NetworkStartResult.Fail(ConnectionRejectionMessages.Create(
                    serializeReason, NetworkConstants.ProtocolVersion, NetworkConstants.ProtocolVersion));
            }

            _networkManager.NetworkConfig.ConnectionData = payloadBytes;
            _localClientStopRequested = false;

            // クライアントは listenAddress を指定しない（既定で接続先アドレスが使われる）。
            _transport.SetConnectionData(true, address, port);

            if (!_networkManager.StartClient())
            {
                return NetworkStartResult.Fail("接続を開始できませんでした。参加コードとネットワーク設定を確認してください。");
            }

            LocalPlayerName = normalizedName;
            ActivePort = port;
            return NetworkStartResult.Ok(port);
        }

        /// <summary>
        /// ホスト / クライアントを停止する。停止していないときは何もしない（ただし <see cref="Stopped"/> は
        /// 呼び出しのたびに発火する。既に停止済みの呼び出しに対しても後始末を確実に行わせるため）。
        /// 実際の停止はフレーム終端で行われるため、直後に開始し直す場合は
        /// <see cref="StartHostWhenReady"/> / <see cref="StartClientWhenReady"/> を使う。
        /// </summary>
        public void Stop()
        {
            if (_disposed)
            {
                return;
            }

            DespawnGameSession();

            if (_networkManager.IsListening || _networkManager.IsClient)
            {
                // 自分で止めた切断を「ホストから切断された」と通知しない（#208）。NGO の切断コールバックは
                // フレーム終端の実際の停止処理の中で呼ばれるので、Shutdown() より前に立てておけば間に合う。
                if (!_networkManager.IsServer)
                {
                    _localClientStopRequested = true;
                }

                _networkManager.Shutdown();
            }

            DetachSessionTokenListener();

            _approvalHandler.Unregister();
            ActivePort = 0;
            Stopped?.Invoke();
        }

        /// <summary>
        /// <see cref="NetworkManager"/> のイベント購読解除・<see cref="ConnectionApprovalHandler"/> の破棄まで行う。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 再入ガードは <c>_disposed</c> ではなく <see cref="IsDisposed"/>（<c>_disposed || _disposing</c>）で
        /// 行う（#116 M-2）。<see cref="Stop"/> の中で発火する <see cref="Stopped"/> のハンドラが
        /// 本メソッドを再入して呼んだ場合、<c>_disposed</c> だけを見ていると（まだ false のため）
        /// もう一度 <see cref="Stop"/> 以下を実行してしまい無限再帰になりうる。<see cref="IsDisposed"/> なら
        /// 最初の呼び出しで立てた <c>_disposing</c> により、再入した呼び出しはここで即座に戻る。
        /// </para>
        /// <para>
        /// <c>_disposed</c> 自体は <see cref="Stop"/> の早期 return が見ている値なので、先頭では
        /// <c>_disposing</c> だけを立てる。こうすると <see cref="Stop"/> はいつもどおり実行され、
        /// その中で発火する <see cref="Stopped"/> のハンドラからも <see cref="IsDisposed"/> が
        /// true に見える（#116 M-1）。<c>_disposed</c> は <c>finally</c> で立てる（M-3）。
        /// これにより、<see cref="Stop"/> や購読解除の途中で想定外の例外が出た場合でも、
        /// 本サービスが「破棄済み」の状態のまま取り残されずに済む。
        /// </para>
        /// </remarks>
        /// <inheritdoc />
        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            _disposing = true;

            try
            {
                Stop();

                if (_networkManager != null)
                {
                    _networkManager.OnClientConnectedCallback -= HandleClientConnected;
                    _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
                    _networkManager.OnTransportFailure -= HandleTransportFailure;
                }

                _approvalHandler.Dispose();
            }
            finally
            {
                _disposed = true;
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            // 自分（クライアント専用ピア）の接続が完了したら、ホストから届く再接続トークンを
            // 受け取れるようにする（#69。ホストは自分にトークンを発行しない）。
            if (!_networkManager.IsServer && clientId == _networkManager.LocalClientId)
            {
                AttachSessionTokenListener();
            }

            ClientConnected?.Invoke(clientId);
        }

        /// <summary>
        /// 切断通知。サーバー視点（誰かが抜けた）と、クライアント視点（自分が切られた）を区別して通知する
        /// （docs/network.md §2.4）。
        /// </summary>
        private void HandleClientDisconnected(ulong clientId)
        {
            var isLocalClient = !_networkManager.IsServer && clientId == _networkManager.LocalClientId;
            if (isLocalClient)
            {
                // 破棄された LobbyState の購読を残さない（#69）。
                DetachSessionTokenListener();

                if (_localClientStopRequested)
                {
                    // 自分で Stop() した切断（#208）。画面の遷移は Stop() を呼んだ側が済ませている。
                    Debug.Log("[NetworkService] 自分で停止したため、ホストからの切断としては通知しません。");
                    return;
                }

                // 承認拒否の Reason はここで受け取れる（NGO が DisconnectReason に入れる）。
                // 日本語の文言に対応づけてから渡す（#208）。
                DisconnectedFromHost?.Invoke(LocalizeDisconnectReason(_networkManager.DisconnectReason));
                return;
            }

            ClientDisconnected?.Invoke(clientId);
        }

        /// <summary>
        /// 切断理由を画面に出す日本語の文言に対応づける（#208、docs/network.md §2.4）。
        /// 元の理由と別の文言にしたときは、元の理由を詳細ログに残す。改行・制御文字を除き
        /// <see cref="DisconnectReasonSanitizer.MaxLogLength"/> 文字までに整えてから書く（ログの行を偽装されないように。#206）。
        /// 未知の理由（改変されたホスト・未対応の NGO の文言）は警告、既知の英語の理由は情報として残す。
        /// </summary>
        private static string LocalizeDisconnectReason(string reason)
        {
            var localized = DisconnectReasonLocalizer.Localize(reason);
            if (localized.IsOriginalShown || localized.Category == DisconnectReasonCategory.None)
            {
                return localized.Message;
            }

            var original = DisconnectReasonSanitizer.SanitizeForLog(reason);
            var detail = $"[NetworkService] 切断理由を「{localized.Message}」として表示します（分類 {localized.Category}、"
                + $"元の長さ {reason.Length}、元の理由: {original}）。";
            if (localized.Category == DisconnectReasonCategory.Unknown)
            {
                Debug.LogWarning(detail);
            }
            else
            {
                Debug.Log(detail);
            }

            return localized.Message;
        }

        /// <summary>
        /// Transport 失敗。ポート再試行中（<see cref="_startingHost"/>）の失敗は、次のポートで
        /// やり直すだけなので外へ流さない。全ポートで失敗した場合は <see cref="StartHost"/> が通知する。
        /// </summary>
        private void HandleTransportFailure()
        {
            if (_startingHost)
            {
                return;
            }

            TransportFailed?.Invoke();
        }

        private void ThrowIfDisposed()
        {
            // IsDisposed ではなく _disposed を見る（#116 M-1）。Dispose() は Stop()（= DespawnGameSession
            // 経由で本メソッドを呼ぶ）を先に実行してから _disposed を立てるため、IsDisposed（_disposing 込み）
            // を見てしまうと Dispose() 自身の実行中に例外を投げてしまう。
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NetworkService));
            }
        }
    }
}
