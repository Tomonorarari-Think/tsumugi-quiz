using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// ホスト開始時の到達性確認をまとめた入口（docs/network.md §2.1 の 4〜6）。
    /// <see cref="PortMappingService"/>（自動ポート開放）、<see cref="PublicIpResolver"/>（グローバル IP）、
    /// <see cref="LanIpResolver"/>（LAN IP）を順に呼び、結果を <see cref="HostAddressInfo"/> にまとめる。
    ///
    /// スレッド: **Unity のメインスレッドから生成・呼び出しすること**
    /// （既定の IP 確認クライアントがメインスレッドを要求する）。
    /// <see cref="PortMappingService.MappingChanged"/> はワーカースレッドで発火しうるため、
    /// 生成時に捕まえた同期コンテキストへ移してから <see cref="Current"/> を差し替える。
    ///
    /// アプリ終了時（<c>Application.quitting</c>）に自動でマッピングを削除する。
    /// 強制終了ではフックが走らないため、次回起動時の掃除（<see cref="PortMappingService"/>）にも頼る
    /// （docs/network-nat.md §1.4）。
    /// </summary>
    public sealed class HostConnectivityService : IDisposable
    {
        private readonly PortMappingService _portMapping;
        private readonly PublicIpResolver _publicIpResolver;
        private readonly Func<string> _lanIpProvider;
        private readonly SynchronizationContext _mainThreadContext;

        /// <summary>
        /// <see cref="ResolveAsync"/> の実行中は true。
        /// 実行中のマッピング通知は <see cref="ResolveAsync"/> 自身が最後に <see cref="Current"/> を
        /// 組み立て直すので無視する（中途半端な状態を画面に出さないため）。
        /// </summary>
        private volatile bool _resolving;

        private bool _disposed;

        /// <summary>
        /// サービスを作る。**Unity のメインスレッドで生成すること**。
        /// </summary>
        /// <param name="options">設定。null なら <see cref="NatOptions.Default"/>。</param>
        /// <param name="discovery">NAT デバイス探索。null なら <see cref="MonoNatDiscovery"/>。</param>
        /// <param name="lookupClient">IP 確認サービスのクライアント。null なら <see cref="UnityWebRequestIpLookupClient"/>。</param>
        /// <param name="lanIpProvider">LAN IP の取得方法。null なら <see cref="LanIpResolver.Resolve"/>。</param>
        public HostConnectivityService(
            NatOptions options = null,
            INatDiscovery discovery = null,
            IIpLookupClient lookupClient = null,
            Func<string> lanIpProvider = null)
        {
            var resolvedOptions = options ?? NatOptions.Default;
            _portMapping = new PortMappingService(discovery, resolvedOptions);
            _publicIpResolver = new PublicIpResolver(lookupClient, resolvedOptions);
            _lanIpProvider = lanIpProvider ?? LanIpResolver.Resolve;
            _mainThreadContext = SynchronizationContext.Current;

            // 未解決でも全プロパティが安全に読める実値を入れておく（default(HostAddressInfo) と同値）。
            Current = HostAddressInfo.NotResolved;

            _portMapping.MappingChanged += HandleMappingChanged;
            Application.quitting += HandleApplicationQuitting;
        }

        /// <summary>直近の結果。まだ解決していなければ <see cref="HostAddressInfo.NotResolved"/>。</summary>
        public HostAddressInfo Current { get; private set; }

        /// <summary>自動ポート開放を担当するサービス（再試行やマッピング削除に使う）。</summary>
        public PortMappingService PortMapping => _portMapping;

        /// <summary>
        /// <see cref="Current"/> が更新されたときに発火する（メインスレッド）。
        /// 画面はこれを購読して表示を更新する。
        /// </summary>
        public event Action<HostAddressInfo> AddressChanged;

        /// <summary>
        /// マッピングの更新で外部ポートが変わり、配布済みの参加コードが使えなくなったときに発火する
        /// （メインスレッド）。引数は画面にそのまま出せる案内文。
        /// </summary>
        public event Action<string> JoinCodeInvalidated;

        /// <summary>
        /// 配布済みの参加コードが無効になっているか。
        /// 画面が案内を出したら <see cref="AcknowledgeJoinCodeChange"/> でクリアする。
        /// </summary>
        public bool IsJoinCodeOutdated { get; private set; }

        /// <summary>
        /// ポート開放とアドレス取得をまとめて実行する。途中で失敗しても例外は投げず、
        /// 手動ポート開放案内・手入力に必要な情報を含めて返す。
        /// </summary>
        /// <param name="port">ホストが待ち受けている UDP ポート。</param>
        /// <param name="cancellationToken">キャンセル用。</param>
        /// <returns>到達性情報。</returns>
        public async Task<HostAddressInfo> ResolveAsync(ushort port, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (port == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, "ホストのポートが決まっていません。");
            }

            _resolving = true;
            try
            {
                var mapping = await _portMapping.MapAsync(port, cancellationToken);
                var publicIp = await _publicIpResolver.ResolveAsync(mapping.DeviceExternalIpAddress, cancellationToken);
                var lanIp = ResolveLanIpAddress();

                IsJoinCodeOutdated = false;
                Current = HostAddressInfo.Create(mapping, publicIp, lanIp, port);
            }
            finally
            {
                _resolving = false;
            }

            LogSummary(Current);
            AddressChanged?.Invoke(Current);
            return Current;
        }

        /// <summary>
        /// ユーザーが手入力したグローバル IP を反映する（docs/network-nat.md §2 の段 3）。
        /// </summary>
        /// <param name="address">入力されたアドレス。空文字で取り消し。</param>
        /// <returns>更新後の到達性情報。</returns>
        public HostAddressInfo ApplyManualPublicIpAddress(string address)
        {
            ThrowIfDisposed();

            Current = Current.WithManualPublicIpAddress(address);
            AddressChanged?.Invoke(Current);
            return Current;
        }

        /// <summary>
        /// 参加コードが変わった案内を表示し終えたことを記録する（<see cref="IsJoinCodeOutdated"/> を false に戻す）。
        /// </summary>
        public void AcknowledgeJoinCodeChange() => IsJoinCodeOutdated = false;

        /// <summary>ポートマッピングを削除する（ホスト停止時）。</summary>
        public async Task ReleaseAsync()
        {
            if (_disposed)
            {
                return;
            }

            await _portMapping.ReleaseAsync();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Application.quitting -= HandleApplicationQuitting;
            _portMapping.MappingChanged -= HandleMappingChanged;
            _portMapping.Dispose();
            _disposed = true;
        }

        /// <summary>
        /// マッピングの更新通知。ワーカースレッドで呼ばれることがあるため、
        /// 生成時の同期コンテキスト（Unity のメインスレッド）へ移してから状態を触る。
        /// </summary>
        private void HandleMappingChanged(PortMappingResult result)
        {
            if (_disposed || _resolving)
            {
                return;
            }

            if (_mainThreadContext != null && SynchronizationContext.Current != _mainThreadContext)
            {
                _mainThreadContext.Post(state => ApplyMappingChanged((PortMappingResult)state), result);
                return;
            }

            ApplyMappingChanged(result);
        }

        private void ApplyMappingChanged(PortMappingResult result)
        {
            if (_disposed || _resolving || Current.InternalPort == 0)
            {
                return;
            }

            var previousPort = Current.ExternalPort;
            Current = Current.WithPortMapping(result);
            AddressChanged?.Invoke(Current);

            if (Current.ExternalPort == previousPort)
            {
                return;
            }

            IsJoinCodeOutdated = true;
            var message = result.Success
                ? $"ルーターが割り当てた外部ポートが {previousPort} から {Current.ExternalPort} に変わりました。"
                  + "参加コードを作り直して配り直してください。"
                : "ポート開放の更新に失敗し、これまでの参加コードでは接続できなくなりました。"
                  + $"ルーターの設定画面で UDP {Current.InternalPort} を開放するか、ホストを開始し直してください。";

            Debug.LogWarning($"[HostConnectivityService] {message}");
            JoinCodeInvalidated?.Invoke(message);
        }

        private string ResolveLanIpAddress()
        {
            try
            {
                return _lanIpProvider() ?? string.Empty;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[HostConnectivityService] LAN IP を取得できませんでした: {exception.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 終了時のマッピング削除。以降フレームが回らないため同期的に待つ
        /// （<see cref="PortMappingService.ReleaseBlocking"/> はデッドロックしない実装になっている）。
        /// </summary>
        private void HandleApplicationQuitting()
        {
            if (_disposed || !_portMapping.HasActiveMapping)
            {
                return;
            }

            if (!_portMapping.ReleaseBlocking())
            {
                Debug.LogWarning("[HostConnectivityService] 終了時にポートマッピングを削除しきれませんでした。次回起動時に掃除します。");
            }
        }

        private static void LogSummary(HostAddressInfo info)
        {
            Debug.Log(
                "[HostConnectivityService] ポート開放="
                + $"{info.PortMapping.Status}（{info.PortMapping.DeviceProtocolName}）"
                + $" / 外部ポート={info.ExternalPort}"
                + $" / グローバル IP={(info.PublicIpAddress.Length > 0 ? info.PublicIpAddress : "未取得")}（{info.PublicIp.Source}）"
                + $" / LAN IP={(info.LanIpAddress.Length > 0 ? info.LanIpAddress : "未取得")}"
                + $" / CGNAT={info.IsCarrierGradeNat}");

            foreach (var warning in info.Warnings)
            {
                Debug.LogWarning($"[HostConnectivityService] {warning}");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HostConnectivityService));
            }
        }
    }
}
