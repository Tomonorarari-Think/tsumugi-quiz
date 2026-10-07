using System;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Tests.EditMode.Core
{
    /// <summary>
    /// <see cref="DisconnectReasonLocalizer"/>（切断理由を日本語の文言に対応づける、issue #208）を検証する。
    /// NGO の文字列は NGO 2.13.2 のソースと、#208 の PlayMode での実測に合わせている（docs/network.md §2.4）。
    /// </summary>
    public class DisconnectReasonLocalizerTests
    {
        /// <summary>#208 の PlayMode で実測した、クライアントが自分で停止したときの NGO の診断文字列（144 文字）。</summary>
        private const string MeasuredClientShutdownReason =
            "[Disconnect Event][Client-1][TransportClientId-4294967296][TransportShutdown] "
            + "NetworkConnectionManager was shutdown. The transport was shutdown.";

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\r\n\t")]
        public void Localize_NoReason_ReturnsDisconnectedWithoutReason(string reason)
        {
            var result = DisconnectReasonLocalizer.Localize(reason);

            Assert.That(result.Message, Is.EqualTo(JoinStatusMessages.DisconnectedWithoutReason));
            Assert.That(result.Category, Is.EqualTo(DisconnectReasonCategory.None));
            Assert.IsFalse(result.IsOriginalShown);
        }

        [Test]
        public void Localize_AllRejectionReasons_AreShownAsIs()
        {
            var versionPairs = new[]
            {
                new[] { (ushort)0, (ushort)0 },
                new[] { (ushort)3, (ushort)2 },
                new[] { ushort.MaxValue, ushort.MaxValue },
            };

            foreach (ConnectionRejectionReason reason in Enum.GetValues(typeof(ConnectionRejectionReason)))
            {
                foreach (var versions in versionPairs)
                {
                    var message = ConnectionRejectionMessages.Create(reason, versions[0], versions[1]);
                    if (string.IsNullOrEmpty(message))
                    {
                        continue;
                    }

                    var result = DisconnectReasonLocalizer.Localize(message);

                    Assert.That(result.Message, Is.EqualTo(message), reason.ToString());
                    Assert.That(result.Category, Is.EqualTo(DisconnectReasonCategory.AppMessage), reason.ToString());
                    Assert.IsTrue(result.IsOriginalShown, reason.ToString());
                }
            }
        }

        [TestCase(DisconnectReasonMessages.SeatTakenOver)]
        [TestCase(DisconnectReasonMessages.RateLimitExceeded)]
        public void Localize_ReasonsSentAfterApproval_AreShownAsIs(string reason)
        {
            var result = DisconnectReasonLocalizer.Localize(reason);

            Assert.That(result.Message, Is.EqualTo(reason));
            Assert.That(result.Category, Is.EqualTo(DisconnectReasonCategory.AppMessage));
        }

        [Test]
        public void Localize_AppMessageWithExtraText_IsNotShownAsIs()
        {
            // 完全一致に限る。自前の文言に足した文字列を画面へ出させない。
            var variants = new[]
            {
                ConnectionRejectionMessages.RoomFull + " ",
                " " + ConnectionRejectionMessages.RoomFull,
                ConnectionRejectionMessages.RoomFull + "\n",
                ConnectionRejectionMessages.RoomFull + "<size=60>今すぐ○○へ</size>",
                DisconnectReasonMessages.SeatTakenOver + DisconnectReasonMessages.SeatTakenOver,
                "バージョンが異なります（ホスト: 3 / あなた: 2）。\n",
                "バージョンが異なります（ホスト: 3 / あなた: 2）。追加の文",
                "バージョンが異なります（ホスト: １ / あなた: 2）。", // 全角の数字
                "バージョンが異なります（ホスト: 123456 / あなた: 2）。", // 6 桁
                "バージョンが異なります（ホスト:  / あなた: 2）。", // 数字なし
                "バージョンが異なります（ホスト: <b>3</b> / あなた: 2）。",
            };

            foreach (var reason in variants)
            {
                var result = DisconnectReasonLocalizer.Localize(reason);

                Assert.That(result.Category, Is.EqualTo(DisconnectReasonCategory.Unknown), reason);
                Assert.That(result.Message, Is.EqualTo(JoinStatusMessages.DisconnectedWithoutReason), reason);
            }
        }

        [TestCase(NgoDisconnectReasons.HostShuttingDown)]
        [TestCase(NgoDisconnectReasons.ServerShuttingDown)]
        public void Localize_NgoHostShutdown_ReturnsHostShutDown(string reason)
        {
            var result = DisconnectReasonLocalizer.Localize(reason);

            Assert.That(result.Message, Is.EqualTo(DisconnectReasonMessages.HostShutDown));
            Assert.That(result.Category, Is.EqualTo(DisconnectReasonCategory.KnownEnglish));
            Assert.IsFalse(result.IsOriginalShown);
        }

        [Test]
        public void Localize_NgoHostShutdown_MatchesNgoFormat()
        {
            // NGO 2.13.2 NetworkManager.ProcessServerShutdown の組み立てと同じ式で作った文字列と一致する。
            foreach (var hostServer in new[] { "host", "server" })
            {
                var reason = $"Disconnected due to {hostServer} shutting down.";
                Assert.That(DisconnectReasonLocalizer.Localize(reason).Message,
                    Is.EqualTo(DisconnectReasonMessages.HostShutDown), reason);
            }
        }

        [Test]
        public void Localize_LegacyRateLimitReason_ReturnsJapanese()
        {
            var result = DisconnectReasonLocalizer.Localize(NgoDisconnectReasons.LegacyRateLimitExceeded);

            Assert.That(result.Message, Is.EqualTo(DisconnectReasonMessages.RateLimitExceeded));
            Assert.That(result.Category, Is.EqualTo(DisconnectReasonCategory.KnownEnglish));
        }

        [Test]
        public void Localize_MeasuredClientShutdownReason_ReturnsDisconnectedWithoutReason()
        {
            Assert.That(MeasuredClientShutdownReason.Length, Is.EqualTo(144), "#208 の実測の長さ。");

            var result = DisconnectReasonLocalizer.Localize(MeasuredClientShutdownReason);

            Assert.That(result.Message, Is.EqualTo(JoinStatusMessages.DisconnectedWithoutReason));
            Assert.That(result.Category, Is.EqualTo(DisconnectReasonCategory.TransportEvent));
        }

        [TestCase("ProtocolTimeout", "Connection closed due to timed out.", DisconnectReasonMessages.ConnectionLost)]
        [TestCase("MaxConnectionAttempts", "Connection closed due to maximum connection attempts reached.", JoinStatusMessages.Timeout)]
        [TestCase("ClosedByRemote", "Connection was closed by remote endpoint.", JoinStatusMessages.DisconnectedWithoutReason)]
        [TestCase("Disconnected", "Gracefully disconnected.", JoinStatusMessages.DisconnectedWithoutReason)]
        [TestCase("ProtocolError", "Gracefully disconnected.", JoinStatusMessages.DisconnectedWithoutReason)]
        [TestCase("AuthenticationFailure", "Connection closed due to authentication failure.", JoinStatusMessages.DisconnectedWithoutReason)]
        [TestCase("SomeFutureEvent", "", JoinStatusMessages.DisconnectedWithoutReason)]
        public void Localize_NgoDisconnectEvent_MapsByEventName(string eventName, string transportMessage, string expected)
        {
            // NGO 2.13.2 NetworkConnectionManager.GenerateDisconnectInformation の組み立て（理由なし）と、
            // UnityTransport.cs の UnityTransportNotificationHandler の文言。ID は最大桁にする。
            var reason = $"[Disconnect Event][Client-{ulong.MaxValue}][TransportClientId-{ulong.MaxValue}][{eventName}] {transportMessage}";

            var result = DisconnectReasonLocalizer.Localize(reason);

            Assert.That(result.Message, Is.EqualTo(expected));
            Assert.That(result.Category, Is.EqualTo(DisconnectReasonCategory.TransportEvent));
        }

        [TestCase("[Disconnect Event]")]
        [TestCase("[Disconnect Event] 満室です")]
        [TestCase("[Disconnect Event][Client-x][TransportClientId-1][ClosedByRemote] ")]
        [TestCase(" [Disconnect Event][Client-1][TransportClientId-1][ClosedByRemote] ")]
        [TestCase("Client-5 disconnected by server.")] // NGO の NetworkManager.DisconnectClient(ulong) の既定の理由（このアプリは使わない）
        [TestCase("Disconnected due to host shutting down")] // 句点なし
        [TestCase("disconnected due to host shutting down.")]
        [TestCase("Rate limit exceeded")]
        [TestCase("<size=60><color=red>拒否</color></size>")]
        [TestCase("管理者の判断で退出させました。")]
        public void Localize_UnknownReason_ReturnsGenericMessage(string reason)
        {
            var result = DisconnectReasonLocalizer.Localize(reason);

            Assert.That(result.Message, Is.EqualTo(JoinStatusMessages.DisconnectedWithoutReason));
            Assert.That(result.Category, Is.EqualTo(DisconnectReasonCategory.Unknown));
            Assert.IsFalse(result.IsOriginalShown);
        }

        [Test]
        public void Localize_LongOrBrokenInput_DoesNotThrowAndReturnsKnownText()
        {
            var inputs = new[]
            {
                new string('x', 100000),
                "[Disconnect Event]" + new string('[', 10000),
                "\uD800",
                "\u0000" + NgoDisconnectReasons.HostShuttingDown,
            };

            var allowed = DisconnectReasonMessages.All
                .Concat(new[] { JoinStatusMessages.DisconnectedWithoutReason, JoinStatusMessages.Timeout })
                .ToArray();
            foreach (var reason in inputs)
            {
                var result = DisconnectReasonLocalizer.Localize(reason);
                Assert.That(allowed, Does.Contain(result.Message));
            }
        }

        [Test]
        public void MessagesThisAppShows_AreUnchangedBySanitizingAndShortEnough()
        {
            foreach (var message in DisconnectReasonMessages.All)
            {
                Assert.That(DisconnectReasonSanitizer.Sanitize(message), Is.EqualTo(message), message);
                Assert.That(message.Length, Is.LessThanOrEqualTo(DisconnectReasonSanitizer.MaxLength / 2), message);
                Assert.That(message, Does.Not.Contain("<"), "平文でもタグに見える文字は使わない。");
            }

            Assert.That(DisconnectReasonMessages.All.Distinct().Count(), Is.EqualTo(DisconnectReasonMessages.All.Count));
        }

        [Test]
        public void SanitizeForLog_KeepsWholeNgoDiagnosticAndRemovesLineBreaks()
        {
            var longest = $"[Disconnect Event][Client-{ulong.MaxValue}][TransportClientId-{ulong.MaxValue}][TransportShutdown] "
                + "NetworkConnectionManager was shutdown. The transport was shutdown.";

            Assert.That(DisconnectReasonSanitizer.SanitizeForLog(MeasuredClientShutdownReason), Is.EqualTo(MeasuredClientShutdownReason));
            Assert.That(DisconnectReasonSanitizer.SanitizeForLog(longest), Is.EqualTo(longest));
            Assert.That(DisconnectReasonSanitizer.SanitizeForLog("理由\r\nError: 偽の行"), Is.EqualTo("理由 Error: 偽の行"));

            var capped = DisconnectReasonSanitizer.SanitizeForLog(new string('x', 100000));
            Assert.That(capped.Length, Is.EqualTo(DisconnectReasonSanitizer.MaxLogLength));
            Assert.That(capped, Does.EndWith(DisconnectReasonSanitizer.Ellipsis));
        }

        [Test]
        public void SanitizeForLog_ReplacesTagOpenersSoTheEditorConsoleDoesNotInterpretThem()
        {
            // Unity Editor のコンソールはログのリッチテキストを解釈する。ホストが送ったタグで表示を崩させない（#208 レビュー L-3）。
            var logged = DisconnectReasonSanitizer.SanitizeForLog(
                "<size=60><color=red>拒否</color></size>" + (char)10 + "<b>理由</b>");

            Assert.That(logged, Is.EqualTo("‹size=60>‹color=red>拒否‹/color>‹/size> ‹b>理由‹/b>"));
            Assert.That(logged, Does.Not.Contain("<"));
            Assert.That((int)DisconnectReasonSanitizer.LogTagOpenerReplacement, Is.EqualTo(0x2039), "U+2039 のはず。");

            // 置き換えは 1 文字を 1 文字にするので、上限ちょうどの長さも変わらない。
            var atLimit = DisconnectReasonSanitizer.SanitizeForLog(new string('<', 100000));
            Assert.That(atLimit.Length, Is.EqualTo(DisconnectReasonSanitizer.MaxLogLength));
            Assert.That(atLimit, Does.Not.Contain("<"));
        }
    }
}
