using TsumugiQuiz.Room;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 確定した <see cref="RoomSettings"/> を、実際に値を使う各コンポーネントへ流し込む
    /// （issue #27、docs/network.md §12）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 適用先は 2 種類ある。
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>全ピアで適用する値</b>: 各 PC がローカルに使う値（<c>tts.speed</c> は各クライアントが
    ///     自分で合成するときの速度、<c>tts.leadTimeSec</c> / <c>tts.readyTimeoutMs</c> /
    ///     <c>buzz.allowDuringReading</c> はサーバーが使うが、再接続時の取り違えを防ぐため全ピアで同じ値にする）
    ///   </description></item>
    ///   <item><description>
    ///     <b>サーバーでのみ適用する値</b>: 権威を持つ側だけが使う値（<c>room.maxPlayers</c> /
    ///     <c>host.role</c> / <c>network.allowLateJoin</c> は <see cref="LobbyState"/>、
    ///     <c>tts.enabled</c> は <c>TtsSyncCoordinator.ReadingEnabled</c>（<c>NetworkVariable</c>）へ）
    ///   </description></item>
    /// </list>
    /// <para>
    /// 制限時間（<c>buzz.*</c> / <c>answer.*</c>）・得点（<c>score.*</c>）・出題（<c>questions.*</c>）は
    /// <see cref="GameSession"/> が「ゲーム開始操作」の時点で
    /// <see cref="GameSession.StartSession"/> / <see cref="GameSession.Configure"/> 経由で確定させるため、
    /// ここでは触らない（進行中の状態機械を壊さないため）。クライアント側の残り時間表示は
    /// <see cref="RoomSettingsSync.Current"/> の制限時間を読む（<see cref="GameSession.CurrentDeadlineServerTime"/>、#154）。
    /// </para>
    /// </remarks>
    internal static class RoomSettingsApplier
    {
        /// <summary>
        /// ルーム設定を各コンポーネントへ適用する。
        /// </summary>
        /// <param name="settings">確定したルーム設定。null なら何もしない。</param>
        /// <param name="isServer">サーバー（ホスト）側での適用か。</param>
        /// <param name="lobby">ロビーの共有状態。無ければ null。</param>
        /// <param name="tts">読み上げ同期。無ければ null。</param>
        public static void Apply(RoomSettings settings, bool isServer, LobbyState lobby, TtsSyncCoordinator tts)
        {
            if (settings == null)
            {
                return;
            }

            ApplyToTts(settings, isServer, tts);

            if (!isServer)
            {
                return;
            }

            ApplyToLobby(settings, lobby);
        }

        /// <summary>読み上げ同期（<c>tts.*</c> / <c>buzz.allowDuringReading</c>）へ適用する。</summary>
        private static void ApplyToTts(RoomSettings settings, bool isServer, TtsSyncCoordinator tts)
        {
            if (tts == null)
            {
                return;
            }

            // 各プロパティの setter が範囲外の値を丸める（RoomSettings 側でも範囲内だが二重に守る）。
            tts.Speed = (float)settings.TtsSpeed;
            tts.LeadTimeSec = settings.TtsLeadTimeSec;
            tts.ReadyTimeoutSec = settings.TtsReadyTimeoutMs / 1000.0;
            tts.AllowBuzzDuringReading = settings.AllowDuringReading;

            if (isServer)
            {
                // ReadingEnabled は NetworkVariable（サーバー書き込み）なのでホストだけが書く。
                tts.SetReadingEnabled(settings.TtsEnabled);
            }
        }

        /// <summary>
        /// ロビー（<c>host.role</c> / <c>room.maxPlayers</c> / <c>network.allowLateJoin</c>）へ適用する。
        /// <c>host.role</c> は <see cref="RoomSettingsSync"/> 側でロビーの値と突き合わせ済み
        /// （<c>ReconcileHostRole</c>）なので、ここでは素直に書き込む。
        /// </summary>
        private static void ApplyToLobby(RoomSettings settings, LobbyState lobby)
        {
            if (lobby == null || !lobby.IsSpawned || !lobby.IsServer)
            {
                // ロビーを立てない構成（PlayMode の GameSession 単体テスト等）では何もしない。
                return;
            }

            lobby.ConfigureRoom(settings.HostRole, settings.MaxPlayers, settings.AllowLateJoin);
        }
    }
}
