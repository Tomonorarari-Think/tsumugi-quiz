using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Images;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 問題画像の分割送信（メタ情報 → チャンク → Ack / NAK → 再送）を実際のネットワーク経路で通す統合テスト
    /// （docs/network.md §8.3 / §8.4、#16）。
    /// </summary>
    public class QuestionImageDistributionTests : GameSessionTestFixture
    {
        /// <summary>テスト画像の 1 辺（ノイズ PNG で 100KB 程度になる大きさ）。</summary>
        private const int ImageSide = 180;

        /// <summary>2MB 近い画像を作るときの開始サイズ（1 辺）。</summary>
        private const int LargeImageStartSide = 816;

        private string _questionsFolder;
        private byte[] _imageBytes;

        private readonly List<TransportMessage> _clientMessages = new List<TransportMessage>();

        private bool _capturingClientPayloads;

        [SetUp]
        public void CreateQuestionsFolder()
        {
            _questionsFolder = Path.Combine(Path.GetTempPath(), "tq-image-dist-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_questionsFolder, "images"));
        }

        [TearDown]
        public void DeleteQuestionsFolder()
        {
            StopCapturingClientPayloads();
            _clientMessages.Clear();

            if (_questionsFolder != null && Directory.Exists(_questionsFolder))
            {
                Directory.Delete(_questionsFolder, recursive: true);
            }

            _questionsFolder = null;
            _imageBytes = null;
        }

        /// <summary>
        /// 画像付きの問題を配信すると、クライアントが全チャンクを受け取り、ハッシュ一致で Ack を返し、
        /// <see cref="QuestionDistributor.TryGetImage"/> からテクスチャが取れること。
        /// 送信が複数フレームに分散し、1 メッセージが過大にならないことも併せて確かめる。
        /// </summary>
        [UnityTest]
        public IEnumerator Image_IsChunkedAndAcked_ThenAvailableAsTexture()
        {
            var source = CreateImageQuestionSource(CreateNoisePng(ImageSide, ImageSide));

            yield return ConnectHostAndClient(source);
            CaptureClientPayloads();

            Assert.IsTrue(
                HostSession.ConfigureImages(new QuestionImageSource(_questionsFolder)),
                "画像の供給元を設定できるはず。");

            var stopwatch = Stopwatch.StartNew();
            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            // 画像の Ack が揃うまでは Reading に留まる（受付開始のゲート、docs/network.md §8.6）。
            Assert.IsTrue(HostDistributor.IsAwaitingImageAck, "現在問の画像 Ack を待っているはず。");
            Assert.AreEqual(
                ImageChunker.GetChunkCount(_imageBytes.Length, NetworkConstants.ImageChunkBytes),
                HostDistributor.GetPreparedImageChunkCount(0),
                "16KB チャンクに分割されているはず。");

            Texture2D received = null;
            yield return WaitUntil(
                () => ClientDistributor.TryGetImage(0, out received),
                () => "クライアントが画像を受け取れませんでした。");
            var imageElapsedMs = stopwatch.ElapsedMilliseconds;

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {ClientSession.Phase.Value}）。");
            stopwatch.Stop();

            StopCapturingClientPayloads();

            Debug.Log(
                $"[#16 実測] 画像 {_imageBytes.Length} バイト / "
                + $"{HostDistributor.GetPreparedImageChunkCount(0)} チャンク: "
                + $"受信完了まで {imageElapsedMs} ms、受付開始まで {stopwatch.ElapsedMilliseconds} ms");

            Assert.IsNotNull(received);
            Assert.AreEqual(ImageSide, received.width, "復号したテクスチャの幅が元画像と一致するはず。");
            Assert.AreEqual(ImageSide, received.height);

            Assert.AreEqual(0, HostDistributor.PendingImageAckCount, "全員分の画像 Ack が揃っているはず。");
            Assert.IsFalse(HostDistributor.IsAwaitingImageAck);
            Assert.IsFalse(HostDistributor.IsAwaitingAck);
            Assert.AreEqual(0, HostDistributor.ImageResendCount, "改竄していないので再送は起きないはず。");

            // ホストもクライアントとして自分自身に配信するため、画像を持つ。
            Assert.IsTrue(HostDistributor.TryGetImage(0, out var hostSide));
            Assert.AreEqual(ImageSide, hostSide.width);

            Assert.AreEqual(1, ClientDistributor.CachedImageCount, "画像付きは現在問の 1 件だけ。");

            AssertTransportMessages();
        }

        /// <summary>
        /// 2MB 近い画像（128 チャンク相当）でも欠落なく送受信できること
        /// （issue #16 受け入れ条件 1。時間がかかるため <c>Slow</c> カテゴリ）。
        /// </summary>
        [UnityTest]
        [Category("Slow")]
        public IEnumerator LargeImage_NearTwoMegabytes_IsDeliveredWithoutLoss()
        {
            var source = CreateImageQuestionSource(CreateLargeNoisePng(QuestionLimits.MaxImageSizeBytes));

            yield return ConnectHostAndClient(source);

            Assert.LessOrEqual(
                _imageBytes.Length, QuestionLimits.MaxImageSizeBytes, "2MB 以内の画像であるはず（前提）。");

            Assert.IsTrue(HostSession.ConfigureImages(new QuestionImageSource(_questionsFolder)));

            var stopwatch = Stopwatch.StartNew();
            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            var chunkCount = HostDistributor.GetPreparedImageChunkCount(0);
            Assert.GreaterOrEqual(chunkCount, 110, "2MB 近い画像は 110 チャンク以上に分割されるはず。");

            Texture2D received = null;
            yield return WaitUntil(
                () => ClientDistributor.TryGetImage(0, out received),
                () => "クライアントが 2MB 近い画像を受け取れませんでした。");
            var imageElapsedMs = stopwatch.ElapsedMilliseconds;

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {ClientSession.Phase.Value}）。");
            stopwatch.Stop();

            Debug.Log(
                $"[#16 実測] 大きい画像 {_imageBytes.Length} バイト / {chunkCount} チャンク: "
                + $"受信完了まで {imageElapsedMs} ms、受付開始まで {stopwatch.ElapsedMilliseconds} ms");

            Assert.IsNotNull(received);
            Assert.AreEqual(0, HostDistributor.ImageResendCount, "欠落・改竄が無ければ再送は起きないはず。");
            Assert.AreEqual(0, HostDistributor.PendingImageAckCount);
            Assert.IsTrue(HostDistributor.TryGetImage(0, out _), "ホスト自身も受け取れるはず。");
        }

        /// <summary>
        /// チャンクを 1 つ改竄すると、クライアントがハッシュ不一致を検出して NAK を返し、
        /// サーバーの再送で復元されること（docs/network.md §8.4）。
        /// </summary>
        [UnityTest]
        public IEnumerator Image_WithTamperedChunk_IsRecoveredByResend()
        {
            var source = CreateImageQuestionSource(CreateNoisePng(ImageSide, ImageSide));

            yield return ConnectHostAndClient(source);

            Assert.IsTrue(HostSession.ConfigureImages(new QuestionImageSource(_questionsFolder)));

            // 最初の 1 回だけチャンク 0 を改竄する（再送では素のチャンクを送る）。
            var tampered = false;
            HostDistributor.ImageChunkTransformForTests = (questionIndex, chunkIndex, chunk) =>
            {
                if (chunkIndex != 0 || tampered)
                {
                    return chunk;
                }

                tampered = true;
                return Corrupt(chunk);
            };

            var stopwatch = Stopwatch.StartNew();
            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            Texture2D received = null;
            yield return WaitUntil(
                () => ClientDistributor.TryGetImage(0, out received),
                () => "再送後もクライアントが画像を受け取れませんでした。");
            stopwatch.Stop();

            Debug.Log($"[#16 実測] 改竄 → NAK → 再送で受信完了まで {stopwatch.ElapsedMilliseconds} ms");

            Assert.IsTrue(tampered, "テストの前提としてチャンクを改竄しているはず。");
            Assert.GreaterOrEqual(HostDistributor.ImageResendCount, 1, "NAK を受けて再送しているはず。");
            Assert.IsNotNull(received);
            Assert.AreEqual(ImageSide, received.width);

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"再送後に受付が開きませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.AreEqual(0, HostDistributor.PendingImageAckCount, "再送で Ack が揃うはず。");
        }

        /// <summary>
        /// 再送しても直らない（2 度目の NAK）場合は、そのクライアントを画像なしで進め、
        /// 出題（受付開始）が止まらないこと（docs/network.md §8.4）。
        /// </summary>
        [UnityTest]
        public IEnumerator Image_FailingTwice_ProceedsWithoutImage()
        {
            var source = CreateImageQuestionSource(CreateNoisePng(ImageSide, ImageSide));

            yield return ConnectHostAndClient(source);

            Assert.IsTrue(HostSession.ConfigureImages(new QuestionImageSource(_questionsFolder)));

            // 何度送ってもチャンク 0 が壊れている状況（＝再送しても直らない）。
            var sentCorruptedChunks = 0;
            HostDistributor.ImageChunkTransformForTests = (questionIndex, chunkIndex, chunk) =>
            {
                if (chunkIndex != 0)
                {
                    return chunk;
                }

                sentCorruptedChunks++;
                return Corrupt(chunk);
            };

            var stopwatch = Stopwatch.StartNew();
            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"画像が壊れていても受付は開くはず（現在: {ClientSession.Phase.Value}）。");
            stopwatch.Stop();

            Debug.Log(
                $"[#16 実測] 2 度目の NAK → 画像なしで受付開始まで {stopwatch.ElapsedMilliseconds} ms"
                + $"（壊れたチャンクの送信 {sentCorruptedChunks} 回、再送 {HostDistributor.ImageResendCount} 回）");

            Assert.GreaterOrEqual(sentCorruptedChunks, 2, "再送でも壊れたチャンクを送っているはず。");
            Assert.GreaterOrEqual(HostDistributor.ImageResendCount, 1, "1 回は再送しているはず。");
            Assert.IsFalse(ClientDistributor.TryGetImage(0, out _), "画像は表示できないままのはず。");
            Assert.AreEqual(0, ClientDistributor.CachedImageCount);
            Assert.AreEqual(0, HostDistributor.PendingImageAckCount, "画像なしと決めた時点で待ちは解けるはず。");
            Assert.IsFalse(HostDistributor.IsAwaitingImageAck);

            // 問題文（DTO）は届いているので、進行そのものは成立している。
            Assert.IsTrue(ClientDistributor.TryGetQuestion(0, out var dto));
            Assert.AreEqual(QuestionText, dto.Text);
        }

        /// <summary>
        /// 先読みで配り終えた画像は、現在問になったときに送り直さないこと（docs/network.md §8.4、レビュー H1）。
        /// 途中参加したクライアントのために再配信する場合も、既に持っているクライアントは
        /// 組み立て直さず（＝保持中のテクスチャを捨てず）その場で Ack を返す。
        /// </summary>
        [UnityTest]
        public IEnumerator PrefetchedImage_IsNotRebuiltWhenItBecomesCurrent()
        {
            var source = CreateTwoImageQuestionSource();

            yield return ConnectHostAndClient(source);
            Assert.IsTrue(HostSession.ConfigureImages(new QuestionImageSource(_questionsFolder)));
            Assert.IsTrue(HostSession.StartQuestion());

            // 現在問（0）と先読み（1）の画像が両方届くまで待つ。
            yield return WaitUntil(
                () => ClientDistributor.TryGetImage(0, out _) && ClientDistributor.TryGetImage(1, out _),
                () => "現在問と先読みの画像が届きませんでした。");

            Assert.IsTrue(ClientDistributor.TryGetImage(1, out var prefetched));
            var prefetchedTexture = prefetched;

            var invalidated = new List<int>();
            ClientDistributor.ImageInvalidated += invalidated.Add;

            // 途中参加のクライアントを増やす（＝全員 Ack 済みではなくなるので再配信が必要になる）。
            yield return ConnectSecondClient();

            // #19（複数問進行）がまだ無いので、配信器を直接動かして「次の問題が現在問になる」状況を作る。
            Assert.IsTrue(HostDistributor.TryPrepareDistribution(1, out var error), error);
            HostDistributor.DistributePrepared();

            yield return WaitUntil(
                () => SecondClientSession.Distributor.TryGetImage(1, out _),
                () => "途中参加したクライアントが画像を受け取れませんでした。");
            yield return WaitUntil(
                () => HostDistributor.PendingImageAckCount == 0,
                () => $"画像 Ack が揃いませんでした（残り {HostDistributor.PendingImageAckCount} 件）。");

            ClientDistributor.ImageInvalidated -= invalidated.Add;

            Assert.AreEqual(0, HostDistributor.ImageResendCount, "NAK は発生していないはず。");
            CollectionAssert.DoesNotContain(
                invalidated, 1, "既に持っている画像（問題 1）は組み立て直さないはず。");
            CollectionAssert.Contains(
                invalidated, 0, "現在問が 1 になった時点で、古い画像（問題 0）は無効になるはず。");

            Assert.IsTrue(ClientDistributor.TryGetImage(1, out var stillHeld));
            Assert.AreSame(prefetchedTexture, stillHeld, "先読みで受け取ったテクスチャがそのまま使われるはず。");
            Assert.IsFalse(ClientDistributor.TryGetImage(0, out _), "古い画像は捨てられているはず。");
        }

        /// <summary>
        /// 画像の供給元を設定しない（画像なしの）構成では、これまでどおりテキストだけで進行すること。
        /// 画像が読めない場合に出題を止めない設計（docs/network.md §8.4）の確認も兼ねる。
        /// </summary>
        [UnityTest]
        public IEnumerator Distribution_WithoutImageSource_ProceedsWithoutImage()
        {
            var source = CreateImageQuestionSource(CreateNoisePng(ImageSide, ImageSide));

            yield return ConnectHostAndClient(source);

            Assert.IsTrue(HostSession.StartQuestion(), "画像を配らなくても出題は始まるはず。");

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.IsFalse(HostDistributor.IsAwaitingImageAck, "画像が無いので画像 Ack は待たない。");
            Assert.IsFalse(ClientDistributor.TryGetImage(0, out _));
            Assert.AreEqual(0, ClientDistributor.CachedImageCount);
        }

        /// <summary>
        /// 配信していない問題インデックスの Ack / NAK はサーバーが棄却し、進行に影響しないこと
        /// （docs/network.md §9）。
        /// </summary>
        [UnityTest]
        public IEnumerator ImageAckAndNak_WithUnknownQuestionIndex_AreRejected()
        {
            var source = CreateImageQuestionSource(CreateNoisePng(ImageSide, ImageSide));

            yield return ConnectHostAndClient(source);
            Assert.IsTrue(HostSession.ConfigureImages(new QuestionImageSource(_questionsFolder)));
            Assert.IsTrue(HostSession.StartQuestion());

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {ClientSession.Phase.Value}）。");

            var phaseHistoryBefore = new List<QuizPhase>(HostSession.ServerPhaseHistory);
            var resendCountBefore = HostDistributor.ImageResendCount;

            // 未知の問題インデックス・不正な列挙値は棄却される（再送も起きない）。
            ClientDistributor.ImageReceivedRpc(999);
            ClientDistributor.ImageFailedRpc(999, ImageFailureReason.HashMismatch);
            ClientDistributor.ImageFailedRpc(0, (ImageFailureReason)12345);

            yield return WaitFrames(10);

            Assert.AreEqual(
                resendCountBefore, HostDistributor.ImageResendCount, "棄却された通知で再送してはいけない。");
            CollectionAssert.AreEqual(
                phaseHistoryBefore,
                HostSession.ServerPhaseHistory,
                "棄却された通知は余分なフェーズ遷移を起こさないはず。");
        }

        /// <summary>
        /// 送信サイズ・送信回数の検査（docs/network.md §8.2 / §8.3）。
        /// </summary>
        /// <remarks>
        /// NGO は同じフレーム内の同一 delivery のメッセージを 1 つの Transport 送信へまとめる
        /// （バッチ化）ため、Transport が受け取る 1 件は「1 回分のチャンク（最大
        /// <see cref="QuestionDistributor.ImageChunksPerTick"/> 件）」になりうる。実測では
        /// 16KB × 4 + ヘッダ = 65,632 バイトが観測された。`MaxPayloadSize` の検査は
        /// 信頼性なしパイプラインにしか適用されない（§8.2）ので、これはエラーにならない。
        /// <para>
        /// そこで確かめるのは次の 2 点。
        /// </para>
        /// <list type="number">
        /// <item>チャンク 1 つ（＋ヘッダ）は <c>MaxPayloadSize</c> に収まる
        /// = 16KB を 32768 へ引き上げた理由（§8.3）が満たされている。</item>
        /// <item>画像が複数フレームに分散して送られ、1 回の送信が「数チャンク分」に収まっている
        /// = 画像を丸ごと 1 度に積んでいない（§8.3 の分割送信の目的）。</item>
        /// </list>
        /// </remarks>
        private void AssertTransportMessages()
        {
            Assert.Greater(_clientMessages.Count, 0, "受信バイト列を記録できているはず（検査方法の妥当性確認）。");

            // 1 メッセージあたりのヘッダ・バッチのオーバーヘッドの見積り（実測 65,632 - 16,384 × 4 = 96 バイト）。
            const int PerMessageOverheadBudget = 512;
            var singleChunkBudget = NetworkConstants.ImageChunkBytes + PerMessageOverheadBudget;
            var batchBudget = QuestionDistributor.ImageChunksPerTick * singleChunkBudget;

            Assert.LessOrEqual(
                singleChunkBudget,
                NetworkConstants.MaxPayloadSizeBytes,
                "チャンク 1 つはヘッダを足しても MaxPayloadSize に収まるはず（docs/network.md §8.3）。");

            var largest = 0;
            var frames = new HashSet<int>();
            for (var i = 0; i < _clientMessages.Count; i++)
            {
                largest = Math.Max(largest, _clientMessages[i].Size);
                frames.Add(_clientMessages[i].Frame);
            }

            Debug.Log(
                $"[#16 実測] Transport が受け取った最大メッセージ: {largest} バイト"
                + $"（{_clientMessages.Count} 件 / {frames.Count} フレーム）");

            Assert.GreaterOrEqual(
                frames.Count, 2, "画像は複数フレームに分散して送られるはず（1 度に積んでいない）。");

            // tick がフレーム落ちでまとまることがあるため、2 回分までは許容する。
            Assert.LessOrEqual(
                largest,
                batchBudget * 2,
                $"1 回の送信が 2 回分のチャンクを超えてはいけない（実際: {largest} バイト）。");
        }

        /// <summary>1 バイトだけ反転させた複製を返す（改竄の模擬）。</summary>
        private static byte[] Corrupt(byte[] chunk)
        {
            var corrupted = (byte[])chunk.Clone();
            corrupted[0] = (byte)(corrupted[0] ^ 0xFF);
            return corrupted;
        }

        /// <summary>
        /// 途中参加・再接続の再送（#109）を同じクライアントへ連続で要求しても、
        /// 画像の送信予約が二重に積まれないこと（PR #114 レビュー M-4）。
        /// </summary>
        /// <remarks>
        /// NGO はクライアント ID を使い回さないため、再接続を繰り返されると再送要求は
        /// 何度でも起こせる。重複排除が無いと 1 回の合流ごとに最大 129 件
        /// （メタ情報 + 128 チャンク）を積め、現在問・先読みの送信を押し流せてしまう。
        /// </remarks>
        [UnityTest]
        public IEnumerator ImageResend_ToTheSameClient_IsNotQueuedTwice()
        {
            var source = CreateImageQuestionSource(CreateNoisePng(ImageSide, ImageSide));

            yield return ConnectHostAndClient(source);

            Assert.IsTrue(
                HostSession.ConfigureImages(new QuestionImageSource(_questionsFolder)),
                "画像の供給元を設定できるはず。");
            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            // 最初の配信を送り切ってからでないと、キューの増減が再送のものか分からない。
            yield return WaitUntil(
                () => ClientDistributor.TryGetImage(0, out _) && HostDistributor.QueuedImageSendCount == 0,
                () => "画像の配信が終わりませんでした"
                      + $"（送信待ち: {HostDistributor.QueuedImageSendCount}）。");

            var clientId = ClientManager.LocalClientId;

            // 1 回目の再送: メタ情報 + 全チャンクが積まれる。
            Assert.IsTrue(HostSession.ResyncClient(clientId), "接続中のクライアントへ再同期できるはず。");
            var queuedAfterFirst = HostDistributor.QueuedImageSendCount;
            Assert.AreEqual(
                HostDistributor.GetPreparedImageChunkCount(0) + 1,
                queuedAfterFirst,
                "再送ではメタ情報 1 件 + 全チャンクが積まれるはず。");

            // 2 回目（同じフレーム内＝まだ 1 件も吐き出していない）は積み直さない。
            Assert.IsTrue(HostSession.ResyncClient(clientId), "2 回目の再同期自体は成功する（DTO は送る）。");
            Assert.AreEqual(
                queuedAfterFirst,
                HostDistributor.QueuedImageSendCount,
                "同じ宛先・同じ問題の画像送信は二重に積まれないはず（PR #114 レビュー M-4）。");

            Assert.LessOrEqual(
                HostDistributor.QueuedImageSendCount,
                QuestionDistributor.MaxQueuedImageSendCount,
                "送信キューは上限を超えないはず。");
        }

        /// <summary>画像付きの問題 1 問を供給する（画像ファイルもテスト用フォルダへ書き出す）。</summary>
        private TestQuestionSource CreateImageQuestionSource(byte[] imageBytes)
        {
            _imageBytes = imageBytes;
            WriteImage("q1.png", imageBytes);

            return new TestQuestionSource(CreateImageQuestion("q-image-1", QuestionText, "images/q1.png"));
        }

        /// <summary>画像付きの問題を 2 問供給する（内容の違う画像を 2 枚書き出す）。</summary>
        private TestQuestionSource CreateTwoImageQuestionSource()
        {
            _imageBytes = CreateNoisePng(ImageSide, ImageSide, seed: 1);
            WriteImage("q1.png", _imageBytes);
            WriteImage("q2.png", CreateNoisePng(ImageSide, ImageSide, seed: 2));

            return new TestQuestionSource(
                CreateImageQuestion("q-image-1", QuestionText, "images/q1.png"),
                CreateImageQuestion("q-image-2", "2 問目の問題文", "images/q2.png"));
        }

        private static Question CreateImageQuestion(string id, string text, string imagePath)
        {
            return new Question(
                id,
                QuestionType.FreeText,
                text,
                answers: new[] { CorrectAnswer },
                imagePath: imagePath);
        }

        private void WriteImage(string fileName, byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(_questionsFolder, "images", fileName), bytes);
        }

        /// <summary>クライアントの Transport が受け取った 1 メッセージのバイト数とフレーム番号を記録する。</summary>
        private void CaptureClientPayloads()
        {
            _clientMessages.Clear();
            ClientTransport.OnTransportEvent += HandleClientTransportEvent;
            _capturingClientPayloads = true;
        }

        private void StopCapturingClientPayloads()
        {
            if (!_capturingClientPayloads)
            {
                return;
            }

            _capturingClientPayloads = false;
            if (ClientTransport != null)
            {
                ClientTransport.OnTransportEvent -= HandleClientTransportEvent;
            }
        }

        private void HandleClientTransportEvent(
            NetworkEvent eventType, ulong clientId, ArraySegment<byte> payload, float receiveTime)
        {
            if (eventType != NetworkEvent.Data || payload.Array == null || payload.Count == 0)
            {
                return;
            }

            _clientMessages.Add(new TransportMessage(payload.Count, Time.frameCount));
        }

        /// <summary>
        /// 2MB を超えない範囲でできるだけ大きいノイズ PNG を作る
        /// （PNG のサイズは圧縮結果に依存するため、上限を超えたら 1 辺を縮めて作り直す）。
        /// </summary>
        private static byte[] CreateLargeNoisePng(int maxBytes)
        {
            for (var side = LargeImageStartSide; side > 64; side -= 32)
            {
                var bytes = CreateNoisePng(side, side);
                if (bytes.Length <= maxBytes)
                {
                    return bytes;
                }
            }

            throw new InvalidOperationException("2MB 以内のテスト画像を生成できませんでした。");
        }

        /// <summary>
        /// ノイズを詰めた PNG を作る（圧縮が効きすぎて意図したバイト数にならないのを避けるため）。
        /// </summary>
        private static byte[] CreateNoisePng(int width, int height, int seed = 20260913)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
            try
            {
                var random = new System.Random(seed);
                var pixels = new Color32[width * height];
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32(
                        (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), byte.MaxValue);
                }

                texture.SetPixels32(pixels);
                texture.Apply(updateMipmaps: false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Transport が受け取った 1 メッセージの記録。</summary>
        private readonly struct TransportMessage
        {
            public TransportMessage(int size, int frame)
            {
                Size = size;
                Frame = frame;
            }

            public int Size { get; }

            public int Frame { get; }
        }
    }
}
