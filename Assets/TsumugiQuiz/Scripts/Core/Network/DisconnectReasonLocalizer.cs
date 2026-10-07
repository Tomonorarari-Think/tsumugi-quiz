using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// ホストから切断されたときの理由（NGO の <c>NetworkManager.DisconnectReason</c>）を、画面に出す日本語の文言に対応づける
    /// （issue #208、docs/network.md §2.4「切断理由の対応づけ」・§9）。純粋関数で、Unity API に依存しない。
    /// </summary>
    /// <remarks>
    /// <para>規則（上から順に判定する）:</para>
    /// <list type="number">
    /// <item>空・空白だけ → <see cref="JoinStatusMessages.DisconnectedWithoutReason"/>（<see cref="DisconnectReasonCategory.None"/>）</item>
    /// <item>このアプリが送る日本語の理由と完全に一致 → そのまま（<see cref="DisconnectReasonCategory.AppMessage"/>）。
    /// 一覧は <see cref="ConnectionRejectionMessages"/> の全理由と <see cref="DisconnectReasonMessages.SeatTakenOver"/> /
    /// <see cref="DisconnectReasonMessages.RateLimitExceeded"/>。バージョン不一致の拒否理由（プロトコルバージョンとビルドの不一致、#204）だけは
    /// 数字が入るので、<see cref="ConnectionRejectionMessages.ProtocolVersionMismatchFormat"/> の書式との一致で判定する</item>
    /// <item>NGO・旧版のこのアプリの既知の英語の理由と完全に一致 → 対応する日本語（<see cref="DisconnectReasonCategory.KnownEnglish"/>）</item>
    /// <item>NGO の診断文字列（<see cref="NgoDisconnectReasons.DisconnectEventHeader"/> で始まる）→ イベントの種類で対応づける
    /// （<see cref="DisconnectReasonCategory.TransportEvent"/>）</item>
    /// <item>どれでもない → <see cref="JoinStatusMessages.DisconnectedWithoutReason"/>（<see cref="DisconnectReasonCategory.Unknown"/>）。
    /// 元の理由は画面に出さない（呼び出し側が詳細ログに残す）</item>
    /// </list>
    /// <para>
    /// 完全一致にしているのは、改変されたホストが自前の文言に任意の文字列を足して画面へ出せないようにするため。
    /// 前後の空白も許さない（このアプリは定数をそのまま送る）。元の理由を整える必要はない
    /// （そのまま出すのは一覧の文言だけで、それらは <see cref="DisconnectReasonSanitizer"/> で変わらないことをテストで確かめている）。
    /// </para>
    /// </remarks>
    public static class DisconnectReasonLocalizer
    {
        /// <summary>ポート番号と同じく、バージョンは ushort（最大 5 桁の ASCII 数字）。</summary>
        private const string VersionDigits = "[0-9]{1,5}";

        private static readonly HashSet<string> AppMessages = BuildAppMessages();

        private static readonly Regex ProtocolVersionMismatchPattern = BuildProtocolVersionMismatchPattern();

        /// <summary>
        /// NGO の診断文字列の先頭（<c>[Disconnect Event][Client-{id}][TransportClientId-{id}][{DisconnectEvent}]</c>）。
        /// ID は ulong の 10 進数、イベント名は <c>NetworkTransport.DisconnectEvents</c> の名前。
        /// </summary>
        private static readonly Regex DisconnectEventPattern = new Regex(
            @"\A" + Regex.Escape(NgoDisconnectReasons.DisconnectEventHeader)
                + @"\[Client-[0-9]{1,20}\]\[TransportClientId-[0-9]{1,20}\]\[(?<event>[A-Za-z]{1,64})\]",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// 切断理由を日本語の文言に対応づける。
        /// </summary>
        /// <param name="reason">受け取った切断理由（整える前のもの）。null 可。</param>
        /// <returns>画面に出す文言と、どう扱ったか。文言は空にならない。</returns>
        public static LocalizedDisconnectReason Localize(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return new LocalizedDisconnectReason(
                    JoinStatusMessages.DisconnectedWithoutReason, DisconnectReasonCategory.None);
            }

            if (IsAppMessage(reason))
            {
                return new LocalizedDisconnectReason(reason, DisconnectReasonCategory.AppMessage);
            }

            if (TryMapKnownEnglish(reason, out var mapped))
            {
                return new LocalizedDisconnectReason(mapped, DisconnectReasonCategory.KnownEnglish);
            }

            var match = DisconnectEventPattern.Match(reason);
            if (match.Success)
            {
                return new LocalizedDisconnectReason(
                    MapDisconnectEvent(match.Groups["event"].Value), DisconnectReasonCategory.TransportEvent);
            }

            return new LocalizedDisconnectReason(
                JoinStatusMessages.DisconnectedWithoutReason, DisconnectReasonCategory.Unknown);
        }

        /// <summary>
        /// このアプリが送る日本語の理由か（完全一致。バージョン不一致は書式との一致）。
        /// </summary>
        /// <param name="reason">切断理由。</param>
        public static bool IsAppMessage(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return false;
            }

            return AppMessages.Contains(reason) || ProtocolVersionMismatchPattern.IsMatch(reason);
        }

        private static bool TryMapKnownEnglish(string reason, out string message)
        {
            switch (reason)
            {
                case NgoDisconnectReasons.HostShuttingDown:
                case NgoDisconnectReasons.ServerShuttingDown:
                    message = DisconnectReasonMessages.HostShutDown;
                    return true;
                case NgoDisconnectReasons.LegacyRateLimitExceeded:
                    message = DisconnectReasonMessages.RateLimitExceeded;
                    return true;
                default:
                    message = null;
                    return false;
            }
        }

        private static string MapDisconnectEvent(string eventName)
        {
            switch (eventName)
            {
                case NgoDisconnectReasons.ProtocolTimeoutEvent:
                    return DisconnectReasonMessages.ConnectionLost;
                case NgoDisconnectReasons.MaxConnectionAttemptsEvent:
                    // 接続を試みている間にしか起きない（参加コードの誤り・ホストに届かない）。Join 画面の UI 側の
                    // タイムアウトと同じ文言にする。
                    return JoinStatusMessages.Timeout;
                default:
                    // ClosedByRemote・TransportShutdown（NGO が自ら停止した場合）など。サーバーからの理由が無い切断。
                    return JoinStatusMessages.DisconnectedWithoutReason;
            }
        }

        private static HashSet<string> BuildAppMessages()
        {
            var messages = new HashSet<string>(StringComparer.Ordinal)
            {
                DisconnectReasonMessages.SeatTakenOver,
                DisconnectReasonMessages.RateLimitExceeded,
            };

            foreach (ConnectionRejectionReason rejection in Enum.GetValues(typeof(ConnectionRejectionReason)))
            {
                // 数字が入る理由（プロトコルバージョン・ビルドの不一致、#204）は書式との一致で判定する。
                if (rejection == ConnectionRejectionReason.ProtocolVersionMismatch
                    || rejection == ConnectionRejectionReason.ClientBuildMismatch)
                {
                    continue;
                }

                var message = ConnectionRejectionMessages.Create(rejection, 0, 0);
                if (!string.IsNullOrEmpty(message))
                {
                    messages.Add(message);
                }
            }

            return messages;
        }

        private static Regex BuildProtocolVersionMismatchPattern()
        {
            // Regex.Escape は "{" を "\{" にし、"}" はそのまま残す。
            var escaped = Regex.Escape(ConnectionRejectionMessages.ProtocolVersionMismatchFormat)
                .Replace(@"\{0}", VersionDigits)
                .Replace(@"\{1}", VersionDigits);
            // 末尾は "$" ではなく "\z"（"$" は末尾の改行 1 つを許してしまう）。
            return new Regex(@"\A" + escaped + @"\z", RegexOptions.CultureInvariant);
        }
    }
}
