namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// NAT デバイスの失敗種別を、結果の分類と UI 向けの日本語メッセージに翻訳する。
    /// メッセージはそのまま <see cref="PortMappingResult.Message"/> に入り、
    /// #5 の手動ポート開放案内画面に表示される（docs/network-nat.md §1.5）。
    /// </summary>
    internal static class PortMappingFailureMessages
    {
        /// <summary>
        /// 失敗種別から結果の分類を決める。
        /// ルーター側が明確に断ったケース（拒否・競合・未対応）は
        /// <see cref="PortMappingStatus.Refused"/> にまとめ、それ以外は
        /// <see cref="PortMappingStatus.Failed"/> とする。
        /// </summary>
        /// <param name="kind">失敗種別。</param>
        public static PortMappingStatus ToStatus(NatFailureKind kind)
        {
            switch (kind)
            {
                case NatFailureKind.Refused:
                case NatFailureKind.Conflict:
                case NatFailureKind.Unsupported:
                    return PortMappingStatus.Refused;
                default:
                    return PortMappingStatus.Failed;
            }
        }

        /// <summary>
        /// 失敗種別に対応する、ユーザー向けの日本語メッセージ。次に取るべき行動まで書く。
        /// </summary>
        /// <param name="kind">失敗種別。</param>
        public static string Describe(NatFailureKind kind)
        {
            switch (kind)
            {
                case NatFailureKind.Refused:
                    return "ルーターがポート開放を拒否しました。ルーターの設定で UPnP を有効にするか、手動でポートを開放してください。";
                case NatFailureKind.Conflict:
                    return "そのポートは別の機器が使用中です。ポート番号を変えるか、手動でポートを開放してください。";
                case NatFailureKind.Unsupported:
                    return "ルーターが自動ポート開放に対応していません。手動でポートを開放してください。";
                default:
                    return "自動ポート開放に失敗しました。手動でポートを開放してください。";
            }
        }
    }
}
