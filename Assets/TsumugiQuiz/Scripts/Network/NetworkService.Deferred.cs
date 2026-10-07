using System;
using System.Collections;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="NetworkService"/> のうち、停止完了を待ってから開始し直す部分
    /// （docs/network.md §2.4）。NGO の <c>Shutdown()</c> はその場では止まらず、
    /// フレーム終端で実際の停止処理が走るため、停止直後の再開始はここのコルーチンを使う。
    /// </summary>
    public sealed partial class NetworkService
    {
        /// <summary>停止完了を待つ上限フレーム数。超えたら失敗として返す（無限待ちを避ける）。</summary>
        private const int ShutdownWaitFrameLimit = 300;

        /// <summary>停止処理中に開始を要求されたときのメッセージ。</summary>
        private static string ShutdownInProgressMessage =>
            "前回の接続を停止しています。少し待ってからもう一度お試しください。";

        /// <summary>停止完了を待ちきれなかったときのメッセージ。</summary>
        private static string ShutdownTimeoutMessage =>
            "前回の接続の停止が完了しませんでした。しばらく待ってからもう一度お試しください。";

        /// <summary>
        /// 停止処理の完了を待ってからホストを開始するコルーチン。
        /// <c>Stop()</c> の直後に開始し直す場合はこちらを使う（NGO の停止はフレーム終端で走るため）。
        /// </summary>
        /// <param name="startPort">最初に試すポート。</param>
        /// <param name="maxPlayers">参加人数の上限。null なら制限なし。</param>
        /// <param name="hostPlayerName">ホスト自身のプレイヤー名。</param>
        /// <param name="spawnGameSession">開始成功時に <see cref="GameSession"/> をスポーンするか。</param>
        /// <param name="onCompleted">結果の受け取り先。</param>
        public IEnumerator StartHostWhenReady(
            ushort startPort = NetworkConstants.DefaultPort,
            int? maxPlayers = null,
            string hostPlayerName = null,
            bool spawnGameSession = true,
            Action<NetworkStartResult> onCompleted = null)
        {
            ThrowIfDisposed();

            for (var frame = 0; frame < ShutdownWaitFrameLimit && _networkManager.ShutdownInProgress; frame++)
            {
                yield return null;
            }

            var result = _networkManager.ShutdownInProgress
                ? NetworkStartResult.Fail(ShutdownTimeoutMessage)
                : StartHost(startPort, maxPlayers, hostPlayerName, spawnGameSession);

            onCompleted?.Invoke(result);
        }

        /// <summary>
        /// 停止処理の完了を待ってからクライアント接続を開始するコルーチン。
        /// </summary>
        /// <param name="address">ホストの IP アドレス。</param>
        /// <param name="port">ホストのポート。</param>
        /// <param name="playerName">プレイヤー名。</param>
        /// <param name="clientBuildHash">ビルドの識別子。null なら自分のビルドの識別子（<see cref="StartClient"/> を参照）。</param>
        /// <param name="onCompleted">結果の受け取り先。</param>
        public IEnumerator StartClientWhenReady(
            string address,
            ushort port,
            string playerName,
            string clientBuildHash = null,
            Action<NetworkStartResult> onCompleted = null)
        {
            ThrowIfDisposed();

            for (var frame = 0; frame < ShutdownWaitFrameLimit && _networkManager.ShutdownInProgress; frame++)
            {
                yield return null;
            }

            var result = _networkManager.ShutdownInProgress
                ? NetworkStartResult.Fail(ShutdownTimeoutMessage)
                : StartClient(address, port, playerName, clientBuildHash);

            onCompleted?.Invoke(result);
        }
    }
}
