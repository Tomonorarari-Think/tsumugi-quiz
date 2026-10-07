using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// ネットワーク層の固定値。
    /// Transport の設定（<see cref="MaxPayloadSizeBytes"/> など）は接続確立前にホスト・クライアント双方で
    /// 一致している必要があり、NGO / UTP に実行時同期の仕組みがないため、ルーム設定にはせず定数で固定する
    /// （docs/tasks/setup-brief.md K14 の追加改訂、docs/network.md §8.3）。
    /// </summary>
    public static class NetworkConstants
    {
        /// <summary>既定のポート番号（docs/network.md §2.1、<see cref="SettingsDefaults.NetworkPort"/> と同じ、#26 統括判断 M6）。</summary>
        public const ushort DefaultPort = (ushort)SettingsDefaults.NetworkPort;

        /// <summary>ポートが使用中だったときに +1 しながら試す最大回数（初回を含む）。</summary>
        public const int PortRetryCount = 10;

        /// <summary>
        /// <c>UnityTransport.MaxPayloadSize</c> に設定する値。
        /// 既定の 6144 では 16KB の画像チャンクが UTP のフラグメンテーションステージに乗らないため
        /// 32768 に引き上げる（K14 改訂、docs/network.md §8.3）。
        /// </summary>
        public const int MaxPayloadSizeBytes = 32768;

        /// <summary>問題画像を分割送信するときの 1 チャンクのバイト数（K14、docs/network.md §8.3）。</summary>
        public const int ImageChunkBytes = 16384;

        /// <summary>
        /// ホストの待ち受けアドレス。全 NIC で待ち受けるため <c>0.0.0.0</c> を明示する。
        /// 省略すると <c>ConnectionData.Address</c> が待ち受けアドレスになり、LAN と WAN の両方から
        /// 受けられなくなる（docs/network.md §2.1）。
        /// </summary>
        public const string AnyAddress = "0.0.0.0";

        /// <summary>ポートを指定するコマンドライン引数（本アプリの正式な名前）。</summary>
        public const string PortArgument = "-tq-port";

        /// <summary>
        /// ポートを指定するコマンドライン引数の別名。
        /// <c>UnityTransport</c> 自身も <c>-port</c> / <c>-ip</c> を見る実装になっているが、
        /// 本アプリは自前で解析した値を <c>SetConnectionData(forceOverrideCommandLineArgs: true, ...)</c> で
        /// 渡すため、UTP 側の解析は無効化される（ポート再試行が引数で上書きされるのを防ぐ）。
        /// </summary>
        public const string PortArgumentAlias = "-port";

        /// <summary>
        /// アプリ独自のプロトコルバージョン。実体は <see cref="ProtocolConstants.Version"/>（Core）にあり、
        /// ここでは Network 層から参照しやすいように公開しているだけ（定義の重複を避ける）。
        /// </summary>
        public static ushort ProtocolVersion => ProtocolConstants.Version;
    }
}
