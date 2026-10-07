using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mono.Nat;
using MonoNatDeviceHandle = Mono.Nat.INatDevice;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// Mono.Nat の <c>INatDevice</c> を <see cref="INatDevice"/> に適合させるアダプタ。
    /// Mono.Nat の型（<c>Mapping</c> / <c>MappingException</c> / <c>ErrorCode</c>）はこのクラスの外へ出さない。
    ///
    /// Mono.Nat の非同期 API は <see cref="CancellationToken"/> を受け付けないため、
    /// キャンセル・タイムアウトは <see cref="Task.WhenAny(Task[])"/> で「待つのをやめる」形で実現する
    /// （下位のリクエスト自体は裏で完走する）。
    /// すべての <c>await</c> に <c>ConfigureAwait(false)</c> を付け、呼び出し元のスレッドに依存しない
    /// （アプリ終了時にメインスレッドから同期的に待てるようにするため）。
    /// </summary>
    internal sealed class MonoNatDevice : INatDevice
    {
        /// <summary>1 回のルーター操作を待つ上限（ミリ秒）。応答しないルーターで固まらないための保険。</summary>
        private const int OperationTimeoutMs = 10000;

        private readonly MonoNatDeviceHandle _device;

        /// <summary>
        /// アダプタを作る。
        /// </summary>
        /// <param name="device">Mono.Nat が発見したデバイス。</param>
        /// <exception cref="ArgumentNullException"><paramref name="device"/> が null。</exception>
        public MonoNatDevice(MonoNatDeviceHandle device)
            => _device = device ?? throw new ArgumentNullException(nameof(device));

        /// <inheritdoc />
        public string ProtocolName => _device.NatProtocol == NatProtocol.Pmp ? "NAT-PMP" : "UPnP";

        /// <inheritdoc />
        public string EndpointDescription
        {
            get
            {
                try
                {
                    return _device.DeviceEndpoint != null ? _device.DeviceEndpoint.ToString() : "(unknown)";
                }
                catch (Exception)
                {
                    // Mono.Nat の実装によっては ToString で例外が出るため、ログ用途で握りつぶす。
                    return "(unknown)";
                }
            }
        }

        /// <inheritdoc />
        public async Task<NatPortMapping> CreatePortMapAsync(NatPortMapping mapping, CancellationToken cancellationToken)
        {
            var created = await RunAsync(
                () => _device.CreatePortMapAsync(ToMonoNat(mapping)),
                "ポートマッピングの作成",
                cancellationToken).ConfigureAwait(false);

            // ルーターによっては作成結果を返さない実装があるため、null なら要求内容をそのまま返す。
            return created != null ? FromMonoNat(created) : mapping;
        }

        /// <inheritdoc />
        public async Task DeletePortMapAsync(NatPortMapping mapping, CancellationToken cancellationToken)
            => await RunAsync(
                () => _device.DeletePortMapAsync(ToMonoNat(mapping)),
                "ポートマッピングの削除",
                cancellationToken).ConfigureAwait(false);

        /// <inheritdoc />
        public async Task<IReadOnlyList<NatPortMapping>> GetAllMappingsAsync(CancellationToken cancellationToken)
        {
            var mappings = await RunAsync(
                () => _device.GetAllMappingsAsync(),
                "マッピング一覧の取得",
                cancellationToken).ConfigureAwait(false);

            if (mappings == null)
            {
                return Array.Empty<NatPortMapping>();
            }

            var results = new List<NatPortMapping>(mappings.Length);
            foreach (var mapping in mappings)
            {
                if (mapping != null)
                {
                    results.Add(FromMonoNat(mapping));
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<string> GetExternalIpAsync(CancellationToken cancellationToken)
        {
            var address = await RunAsync(
                () => _device.GetExternalIPAsync(),
                "外部 IP の取得",
                cancellationToken).ConfigureAwait(false);

            return address != null ? address.ToString() : string.Empty;
        }

        private static Mapping ToMonoNat(NatPortMapping mapping)
            => new Mapping(Protocol.Udp, mapping.PrivatePort, mapping.PublicPort, mapping.LifetimeSeconds, mapping.Description);

        private static NatPortMapping FromMonoNat(Mapping mapping)
            => new NatPortMapping(mapping.PrivatePort, mapping.PublicPort, mapping.Lifetime, mapping.Description);

        /// <summary>
        /// Mono.Nat の非同期操作を実行し、例外を <see cref="NatDeviceException"/> に翻訳する。
        /// </summary>
        /// <typeparam name="T">戻り値の型。</typeparam>
        /// <param name="operation">実行する操作。</param>
        /// <param name="description">失敗メッセージに載せる操作名。</param>
        /// <param name="cancellationToken">キャンセル用。</param>
        private static async Task<T> RunAsync<T>(Func<Task<T>> operation, string description, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<T> task;
            try
            {
                task = operation();
            }
            catch (Exception exception)
            {
                throw Translate(exception, description);
            }

            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var timeout = Task.Delay(OperationTimeoutMs, timeoutSource.Token);
                var finished = await Task.WhenAny(task, timeout).ConfigureAwait(false);
                if (!ReferenceEquals(finished, task))
                {
                    // 待つのをやめても下位のリクエストは裏で完走する。そのまま放置すると
                    // 失敗時に未処理タスク例外として後から報告されるため、ここで観測しておく（M-10）。
                    ObserveFaultLater(task, description);

                    cancellationToken.ThrowIfCancellationRequested();
                    throw new NatDeviceException(
                        NatFailureKind.Network,
                        $"{description}がタイムアウトしました（{OperationTimeoutMs}ms）。");
                }

                timeoutSource.Cancel();
            }

            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw Translate(exception, description);
            }
        }

        /// <summary>
        /// 見捨てたタスクが後から失敗しても、未処理タスク例外にならないよう観測だけしておく。
        /// </summary>
        /// <param name="task">見捨てたタスク。</param>
        /// <param name="description">ログに載せる操作名。</param>
        private static void ObserveFaultLater(Task task, string description)
            => task.ContinueWith(
                faulted => UnityEngine.Debug.LogWarning(
                    $"[MonoNatDevice] タイムアウト後に{description}が失敗しました: {faulted.Exception?.InnerException?.Message ?? faulted.Exception?.Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        private static NatDeviceException Translate(Exception exception, string description)
        {
            if (exception is MappingException mappingException)
            {
                return new NatDeviceException(
                    ToFailureKind(mappingException.ErrorCode),
                    $"{description}にルーターが失敗を返しました（{mappingException.ErrorCode}: {mappingException.ErrorText}）。",
                    mappingException);
            }

            return new NatDeviceException(NatFailureKind.Network, $"{description}に失敗しました。", exception);
        }

        private static NatFailureKind ToFailureKind(ErrorCode errorCode)
        {
            switch (errorCode)
            {
                case ErrorCode.NotAuthorizedOrRefused:
                    return NatFailureKind.Refused;
                case ErrorCode.ConflictInMappingEntry:
                case ErrorCode.SamePortValuesRequired:
                case ErrorCode.WildCardNotPermittedInExternalPort:
                case ErrorCode.WildCardNotPermittedInSourceIP:
                    return NatFailureKind.Conflict;
                case ErrorCode.UnsupportedOperation:
                case ErrorCode.UnsupportedVersion:
                    return NatFailureKind.Unsupported;
                case ErrorCode.NetworkFailure:
                case ErrorCode.OutOfResources:
                    return NatFailureKind.Network;
                default:
                    return NatFailureKind.Unknown;
            }
        }
    }
}
