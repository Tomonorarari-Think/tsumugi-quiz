using System.Net;
using System.Net.Sockets;
using Unity.Netcode.Transports.UTP;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// ポートの空き確認と、実際にバインドされたポートの取得。
    /// <see cref="NetworkService"/> のホスト開始処理から使う。
    /// </summary>
    internal static class NetworkPortProbe
    {
        /// <summary>
        /// 指定した UDP ポートが空いているかを実際にバインドして確かめる。
        /// ポート 0 は「OS に任せる」意味なので常に空きとして扱う。
        ///
        /// バインドしてすぐ閉じるため、確認から実際の待ち受け開始までの間に他プロセスが
        /// 奪う可能性は残る（その場合は <c>NetworkManager.StartHost()</c> が false を返し、次のポートへ進む）。
        /// 事前確認を入れているのは、使用中とわかっているポートを UnityTransport に渡すと
        /// <c>Debug.LogError</c>（"Server failed to bind"）が出てホストのログが汚れるため。
        /// </summary>
        /// <param name="port">確認するポート。</param>
        /// <returns>空いていれば true。</returns>
        public static bool IsUdpPortAvailable(ushort port)
        {
            if (port == 0)
            {
                return true;
            }

            try
            {
                using (var probe = new UdpClient(new IPEndPoint(IPAddress.Any, port)))
                {
                    return true;
                }
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>
        /// 実際にバインドされたポートを取得する。ポート 0 を指定した場合は OS が選んだ値になるため、
        /// <c>UnityTransport.GetLocalEndpoint()</c>（ドライバの local endpoint）から読み取る。
        /// 取得できない場合は要求したポートをそのまま返す。
        /// </summary>
        /// <param name="transport">対象の Transport。</param>
        /// <param name="requestedPort">要求したポート。</param>
        /// <returns>実際のポート。</returns>
        public static ushort ResolveBoundPort(UnityTransport transport, ushort requestedPort)
        {
            if (transport == null)
            {
                return requestedPort;
            }

            var localEndpoint = transport.GetLocalEndpoint();
            return localEndpoint.IsValid && localEndpoint.Port != 0 ? localEndpoint.Port : requestedPort;
        }
    }
}
