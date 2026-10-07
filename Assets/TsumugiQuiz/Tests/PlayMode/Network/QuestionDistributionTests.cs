using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// 問題配信（DTO 配信 → 受信確認 → 出題）を実際のネットワーク経路で通す統合テスト
    /// （docs/network.md §8、docs/question-data.md §7、#13）。
    /// </summary>
    public class QuestionDistributionTests : GameSessionTestFixture
    {
        private const string ReadingText = "にほんのしゅとはどこ";
        private const string SecondQuestionText = "世界一高い山は？";
        private const string SecondAnswer = "えべれすと";

        /// <summary>TTS（#23）が要求する読み上げ開始時刻の、出題からの相対秒。</summary>
        private const double TtsRequestDelaySec = 0.75;

        private readonly List<byte[]> _clientReceivedPayloads = new List<byte[]>();

        private bool _capturingClientPayloads;

        [UnityTest]
        public IEnumerator Distribution_DeliversDtoAndPrefetch_ThenShowsQuestion()
        {
            CaptureClientPayloads();

            yield return ConnectHostAndClient(CreateTwoQuestionSource());

            var shown = new List<QuestionDto>();
            var shownIndices = new List<int>();
            ClientSession.QuestionShown += (index, question, _) =>
            {
                shownIndices.Add(index);
                shown.Add(question);
            };

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            // 配信（DTO）→ 受信確認 → 出題（提示）→ 受付開始、の順に進む。
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {ClientSession.Phase.Value}）。");

            // 受付開始まででバイト列の記録を打ち切る（この後の通信量に結果が左右されないようにする）。
            StopCapturingClientPayloads();

            Assert.AreEqual(1, shown.Count, "提示は 1 度だけ届くはず。");
            CollectionAssert.AreEqual(new[] { 0 }, shownIndices);

            var dto = shown[0];
            Assert.AreEqual("q-1", dto.Id);
            Assert.AreEqual(QuestionText, dto.Text, "クライアントは DTO の問題文を参照できるはず。");
            Assert.AreEqual(ReadingText, dto.ReadingText);
            Assert.AreEqual(2, dto.Difficulty);
            CollectionAssert.AreEqual(new[] { "地理" }, dto.Tags);
            CollectionAssert.IsEmpty(dto.Choices, "freeText では選択肢を送らない。");

            // 受信確認が揃ったので、サーバーは待ちを終えている。
            Assert.IsFalse(HostDistributor.IsAwaitingAck, "全員分の受信確認が揃っているはず。");
            Assert.AreEqual(0, HostDistributor.PendingAckCount);

            // 先読み（次の 1 問）も届く。
            yield return WaitUntil(
                () => ClientDistributor.TryGetQuestion(1, out _),
                () => "先読みの問題データが届きませんでした。");
            Assert.IsTrue(ClientDistributor.TryGetQuestion(1, out var prefetched));
            Assert.AreEqual(SecondQuestionText, prefetched.Text);
            Assert.AreEqual(
                2,
                ClientDistributor.CachedQuestionCount,
                "先読みは 1 問だけ（現在問 + 次問）であるはず。");

            // ホスト自身もクライアントとして DTO を持つ。
            Assert.IsTrue(HostDistributor.TryGetQuestion(0, out var hostSide));
            Assert.AreEqual(QuestionText, hostSide.Text);

            // 受信したバイト列に正解が現れないこと（docs/question-data.md §7）。
            Assert.IsTrue(
                PayloadsContain(QuestionText),
                "問題文は受信バイト列に含まれるはず（検査方法の妥当性確認）。");
            Assert.IsFalse(PayloadsContain(CorrectAnswer), "現在問の正解が送られてはいけない。");
            Assert.IsFalse(PayloadsContain(SecondAnswer), "先読みした問題の正解が送られてはいけない。");
        }

        /// <summary>
        /// 受信確認（<see cref="QuestionDistributor.QuestionReceivedRpc"/>）のうち、
        /// 範囲外・未配信・待ち終了後に遅れて届いたものをサーバーが弾き、進行が動かないこと
        /// （docs/network.md §9）。
        /// </summary>
        [UnityTest]
        public IEnumerator Ack_WithInvalidIndexOrAfterCompletion_IsRejectedByServer()
        {
            yield return ConnectHostAndClient(CreateTwoQuestionSource());

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {ClientSession.Phase.Value}）。");

            var phaseHistoryBefore = new List<QuizPhase>(HostSession.ServerPhaseHistory);

            // 範囲外・未配信・待ち終了後に遅れて届いた Ack を、生の RPC で送る。
            ClientDistributor.QuestionReceivedRpc(-1);
            ClientDistributor.QuestionReceivedRpc(999);
            ClientDistributor.QuestionReceivedRpc(0);

            yield return WaitFrames(10);

            Assert.IsFalse(HostDistributor.IsAwaitingAck, "棄却された Ack で待ちが復活してはいけない。");
            Assert.AreEqual(0, HostDistributor.PendingAckCount);
            CollectionAssert.AreEqual(
                phaseHistoryBefore,
                HostSession.ServerPhaseHistory,
                "棄却された Ack は余分なフェーズ遷移を起こさないはず。");
        }

        /// <summary>
        /// 受付開始 T0 は「配信の受信確認が終わった時刻」と「TTS（#23）が要求した時刻」の
        /// 大きい方になること（docs/network.md §8.6 の契約、レビュー H-3）。
        /// </summary>
        [UnityTest]
        public IEnumerator BuzzOpenTime_IsTheLaterOfAckCompletionAndTtsRequest()
        {
            yield return ConnectHostAndClient(CreateTwoQuestionSource());

            Assert.IsTrue(HostSession.StartQuestion(), "出題を開始できるはず。");

            // StartQuestion と同じフレームなので、まだクライアントの受信確認は届いていない。
            Assert.IsTrue(HostDistributor.IsAwaitingAck, "この時点では受信確認を待っているはず。");

            var requestedBuzzOpenTime = HostManager.ServerTime.Time + TtsRequestDelaySec;
            Assert.IsTrue(
                HostSession.NotifyReadingStarted(requestedBuzzOpenTime),
                "Reading 中なので読み上げ開始時刻は受理されるはず。");

            yield return WaitUntil(
                () => ClientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {ClientSession.Phase.Value}）。");

            Assert.GreaterOrEqual(
                HostSession.BuzzOpenServerTime.Value,
                requestedBuzzOpenTime - 1e-6,
                "TTS が要求した時刻より前に受付が開いてはいけない。");
            Assert.Less(
                HostSession.BuzzOpenServerTime.Value,
                requestedBuzzOpenTime + QuestionDistributor.AckTimeoutSec,
                "受信確認が揃っているので、Ack のタイムアウトまで待ってはいけない。");
        }

        [TearDown]
        public void ClearCapturedPayloads()
        {
            StopCapturingClientPayloads();
            _clientReceivedPayloads.Clear();
        }

        /// <summary>バイト列の記録を止める（多重に呼んでよい）。</summary>
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

        private static TestQuestionSource CreateTwoQuestionSource()
        {
            return new TestQuestionSource(
                TestQuestionSource.FreeText(
                    "q-1", QuestionText, ReadingText, new[] { "地理" }, 2, CorrectAnswer, "トウキョウ"),
                TestQuestionSource.FreeText(
                    "q-2", SecondQuestionText, null, new[] { "地理" }, 4, SecondAnswer));
        }

        /// <summary>クライアントの Transport が受け取った生のバイト列を全て記録する。</summary>
        private void CaptureClientPayloads()
        {
            _clientReceivedPayloads.Clear();
            ClientTransport.OnTransportEvent += HandleClientTransportEvent;
            _capturingClientPayloads = true;
        }

        private void HandleClientTransportEvent(
            NetworkEvent eventType, ulong clientId, ArraySegment<byte> payload, float receiveTime)
        {
            if (eventType != NetworkEvent.Data || payload.Array == null || payload.Count == 0)
            {
                return;
            }

            // ArraySegment の実体は Transport が再利用するため、必ずコピーしてから溜める。
            var copy = new byte[payload.Count];
            Array.Copy(payload.Array, payload.Offset, copy, 0, payload.Count);
            _clientReceivedPayloads.Add(copy);
        }

        private bool PayloadsContain(string text)
        {
            var utf16 = Encoding.Unicode.GetBytes(text);
            var utf8 = Encoding.UTF8.GetBytes(text);
            for (var i = 0; i < _clientReceivedPayloads.Count; i++)
            {
                var payload = _clientReceivedPayloads[i];
                if (Contains(payload, utf16) || Contains(payload, utf8))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length)
            {
                return false;
            }

            for (var offset = 0; offset <= haystack.Length - needle.Length; offset++)
            {
                var matched = true;
                for (var i = 0; i < needle.Length; i++)
                {
                    if (haystack[offset + i] != needle[i])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
