using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// <see cref="UnityWebRequest"/> による IP 確認サービスへのアクセス。
    ///
    /// <see cref="UnityWebRequest"/> は **Unity のメインスレッドからしか送信できない**。
    /// そのため生成時にメインスレッドの <see cref="SynchronizationContext"/> を捕まえておき、
    /// 別スレッドから呼ばれた場合はそこへ <c>Post</c> してから送信する。
    /// （<see cref="PortMappingService"/> 側は <c>ConfigureAwait(false)</c> で動くため、
    /// 呼び出し経路によってはワーカースレッドに居ることがある。）
    ///
    /// 外部データを信用しないための措置（docs/network-nat.md §2）:
    /// リダイレクト禁止、<c>Content-Length</c> が上限超なら即 <c>Abort()</c>、本文サイズの再検証、
    /// <see cref="UnityWebRequest.timeout"/> に加えて <see cref="Task.Delay(int)"/> による外側の締め切り。
    /// </summary>
    public sealed class UnityWebRequestIpLookupClient : IIpLookupClient
    {
        /// <summary><see cref="UnityWebRequest.timeout"/> が効かない場合に備えた外側の締め切りの上乗せ（ミリ秒）。</summary>
        private const int DeadlineMarginMs = 1000;

        private readonly SynchronizationContext _mainThreadContext;

        /// <summary>
        /// クライアントを作る。**Unity のメインスレッドで生成すること**。
        /// </summary>
        public UnityWebRequestIpLookupClient() => _mainThreadContext = SynchronizationContext.Current;

        /// <inheritdoc />
        public async Task<IpLookupResponse> GetTextAsync(string url, int timeoutMs, int maxResponseBytes, CancellationToken cancellationToken)
        {
            if (!NatOptions.IsAllowedLookupUrl(url))
            {
                return IpLookupResponse.Fail($"URL が https の絶対 URL ではありません: {url}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var completion = new TaskCompletionSource<IpLookupResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Send(object _) => BeginRequest(url, timeoutMs, maxResponseBytes, cancellationToken, completion);

            if (_mainThreadContext != null && SynchronizationContext.Current != _mainThreadContext)
            {
                _mainThreadContext.Post(Send, null);
            }
            else
            {
                Send(null);
            }

            // UnityWebRequest.timeout が効かない状況（メインスレッドが止まっている等）でも
            // 呼び出し側が待ち続けないよう、外側にも締め切りを置く。
            using (var deadlineSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var deadline = Task.Delay(timeoutMs + DeadlineMarginMs, deadlineSource.Token);
                var finished = await Task.WhenAny(completion.Task, deadline);

                if (!ReferenceEquals(finished, completion.Task))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 実際のリクエストは裏で終わる（結果は捨てられる）。ここでは待つのをやめるだけ。
                    return IpLookupResponse.Fail($"応答が {timeoutMs + DeadlineMarginMs}ms 以内に返りませんでした。");
                }

                deadlineSource.Cancel();
            }

            return await completion.Task;
        }

        private static void BeginRequest(
            string url,
            int timeoutMs,
            int maxResponseBytes,
            CancellationToken cancellationToken,
            TaskCompletionSource<IpLookupResponse> completion)
        {
            UnityWebRequest request;
            try
            {
                request = UnityWebRequest.Get(url);

                // UnityWebRequest.timeout は秒単位。0 だと「無制限」になるため最低 1 秒にする。
                request.timeout = Math.Max(1, (int)Math.Ceiling(timeoutMs / 1000.0));

                // リダイレクトは追わない。許可 URL の検証（https のみ）をリダイレクトで迂回されないため。
                request.redirectLimit = 0;
            }
            catch (Exception exception)
            {
                completion.TrySetResult(IpLookupResponse.Fail($"リクエストを作成できませんでした: {exception.Message}"));
                return;
            }

            CancellationTokenRegistration registration = default;
            var finished = false;

            void Complete(IpLookupResponse response)
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                registration.Dispose();
                request.Dispose();
                completion.TrySetResult(response);
            }

            void Cancel()
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                registration.Dispose();
                request.Dispose();
                completion.TrySetCanceled(cancellationToken);
            }

            try
            {
                var operation = request.SendWebRequest();

                if (cancellationToken.CanBeCanceled)
                {
                    registration = cancellationToken.Register(() =>
                    {
                        try
                        {
                            request.Abort();
                        }
                        catch (Exception)
                        {
                            // 既に破棄されている場合は無視する。
                        }
                    });
                }

                void Finish()
                {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            Cancel();
                            return;
                        }

                        Complete(Evaluate(request, maxResponseBytes));
                    }
                    catch (Exception exception)
                    {
                        // Evaluate が投げた場合も request を必ず破棄する。
                        Complete(IpLookupResponse.Fail($"応答の読み取りに失敗しました: {exception.Message}"));
                    }
                }

                // 既に完了している場合に completed が発火しない実装差に備えて先に確認する。
                if (operation.isDone)
                {
                    Finish();
                    return;
                }

                operation.completed += _ => Finish();
            }
            catch (Exception exception)
            {
                registration.Dispose();
                request.Dispose();
                finished = true;
                completion.TrySetResult(IpLookupResponse.Fail($"リクエストを送信できませんでした: {exception.Message}"));
            }
        }

        /// <summary>
        /// 応答を検証する。HTTP 200 以外・サイズ超過は失敗として扱う（docs/network-nat.md §2）。
        /// </summary>
        private static IpLookupResponse Evaluate(UnityWebRequest request, int maxResponseBytes)
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                return IpLookupResponse.Fail(request.error ?? request.result.ToString(), request.responseCode);
            }

            if (request.responseCode != 200)
            {
                return IpLookupResponse.Fail($"HTTP ステータスが 200 ではありません: {request.responseCode}", request.responseCode);
            }

            if (ExceedsDeclaredLength(request, maxResponseBytes, out var declaredLength))
            {
                AbortSafely(request);
                return IpLookupResponse.Fail(
                    $"Content-Length が大きすぎます（{declaredLength} バイト > {maxResponseBytes} バイト）。",
                    request.responseCode);
            }

            var data = request.downloadHandler?.data;
            if (data == null || data.Length == 0)
            {
                return IpLookupResponse.Fail("応答が空でした。", request.responseCode);
            }

            if (data.Length > maxResponseBytes)
            {
                return IpLookupResponse.Fail(
                    $"応答が大きすぎます（{data.Length} バイト > {maxResponseBytes} バイト）。",
                    request.responseCode);
            }

            return IpLookupResponse.Ok(request.responseCode, Encoding.UTF8.GetString(data));
        }

        /// <summary>
        /// <c>Content-Length</c> が上限を超えていると宣言されているか。
        /// ヘッダが無い / 読めない場合は false（本文サイズで判定する）。
        /// </summary>
        private static bool ExceedsDeclaredLength(UnityWebRequest request, int maxResponseBytes, out long declaredLength)
        {
            declaredLength = 0;

            string header;
            try
            {
                header = request.GetResponseHeader("Content-Length");
            }
            catch (Exception)
            {
                return false;
            }

            return !string.IsNullOrEmpty(header)
                   && long.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out declaredLength)
                   && declaredLength > maxResponseBytes;
        }

        private static void AbortSafely(UnityWebRequest request)
        {
            try
            {
                request.Abort();
            }
            catch (Exception)
            {
                // 既に完了・破棄されている場合は無視する。
            }
        }
    }
}
