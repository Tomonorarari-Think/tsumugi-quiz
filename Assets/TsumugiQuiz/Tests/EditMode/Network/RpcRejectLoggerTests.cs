using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Network;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// <see cref="RpcRejectLogger"/>（棄却ログの間引き・サマリ・Forget、#72）のテスト。
    /// 間引きの判定そのものは <c>TsumugiQuiz.Core.RejectLogThrottleTests</c> でカバー済みなので、
    /// ここでは「間引かれなかった呼び出しだけ実際に <see cref="Debug.LogWarning(object)"/> されるか」
    /// 「サマリの付き方」「Forget 後の挙動」を確かめる。
    /// </summary>
    public sealed class RpcRejectLoggerTests
    {
        private const ulong ClientA = 1UL;
        private const ulong ClientB = 2UL;

        [Test]
        public void LogRejected_FirstCall_LogsWithoutSummary()
        {
            var logger = new RpcRejectLogger(intervalSeconds: 1.0);

            logger.LogRejected(ClientA, 0.0, "棄却しました");

            LogAssert.Expect(LogType.Warning, "棄却しました");
        }

        [Test]
        public void LogRejected_WithinInterval_IsSuppressedAndDoesNotLog()
        {
            var logger = new RpcRejectLogger(intervalSeconds: 1.0);
            logger.LogRejected(ClientA, 0.0, "棄却 1 件目");
            LogAssert.Expect(LogType.Warning, "棄却 1 件目");

            logger.LogRejected(ClientA, 0.5, "棄却 2 件目");

            // 間引かれた呼び出しは何もログを出さないはず（想定外のログが無いことを確認）。
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogRejected_AfterInterval_LogsWithSuppressedSummary()
        {
            var logger = new RpcRejectLogger(intervalSeconds: 1.0);
            logger.LogRejected(ClientA, 0.0, "棄却 1 件目");
            LogAssert.Expect(LogType.Warning, "棄却 1 件目");

            logger.LogRejected(ClientA, 0.2, "間引かれる 1"); // 間引かれる。
            logger.LogRejected(ClientA, 0.4, "間引かれる 2"); // 間引かれる。

            logger.LogRejected(ClientA, 1.0, "棄却 2 件目");

            LogAssert.Expect(LogType.Warning, new Regex(@"^棄却 2 件目（ほか 2 件を間引きました）$"));
        }

        [Test]
        public void LogRejected_DifferentClients_AreIndependent()
        {
            var logger = new RpcRejectLogger(intervalSeconds: 1.0);
            logger.LogRejected(ClientA, 0.0, "A の棄却");
            LogAssert.Expect(LogType.Warning, "A の棄却");

            logger.LogRejected(ClientB, 0.0, "B の棄却");

            LogAssert.Expect(LogType.Warning, "B の棄却");
        }

        [Test]
        public void Forget_AllowsImmediateLogAgainWithoutSummary()
        {
            var logger = new RpcRejectLogger(intervalSeconds: 1.0);
            logger.LogRejected(ClientA, 0.0, "1 回目");
            LogAssert.Expect(LogType.Warning, "1 回目");

            logger.Forget(ClientA);

            logger.LogRejected(ClientA, 0.1, "Forget 後");

            LogAssert.Expect(LogType.Warning, "Forget 後");
        }

        /// <summary>
        /// 引数なしのコンストラクタは <see cref="RpcRejectLogger.DefaultIntervalSeconds"/>（1 秒）を使うはず
        /// （docs/network.md §9.1、レビュー M2）。
        /// </summary>
        [Test]
        public void DefaultConstructor_UsesOneSecondInterval()
        {
            var logger = new RpcRejectLogger();
            logger.LogRejected(ClientA, 0.0, "既定間隔 1 件目");
            LogAssert.Expect(LogType.Warning, "既定間隔 1 件目");

            logger.LogRejected(ClientA, 0.9, "0.9 秒後は間引かれるはず");
            LogAssert.NoUnexpectedReceived();

            logger.LogRejected(ClientA, 1.0, "1.0 秒後は出力されるはず");
            LogAssert.Expect(LogType.Warning, new Regex(@"^1\.0 秒後は出力されるはず（ほか 1 件を間引きました）$"));
        }

        /// <summary>
        /// <c>message</c> が null・空文字のときは間引きの状態を変えずに何もしないはず（レビュー L7）。
        /// </summary>
        [Test]
        public void LogRejected_WithNullOrEmptyMessage_DoesNothing()
        {
            var logger = new RpcRejectLogger(intervalSeconds: 1.0);

            logger.LogRejected(ClientA, 0.0, null);
            logger.LogRejected(ClientA, 0.0, string.Empty);
            LogAssert.NoUnexpectedReceived();

            // 状態を変えていないはずなので、続く通常の呼び出しは「初回」としてサマリなしで出るはず。
            logger.LogRejected(ClientA, 0.0, "通常の棄却");
            LogAssert.Expect(LogType.Warning, "通常の棄却");
        }

        /// <summary>
        /// 公開コンストラクタは <c>NetworkManager</c> を必須にしており、null は許可しない
        /// （<c>RpcRateGuard</c> と同じ方針。#83 レビュー M1）。
        /// <c>NetworkManager</c> 付きで 1 回目は出て 2 回目は間引かれる挙動そのものは、
        /// 実際の <c>NetworkManager</c>（<c>ServerTime</c>）を必要とするため PlayMode の
        /// <c>GameSessionRejectionTests.RepeatedRejectedSubmitAnswerRpc_FromSameClient_LogsOnlyOnceWithinInterval</c>
        /// で間接的に確認済み（レビュー M3）。
        /// </summary>
        [Test]
        public void Constructor_WithNullNetworkManager_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RpcRejectLogger(networkManager: null));
        }
    }
}
