using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.Network
{
    /// <summary>
    /// クライアントが RPC を連打したとき、レート制限（docs/network.md §9、#52）が
    /// 実際のネットワーク経路で機能することを確かめるテスト。
    /// バースト（60）を超えた分は無視され、警告ログは 1 秒 1 回に間引かれ、
    /// 5 秒間の超過閾値（<see cref="RpcRateGuard.DisconnectThreshold"/>）を超えたクライアントは切断される。
    /// </summary>
    public class RpcRateLimitTests : GameSessionTestFixture
    {
        /// <summary>
        /// バースト（60）と切断閾値（120 超過）の両方を確実に超え、かつ「切断を決めたあとも
        /// バッチに残っていた RPC が二重に処理されない」ことを検証できるだけの連打数。
        /// 閾値超過そのものは 200 件程度でも起こるが、500 件にしておくことで、万一
        /// 切断決定と同時に状態を消してしまう（Forget を早く呼びすぎる）不具合が再発したとき、
        /// 残りの連打だけでもう一度フルの切断サイクルが回ってしまうだけの余地を残す。
        /// それが起きない＝切断の警告ログがちょうど 1 回しか出ないことで回帰を検出できる（#52 レビュー H2）。
        /// </summary>
        private const int SpamCount = 500;

        [UnityTest]
        public IEnumerator BuzzRpc_RapidSpam_IsRateLimitedLogThrottledAndDisconnected()
        {
            yield return ConnectHostAndClient();
            var hostSession = HostSession;
            var clientSession = ClientSession;
            var clientId = ClientManager.LocalClientId;

            var buzzWinners = new List<ulong>();
            clientSession.BuzzLocked += (winner, lockedAt, wasTie) => buzzWinners.Add(winner);

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.BuzzOpen,
                () => $"クライアントが BuzzOpen を受け取れませんでした（現在: {clientSession.Phase.Value}）。");

            var counter = new WarningLogCounter();
            try
            {
                // 生の RPC を直接叩いて連打を再現する（GameSessionRejectionTests と同じ作法）。
                // ローカルの二重送信ガード（RequestBuzz の _hasBuzzedInCurrentPhase）を経由しないため、
                // サーバー側のレート制限だけを純粋に検証できる。
                var buzzTime = ClientManager.LocalTime.Time;
                for (var i = 0; i < SpamCount; i++)
                {
                    clientSession.BuzzRpc(buzzTime);
                }

                yield return WaitUntil(
                    () => !ClientManager.IsConnectedClient,
                    () => "レート制限違反のクライアントが切断されませんでした。");
            }
            finally
            {
                counter.Dispose();
            }

            Assert.AreEqual(
                DisconnectReasonMessages.RateLimitExceeded,
                ClientManager.DisconnectReason,
                "レート制限超過を理由に切断されるはず。");

            // 裁定（BuzzResultRpc）はサーバーの tick を挟んで初めて配信されるため、
            // 切断が tick より先に起きた場合は 0 件のままでも正しい（早押しの当落自体は本 issue の対象外）。
            // ここで確かめたいのは「2 件目以降で余分な当落が増えない」こと。
            Assert.LessOrEqual(buzzWinners.Count, 1, "連打しても当落は高々 1 件しか確定しないはず。");
            if (buzzWinners.Count == 1)
            {
                Assert.AreEqual(clientId, buzzWinners[0], "当落が確定するならこのクライアントのはず。");
            }

            Assert.AreEqual(
                1,
                counter.DisconnectCount,
                "切断の警告ログはちょうど 1 回のはず（H2: 切断済みクライアントのバッチ残りの RPC は無視され、"
                + "二重に切断サイクルが回らない）。");
            Assert.LessOrEqual(
                counter.IgnoredCount,
                5,
                $"{SpamCount} 件連打してもレート制限の警告ログは 1 秒 1 回に間引かれ、少数で収まるはず。");
        }

        /// <summary>
        /// <see cref="TsumugiQuiz.Network.QuestionDistributor"/> 側の RPC（<c>QuestionReceivedRpc</c>）でも
        /// 同じレート制限・切断が働くことを確認する（L10: <c>BuzzRpc</c> 経路しか見ていなかった指摘への対応）。
        /// このコンポーネントは <c>GameSession</c> とは別インスタンスの <c>RpcRateGuard</c> を持つため、
        /// 別経路でも同様に機能することを個別に確かめる価値がある。
        /// </summary>
        [UnityTest]
        public IEnumerator QuestionReceivedRpc_RapidSpam_IsRateLimitedAndDisconnected()
        {
            yield return ConnectHostAndClient();

            var counter = new WarningLogCounter();
            try
            {
                // 配信していない問題インデックスへの受信確認は、この後の入力検証で棄却されるが、
                // それより前に RpcRateGuard.Allow が呼ばれるので、レート制限の検証には影響しない。
                for (var i = 0; i < SpamCount; i++)
                {
                    ClientDistributor.QuestionReceivedRpc(0);
                }

                yield return WaitUntil(
                    () => !ClientManager.IsConnectedClient,
                    () => "QuestionReceivedRpc の連打でクライアントが切断されませんでした。");
            }
            finally
            {
                counter.Dispose();
            }

            Assert.AreEqual(
                DisconnectReasonMessages.RateLimitExceeded,
                ClientManager.DisconnectReason,
                "レート制限超過を理由に切断されるはず。");
            Assert.AreEqual(
                1,
                counter.DisconnectCount,
                "QuestionDistributor 経由でも切断の警告ログはちょうど 1 回のはず。");
        }

        /// <summary>
        /// 選択式（<c>choice</c>、#17）の <see cref="GameSession.SubmitChoiceRpc"/> でも
        /// 同じレート制限・切断が働くことを確認する。選択式は <see cref="GameSession"/> 側の
        /// <c>RpcRateGuard</c> インスタンスを <c>BuzzRpc</c> / <c>SubmitAnswerRpc</c> と共有するため、
        /// 経路が違っても同じガードで弾かれることを確かめる価値がある。
        /// </summary>
        [UnityTest]
        public IEnumerator SubmitChoiceRpc_RapidSpam_IsRateLimitedAndDisconnected()
        {
            var questionSource = new TestQuestionSource(
                TestQuestionSource.Choice("q-choice-1", "選択式のテスト問題", 0, "A", "B", "C"));
            yield return ConnectHostAndClient(questionSource);

            var hostSession = HostSession;
            var clientSession = ClientSession;

            Assert.IsTrue(hostSession.StartQuestion(), "出題を開始できるはず。");
            yield return WaitUntil(
                () => clientSession.Phase.Value == QuizPhase.ChoiceAnswering,
                () => $"クライアントが ChoiceAnswering を受け取れませんでした（現在: {clientSession.Phase.Value}）。");

            var counter = new WarningLogCounter();
            try
            {
                for (var i = 0; i < SpamCount; i++)
                {
                    clientSession.SubmitChoiceRpc(0);
                }

                yield return WaitUntil(
                    () => !ClientManager.IsConnectedClient,
                    () => "SubmitChoiceRpc の連打でクライアントが切断されませんでした。");
            }
            finally
            {
                counter.Dispose();
            }

            Assert.AreEqual(
                DisconnectReasonMessages.RateLimitExceeded,
                ClientManager.DisconnectReason,
                "レート制限超過を理由に切断されるはず。");
            Assert.AreEqual(
                1,
                counter.DisconnectCount,
                "SubmitChoiceRpc 経由でも切断の警告ログはちょうど 1 回のはず。");
        }

        /// <summary>
        /// <see cref="RpcRateGuard"/> の警告ログを種類別に数える、テスト専用の小さなヘルパー。
        /// </summary>
        private sealed class WarningLogCounter : IDisposable
        {
            private bool _disposed;

            public WarningLogCounter()
            {
                Application.logMessageReceived += OnLogMessage;
            }

            /// <summary>「レート制限を超えたため無視しました」警告の件数。</summary>
            public int IgnoredCount { get; private set; }

            /// <summary>「切断します」警告の件数。</summary>
            public int DisconnectCount { get; private set; }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                Application.logMessageReceived -= OnLogMessage;
                _disposed = true;
            }

            private void OnLogMessage(string condition, string stackTrace, LogType type)
            {
                if (type != LogType.Warning)
                {
                    return;
                }

                if (condition.Contains("レート制限") && condition.Contains("無視しました"))
                {
                    IgnoredCount++;
                }
                else if (condition.Contains("切断します"))
                {
                    DisconnectCount++;
                }
            }
        }
    }
}
