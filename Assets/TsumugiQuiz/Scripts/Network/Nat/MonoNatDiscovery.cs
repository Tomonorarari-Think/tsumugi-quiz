using System;
using System.Threading;
using System.Threading.Tasks;
using Mono.Nat;
using UnityEngine;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// Mono.Nat を使った NAT デバイス探索（docs/network-nat.md §1.3）。
    ///
    /// 注意点:
    /// - <c>NatUtility</c> は **静的（プロセス全体で 1 つ）** なので、同時に 2 つの探索が走らないよう
    ///   セマフォで直列化する。
    /// - <c>DeviceFound</c> は **Mono.Nat のワーカースレッド**で発火する。ここでは
    ///   <see cref="TaskCompletionSource{TResult}"/> に詰め替えるだけにし、
    ///   Unity API には触れない（結果の受け取り側が <c>await</c> でメインスレッドへ戻す）。
    /// - 最初に応答した 1 台を採用する。家庭用ルーター 1 台の環境を想定しているため、
    ///   複数見つかった場合の選択は行わない。
    /// </summary>
    public sealed class MonoNatDiscovery : INatDiscovery
    {
        private static readonly SemaphoreSlim DiscoveryGate = new SemaphoreSlim(1, 1);

        /// <inheritdoc />
        public async Task<INatDevice> DiscoverAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), timeoutMs, "探索のタイムアウトは 1ms 以上にしてください。");
            }

            cancellationToken.ThrowIfCancellationRequested();

            await DiscoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await DiscoverCoreAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                DiscoveryGate.Release();
            }
        }

        private static async Task<INatDevice> DiscoverCoreAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            // ワーカースレッドから SetResult されるため、継続を同期実行しないようにする。
            var found = new TaskCompletionSource<Mono.Nat.INatDevice>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnDeviceFound(object sender, DeviceEventArgs args)
            {
                if (args?.Device != null)
                {
                    found.TrySetResult(args.Device);
                }
            }

            NatUtility.DeviceFound += OnDeviceFound;
            try
            {
                NatUtility.StartDiscovery(NatProtocol.Upnp, NatProtocol.Pmp);

                using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var timeout = Task.Delay(timeoutMs, timeoutSource.Token);
                    var finished = await Task.WhenAny(found.Task, timeout).ConfigureAwait(false);

                    if (!ReferenceEquals(finished, found.Task))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // タイムアウトは「失敗」ではなく「見つからなかった」として null を返す。
                        return null;
                    }

                    timeoutSource.Cancel();
                }

                return new MonoNatDevice(await found.Task.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // 探索自体の失敗（ソケットを開けないなど）はホスト開始を止める理由にならない。
                // 手動ポート開放案内へ進めるよう、警告だけ出して「見つからなかった」扱いにする。
                Debug.LogWarning($"[MonoNatDiscovery] NAT デバイスの探索に失敗しました: {exception}");
                return null;
            }
            finally
            {
                NatUtility.DeviceFound -= OnDeviceFound;
                StopDiscoverySafely();
            }
        }

        private static void StopDiscoverySafely()
        {
            try
            {
                NatUtility.StopDiscovery();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MonoNatDiscovery] 探索の停止に失敗しました: {exception}");
            }
        }
    }
}
