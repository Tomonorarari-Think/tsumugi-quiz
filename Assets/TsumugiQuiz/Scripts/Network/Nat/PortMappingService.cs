using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// UPnP / NAT-PMP による自動ポート開放（docs/network-nat.md §1.3 / §1.4）。
    ///
    /// 手順は次のとおり。
    /// <list type="number">
    ///   <item>NAT デバイスを探索する（<c>upnp.discoveryTimeoutMs</c>）。</item>
    ///   <item>
    ///     **初回の <see cref="MapAsync"/> のときだけ** <c>Description == "TsumugiQuiz"</c> の
    ///     古いマッピング（前回の強制終了で残ったもの）を削除する。
    ///     更新（<see cref="RenewAsync"/>）では掃除しない。自分が今使っているマッピングを
    ///     消してしまい、その直後に作り直すまでの間に接続が切れるため。
    ///   </item>
    ///   <item>UDP のマッピングを作成し、返ってきた <c>PublicPort</c> を採用する。</item>
    ///   <item><c>upnp.renewIntervalMs</c> ごとに上書き作成する（lifetime 切れ対策）。</item>
    ///   <item>終了時に削除する（<see cref="ReleaseAsync"/> / <see cref="ReleaseBlocking"/>）。</item>
    /// </list>
    ///
    /// スレッド: 内部の await はすべて <c>ConfigureAwait(false)</c> で、呼び出し元のスレッドに依存しない。
    /// そのためアプリ終了時にメインスレッドから <see cref="ReleaseBlocking"/> で同期的に待っても
    /// デッドロックしない。呼び出し側が await した継続は、呼び出し側が捕まえた同期コンテキスト
    /// （Unity のメインスレッド）へ戻る。
    /// **<see cref="MappingChanged"/> はワーカースレッドで発火しうる**ので、購読側が必要に応じて
    /// メインスレッドへ移すこと（<see cref="HostConnectivityService"/> はそうしている）。
    /// </summary>
    public sealed class PortMappingService : IDisposable
    {
        /// <summary>アプリ終了時に削除完了を待つ既定の上限（ミリ秒）。</summary>
        public const int DefaultReleaseTimeoutMs = 2000;

        private readonly INatDiscovery _discovery;
        private readonly NatOptions _options;
        private readonly SemaphoreSlim _stateGate = new SemaphoreSlim(1, 1);

        private INatDevice _device;
        private NatPortMapping _activeMapping;
        private bool _hasActiveMapping;

        /// <summary>
        /// 要求した内部ポート。ルーターが返す <c>PrivatePort</c> は信用せず、
        /// 更新・削除は必ずこの値で行う（ルーターが別の値を返すと、更新のたびに別ポートの
        /// マッピングが増え、削除も効かなくなるため）。
        /// </summary>
        private ushort _requestedPort;

        private CancellationTokenSource _renewCancellation;
        private Task _renewLoop;
        private bool _disposed;

        /// <summary>
        /// サービスを作る。
        /// </summary>
        /// <param name="discovery">NAT デバイス探索。null なら <see cref="MonoNatDiscovery"/>。</param>
        /// <param name="options">設定。null なら <see cref="NatOptions.Default"/>。</param>
        public PortMappingService(INatDiscovery discovery = null, NatOptions options = null)
        {
            _discovery = discovery ?? new MonoNatDiscovery();
            _options = options ?? NatOptions.Default;
        }

        /// <summary>
        /// マッピングの作成・更新が終わるたびに発火する（成功・失敗の両方）。
        /// **ワーカースレッドから呼ばれることがある**。
        /// 外部ポートが変わった場合は配布済みの参加コードが無効になるため、購読側が案内を出す。
        /// </summary>
        public event Action<PortMappingResult> MappingChanged;

        /// <summary>直近の結果。一度も試していなければ <see cref="PortMappingResult.NotAttempted"/>。</summary>
        public PortMappingResult LastResult { get; private set; } = PortMappingResult.NotAttempted;

        /// <summary>有効なマッピングを保持しているか。</summary>
        public bool HasActiveMapping => _hasActiveMapping;

        /// <summary>発見したデバイスのプロトコル名（未発見なら空文字）。</summary>
        public string DeviceProtocolName => _device != null ? _device.ProtocolName : string.Empty;

        /// <summary>
        /// ポートマッピングを作成する。失敗しても例外は投げず（キャンセルを除く）、理由を結果に入れて返す。
        /// 既にマッピングを持っている場合は、それを削除してから作り直す。
        /// </summary>
        /// <param name="port">開放する UDP ポート（内部・外部とも同じ値を要求する）。</param>
        /// <param name="cancellationToken">キャンセル用。</param>
        /// <returns>結果。</returns>
        public async Task<PortMappingResult> MapAsync(ushort port, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (port == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, "ポート 0 は開放できません。");
            }

            if (!_options.Enabled)
            {
                return LastResult = PortMappingResult.Fail(
                    PortMappingStatus.Disabled, port, "自動ポート開放は設定で無効になっています。");
            }

            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ReleaseCoreAsync().ConfigureAwait(false);

                var device = await DiscoverAsync(port, cancellationToken).ConfigureAwait(false);
                if (device == null)
                {
                    return LastResult;
                }

                _device = device;
                _requestedPort = port;

                // 前回の強制終了で残ったマッピングの掃除は、この「初回作成」のときだけ行う（M-1）。
                await RemoveStaleMappingsAsync(device, cancellationToken).ConfigureAwait(false);

                return LastResult = await CreateMappingAsync(device, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // 想定外の例外でホスト開始を止めない。手動ポート開放案内へ進める（M-9）。
                Debug.LogWarning($"[PortMappingService] 自動ポート開放で想定外の例外が発生しました: {exception}");
                return LastResult = PortMappingResult.Fail(
                    PortMappingStatus.Failed,
                    port,
                    "自動ポート開放に失敗しました。手動でポートを開放してください。");
            }
            finally
            {
                _stateGate.Release();
            }
        }

        /// <summary>
        /// 現在のマッピングを作り直す（上書き作成）。更新ループから呼ばれるが、手動リトライやテストからも呼べる。
        /// マッピングを持っていない場合は何もしない。
        /// 掃除（古いマッピングの削除）は行わない（<see cref="MapAsync"/> の初回のみ）。
        /// </summary>
        /// <param name="cancellationToken">キャンセル用。</param>
        /// <returns>作り直した結果。マッピングが無い場合は直近の結果。</returns>
        public async Task<PortMappingResult> RenewAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_hasActiveMapping || _device == null)
                {
                    return LastResult;
                }

                return LastResult = await CreateMappingAsync(_device, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _stateGate.Release();
            }
        }

        /// <summary>
        /// マッピングを削除して更新ループを止める。削除に失敗しても例外は投げない
        /// （ホストの終了処理を止めないため。docs/network-nat.md §1.4）。
        /// </summary>
        public async Task ReleaseAsync()
        {
            if (_disposed)
            {
                return;
            }

            await _stateGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await ReleaseCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _stateGate.Release();
            }
        }

        /// <summary>
        /// <see cref="ReleaseAsync"/> を同期的に待つ。<c>Application.quitting</c> のように
        /// 以降のフレームが回らない場面で使う。内部が <c>ConfigureAwait(false)</c> で統一されているため
        /// メインスレッドから呼んでもデッドロックしない。
        /// </summary>
        /// <param name="timeoutMs">待つ上限（ミリ秒）。</param>
        /// <returns>時間内に削除できたら true。</returns>
        public bool ReleaseBlocking(int timeoutMs = DefaultReleaseTimeoutMs)
        {
            if (_disposed)
            {
                return true;
            }

            try
            {
                return ReleaseAsync().Wait(timeoutMs);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PortMappingService] マッピングの削除に失敗しました: {exception}");
                return false;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            ReleaseBlocking();
            _disposed = true;

            // _stateGate は Dispose しない。更新ループなど別スレッドがまだ WaitAsync している可能性があり、
            // 破棄すると ObjectDisposedException になる。SemaphoreSlim は GC に任せて問題ない（L-9）。
        }

        /// <summary>NAT デバイスを探索する。見つからない場合は <see cref="LastResult"/> に理由を入れて null を返す。</summary>
        private async Task<INatDevice> DiscoverAsync(ushort port, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var device = await _discovery.DiscoverAsync(_options.DiscoveryTimeoutMs, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (device != null)
            {
                return device;
            }

            // 探索が時間いっぱい掛かったならタイムアウト、早く終わったなら探索自体が動かなかったと判断する。
            var timedOut = stopwatch.ElapsedMilliseconds >= _options.DiscoveryTimeoutMs;
            LastResult = PortMappingResult.Fail(
                timedOut ? PortMappingStatus.Timeout : PortMappingStatus.DeviceNotFound,
                port,
                timedOut
                    ? $"ルーターが {_options.DiscoveryTimeoutMs}ms 以内に応答しませんでした（UPnP / NAT-PMP が無効の可能性があります）。"
                    : "UPnP / NAT-PMP に対応したルーターが見つかりませんでした。");
            return null;
        }

        /// <summary>
        /// <see cref="_requestedPort"/> で UDP マッピングを作成する（既にあれば上書き）。
        /// 結果は <see cref="MappingChanged"/> で通知する。
        /// </summary>
        private async Task<PortMappingResult> CreateMappingAsync(INatDevice device, CancellationToken cancellationToken)
        {
            var port = _requestedPort;
            var requested = new NatPortMapping(port, port, _options.MappingLifetimeSec, NatOptions.MappingDescription);

            NatPortMapping created;
            try
            {
                created = await device.CreatePortMapAsync(requested, cancellationToken).ConfigureAwait(false);
            }
            catch (NatDeviceException exception)
            {
                Debug.LogWarning($"[PortMappingService] ポートマッピングを作成できませんでした: {exception}");
                return Publish(PortMappingResult.Fail(
                    PortMappingFailureMessages.ToStatus(exception.Kind),
                    port,
                    PortMappingFailureMessages.Describe(exception.Kind),
                    device.ProtocolName));
            }

            if (!created.HasValidPublicPort)
            {
                Debug.LogWarning($"[PortMappingService] ルーターが不正な外部ポートを返しました: {created.PublicPort}");
                return Publish(PortMappingResult.Fail(
                    PortMappingStatus.Failed,
                    port,
                    "ルーターが不正な外部ポートを返しました。手動でポートを開放してください。",
                    device.ProtocolName));
            }

            if (created.PrivatePort != port)
            {
                // 内部ポートは「このPCが待ち受けているポート」なので、ルーターの言い値より要求値が正しい。
                Debug.LogWarning(
                    $"[PortMappingService] ルーターが要求と異なる内部ポートを返しました（要求 {port} / 応答 {created.PrivatePort}）。要求値を採用します。");
            }

            _activeMapping = new NatPortMapping(port, created.PublicPort, created.LifetimeSeconds, created.Description);
            _hasActiveMapping = true;
            StartRenewLoop();

            var externalIp = await TryGetExternalIpAsync(device, cancellationToken).ConfigureAwait(false);
            return Publish(PortMappingResult.Ok(port, (ushort)created.PublicPort, externalIp, device.ProtocolName));
        }

        /// <summary>結果を <see cref="MappingChanged"/> で通知してからそのまま返す。</summary>
        private PortMappingResult Publish(PortMappingResult result)
        {
            try
            {
                MappingChanged?.Invoke(result);
            }
            catch (Exception exception)
            {
                // 購読側の例外でポート開放処理を壊さない。
                Debug.LogWarning($"[PortMappingService] MappingChanged の購読者が例外を投げました: {exception}");
            }

            return result;
        }

        /// <summary>
        /// 前回の実行が残した <c>Description == "TsumugiQuiz"</c> のマッピングを削除する。
        /// 一覧取得や削除に失敗しても続行する（同じポートを指しているなら実害がないため）。
        /// </summary>
        private static async Task RemoveStaleMappingsAsync(INatDevice device, CancellationToken cancellationToken)
        {
            try
            {
                var mappings = await device.GetAllMappingsAsync(cancellationToken).ConfigureAwait(false);
                if (mappings == null)
                {
                    return;
                }

                foreach (var mapping in mappings)
                {
                    if (!string.Equals(mapping.Description, NatOptions.MappingDescription, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        await device.DeletePortMapAsync(mapping, cancellationToken).ConfigureAwait(false);
                        Debug.Log($"[PortMappingService] 古いマッピングを削除しました: {mapping}");
                    }
                    catch (NatDeviceException exception)
                    {
                        Debug.LogWarning($"[PortMappingService] 古いマッピングを削除できませんでした（続行します）: {exception.Message}");
                    }
                }
            }
            catch (NatDeviceException exception)
            {
                Debug.LogWarning($"[PortMappingService] マッピング一覧を取得できませんでした（続行します）: {exception.Message}");
            }
        }

        private static async Task<string> TryGetExternalIpAsync(INatDevice device, CancellationToken cancellationToken)
        {
            try
            {
                return await device.GetExternalIpAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (NatDeviceException exception)
            {
                Debug.LogWarning($"[PortMappingService] 外部 IP を取得できませんでした: {exception.Message}");
                return string.Empty;
            }
        }

        /// <summary>マッピングの削除と更新ループの停止。<c>_stateGate</c> を取得した状態で呼ぶこと。</summary>
        private async Task ReleaseCoreAsync()
        {
            StopRenewLoop();

            if (!_hasActiveMapping || _device == null)
            {
                _device = null;
                _hasActiveMapping = false;
                return;
            }

            try
            {
                await _device.DeletePortMapAsync(_activeMapping, CancellationToken.None).ConfigureAwait(false);
                Debug.Log($"[PortMappingService] マッピングを削除しました: {_activeMapping}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PortMappingService] マッピングを削除できませんでした: {exception.Message}");
            }
            finally
            {
                _hasActiveMapping = false;
                _device = null;
                _requestedPort = 0;
            }
        }

        /// <summary>
        /// <c>upnp.renewIntervalMs</c> ごとにマッピングを上書き作成するループを開始する。
        /// ループは <see cref="ReleaseAsync"/> / <see cref="Dispose"/> で止まる。
        /// </summary>
        private void StartRenewLoop()
        {
            if (_renewLoop != null && !_renewLoop.IsCompleted)
            {
                return;
            }

            _renewCancellation = new CancellationTokenSource();
            _renewLoop = RunRenewLoopAsync(_renewCancellation.Token);
        }

        private void StopRenewLoop()
        {
            if (_renewCancellation == null)
            {
                return;
            }

            _renewCancellation.Cancel();
            _renewCancellation.Dispose();
            _renewCancellation = null;
            _renewLoop = null;
        }

        private async Task RunRenewLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(_options.RenewIntervalMs, cancellationToken).ConfigureAwait(false);

                    // RenewAsync は _stateGate を取るため、ここでは取らない。
                    var result = await RenewAsync(cancellationToken).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        Debug.LogWarning($"[PortMappingService] マッピングの更新に失敗しました: {result.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常な停止。
            }
            catch (ObjectDisposedException)
            {
                // Dispose と競合した場合。停止として扱う。
            }
            catch (Exception exception)
            {
                // ここで止まってもホストは動き続ける（lifetime 切れまでは接続できる）ので、
                // テストを落とさない警告に留める（L-10）。
                Debug.LogWarning($"[PortMappingService] マッピング更新ループが停止しました: {exception}");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PortMappingService));
            }
        }
    }
}
