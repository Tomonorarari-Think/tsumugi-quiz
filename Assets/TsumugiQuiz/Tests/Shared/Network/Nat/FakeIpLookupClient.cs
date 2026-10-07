using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Network.Nat;

namespace TsumugiQuiz.Tests.Shared.Network.Nat
{
    /// <summary>
    /// テスト用の IP 確認サービスクライアント。URL ごとに応答を仕込める。
    /// 実ネットワークへは一切アクセスしない。
    /// </summary>
    internal sealed class FakeIpLookupClient : IIpLookupClient
    {
        private readonly Dictionary<string, Func<IpLookupResponse>> _responses =
            new Dictionary<string, Func<IpLookupResponse>>(StringComparer.Ordinal);

        /// <summary>リクエストされた URL（呼ばれた順）。</summary>
        public List<string> RequestedUrls { get; } = new List<string>();

        /// <summary>仕込みが無い URL に対する既定の応答。</summary>
        public IpLookupResponse DefaultResponse { get; set; } = IpLookupResponse.Fail("仕込みがありません。");

        /// <summary>URL に対して本文を返すよう仕込む。</summary>
        /// <param name="url">対象 URL。</param>
        /// <param name="body">返す本文。</param>
        public FakeIpLookupClient WithBody(string url, string body)
        {
            _responses[url] = () => IpLookupResponse.Ok(200, body);
            return this;
        }

        /// <summary>URL に対して失敗を返すよう仕込む。</summary>
        /// <param name="url">対象 URL。</param>
        /// <param name="error">失敗理由。</param>
        /// <param name="statusCode">HTTP ステータスコード。</param>
        public FakeIpLookupClient WithFailure(string url, string error, long statusCode = 0)
        {
            _responses[url] = () => IpLookupResponse.Fail(error, statusCode);
            return this;
        }

        /// <summary>URL に対して例外を投げるよう仕込む（実装が例外を投げても段 3 に落ちることの確認用）。</summary>
        /// <param name="url">対象 URL。</param>
        public FakeIpLookupClient WithException(string url)
        {
            _responses[url] = () => throw new InvalidOperationException("テスト用の例外");
            return this;
        }

        /// <inheritdoc />
        public Task<IpLookupResponse> GetTextAsync(string url, int timeoutMs, int maxResponseBytes, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(url);

            if (!_responses.TryGetValue(url, out var factory))
            {
                return Task.FromResult(DefaultResponse);
            }

            return Task.FromResult(factory());
        }
    }
}
