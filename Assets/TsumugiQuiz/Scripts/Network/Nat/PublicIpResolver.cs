using System;
using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Core.Network;
using UnityEngine;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// グローバル IP の取得（docs/network-nat.md §2）。
    ///
    /// <list type="number">
    ///   <item>段 1: NAT デバイスの <c>GetExternalIPAsync</c> の結果（呼び出し側が渡す）。</item>
    ///   <item>段 2: <c>network.ipLookupUrls</c> を先頭から順に試す（既定 <c>https://api.ipify.org</c>）。</item>
    ///   <item>段 3: どちらも失敗したら <see cref="PublicIpResult.Found"/> が false の結果を返し、手入力へ落とす。</item>
    /// </list>
    ///
    /// docs/network-nat.md §1.6 の二重 NAT 判定のため、**段 1 が成功しても段 2 を実行する**。
    /// 両方取得できて値が食い違う場合は警告を出し、外から見える真のアドレスである段 2 を採用する。
    /// 段 2 が 1 件も成功しなかった場合は段 1 の値を使う。
    ///
    /// スレッド: Unity のメインスレッドから呼ぶこと（既定の <see cref="UnityWebRequestIpLookupClient"/> が
    /// メインスレッドを要求する）。内部では <c>ConfigureAwait</c> を使わないため、継続はメインスレッドに戻る。
    /// </summary>
    public sealed class PublicIpResolver
    {
        private readonly IIpLookupClient _lookupClient;
        private readonly NatOptions _options;

        /// <summary>
        /// リゾルバを作る。
        /// </summary>
        /// <param name="lookupClient">IP 確認サービスのクライアント。null なら <see cref="UnityWebRequestIpLookupClient"/>。</param>
        /// <param name="options">設定。null なら <see cref="NatOptions.Default"/>。</param>
        public PublicIpResolver(IIpLookupClient lookupClient = null, NatOptions options = null)
        {
            _lookupClient = lookupClient ?? new UnityWebRequestIpLookupClient();
            _options = options ?? NatOptions.Default;
        }

        /// <summary>
        /// グローバル IP を解決する。失敗しても例外は投げない。
        /// </summary>
        /// <param name="natDeviceAddress">
        /// NAT デバイスが答えた外部 IP（段 1）。未取得なら null / 空文字を渡す。
        /// グローバルでないアドレス（プライベート・0.0.0.0 など）は段 1 の失敗として扱う。
        /// </param>
        /// <param name="cancellationToken">キャンセル用。</param>
        /// <returns>結果。</returns>
        public async Task<PublicIpResult> ResolveAsync(string natDeviceAddress, CancellationToken cancellationToken = default)
        {
            var natAddress = NormalizeAddress(natDeviceAddress);
            var natCategory = IpRangeClassifier.Classify(natAddress);

            if (natAddress.Length > 0 && !IsUsableGlobalAddress(natCategory))
            {
                // 段 1 の失敗（docs/network-nat.md §2「プライベート IP が返る」）。値自体は CGNAT 判定に使うため残す。
                Debug.LogWarning(
                    $"[PublicIpResolver] ルーターが返した外部 IP は外部から到達できるアドレスではありません（{natAddress}: {IpRangeClassifier.Describe(natCategory)}）。");
            }

            var lookupAddress = await ResolveFromLookupServicesAsync(cancellationToken);

            if (lookupAddress.Length > 0)
            {
                var message = natAddress.Length > 0 && natAddress != lookupAddress
                    ? $"ルーターの外部 IP（{natAddress}）と IP 確認サービスの結果（{lookupAddress}）が一致しません。二重 NAT の可能性があるため、確認サービスの値を採用します。"
                    : $"IP 確認サービスからグローバル IP（{lookupAddress}）を取得しました。";

                if (natAddress.Length > 0 && natAddress != lookupAddress)
                {
                    Debug.LogWarning($"[PublicIpResolver] {message}");
                }

                return PublicIpResult.Resolved(lookupAddress, PublicIpSource.IpLookupService, natAddress, lookupAddress, message);
            }

            if (IsUsableGlobalAddress(natCategory))
            {
                return PublicIpResult.Resolved(
                    natAddress,
                    PublicIpSource.NatDevice,
                    natAddress,
                    string.Empty,
                    $"ルーターからグローバル IP（{natAddress}）を取得しました。");
            }

            return PublicIpResult.NotFound(
                natAddress,
                "グローバル IP を自動取得できませんでした。インターネット用の参加コードを作るには、グローバル IP を手入力してください。");
        }

        /// <summary>
        /// <c>network.ipLookupUrls</c> を順に試し、最初に取得できた IPv4 を返す。1 件も成功しなければ空文字。
        /// </summary>
        private async Task<string> ResolveFromLookupServicesAsync(CancellationToken cancellationToken)
        {
            foreach (var url in _options.IpLookupUrls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IpLookupResponse response;
                try
                {
                    response = await _lookupClient.GetTextAsync(
                        url,
                        NatOptions.IpLookupTimeoutMs,
                        NatOptions.IpLookupMaxResponseBytes,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // 実装が例外を投げても次の URL へ進む（段 2 は失敗しても例外にしない）。
                    Debug.LogWarning($"[PublicIpResolver] {url} へのアクセスに失敗しました: {exception.Message}");
                    continue;
                }

                if (!response.Success)
                {
                    Debug.LogWarning($"[PublicIpResolver] {url} から IP を取得できませんでした: {response.Error}");
                    continue;
                }

                if (TryValidateLookupBody(response.Text, out var address))
                {
                    return address;
                }

                Debug.LogWarning($"[PublicIpResolver] {url} の応答を IPv4 として解釈できませんでした。");
            }

            return string.Empty;
        }

        /// <summary>
        /// IP 確認サービスの応答本文を検証する（docs/network-nat.md §2「外部データを信用しない」）。
        /// 64 バイト超の破棄はクライアント側で行うため、ここでは書式と分類だけを見る。
        /// </summary>
        /// <param name="body">応答本文。</param>
        /// <param name="address">検証を通ったアドレス。</param>
        /// <returns>グローバルアドレスとして採用できるなら true。</returns>
        public static bool TryValidateLookupBody(string body, out string address)
        {
            address = string.Empty;

            if (string.IsNullOrWhiteSpace(body) || body.Length > NatOptions.IpLookupMaxResponseBytes)
            {
                return false;
            }

            var candidate = body.Trim();

            // System.Net.IPAddress による検証（docs の要求）と、Core の厳密な書式検証の両方を通す。
            if (!System.Net.IPAddress.TryParse(candidate, out var parsed)
                || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return false;
            }

            // CGNAT 帯は「取得できた」として採用し、到達できないことは呼び出し側が
            // PublicIpResult.IsCarrierGradeNat で判定して案内する（docs/network-nat.md §1.6）。
            if (!IsUsableGlobalAddress(IpRangeClassifier.Classify(candidate)))
            {
                return false;
            }

            address = candidate;
            return true;
        }

        /// <summary>
        /// 「取得できたグローバル IP」として扱える分類か。
        /// CGNAT はインターネットから到達できないが、ユーザーへ理由を説明するために採用する
        /// （プライベート・ループバックなどは取得失敗として扱う）。
        /// </summary>
        /// <param name="category">分類。</param>
        private static bool IsUsableGlobalAddress(IpAddressCategory category)
            => category == IpAddressCategory.Public || category == IpAddressCategory.CarrierGradeNat;

        private static string NormalizeAddress(string address)
            => string.IsNullOrWhiteSpace(address) ? string.Empty : address.Trim();
    }
}
