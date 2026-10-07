using System.Collections.Generic;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Lobby
{
    /// <summary>
    /// <see cref="LobbyView"/> のうち、参加者一覧の描画と参加通知 SE を担う部分クラス。
    /// 表示元は <see cref="LobbyState"/> の <c>NetworkList</c>（サーバー書き込み・クライアント読み取り専用）で、
    /// この層は一切書き換えない（docs/network.md §1.2）。
    /// </summary>
    /// <remarks>
    /// <see cref="LobbyState.RosterChanged"/> は 1 回の変更で複数回発火し、その途中では名簿が
    /// 空に見えることがある（サーバーが <c>NetworkList</c> を作り直すため）。そのため
    /// 再描画はフラグを立てるだけにして、実際の描画と参加・退出の判定は
    /// <see cref="LobbyView"/> のコルーチンが 1 フレームに 1 回まとめて行う。
    /// </remarks>
    public sealed partial class LobbyView
    {
        private const string PlayerRowClass = "lobby-player-row";
        private const string PlayerRowDisconnectedClass = "lobby-player-row--disconnected";
        private const string PlayerNameClass = "lobby-player-name";
        private const string PlayerBadgeClass = "lobby-player-badge";
        private const string PlayerStateClass = "lobby-player-state";

        private const string HostBadgeText = "ホスト";
        private const string ModeratorBadgeText = "司会";
        private const string YouBadgeText = "あなた";
        private const string DisconnectedStateText = "切断中";

        /// <summary>
        /// 直前に描画した「クライアント ID → 接続中か」。参加の判定に使う。
        /// 名前ではなくクライアント ID をキーにするのは、切断中エントリの名前が
        /// クライアントには伏せ字で届く（レビュー H-2）ため、名前では区別できないから。
        /// </summary>
        private readonly Dictionary<ulong, bool> _lastConnectedByClientId = new Dictionary<ulong, bool>();

        /// <summary>初回描画を終えたか（開いた瞬間に人数分の SE を鳴らさないための抑止）。</summary>
        private bool _rosterRendered;

        /// <summary>次のフレームで名簿を描き直す必要があるか。</summary>
        private bool _rosterRenderPending;

        /// <summary>描画状態を初期化する（<see cref="LobbyView.OnShow"/> から。レビュー M-6）。</summary>
        private void ResetRosterRenderState()
        {
            _lastConnectedByClientId.Clear();
            _rosterRendered = false;
            _rosterRenderPending = false;
        }

        /// <summary>
        /// 名簿の購読を始める。初回の全件同期（<c>NetworkList.ReadField</c>）では変更通知が
        /// 発火しないため、購読したあとに必ず 1 回描画する。
        /// </summary>
        private void BindLobbyState(LobbyState lobbyState)
        {
            if (lobbyState == null)
            {
                return;
            }

            UnbindLobbyState();

            _lobbyState = lobbyState;
            _lobbyState.RosterChanged += HandleRosterChanged;
            _lobbyState.MaxPlayers.OnValueChanged += HandleMaxPlayersChanged;
            _lobbyState.Role.OnValueChanged += HandleRoleChanged;
            _lobbyState.AllowLateJoin.OnValueChanged += HandleAllowLateJoinChanged;

            // レビュー M-8: ルーム設定の流し込みは NetworkBootstrap がホスト開始時に 1 度だけ行う。
            // View は現在値を表示に反映するだけ（ホストが途中参加の可否を切り替える操作は別途）。
            _allowLateJoinToggle.SetValueWithoutNotify(_lobbyState.AllowLateJoin.Value);

            if (_isHost)
            {
                _startGameButton.SetEnabled(true);
            }

            ApplyRoomLabels();
            RenderRoster();
        }

        private void UnbindLobbyState()
        {
            if (_lobbyState == null)
            {
                return;
            }

            _lobbyState.RosterChanged -= HandleRosterChanged;
            _lobbyState.MaxPlayers.OnValueChanged -= HandleMaxPlayersChanged;
            _lobbyState.Role.OnValueChanged -= HandleRoleChanged;
            _lobbyState.AllowLateJoin.OnValueChanged -= HandleAllowLateJoinChanged;
            _lobbyState = null;
        }

        /// <summary>
        /// 名簿が動いたことだけを覚えておく。実際の描画は次のフレームに 1 回だけ行う
        /// （remarks 参照。途中の中間状態を画面や SE に出さないため）。
        /// </summary>
        private void HandleRosterChanged() => _rosterRenderPending = true;

        private void HandleMaxPlayersChanged(int previous, int current) => _rosterRenderPending = true;

        private void HandleRoleChanged(HostRole previous, HostRole current)
        {
            ApplyRoomLabels();
            _rosterRenderPending = true;
        }

        private void HandleAllowLateJoinChanged(bool previous, bool current)
        {
            if (_allowLateJoinToggle != null && _allowLateJoinToggle.value != current)
            {
                _allowLateJoinToggle.SetValueWithoutNotify(current);
            }
        }

        /// <summary>保留中の再描画があれば 1 回だけ行う（コルーチンから毎フレーム呼ばれる）。</summary>
        private void FlushPendingRosterRender()
        {
            if (!_rosterRenderPending)
            {
                return;
            }

            _rosterRenderPending = false;
            RenderRoster();
        }

        private void ApplyRoomLabels()
        {
            if (_roleLabel == null || !_isHost)
            {
                return;
            }

            var role = _lobbyState != null ? _lobbyState.Role.Value : LoadHostRole();
            _roleLabel.text = "あなたの役割: " + HostRoles.ToDisplayName(role);
        }

        /// <summary>
        /// 参加者一覧を作り直す。最大 12 行（<see cref="LobbyRoster.MaxMaxPlayers"/>）なので
        /// 差分更新はせず毎回組み立て直す。あわせて前回との差分から参加を判定し、
        /// 参加通知 SE（<see cref="SeKind.Join"/>、setup-brief K17）を鳴らす。
        /// </summary>
        /// <remarks>
        /// 司会専任（<c>host.role = "moderator"</c>）のホストはプレイヤーではないので一覧に出さず、
        /// 人数表示にも数えない（レビュー H-4。定員の数え方（<see cref="LobbyRoster.OccupiedSlotCount"/>）と揃える）。
        /// </remarks>
        private void RenderRoster()
        {
            if (_playerList == null)
            {
                return;
            }

            _playerList.Clear();

            var localClientId = GetLocalClientId();
            var connected = 0;
            var total = 0;
            var disconnected = 0;
            var someoneJoined = false;
            var seen = new HashSet<ulong>();
            var visible = new List<PlayerEntry>();

            if (_lobbyState != null && _lobbyState.IsSpawned)
            {
                foreach (var entry in _lobbyState.GetPlayersSnapshot())
                {
                    // H-4: 司会専任のホストは参加者ではないので一覧にも人数にも出さない。
                    if (entry.IsHost && entry.IsModerator)
                    {
                        continue;
                    }

                    visible.Add(entry);

                    total++;
                    if (entry.IsConnected)
                    {
                        connected++;
                    }
                    else
                    {
                        disconnected++;
                    }

                    seen.Add(entry.ClientId);

                    // M-7: 鳴らすのは「未登録 / 切断中 → 接続中」の遷移だけ。退出は無音にする。
                    if (entry.IsConnected
                        && (!_lastConnectedByClientId.TryGetValue(entry.ClientId, out var wasConnected) || !wasConnected))
                    {
                        someoneJoined = true;
                    }

                    _lastConnectedByClientId[entry.ClientId] = entry.IsConnected;
                }
            }

            // #85: 同名が同時に接続している（再接続経路でだけ起こる）場合に連番で区別する。
            // 規則は GameView の回答者表示と共有する（PlayerEntryDisplayNames）。
            var displayNames = PlayerEntryDisplayNames.Resolve(visible);
            for (var i = 0; i < visible.Count; i++)
            {
                _playerList.Add(CreatePlayerRow(visible[i], displayNames.GetLabel(i), localClientId));
            }

            RemoveVanishedClientIds(seen);
            ApplyDuplicateNameNotice(displayNames);

            _emptyLabel.style.display = total == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            var maxPlayers = _lobbyState != null && _lobbyState.IsSpawned
                ? _lobbyState.MaxPlayers.Value
                : LobbyRoster.DefaultMaxPlayers;
            _playerCountLabel.text = total == 0
                ? string.Empty
                : $"接続中 {connected} 人 / 定員 {maxPlayers} 人（登録 {total} 人）";

            // H-2 の緩和策: 切断中のエントリがあるときだけ、ホストに手動削除の手段を出す。
            _removeDisconnectedButton.style.display = _isHost && disconnected > 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            // 初回描画（画面を開いた時点の同期）では鳴らさない。
            if (_rosterRendered && someoneJoined)
            {
                PlayJoinSe();
            }

            _rosterRendered = true;
        }

        /// <summary>
        /// 同名が同時に接続している場合の注意を出す（#85）。ホストには対処
        /// （切断中エントリの削除）の案内も添える。
        /// </summary>
        private void ApplyDuplicateNameNotice(PlayerDisplayNameSet displayNames)
        {
            if (_duplicateNameLabel == null)
            {
                return;
            }

            var message = displayNames.HasDuplicates
                ? DuplicateNameNotice.Build(displayNames, includeHostAdvice: _isHost)
                : string.Empty;

            _duplicateNameLabel.text = message;
            _duplicateNameLabel.style.display = string.IsNullOrEmpty(message)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        /// <summary>名簿から消えたクライアント ID を追跡表から落とす（保持期間切れの削除など）。</summary>
        private void RemoveVanishedClientIds(HashSet<ulong> seen)
        {
            if (_lastConnectedByClientId.Count == seen.Count)
            {
                return;
            }

            List<ulong> vanished = null;
            foreach (var pair in _lastConnectedByClientId)
            {
                if (seen.Contains(pair.Key))
                {
                    continue;
                }

                vanished ??= new List<ulong>();
                vanished.Add(pair.Key);
            }

            if (vanished == null)
            {
                return;
            }

            foreach (var clientId in vanished)
            {
                _lastConnectedByClientId.Remove(clientId);
            }
        }

        /// <summary>
        /// 参加の通知音（setup-brief K17 の <see cref="SeKind.Join"/>）。
        /// 退出は無音にする（レビュー M-7。専用の退出 SE が無いので参加音を流用すると紛らわしい）。
        /// Network 層はイベントを出すだけで、鳴らすのは UI 層の責務（統括メモ #35）。
        /// </summary>
        private static void PlayJoinSe()
        {
            var sePlayer = SePlayer.Instance;
            if (sePlayer != null)
            {
                sePlayer.Play(SeKind.Join);
            }
        }

        /// <param name="entry">名簿のエントリ。</param>
        /// <param name="displayName">
        /// 表示名（#85 の連番つき）。空なら元の名前にフォールバックする。
        /// </param>
        /// <param name="localClientId">自分のクライアント ID。</param>
        private static VisualElement CreatePlayerRow(PlayerEntry entry, string displayName, ulong localClientId)
        {
            var row = new VisualElement();
            row.AddToClassList(PlayerRowClass);
            if (!entry.IsConnected)
            {
                row.AddToClassList(PlayerRowDisconnectedClass);
            }

            // #206: 名前は参加者が決める文字列なので、リッチテキストとして解釈させない（名前は表示用に整えたもの）。
            var nameLabel = PlainText.CreateLabel(string.IsNullOrEmpty(displayName) ? entry.GetDisplayName() : displayName);
            nameLabel.AddToClassList(PlayerNameClass);
            row.Add(nameLabel);

            if (entry.IsHost)
            {
                row.Add(CreateBadge(entry.IsModerator ? ModeratorBadgeText : HostBadgeText));
            }

            if (entry.ClientId == localClientId)
            {
                row.Add(CreateBadge(YouBadgeText));
            }

            var stateLabel = new Label(entry.IsConnected ? string.Empty : DisconnectedStateText);
            stateLabel.AddToClassList(PlayerStateClass);
            row.Add(stateLabel);

            return row;
        }

        private static VisualElement CreateBadge(string text)
        {
            var badge = new Label(text);
            badge.AddToClassList(PlayerBadgeClass);
            return badge;
        }

        /// <summary>
        /// 自分のクライアント ID。<see cref="LobbyState"/> が属する <see cref="NetworkManager"/> から取る
        /// （レビュー M-5。<c>NetworkManager.Singleton</c> だと 1 プロセスに複数の
        /// <see cref="NetworkManager"/> が立つ PlayMode テストで別のピアの ID を見てしまう）。
        /// </summary>
        private ulong GetLocalClientId()
        {
            var networkManager = _lobbyState != null ? _lobbyState.NetworkManager : null;
            return networkManager != null ? networkManager.LocalClientId : ulong.MaxValue;
        }
    }
}
