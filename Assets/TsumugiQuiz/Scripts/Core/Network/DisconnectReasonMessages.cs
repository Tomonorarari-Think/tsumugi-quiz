using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core.Network
{
    /// <summary>
    /// 承認後の切断で使う日本語の文言（issue #208、docs/network.md §2.4・§9）。
    /// ホストが送る理由（<see cref="SeatTakenOver"/> / <see cref="RateLimitExceeded"/>）と、
    /// クライアントが NGO の英語の理由から対応づける文言（<see cref="HostShutDown"/> / <see cref="ConnectionLost"/>）を持つ。
    /// 承認時の拒否理由は <see cref="ConnectionRejectionMessages"/>、Join 画面の定型文は <see cref="JoinStatusMessages"/>。
    /// </summary>
    public static class DisconnectReasonMessages
    {
        /// <summary>
        /// 同じ席を別の接続が引き継いだため、古い接続を切るときの理由（ホストが送る。#69）。
        /// </summary>
        public const string SeatTakenOver = "別の接続が同じ席を引き継ぎました。";

        /// <summary>
        /// RPC のレート制限を超え続けたため切断するときの理由（ホストが送る。#52、docs/network.md §9.1）。
        /// #208 より前のホストは英語の <see cref="NgoDisconnectReasons.LegacyRateLimitExceeded"/> を送る。
        /// </summary>
        public const string RateLimitExceeded = "送信が多すぎるため、ホストから切断されました。";

        /// <summary>
        /// ホストが停止した（NGO の <see cref="NgoDisconnectReasons.HostShuttingDown"/>）ときの文言。
        /// ホストの退出確認（「ゲームを終了して退出しますか？」）と語をそろえた。
        /// </summary>
        public const string HostShutDown = "ホストがゲームを終了しました。";

        /// <summary>
        /// ホストとの通信が途絶えた（Transport のタイムアウト。NGO の
        /// <see cref="NgoDisconnectReasons.ProtocolTimeoutEvent"/>）ときの文言。
        /// </summary>
        public const string ConnectionLost = "ホストとの通信が途絶えました。";

        /// <summary>
        /// ホストが送る理由と、クライアントが対応づける文言のすべて（重複なし）。
        /// 表示・折り返しのテストが列挙に使う。
        /// </summary>
        public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[]
        {
            SeatTakenOver,
            RateLimitExceeded,
            HostShutDown,
            ConnectionLost,
        });
    }
}
