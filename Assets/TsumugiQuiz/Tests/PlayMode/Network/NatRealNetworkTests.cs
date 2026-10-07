using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.Network.Nat;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 実ネットワーク（家庭用ルーター / インターネット）に対する実測テスト。
    ///
    /// 実行環境のネットワーク構成に依存するため、**「見つかったか」「取得できたか」は判定しない**。
    /// 代わりに **環境に依存しない不変条件だけを Assert** し、実測値はログに残す
    /// （docs/network-nat.md §3.3 の実測記録用）。
    ///
    /// 検証する不変条件:
    /// <list type="bullet">
    ///   <item>探索はタイムアウト + 余裕の範囲内で必ず終わる（無期限に待たない）。</item>
    ///   <item>デバイスが取れてマッピングを作成できたら、作成と削除が必ず対で呼ばれる。</item>
    ///   <item>グローバル IP を取得できたなら、それは IPv4 として解釈できる正準形である。</item>
    /// </list>
    ///
    /// <c>[Category("Network")]</c> を付けているので、<c>scripts/verify.ps1</c> の既定
    /// （<c>-testCategory "!Network"</c>）では除外される。実行するには
    /// <c>pwsh ./scripts/verify.ps1 -IncludeNetwork</c> を使う。
    /// </summary>
    [Category("Network")]
    public class NatRealNetworkTests
    {
        /// <summary>探索のタイムアウト（docs/room-settings.md §2 の既定値）。</summary>
        private const int DiscoveryTimeoutMs = NatOptions.DefaultDiscoveryTimeoutMs;

        /// <summary>タイムアウト判定の許容幅（バッチ実行のフレーム遅延を見込む）。</summary>
        private const int DiscoveryToleranceMs = 5000;

        /// <summary>1 回のルーター操作を待つ上限（秒）。</summary>
        private const double OperationTimeoutSeconds = 15.0;

        [UnityTest]
        public IEnumerator Discovery_AgainstRealRouter_CreatesAndDeletesMapping()
        {
            var discovery = new MonoNatDiscovery();
            var stopwatch = Stopwatch.StartNew();
            var discoverTask = discovery.DiscoverAsync(DiscoveryTimeoutMs, CancellationToken.None);

            yield return WaitFor(discoverTask, (DiscoveryTimeoutMs + DiscoveryToleranceMs) / 1000.0 + 5.0);
            stopwatch.Stop();

            var device = discoverTask.Result;

            // 不変条件 1: 探索は必ずタイムアウト + 余裕の範囲内で終わる（環境に依存しない）。
            Assert.LessOrEqual(
                stopwatch.ElapsedMilliseconds,
                DiscoveryTimeoutMs + DiscoveryToleranceMs,
                $"探索が upnp.discoveryTimeoutMs（{DiscoveryTimeoutMs}ms）を大きく超えて掛かった。");

            if (device == null)
            {
                Debug.Log(
                    $"[NatRealNetworkTests] NAT デバイス: 未検出（{stopwatch.ElapsedMilliseconds}ms / タイムアウト {DiscoveryTimeoutMs}ms）。"
                    + " UPnP / NAT-PMP が無効、またはルーターが非対応。手動ポート開放案内へ進む経路になる。");

                // 見つからなかった場合、時間いっぱい待っているはず（早期に null を返していない）。
                Assert.GreaterOrEqual(
                    stopwatch.ElapsedMilliseconds,
                    DiscoveryTimeoutMs,
                    "デバイス未検出なのにタイムアウト前に諦めている。");
                yield break;
            }

            Debug.Log(
                $"[NatRealNetworkTests] NAT デバイス: 検出 protocol={device.ProtocolName}"
                + $" endpoint={device.EndpointDescription} 所要={stopwatch.ElapsedMilliseconds}ms");
            Assert.IsNotEmpty(device.ProtocolName);

            var externalIpTask = device.GetExternalIpAsync(CancellationToken.None);
            yield return WaitFor(externalIpTask, OperationTimeoutSeconds, allowFailure: true);
            var externalIp = externalIpTask.Status == TaskStatus.RanToCompletion ? externalIpTask.Result : string.Empty;
            var category = IpRangeClassifier.Classify(externalIp);
            Debug.Log(
                $"[NatRealNetworkTests] ルーターの外部 IP: '{externalIp}' 分類={category}"
                + $" CGNAT={IpRangeClassifier.IsCarrierGradeNat(externalIp)}");

            var requested = new NatPortMapping(
                NetworkConstants.DefaultPort,
                NetworkConstants.DefaultPort,
                NatOptions.DefaultMappingLifetimeSec,
                NatOptions.MappingDescription);

            var createTask = device.CreatePortMapAsync(requested, CancellationToken.None);
            yield return WaitFor(createTask, OperationTimeoutSeconds, allowFailure: true);

            if (createTask.Status != TaskStatus.RanToCompletion)
            {
                Debug.Log($"[NatRealNetworkTests] マッピング作成: 失敗 {Describe(createTask.Exception)}");

                // 失敗は環境依存なので許容するが、翻訳済みの例外型で返ること（Mono.Nat の型を漏らさないこと）は不変条件。
                Assert.IsInstanceOf<NatDeviceException>(createTask.Exception?.InnerException);
                yield break;
            }

            var created = createTask.Result;
            Debug.Log($"[NatRealNetworkTests] マッピング作成: 成功 {created}");

            // ルーター側に登録されたかを一覧で読み返す（ルーター管理画面の確認に相当する実測）。
            var listTask = device.GetAllMappingsAsync(CancellationToken.None);
            yield return WaitFor(listTask, OperationTimeoutSeconds, allowFailure: true);
            if (listTask.Status == TaskStatus.RanToCompletion)
            {
                var found = false;
                foreach (var entry in listTask.Result)
                {
                    if (entry.PublicPort == created.PublicPort)
                    {
                        found = true;
                        Debug.Log($"[NatRealNetworkTests] 読み返し: 一致するマッピングを検出 {entry}");
                    }
                }

                Debug.Log($"[NatRealNetworkTests] 読み返し: 件数={listTask.Result.Count} 一致={found}");
            }
            else
            {
                Debug.Log($"[NatRealNetworkTests] 読み返し: 一覧取得に失敗 {Describe(listTask.Exception)}");
            }

            // 不変条件 2: 作成できたら必ず削除まで行う（テストがルーターにゴミを残さない）。
            // Assert は削除を実行したあとにまとめて行う。
            var deleteTask = device.DeletePortMapAsync(created, CancellationToken.None);
            yield return WaitFor(deleteTask, OperationTimeoutSeconds, allowFailure: true);
            var deleted = deleteTask.Status == TaskStatus.RanToCompletion;

            Debug.Log(deleted
                ? "[NatRealNetworkTests] マッピング削除: 成功"
                : $"[NatRealNetworkTests] マッピング削除: 失敗 {Describe(deleteTask.Exception)}");

            Assert.IsTrue(deleteTask.IsCompleted, "作成と削除は必ず対で行う（削除が完了していない）。");
            Assert.IsTrue(created.HasValidPublicPort, $"ルーターが不正な外部ポートを返した: {created.PublicPort}");
            Assert.AreEqual(NatOptions.MappingDescription, created.Description);
        }

        [UnityTest]
        public IEnumerator PublicIpResolver_AgainstRealService_LogsAddress()
        {
            var resolver = new PublicIpResolver(new UnityWebRequestIpLookupClient(), NatOptions.Default);
            var stopwatch = Stopwatch.StartNew();
            var task = resolver.ResolveAsync(null, CancellationToken.None);

            yield return WaitFor(task, NatOptions.IpLookupTimeoutMs / 1000.0 + 15.0, allowFailure: true);
            stopwatch.Stop();

            if (task.Status != TaskStatus.RanToCompletion)
            {
                Debug.Log($"[NatRealNetworkTests] グローバル IP 取得: 例外 {Describe(task.Exception)}");
                Assert.Fail($"ResolveAsync は失敗しても例外を投げない契約: {Describe(task.Exception)}");
                yield break;
            }

            var result = task.Result;
            Debug.Log(
                $"[NatRealNetworkTests] グローバル IP 取得: found={result.Found} address='{result.Address}'"
                + $" source={result.Source} 分類={result.Category} CGNAT={result.IsCarrierGradeNat}"
                + $" 所要={stopwatch.ElapsedMilliseconds}ms message={result.Message}");

            // 不変条件: 取得できたなら IPv4 の正準形で、到達性の分類が付いている（値そのものは環境依存）。
            if (result.Found)
            {
                Assert.IsTrue(IpRangeClassifier.TryParseIpv4(result.Address, out _), result.Address);
                Assert.IsTrue(
                    result.Category == IpAddressCategory.Public || result.Category == IpAddressCategory.CarrierGradeNat,
                    $"採用したアドレスの分類が想定外: {result.Category}");
            }
            else
            {
                Assert.AreEqual(string.Empty, result.Address);
                Assert.IsNotEmpty(result.Message, "取得できなかった理由を必ず入れる（手入力へ案内するため）。");
            }
        }

        /// <summary>タスクの完了を待つ（PlayMode のフレームを回しながら）。</summary>
        /// <param name="task">待つタスク。</param>
        /// <param name="timeoutSeconds">上限（秒）。</param>
        /// <param name="allowFailure">true なら失敗しても例外を投げ直さない（呼び出し側で状態を見る）。</param>
        private static IEnumerator WaitFor(Task task, double timeoutSeconds, bool allowFailure = false)
        {
            var startedAt = DateTime.UtcNow;
            while (!task.IsCompleted)
            {
                if ((DateTime.UtcNow - startedAt).TotalSeconds > timeoutSeconds)
                {
                    Assert.Fail($"タスクが {timeoutSeconds} 秒以内に完了しませんでした。");
                }

                yield return null;
            }

            if (!allowFailure)
            {
                task.GetAwaiter().GetResult();
            }
            else if (task.Exception != null)
            {
                // 観測しておかないと未処理タスク例外として後から報告される。
                _ = task.Exception;
            }
        }

        private static string Describe(AggregateException exception)
            => exception?.InnerException?.Message ?? exception?.Message ?? "(不明)";
    }
}
