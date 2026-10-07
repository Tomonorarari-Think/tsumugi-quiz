using NUnit.Framework;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using Unity.Netcode;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// Network 層が承認後の切断で送る理由と、受信側の整形・対応づけ（<see cref="DisconnectReasonSanitizer"/> /
    /// <see cref="DisconnectReasonLocalizer"/>）が食い違わないことを確かめる（issue #206 / #208。拒否理由は EditMode/Core の
    /// <c>DisconnectReasonSanitizerTests</c> / <c>DisconnectReasonLocalizerTests</c>）。
    /// </summary>
    public class NetworkDisconnectReasonTests
    {
        [TestCase(LobbyState.SeatTakenOverDisconnectReason)]
        [TestCase(DisconnectReasonMessages.RateLimitExceeded)] // RpcRateGuard の理由（非公開の定数は Core のこの定数を参照している）
        public void ReasonsSentAfterApproval_AreUnchangedBySanitizing(string reason)
        {
            Assert.That(DisconnectReasonSanitizer.Sanitize(reason), Is.EqualTo(reason));
        }

        [Test]
        public void SeatTakenOverReason_IsShownAsIsByReceiver()
        {
            var result = DisconnectReasonLocalizer.Localize(LobbyState.SeatTakenOverDisconnectReason);

            Assert.That(result.Category, Is.EqualTo(DisconnectReasonCategory.AppMessage));
            Assert.That(result.Message, Is.EqualTo(LobbyState.SeatTakenOverDisconnectReason));
        }

        /// <summary>
        /// 対応づけに使う NGO のイベント名が、NGO の列挙（<see cref="NetworkTransport.DisconnectEvents"/>）の名前と一致する
        /// （NGO を更新して名前が変わったら落ちる。列挙の値が消えたらコンパイルが通らない。#208）。
        /// </summary>
        [Test]
        public void NgoDisconnectEventNames_MatchNgoEnum()
        {
            Assert.That(NgoDisconnectReasons.ProtocolTimeoutEvent,
                Is.EqualTo(nameof(NetworkTransport.DisconnectEvents.ProtocolTimeout)));
            Assert.That(NgoDisconnectReasons.MaxConnectionAttemptsEvent,
                Is.EqualTo(nameof(NetworkTransport.DisconnectEvents.MaxConnectionAttempts)));
            Assert.That(NgoDisconnectReasons.TransportShutdownEvent,
                Is.EqualTo(nameof(NetworkTransport.DisconnectEvents.TransportShutdown)));
        }
    }
}
