using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Images;
using TsumugiQuiz.Room;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="QuestionDistributor"/> のうち、問題画像の分割送信（サーバー側）と
    /// Ack / NAK・再送をまとめた部分（docs/network.md §8.3 / §8.4、#16）。
    /// クライアント側の組み立て・復号・キャッシュは <c>QuestionDistributor.ImageRpc.cs</c> にある。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 流れ（1 問分）:
    /// </para>
    /// <code>
    /// TryPrepareDistribution : 画像ファイルを読み、SHA-256 とチャンク数を用意（Ack 期限も延長する）
    /// DistributePrepared     : DTO 配信に続けて、現在問 → 先読みの順にチャンク送信を予約
    /// 各 tick / フレーム      : 現在問のキューを優先して数チャンクずつ送る（帯域を平準化する）
    /// クライアント           : 全チャンク + ハッシュ一致 → ImageReceivedRpc（Ack）
    ///                         不一致・欠落・不正 → ImageFailedRpc（NAK）
    /// サーバー               : NAK を受けたクライアントへ 1 回だけ再送。それでも失敗したら
    ///                         そのクライアントは画像なしで進行（警告ログ）
    /// </code>
    /// <para>
    /// 現在問の画像 Ack は受付開始のゲート（docs/network.md §8.6）に含める。先読み分の画像も配信するが
    /// Ack は待たない。先読みで全員が受け取り終えた画像は、現在問になったときに**送り直さない**
    /// （<see cref="IsImageFullyAcked"/>）。
    /// </para>
    /// </remarks>
    public sealed partial class QuestionDistributor
    {
        /// <summary>
        /// 1 tick に送るチャンク数の基準（クライアント 1 人あたり）。実際に送る件数は
        /// 接続クライアント数で割った値（最低 1）で、人数が増えても上り帯域を食い潰さないようにする
        /// （docs/network.md §8.4）。
        /// </summary>
        public const int ImageChunksPerTick = 4;

        /// <summary>
        /// 画像込みの Ack 待ちの上限（秒）。<see cref="AckTimeoutSec"/> に送信時間の見積りを足した値を
        /// この範囲へクランプする（docs/network.md §8.4）。
        /// </summary>
        public const double MaxImageAckTimeoutSec = 15.0;

        /// <summary>
        /// Ack 期限の見積りに使う想定上り帯域（bps）。家庭用回線の上り 8Mbps を下限の目安として固定する
        /// （docs/network.md §8.4。実測値ではなく「これ以上遅い回線は待たない」という判断の閾値）。
        /// </summary>
        public const long AssumedUplinkBitsPerSec = 8_000_000L;

        /// <summary>想定上り帯域（バイト/秒）。</summary>
        private const long AssumedUplinkBytesPerSec = AssumedUplinkBitsPerSec / 8L;

        /// <summary>メタ情報の送信を表すチャンク番号。</summary>
        private const int MetaChunkIndex = -1;

        /// <summary>
        /// 画像 1 枚分の送信ジョブ数の上限（メタ情報 1 件 + チャンク最大数）。
        /// 画像は <see cref="QuestionLimits.MaxImageSizeBytes"/>（2MB）以下、
        /// 1 チャンク <see cref="NetworkConstants.ImageChunkBytes"/>（16KB）なので 1 + 128 件。
        /// </summary>
        private const int MaxImageSendJobsPerTransfer =
            1 + ((QuestionLimits.MaxImageSizeBytes + NetworkConstants.ImageChunkBytes - 1)
                 / NetworkConstants.ImageChunkBytes);

        /// <summary>
        /// 送信キューへ同時に積める「転送」の本数（PR #114 レビュー M-4）。
        /// 全員宛は現在問 + 先読み（<see cref="MaxPrefetchCount"/>）で最大 4 本、
        /// 個別宛（NAK の再送・途中参加の再同期）は定員（<see cref="RoomSettings.MaxMaxPlayers"/>）ぶんを見込む。
        /// </summary>
        private const int MaxQueuedImageTransfers = MaxPrefetchCount + 1 + RoomSettings.MaxMaxPlayers;

        /// <summary>
        /// 送信キューに積めるジョブ（メタ情報・チャンク）の総数の上限（PR #114 レビュー M-4）。
        /// これを超える予約は積まずに警告する。再接続を繰り返して個別再送を積み増し、
        /// 現在問・先読みの送信を押し流すことを防ぐための歯止め。
        /// </summary>
        internal const int MaxQueuedImageSendCount = MaxQueuedImageTransfers * MaxImageSendJobsPerTransfer;

        /// <summary>サーバー側: 問題インデックス → 送信準備済みの画像。</summary>
        private readonly Dictionary<int, PreparedImage> _preparedImages = new Dictionary<int, PreparedImage>();

        /// <summary>サーバー側: 現在問の送信待ち（先読みより優先して吐き出す）。</summary>
        private readonly Queue<ImageSendJob> _currentImageSendQueue = new Queue<ImageSendJob>();

        /// <summary>サーバー側: 先読み分の送信待ち。</summary>
        private readonly Queue<ImageSendJob> _prefetchImageSendQueue = new Queue<ImageSendJob>();

        /// <summary>
        /// サーバー側: キューに残っている転送（宛先 + 問題インデックス）と、その残ジョブ数
        /// （PR #114 レビュー M-4）。同じ宛先・同じ問題の転送を二重に積まないための記録で、
        /// 最後のジョブを取り出した時点で消える（＝ 送り終えたあとの再送は改めて積める）。
        /// </summary>
        private readonly Dictionary<(bool Broadcast, ulong ClientId, int QuestionIndex), int>
            _queuedImageTransfers = new Dictionary<(bool, ulong, int), int>();

        /// <summary>サーバー側: 現在問の画像 Ack がまだ届いていないクライアント。</summary>
        private readonly HashSet<ulong> _pendingImageAckClientIds = new HashSet<ulong>();

        /// <summary>サーバー側: 現在問の画像 Ack を既に受理したクライアント（重複の判別用）。</summary>
        private readonly HashSet<ulong> _imageAckedClientIds = new HashSet<ulong>();

        /// <summary>
        /// サーバー側: 問題インデックス → 既に再送したクライアント（再送は 1 問 1 クライアントにつき 1 回だけ）。
        /// </summary>
        private readonly Dictionary<int, HashSet<ulong>> _imageResentClientIds =
            new Dictionary<int, HashSet<ulong>>();

        /// <summary>
        /// サーバー側: 問題インデックス → 「その画像を受け取り終えたクライアント」の記録。
        /// 先読みで配り終えた画像を、現在問になったときに送り直さないために使う。
        /// </summary>
        private readonly Dictionary<int, ImageAckRecord> _imageAckRecords = new Dictionary<int, ImageAckRecord>();

        /// <summary>サーバー側: チャンク送信で使い回すバッファ（丸ごとのチャンク用）。</summary>
        private byte[] _fullChunkSendBuffer;

        /// <summary>サーバー側: チャンク送信で使い回すバッファ（末尾の端数チャンク用）。</summary>
        private byte[] _tailChunkSendBuffer;

        private IQuestionImageSource _imageSource;
        private int _imageAwaitingQuestionIndex = NoQuestionIndex;

        /// <summary>
        /// 直近の配信で「現在問」として扱った問題インデックス（Ack 待ちが終わったあとも保持する）。
        /// 再送を現在問のキューへ入れるかどうかの判断に使う。
        /// </summary>
        private int _currentImageQuestionIndex = NoQuestionIndex;
        private int _imageResendCount;

        /// <summary>直近にチャンクを送ったフレーム番号（1 フレームに 1 回だけ送るための記録）。</summary>
        private int _lastImageSendFrame = -1;

        /// <summary>現在問の画像 Ack を待っているか（サーバーのみ意味を持つ）。</summary>
        internal bool IsAwaitingImageAck => _imageAwaitingQuestionIndex != NoQuestionIndex;

        /// <summary>画像 Ack がまだ届いていないクライアント数（サーバーのみ意味を持つ）。</summary>
        internal int PendingImageAckCount => _pendingImageAckClientIds.Count;

        /// <summary>再送した回数（テスト・診断用）。</summary>
        internal int ImageResendCount => _imageResendCount;

        /// <summary>送信待ちのメタ情報・チャンクの件数（テスト・診断用）。</summary>
        internal int QueuedImageSendCount => _currentImageSendQueue.Count + _prefetchImageSendQueue.Count;

#if UNITY_INCLUDE_TESTS
        /// <summary>
        /// テスト専用のフック。送信直前にチャンクの内容を差し替える
        /// （引数は 問題インデックス・チャンク番号・元のチャンク。戻り値が実際に送るバイト列）。
        /// 改竄 → NAK → 再送の経路を PlayMode テストで通すために用意しており、
        /// テストを含まないビルドには存在しない（<c>UNITY_INCLUDE_TESTS</c>）。
        /// </summary>
        internal Func<int, int, byte[], byte[]> ImageChunkTransformForTests { get; set; }
#endif

        /// <summary>
        /// 現在問と先読み分の Ack が（DTO・画像ともに）揃ったか。
        /// </summary>
        private bool IsAckComplete => _pendingAckClientIds.Count == 0 && _pendingImageAckClientIds.Count == 0;

        /// <summary>
        /// 画像の供給元を設定する（サーバーのみ）。設定しない場合、画像は配信されない
        /// （テキストのみの進行になる）。
        /// </summary>
        /// <param name="imageSource">画像の供給元。null を渡すと画像配信を止める。</param>
        internal void SetImageSource(IQuestionImageSource imageSource)
        {
            _imageSource = imageSource;
        }

        /// <summary>
        /// 送信準備済みの画像のチャンク数を返す（テスト・診断用）。用意が無ければ 0。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <returns>チャンク数。</returns>
        internal int GetPreparedImageChunkCount(int questionIndex)
        {
            return _preparedImages.TryGetValue(questionIndex, out var image) ? image.ChunkCount : 0;
        }

        /// <summary>
        /// <see cref="_prepared"/> に積んだ問題の画像を読み込み、現在問の Ack 期限に足す秒数を返す
        /// （サーバーのみ、<see cref="TryPrepareDistribution"/> から呼ぶ）。
        /// </summary>
        /// <remarks>
        /// 画像の読み込みに失敗しても出題は止めない（警告を残して画像なしで進む）。
        /// 「画像が無い問題」と「画像を読めなかった問題」はログの有無で区別する。
        /// </remarks>
        /// <param name="currentQuestionIndex">現在問の問題インデックス。</param>
        /// <returns>Ack 期限に足す秒数（画像が無ければ 0）。</returns>
        private double PrepareImages(int currentQuestionIndex)
        {
            _preparedImages.Clear();
            _currentImageSendQueue.Clear();
            _prefetchImageSendQueue.Clear();
            _queuedImageTransfers.Clear();
            PruneImageBookkeeping(currentQuestionIndex);

            if (_imageSource == null)
            {
                return 0.0;
            }

            for (var i = 0; i < _prepared.Count; i++)
            {
                var questionIndex = _prepared[i].Index;
                if (TryLoadImage(questionIndex, out var image))
                {
                    _preparedImages[questionIndex] = image;
                }
            }

            if (!_preparedImages.TryGetValue(currentQuestionIndex, out var current))
            {
                return 0.0;
            }

            if (IsImageFullyAcked(current))
            {
                // 先読みで全員が受け取り終えている画像は送り直さないので、期限も延ばさない。
                return 0.0;
            }

            return EstimateImageAckTimeoutSec(current.TotalBytes, ConnectedClientCount()) - AckTimeoutSec;
        }

        /// <summary>画像ファイルを読み、ハッシュとチャンク数を用意する。</summary>
        private bool TryLoadImage(int questionIndex, out PreparedImage image)
        {
            image = default;

            if (_questionSource == null || !_questionSource.TryGetQuestion(questionIndex, out var question))
            {
                return false;
            }

            // 出題列は #19 でフィルタ・シャッフルされるため、画像は問題インデックスではなく
            // 問題そのもの（imagePath）から引く。
            if (!_imageSource.TryGetImagePath(question, out var path, out var error))
            {
                if (error != null)
                {
                    Debug.LogWarning(
                        $"[QuestionDistributor] 問題 {questionIndex} の画像を配信できません（画像なしで進みます）: {error}");
                }

                return false;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException
                                       || ex is UnauthorizedAccessException
                                       || ex is SecurityException
                                       || ex is NotSupportedException
                                       || ex is ArgumentException)
            {
                // ファイルが消えた・ロックされた・パスが壊れた等。出題は止めず画像なしで進む。
                Debug.LogWarning(
                    $"[QuestionDistributor] 問題 {questionIndex} の画像を読み込めませんでした（画像なしで進みます）: "
                    + $"{ex.GetType().Name}: {ex.Message}");
                return false;
            }

            // 拡張子とファイルサイズは供給元が検証済みだが、中身（PNG/JPG か・解像度）は送る前にここで確かめる。
            if (!ImageFormatProbe.TryValidate(bytes, out var reason))
            {
                Debug.LogWarning(
                    $"[QuestionDistributor] 問題 {questionIndex} の画像は配信できません（画像なしで進みます）: "
                    + $"{ImageFailureReasons.Describe(reason)}");
                return false;
            }

            image = new PreparedImage(
                questionIndex,
                bytes,
                ImageChunker.ComputeHash(bytes),
                ImageChunker.GetChunkCount(bytes.Length, NetworkConstants.ImageChunkBytes));
            return true;
        }

        /// <summary>
        /// 画像の送信を開始する（サーバーのみ、<see cref="DistributePrepared"/> から DTO 送信の前に呼ぶ）。
        /// Ack 待ちの登録を送信より先に済ませるのは、ホスト自身への RPC がローカルで同期実行されるため。
        /// </summary>
        /// <param name="currentQuestionIndex">現在問の問題インデックス。</param>
        private void BeginImageDistribution(int currentQuestionIndex)
        {
            _pendingImageAckClientIds.Clear();
            _imageAckedClientIds.Clear();
            _imageResentClientIds.Clear();
            _currentImageSendQueue.Clear();
            _prefetchImageSendQueue.Clear();
            _queuedImageTransfers.Clear();
            _imageAwaitingQuestionIndex = NoQuestionIndex;
            _currentImageQuestionIndex = currentQuestionIndex;
            _imageResendCount = 0;

            if (_preparedImages.Count == 0)
            {
                return;
            }

            if (_preparedImages.TryGetValue(currentQuestionIndex, out var current))
            {
                if (IsImageFullyAcked(current))
                {
                    // 先読みで全員が受け取り終えている。送り直さず、Ack も待たない
                    // （途中参加したクライアントは、未知のチャンク・メタ不在を NAK して個別再送で回収する）。
                    Debug.Log(
                        $"[QuestionDistributor] 問題 {currentQuestionIndex} の画像は先読みで全員に届いているため再送信しません。");
                }
                else
                {
                    _imageAwaitingQuestionIndex = currentQuestionIndex;
                    foreach (var clientId in NetworkManager.ConnectedClientsIds)
                    {
                        _pendingImageAckClientIds.Add(clientId);
                    }

                    EnqueueImageTransfer(current, broadcast: true, targetClientId: 0UL);
                }
            }

            foreach (var entry in _preparedImages)
            {
                if (entry.Key == currentQuestionIndex || IsImageFullyAcked(entry.Value))
                {
                    continue;
                }

                EnqueueImageTransfer(entry.Value, broadcast: true, targetClientId: 0UL);
            }
        }

        /// <summary>
        /// メタ情報 + 全チャンクを送信キューへ積む。現在問は先読みより優先されるキューへ入れる
        /// （先読みの 128 チャンクで現在問の表示が待たされないようにする）。
        /// </summary>
        /// <remarks>
        /// PR #114 レビュー M-4: 同じ宛先・同じ問題の転送が既にキューに残っていれば積み直さず、
        /// キュー全体にも上限（<see cref="MaxQueuedImageSendCount"/>）を設ける。
        /// 個別再送（NAK・途中参加の再同期）は外から何度でも起こせるため、
        /// 歯止めが無いと現在問・先読みの送信を押し流せてしまう。
        /// </remarks>
        /// <param name="image">送信準備済みの画像。</param>
        /// <param name="broadcast">全員宛か（false なら <paramref name="targetClientId"/> 1 人へ）。</param>
        /// <param name="targetClientId">個別送信の宛先（<paramref name="broadcast"/> が true なら無視）。</param>
        /// <returns>キューへ積んだら true。重複・上限超過で積まなかったら false。</returns>
        private bool EnqueueImageTransfer(PreparedImage image, bool broadcast, ulong targetClientId)
        {
            var key = ImageTransferKey(image.QuestionIndex, broadcast, targetClientId);
            if (_queuedImageTransfers.ContainsKey(key))
            {
                // 同じ宛先・同じ問題の転送がまだキューに残っている（NAK の連打・同一接続での再要求）。積み直さない。
                // なお NGO はクライアント ID を使い回さないため、切断 → 再接続を繰り返されると宛先が毎回変わり、
                // この重複排除は効かない。その場合の歯止めは下の総量上限（PR #114 再レビュー LOW-4）。
                return false;
            }

            var jobCount = image.ChunkCount + 1;
            if (QueuedImageSendCount + jobCount > MaxQueuedImageSendCount)
            {
                // 上限に張り付いている間は同じ警告が毎回出るため、棄却ログと同じ間引きに寄せる
                // （PR #114 再レビュー LOW-3。全員宛はサーバー自身のクライアント ID をキーにする）。
                LogRejected(
                    broadcast ? NetworkManager.ServerClientId : targetClientId,
                    $"[QuestionDistributor] 画像の送信キューが上限（{MaxQueuedImageSendCount} 件）に達したため、"
                    + $"問題 {image.QuestionIndex} の送信予約を見送りました"
                    + $"（宛先: {(broadcast ? "全員" : targetClientId.ToString())}）。");
                return false;
            }

            var queue = image.QuestionIndex == _currentImageQuestionIndex
                ? _currentImageSendQueue
                : _prefetchImageSendQueue;

            queue.Enqueue(new ImageSendJob(image.QuestionIndex, MetaChunkIndex, broadcast, targetClientId));
            for (var chunkIndex = 0; chunkIndex < image.ChunkCount; chunkIndex++)
            {
                queue.Enqueue(new ImageSendJob(image.QuestionIndex, chunkIndex, broadcast, targetClientId));
            }

            _queuedImageTransfers[key] = jobCount;
            return true;
        }

        /// <summary>指定した問題の画像が送信準備済みか（<see cref="TryResendTo"/> の警告の出し分け用）。</summary>
        private bool HasPreparedImage(int questionIndex) => _preparedImages.ContainsKey(questionIndex);

        /// <summary>送信キューの重複排除に使うキー（全員宛は宛先を 0 に寄せる）。</summary>
        private static (bool Broadcast, ulong ClientId, int QuestionIndex) ImageTransferKey(
            int questionIndex, bool broadcast, ulong targetClientId)
            => (broadcast, broadcast ? 0UL : targetClientId, questionIndex);

        /// <summary>キューから 1 件取り出したぶんだけ残ジョブ数を減らす（0 になったら記録を消す）。</summary>
        private void ForgetDequeuedImageJob(in ImageSendJob job)
        {
            var key = ImageTransferKey(job.QuestionIndex, job.Broadcast, job.TargetClientId);
            if (!_queuedImageTransfers.TryGetValue(key, out var remaining))
            {
                return;
            }

            if (remaining <= 1)
            {
                _queuedImageTransfers.Remove(key);
                return;
            }

            _queuedImageTransfers[key] = remaining - 1;
        }

        /// <summary>
        /// 送信キューを少しずつ吐き出す（サーバーの tick から呼ぶ）。
        /// </summary>
        /// <remarks>
        /// 1 フレームに複数の tick が走ることがある（フレームレートが tick レートを下回る場合）ため、
        /// フレーム番号で 1 回に制限してバーストを防ぐ。
        /// 1 回に送る件数は <see cref="ImageChunksPerTick"/> を接続クライアント数で割った値（最低 1）。
        /// </remarks>
        private void DrainImageSends()
        {
            if (_currentImageSendQueue.Count == 0 && _prefetchImageSendQueue.Count == 0)
            {
                return;
            }

            var frame = Time.frameCount;
            if (_lastImageSendFrame == frame)
            {
                return;
            }

            _lastImageSendFrame = frame;

            var budget = Math.Max(1, ImageChunksPerTick / ConnectedClientCount());

            // 送信中にホスト自身の Ack がローカルで返るため、キューを吐き終えるまで完了判定を抑える
            // （複数チャンクの途中で出題へ進んでしまわないように）。
            BeginCompletionSuppression();
            try
            {
                for (var sent = 0; sent < budget; sent++)
                {
                    if (!TryDequeueImageJob(out var job))
                    {
                        break;
                    }

                    SendImageJob(job);
                }
            }
            finally
            {
                EndCompletionSuppression();
            }

            if (_awaitingQuestionIndex != NoQuestionIndex && IsAckComplete && !IsCompletionSuppressed)
            {
                CompleteDistribution(timedOut: false);
            }
        }

        /// <summary>
        /// 途中参加・再接続したクライアント 1 人へ、送信準備済みの画像を送り直す
        /// （サーバーのみ、<see cref="TryResendTo"/> から呼ぶ、#19 連携）。
        /// </summary>
        /// <remarks>
        /// 通常の配信と違い Ack は待たない（進行はサーバー権威で既に動いているため）。
        /// 受け取ったクライアントが返す <see cref="ImageReceivedRpc"/> は記録だけに使う。
        /// </remarks>
        /// <param name="clientId">送信先クライアント ID。</param>
        /// <param name="questionIndex">送り直す問題インデックス。</param>
        /// <returns>
        /// 送信を予約したら true。用意済みの画像が無い（画像なしの問題・読み込み失敗）場合と、
        /// 同じ転送が既にキューにある・キューが上限に達した場合は false。
        /// </returns>
        private bool TryResendImageTo(ulong clientId, int questionIndex)
        {
            if (!_preparedImages.TryGetValue(questionIndex, out var image))
            {
                return false;
            }

            return EnqueueImageTransfer(image, broadcast: false, targetClientId: clientId);
        }

        /// <summary>現在問のキューを優先して 1 件取り出す。</summary>
        private bool TryDequeueImageJob(out ImageSendJob job)
        {
            if (_currentImageSendQueue.Count > 0)
            {
                job = _currentImageSendQueue.Dequeue();
                ForgetDequeuedImageJob(job);
                return true;
            }

            if (_prefetchImageSendQueue.Count > 0)
            {
                job = _prefetchImageSendQueue.Dequeue();
                ForgetDequeuedImageJob(job);
                return true;
            }

            job = default;
            return false;
        }

        /// <summary>1 件分（メタ情報かチャンク 1 つ）を送る。</summary>
        private void SendImageJob(ImageSendJob job)
        {
            if (!_preparedImages.TryGetValue(job.QuestionIndex, out var image))
            {
                // 出題が切り替わって用意が捨てられた場合。残りの送信予約は黙って捨てる。
                return;
            }

            BaseRpcTarget target;
            if (job.Broadcast)
            {
                target = RpcTarget.ClientsAndHost;
            }
            else
            {
                if (!NetworkManager.ConnectedClients.ContainsKey(job.TargetClientId))
                {
                    // 再送先が切断済み。
                    return;
                }

                target = RpcTarget.Single(job.TargetClientId, RpcTargetUse.Temp);
            }

            if (job.ChunkIndex == MetaChunkIndex)
            {
                ImageMetaRpc(image.QuestionIndex, image.TotalBytes, image.ChunkCount, image.Hash, target);
                return;
            }

            var chunk = RentChunkBuffer(image, job.ChunkIndex);
#if UNITY_INCLUDE_TESTS
            if (ImageChunkTransformForTests != null)
            {
                chunk = ImageChunkTransformForTests(image.QuestionIndex, job.ChunkIndex, chunk) ?? chunk;
            }
#endif

            ImageChunkRpc(image.QuestionIndex, job.ChunkIndex, image.ChunkCount, chunk, target);
        }

        /// <summary>
        /// 送信するチャンクを使い回しのバッファへ複製して返す
        /// （1 枚 128 チャンク × クライアント数ぶんの一時配列を作らないため）。
        /// RPC は呼び出しの中で直列化されるので、戻した配列を次の送信で再利用してよい。
        /// </summary>
        private byte[] RentChunkBuffer(PreparedImage image, int chunkIndex)
        {
            var length = ImageChunker.GetChunkLength(
                image.TotalBytes, chunkIndex, NetworkConstants.ImageChunkBytes);

            byte[] buffer;
            if (length == NetworkConstants.ImageChunkBytes)
            {
                buffer = _fullChunkSendBuffer ??= new byte[NetworkConstants.ImageChunkBytes];
            }
            else
            {
                if (_tailChunkSendBuffer == null || _tailChunkSendBuffer.Length != length)
                {
                    _tailChunkSendBuffer = new byte[length];
                }

                buffer = _tailChunkSendBuffer;
            }

            ImageChunker.CopyChunkInto(image.Bytes, chunkIndex, NetworkConstants.ImageChunkBytes, buffer);
            return buffer;
        }

        /// <summary>
        /// 現在問の Ack 待ちを終えるときに、画像 Ack の状況をログに残して待ちを解く
        /// （<see cref="CompleteDistribution"/> から呼ぶ）。
        /// </summary>
        /// <param name="questionIndex">対象の問題インデックス。</param>
        /// <param name="timedOut">タイムアウトで終えたか。</param>
        private void CompleteImageDistribution(int questionIndex, bool timedOut)
        {
            if (timedOut && _pendingImageAckClientIds.Count > 0)
            {
                Debug.LogWarning(
                    $"[QuestionDistributor] 問題 {questionIndex} の画像の受信確認が揃いませんでした"
                    + $"（未受信: {string.Join(", ", _pendingImageAckClientIds)}）。"
                    + "該当クライアントは画像なしで出題します。");
            }

            _pendingImageAckClientIds.Clear();
            _imageAwaitingQuestionIndex = NoQuestionIndex;

            // 送信キューはそのまま残す（Ack 待ちは終えても、残りのチャンクと先読み分は送り切る）。
        }

        /// <summary>
        /// 画像込みの Ack 期限（秒）を見積もる（docs/network.md §8.4）。
        /// 「総バイト数 × クライアント数 ÷ 想定上り帯域」を <see cref="AckTimeoutSec"/> に足し、
        /// <see cref="AckTimeoutSec"/>〜<see cref="MaxImageAckTimeoutSec"/> にクランプする。
        /// </summary>
        /// <param name="totalBytes">画像の総バイト数。</param>
        /// <param name="clientCount">配信対象のクライアント数（ホストを含む）。</param>
        /// <returns>Ack 期限（秒）。</returns>
        private static double EstimateImageAckTimeoutSec(int totalBytes, int clientCount)
        {
            var transferSeconds = (double)totalBytes * Math.Max(1, clientCount) / AssumedUplinkBytesPerSec;
            return Math.Min(Math.Max(AckTimeoutSec + transferSeconds, AckTimeoutSec), MaxImageAckTimeoutSec);
        }

        /// <summary>接続しているクライアント数（ホストを含む。最低 1）。</summary>
        private int ConnectedClientCount()
        {
            var count = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;
            return Math.Max(1, count);
        }

        /// <summary>
        /// 「その画像を、いま接続している全クライアントが受け取り終えている」か。
        /// 先読みで配り終えた画像を現在問で送り直さないための判定（docs/network.md §8.4）。
        /// </summary>
        private bool IsImageFullyAcked(PreparedImage image)
        {
            if (!_imageAckRecords.TryGetValue(image.QuestionIndex, out var record)
                || !ImageChunker.HashEquals(record.Hash, image.Hash))
            {
                return false;
            }

            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                if (!record.AckedClientIds.Contains(clientId))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Ack を受け取ったことを記録する（画像の内容が変わったら記録は作り直す）。</summary>
        private void RecordImageAck(int questionIndex, ulong clientId)
        {
            if (!_preparedImages.TryGetValue(questionIndex, out var image))
            {
                return;
            }

            if (!_imageAckRecords.TryGetValue(questionIndex, out var record)
                || !ImageChunker.HashEquals(record.Hash, image.Hash))
            {
                record = new ImageAckRecord(image.Hash);
                _imageAckRecords[questionIndex] = record;
            }

            record.AckedClientIds.Add(clientId);
        }

        /// <summary>再送済みのクライアントとして登録する（1 問 1 クライアントにつき 1 回だけ true）。</summary>
        private bool TryMarkImageResent(int questionIndex, ulong clientId)
        {
            if (!_imageResentClientIds.TryGetValue(questionIndex, out var clients))
            {
                clients = new HashSet<ulong>();
                _imageResentClientIds[questionIndex] = clients;
            }

            return clients.Add(clientId);
        }

        /// <summary>
        /// <see cref="TryMarkImageResent"/> で消費した再送権を戻す（PR #114 再レビュー LOW-2）。
        /// 送信キューが上限で積めなかったときに呼び、次の NAK で改めて再送できるようにする。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="clientId">対象のクライアント ID。</param>
        private void ForgetImageResent(int questionIndex, ulong clientId)
        {
            if (_imageResentClientIds.TryGetValue(questionIndex, out var clients))
            {
                clients.Remove(clientId);
            }
        }

        /// <summary>切断したクライアントの画像 Ack は待たない。</summary>
        private void HandleImageClientDisconnected(ulong clientId)
        {
            _imageAckedClientIds.Remove(clientId);
            _pendingImageAckClientIds.Remove(clientId);

            foreach (var entry in _imageResentClientIds)
            {
                entry.Value.Remove(clientId);
            }

            // Ack の記録は「いま接続している全員が含まれるか」で判定するため、
            // 切断したクライアントの分を残しても誤判定にはならない（出題ごとに古い分を捨てる）。
        }

        /// <summary>出題より古い問題の記録を捨てる（辞書を無限に伸ばさないため）。</summary>
        private void PruneImageBookkeeping(int currentQuestionIndex)
        {
            RemoveKeysBelow(_imageAckRecords, currentQuestionIndex);
            RemoveKeysBelow(_imageResentClientIds, currentQuestionIndex);
        }

        private static void RemoveKeysBelow<TValue>(Dictionary<int, TValue> source, int threshold)
        {
            if (source.Count == 0)
            {
                return;
            }

            var stale = new List<int>();
            foreach (var key in source.Keys)
            {
                if (key < threshold)
                {
                    stale.Add(key);
                }
            }

            for (var i = 0; i < stale.Count; i++)
            {
                source.Remove(stale[i]);
            }
        }

        /// <summary>サーバー側の画像配信の状態を捨てる。</summary>
        private void ResetImageDistribution()
        {
            _preparedImages.Clear();
            _currentImageSendQueue.Clear();
            _prefetchImageSendQueue.Clear();
            _queuedImageTransfers.Clear();
            _pendingImageAckClientIds.Clear();
            _imageAckedClientIds.Clear();
            _imageResentClientIds.Clear();
            _imageAckRecords.Clear();
            _imageAwaitingQuestionIndex = NoQuestionIndex;
            _currentImageQuestionIndex = NoQuestionIndex;
            _imageResendCount = 0;
            _lastImageSendFrame = -1;
        }

        /// <summary>送信準備済みの画像 1 枚（サーバー側）。</summary>
        private readonly struct PreparedImage
        {
            public PreparedImage(int questionIndex, byte[] bytes, byte[] hash, int chunkCount)
            {
                QuestionIndex = questionIndex;
                Bytes = bytes;
                Hash = hash;
                ChunkCount = chunkCount;
            }

            public int QuestionIndex { get; }

            /// <summary>画像のバイト列（読み込み後は書き換えない）。</summary>
            public byte[] Bytes { get; }

            /// <summary>SHA-256（32 バイト）。</summary>
            public byte[] Hash { get; }

            public int ChunkCount { get; }

            public int TotalBytes => Bytes.Length;
        }

        /// <summary>1 枚の画像について「誰が受け取り終えたか」の記録。</summary>
        private sealed class ImageAckRecord
        {
            public ImageAckRecord(byte[] hash)
            {
                Hash = hash;
                AckedClientIds = new HashSet<ulong>();
            }

            /// <summary>記録の対象となる画像の SHA-256（画像が差し替わったら記録を捨てるため）。</summary>
            public byte[] Hash { get; }

            public HashSet<ulong> AckedClientIds { get; }
        }

        /// <summary>送信キューの 1 件（メタ情報またはチャンク 1 つ）。</summary>
        private readonly struct ImageSendJob
        {
            public ImageSendJob(int questionIndex, int chunkIndex, bool broadcast, ulong targetClientId)
            {
                QuestionIndex = questionIndex;
                ChunkIndex = chunkIndex;
                Broadcast = broadcast;
                TargetClientId = targetClientId;
            }

            public int QuestionIndex { get; }

            /// <summary>チャンク番号。<see cref="MetaChunkIndex"/> ならメタ情報。</summary>
            public int ChunkIndex { get; }

            /// <summary>全員へ送るか（false なら <see cref="TargetClientId"/> だけへ再送する）。</summary>
            public bool Broadcast { get; }

            public ulong TargetClientId { get; }
        }
    }
}
