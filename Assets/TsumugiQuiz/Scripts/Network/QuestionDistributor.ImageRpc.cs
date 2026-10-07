using System;
using System.Collections.Generic;
using TsumugiQuiz.Questions.Images;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="QuestionDistributor"/> のうち、問題画像の RPC 定義（docs/network.md §8.3 / §8.4）と
    /// クライアント側の組み立て・復号・キャッシュをまとめた部分（#16）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// サーバー → クライアントの <see cref="ImageMetaRpc"/> / <see cref="ImageChunkRpc"/> は
    /// <c>InvokePermission = RpcInvokePermission.Server</c> かつ <c>private</c> で、クライアントから送れない。
    /// 送り先は <c>SendTo.SpecifiedInParams</c> で実行時に決める（全員への配信と、NAK を返した
    /// 1 クライアントへの再送で同じ RPC を使うため）。
    /// </para>
    /// <para>
    /// クライアント → サーバーの <see cref="ImageReceivedRpc"/>（Ack）/ <see cref="ImageFailedRpc"/>（NAK）は
    /// 送信元を <c>rpcParams.Receive.SenderClientId</c> から取り、問題インデックス・重複を検証する
    /// （docs/network.md §9）。
    /// </para>
    /// <para>
    /// 受信したバイト列は、ハッシュ一致を確かめたうえで
    /// PNG / JPG かつ解像度 <see cref="TsumugiQuiz.Questions.QuestionLimits.MaxImageDimension"/> 以内であることを
    /// ヘッダから確認してから <c>Texture2D.LoadImage</c> に渡す（メインスレッド）。
    /// </para>
    /// <para>
    /// 既に持っている画像と同じ SHA-256 のメタ情報が届いた場合（先読みで受け取った画像が
    /// 現在問として配り直された場合など）は、組み立てをやり直さずその場で Ack を返す。
    /// </para>
    /// </remarks>
    public sealed partial class QuestionDistributor
    {
        /// <summary>
        /// クライアントが保持する画像の最大件数（現在問 + 先読み + 余裕 1 件）。
        /// これを超えたら現在問から遠いものを破棄する（テクスチャは <c>Destroy</c> する）。
        /// </summary>
        public const int MaxCachedImages = 3;

        /// <summary>クライアント側: 問題インデックス → 組み立て中の画像。</summary>
        private readonly Dictionary<int, ImageAssembler> _imageAssemblers = new Dictionary<int, ImageAssembler>();

        /// <summary>クライアント側: 問題インデックス → 復号済みのテクスチャ。</summary>
        private readonly Dictionary<int, Texture2D> _imageTextures = new Dictionary<int, Texture2D>();

        /// <summary>クライアント側: 問題インデックス → 保持している画像の SHA-256（再受信の判定に使う）。</summary>
        private readonly Dictionary<int, byte[]> _imageHashes = new Dictionary<int, byte[]>();

        /// <summary>
        /// クライアント側: NAK を送った問題インデックス。
        /// 1 回の転送につき NAK は 1 度だけ送る（128 チャンク分の NAK を送りつけないため）。
        /// 新しいメタ情報を受け取った時点で解除する。
        /// </summary>
        private readonly HashSet<int> _nakSentQuestionIndices = new HashSet<int>();

        /// <summary>
        /// クライアント側: 差し替え・破棄が決まったが、まだ <c>Destroy</c> していないテクスチャ。
        /// 表示中のテクスチャをフレームの途中で消さないよう、実際の破棄は次の出題まで遅らせる。
        /// </summary>
        private readonly List<Texture2D> _retiredTextures = new List<Texture2D>();

        /// <summary>クライアント側: 現在出題中の問題インデックス（保持件数を絞る基準）。</summary>
        private int _clientCurrentQuestionIndex;

        /// <summary>
        /// 画像を復号し終えたとき（クライアント・ホスト双方で発火）。引数は（問題インデックス, テクスチャ）。
        /// </summary>
        public event Action<int, Texture2D> ImageReceived;

        /// <summary>
        /// 保持していた画像が無効になったとき（差し替え・破棄予定）。引数は問題インデックス。
        /// 受け取った側は <see cref="TryGetImage"/> で取り直すか、参照を捨てること
        /// （実際の <c>Destroy</c> は次の出題まで遅らせるので、同じフレーム内で描画が壊れることはない）。
        /// </summary>
        public event Action<int> ImageInvalidated;

        /// <summary>クライアントが保持している画像の件数（テスト・診断用）。</summary>
        internal int CachedImageCount => _imageTextures.Count;

        /// <summary>
        /// 受信済みの問題画像を取り出す（クライアント・ホスト双方で使える）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="image">復号済みのテクスチャ。無ければ null。</param>
        /// <returns>保持していたら true。</returns>
        public bool TryGetImage(int questionIndex, out Texture2D image)
        {
            if (_imageTextures.TryGetValue(questionIndex, out var texture) && texture != null)
            {
                image = texture;
                return true;
            }

            image = null;
            return false;
        }

        /// <summary>
        /// 保持している画像を全部捨てる（テクスチャも破棄する）。
        /// <see cref="ResetCache"/> から呼ばれるため、通常は直接呼ばなくてよい。
        /// </summary>
        /// <remarks>
        /// ここでは <see cref="ImageInvalidated"/> を発火しない（テクスチャを即座に <c>Destroy</c> する）。
        /// 呼ばれるのは現在問 0 の配信（新しいセッションの開始）と配信器の破棄のときだけで、
        /// いずれも表示側（<c>GameView</c>）は直後の出題（<c>QuestionShown</c>）で画像を取り直すか、
        /// 画面ごと破棄される前提である（#185、<c>GameView.QuestionImage.cs</c>）。
        /// 途中で呼ぶ用途を増やす場合は、表示中のテクスチャが消えることに注意して発火を検討すること。
        /// </remarks>
        public void ResetImageCache()
        {
            foreach (var entry in _imageTextures)
            {
                DestroyTexture(entry.Value);
            }

            _imageTextures.Clear();
            _imageHashes.Clear();
            _imageAssemblers.Clear();
            _nakSentQuestionIndices.Clear();
            DestroyRetiredTextures();
        }

        /// <summary>
        /// 画像のメタ情報（サーバー → 指定の送り先）。総バイト数・チャンク数・SHA-256 を先に配る
        /// （docs/network.md §8.4）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="totalBytes">画像の総バイト数。</param>
        /// <param name="chunkCount">チャンク数。</param>
        /// <param name="sha256">画像全体の SHA-256（32 バイト）。</param>
        /// <param name="rpcParams">送り先（送信時）／受信情報（受信時）。</param>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ImageMetaRpc(
            int questionIndex, int totalBytes, int chunkCount, byte[] sha256, RpcParams rpcParams = default)
        {
            if (questionIndex < 0)
            {
                // どの問題の画像かが分からないので NAK も返せない。
                Debug.LogWarning($"[QuestionDistributor] 画像メタ情報の問題インデックスが不正です（{questionIndex}）。");
                return;
            }

            // 新しい転送が始まるので、前の転送で立てた NAK 抑止を解除する
            // （この転送でも失敗したら、あらためて 1 度だけ NAK を送れるようにする）。
            _nakSentQuestionIndices.Remove(questionIndex);

            if (_imageHashes.TryGetValue(questionIndex, out var knownHash)
                && ImageChunker.HashEquals(knownHash, sha256)
                && TryGetImage(questionIndex, out _))
            {
                // 先読みで受け取り済みの画像が現在問として配り直された場合。
                // 組み立て直さず、その場で受信確認を返す（docs/network.md §8.4）。
                _imageAssemblers.Remove(questionIndex);
                ImageReceivedRpc(questionIndex);
                return;
            }

            if (!ImageAssembler.TryCreate(
                    totalBytes,
                    chunkCount,
                    NetworkConstants.ImageChunkBytes,
                    sha256,
                    out var assembler,
                    out var reason))
            {
                Debug.LogWarning(
                    $"[QuestionDistributor] 問題 {questionIndex} の画像メタ情報を破棄しました"
                    + $"（{ImageFailureReasons.Describe(reason)}: {totalBytes} バイト / {chunkCount} チャンク）。");
                _imageAssemblers.Remove(questionIndex);
                SendImageNak(questionIndex, reason);
                return;
            }

            // 内容が変わる転送なので、持っている画像は無効にする（破棄は次の出題まで遅らせる）。
            _imageAssemblers[questionIndex] = assembler;
            InvalidateImage(questionIndex);
        }

        /// <summary>
        /// 画像チャンク（サーバー → 指定の送り先）。1 チャンクは
        /// <see cref="NetworkConstants.ImageChunkBytes"/> バイト以下（docs/network.md §8.3）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="chunkIndex">チャンク番号（0 始まり）。</param>
        /// <param name="chunkCount">チャンク数（メタ情報との整合を確かめるために毎回送る）。</param>
        /// <param name="data">チャンクのバイト列。</param>
        /// <param name="rpcParams">送り先（送信時）／受信情報（受信時）。</param>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ImageChunkRpc(
            int questionIndex, int chunkIndex, int chunkCount, byte[] data, RpcParams rpcParams = default)
        {
            if (!_imageAssemblers.TryGetValue(questionIndex, out var assembler))
            {
                if (TryGetImage(questionIndex, out _))
                {
                    // 受信済みの画像のチャンクが遅れて届いた（自分の Ack より前に送られた分）。棄却でよい。
                    return;
                }

                // メタ情報を受け取っていない（取りこぼした・途中参加した）。再送を要求する。
                LogImageRejected(
                    questionIndex,
                    $"[QuestionDistributor] メタ情報の無い画像チャンクを破棄しました（問題 {questionIndex}、チャンク {chunkIndex}）。");
                SendImageNak(questionIndex, ImageFailureReason.UnknownQuestion);
                return;
            }

            if (chunkCount != assembler.ChunkCount || data == null || data.Length > NetworkConstants.ImageChunkBytes)
            {
                LogImageRejected(
                    questionIndex,
                    $"[QuestionDistributor] 画像チャンクが不正です（問題 {questionIndex}、チャンク {chunkIndex}、"
                    + $"チャンク数 {chunkCount}、長さ {data?.Length ?? 0}）。");
                SendImageNak(questionIndex, ImageFailureReason.InvalidChunk);
                return;
            }

            var accepted = assembler.Accept(chunkIndex, data);
            if (accepted != ImageFailureReason.None)
            {
                LogImageRejected(
                    questionIndex,
                    $"[QuestionDistributor] 画像チャンクを破棄しました（問題 {questionIndex}、チャンク {chunkIndex}、"
                    + $"理由: {ImageFailureReasons.Describe(accepted)}）。");
                SendImageNak(questionIndex, accepted);
                return;
            }

            if (!assembler.IsComplete)
            {
                return;
            }

            CompleteImageAssembly(questionIndex, assembler);
        }

        /// <summary>全チャンクが揃った画像を検証し、復号して保持する。</summary>
        private void CompleteImageAssembly(int questionIndex, ImageAssembler assembler)
        {
            if (!assembler.TryGetImage(out var bytes, out var reason))
            {
                Debug.LogWarning(
                    $"[QuestionDistributor] 問題 {questionIndex} の画像を破棄しました"
                    + $"（{ImageFailureReasons.Describe(reason)}）。再送を要求します。");

                // 組み立て器を捨てる。再送のメタ情報で作り直す。
                _imageAssemblers.Remove(questionIndex);
                SendImageNak(questionIndex, reason);
                return;
            }

            if (!ImageFormatProbe.TryValidate(bytes, out var formatReason))
            {
                // ハッシュは一致しているので再送しても同じ結果になる。再送を求めない理由として NAK を返す。
                Debug.LogWarning(
                    $"[QuestionDistributor] 問題 {questionIndex} の画像は表示できません"
                    + $"（{ImageFailureReasons.Describe(formatReason)}）。画像なしで進みます。");
                _imageAssemblers.Remove(questionIndex);
                SendImageNak(questionIndex, formatReason);
                return;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);

            // markNonReadable: true で CPU 側の複製を持たない（表示するだけなので読み戻さない）。
            if (!texture.LoadImage(bytes, markNonReadable: true))
            {
                DestroyTexture(texture);
                Debug.LogWarning(
                    $"[QuestionDistributor] 問題 {questionIndex} の画像を復号できませんでした。画像なしで進みます。");
                _imageAssemblers.Remove(questionIndex);
                SendImageNak(questionIndex, ImageFailureReason.DecodeFailed);
                return;
            }

            texture.name = $"QuestionImage({questionIndex})";

            _imageAssemblers.Remove(questionIndex);
            _nakSentQuestionIndices.Remove(questionIndex);
            InvalidateImage(questionIndex);
            _imageTextures[questionIndex] = texture;
            _imageHashes[questionIndex] = ImageChunker.ComputeHash(bytes);
            TrimImageCache();

            ImageReceived?.Invoke(questionIndex, texture);

            // 受信完了を通知する（現在問なら受付開始のゲートが外れる、docs/network.md §8.6）。
            ImageReceivedRpc(questionIndex);
        }

        /// <summary>
        /// 画像の受信完了（クライアント → サーバー）。
        /// 現在問の分はサーバーが受付開始のゲートとして待っている。
        /// </summary>
        /// <param name="questionIndex">受信した問題インデックス。</param>
        /// <param name="rpcParams">NGO が埋める受信情報。送信元 ID はここから取る。</param>
        [Rpc(SendTo.Server)]
        internal void ImageReceivedRpc(int questionIndex, RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time)) { return; }

            if (!_preparedImages.ContainsKey(questionIndex))
            {
                LogRejected(
                    senderId,
                    $"[QuestionDistributor] 配信していない画像の受信確認を棄却しました（送信元 {senderId}、問題 {questionIndex}）。");
                return;
            }

            // 「誰がどの画像を受け取り終えたか」は、先読み分も含めて記録する
            // （次の出題で同じ画像を送り直さないため、docs/network.md §8.4）。
            RecordImageAck(questionIndex, senderId);

            if (questionIndex != _imageAwaitingQuestionIndex)
            {
                // 先読み分の Ack、または待ち終了後に遅れて届いた Ack。進行には影響しない。
                return;
            }

            if (_imageAckedClientIds.Contains(senderId))
            {
                LogRejected(
                    senderId,
                    $"[QuestionDistributor] 重複した画像の受信確認を棄却しました（送信元 {senderId}、問題 {questionIndex}）。");
                return;
            }

            if (!_pendingImageAckClientIds.Remove(senderId))
            {
                LogRejected(
                    senderId,
                    $"[QuestionDistributor] 待ち対象外のクライアントからの画像の受信確認を棄却しました"
                    + $"（送信元 {senderId}、問題 {questionIndex}）。");
                return;
            }

            _imageAckedClientIds.Add(senderId);

            if (_awaitingQuestionIndex != NoQuestionIndex && IsAckComplete && !IsCompletionSuppressed)
            {
                CompleteDistribution(timedOut: false);
            }
        }

        /// <summary>
        /// 画像の受信失敗（クライアント → サーバー、NAK。docs/network.md §8.4）。
        /// 再送は 1 クライアントにつき 1 回だけで、それでも失敗したらそのクライアントは画像なしで進む。
        /// </summary>
        /// <param name="questionIndex">失敗した問題インデックス。</param>
        /// <param name="reason">失敗理由。</param>
        /// <param name="rpcParams">NGO が埋める受信情報。送信元 ID はここから取る。</param>
        [Rpc(SendTo.Server)]
        internal void ImageFailedRpc(int questionIndex, ImageFailureReason reason, RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time)) { return; }

            if (!_preparedImages.TryGetValue(questionIndex, out var image))
            {
                LogRejected(
                    senderId,
                    $"[QuestionDistributor] 配信していない画像の失敗通知を棄却しました（送信元 {senderId}、問題 {questionIndex}）。");
                return;
            }

            if (!Enum.IsDefined(typeof(ImageFailureReason), reason))
            {
                LogRejected(
                    senderId,
                    $"[QuestionDistributor] 不正な失敗理由の通知を棄却しました（送信元 {senderId}、値 {(int)reason}）。");
                return;
            }

            if (ImageFailureReasons.IsRetryable(reason) && TryMarkImageResent(questionIndex, senderId))
            {
                if (EnqueueImageTransfer(image, broadcast: false, targetClientId: senderId))
                {
                    _imageResendCount++;

                    // 連続した NAK でログを埋めないよう、クライアント単位で間引く（docs/network.md §9）。
                    LogRejected(
                        senderId,
                        $"[QuestionDistributor] 問題 {questionIndex} の画像をクライアント {senderId} へ再送します"
                        + $"（理由: {ImageFailureReasons.Describe(reason)}）。");
                    return;
                }

                // 送信キューが上限に達していて積めなかった（PR #114 再レビュー LOW-2）。
                // 消費した「1 問 1 クライアントにつき 1 回」の再送権を戻し、
                // キューが空いたあとの NAK で改めて再送できるようにする。
                // 同じ転送が既にキューに残っている場合も戻すが、そのときは送信中なので二重にはならない。
                ForgetImageResent(questionIndex, senderId);
            }

            LogRejected(
                senderId,
                $"[QuestionDistributor] 問題 {questionIndex} の画像をクライアント {senderId} へ届けられませんでした"
                + $"（理由: {ImageFailureReasons.Describe(reason)}）。このクライアントは画像なしで進行します。");

            if (questionIndex == _imageAwaitingQuestionIndex
                && _pendingImageAckClientIds.Remove(senderId)
                && _awaitingQuestionIndex != NoQuestionIndex
                && IsAckComplete
                && !IsCompletionSuppressed)
            {
                CompleteDistribution(timedOut: false);
            }
        }

        /// <summary>NAK を 1 転送につき 1 回だけ送る。</summary>
        private void SendImageNak(int questionIndex, ImageFailureReason reason)
        {
            if (questionIndex < 0 || !_nakSentQuestionIndices.Add(questionIndex))
            {
                return;
            }

            ImageFailedRpc(questionIndex, reason);
        }

        /// <summary>
        /// 指定した問題インデックスより古い画像を捨て、破棄を待っていたテクスチャを解放する
        /// （<c>QuestionDataRpc</c> が現在問を受け取ったときに呼ぶ）。
        /// </summary>
        private void PruneImagesOlderThan(int questionIndex)
        {
            _clientCurrentQuestionIndex = questionIndex;

            RemoveStaleKeys(_imageTextures, questionIndex, InvalidateImage);
            RemoveStaleKeys(_imageAssemblers, questionIndex, index => _imageAssemblers.Remove(index));
            RemoveStaleKeys(_imageHashes, questionIndex, index => _imageHashes.Remove(index));

            var staleNaks = new List<int>();
            foreach (var index in _nakSentQuestionIndices)
            {
                if (index < questionIndex)
                {
                    staleNaks.Add(index);
                }
            }

            for (var i = 0; i < staleNaks.Count; i++)
            {
                _nakSentQuestionIndices.Remove(staleNaks[i]);
            }

            // ここが「次の出題」のタイミング。差し替え・破棄が決まっていたテクスチャを解放する。
            DestroyRetiredTextures();
        }

        /// <summary>
        /// 保持件数の上限（<see cref="MaxCachedImages"/>）を超えた分を、
        /// **現在問から**遠い順に捨てる（先読みを受け取った拍子に現在問を落とさないため）。
        /// </summary>
        private void TrimImageCache()
        {
            while (_imageTextures.Count > MaxCachedImages)
            {
                var farthestIndex = _clientCurrentQuestionIndex;
                var farthestDistance = -1;
                foreach (var index in _imageTextures.Keys)
                {
                    var distance = Mathf.Abs(index - _clientCurrentQuestionIndex);
                    if (distance > farthestDistance)
                    {
                        farthestDistance = distance;
                        farthestIndex = index;
                    }
                }

                if (farthestDistance <= 0)
                {
                    return;
                }

                InvalidateImage(farthestIndex);
            }
        }

        /// <summary>
        /// 1 件の画像を無効にする。テクスチャは即座に <c>Destroy</c> せず、
        /// 次の出題（<see cref="PruneImagesOlderThan"/>）まで保持してから解放する。
        /// </summary>
        private void InvalidateImage(int questionIndex)
        {
            if (!_imageTextures.TryGetValue(questionIndex, out var texture))
            {
                return;
            }

            _imageTextures.Remove(questionIndex);
            _imageHashes.Remove(questionIndex);

            if (texture != null)
            {
                _retiredTextures.Add(texture);
            }

            ImageInvalidated?.Invoke(questionIndex);
        }

        /// <summary>破棄待ちのテクスチャを解放する。</summary>
        private void DestroyRetiredTextures()
        {
            for (var i = 0; i < _retiredTextures.Count; i++)
            {
                DestroyTexture(_retiredTextures[i]);
            }

            _retiredTextures.Clear();
        }

        /// <summary>
        /// 指定インデックスより小さいキーを列挙して取り除く
        /// （辞書を列挙しながら消せないため、一度キーを集める）。
        /// </summary>
        private static void RemoveStaleKeys<TValue>(
            Dictionary<int, TValue> source, int questionIndex, Action<int> remove)
        {
            if (source.Count == 0)
            {
                return;
            }

            var stale = new List<int>();
            foreach (var index in source.Keys)
            {
                if (index < questionIndex)
                {
                    stale.Add(index);
                }
            }

            for (var i = 0; i < stale.Count; i++)
            {
                remove(stale[i]);
            }
        }

        /// <summary>棄却ログを問題インデックス単位で間引いて残す（チャンクごとに出さない）。</summary>
        private void LogImageRejected(int questionIndex, string message)
        {
            if (_nakSentQuestionIndices.Contains(questionIndex))
            {
                return;
            }

            Debug.LogWarning(message);
        }

        /// <summary>テクスチャを破棄する（エディタの非再生時にも安全なように分岐する）。</summary>
        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
