using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Network;
using TsumugiQuiz.UI.TextLayout;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views
{
    /// <summary>
    /// 結果画面のコントローラ（docs/architecture.md §2、issue #20）。
    /// 全問終了時の最終得点（<see cref="GameSession.GetScoreSnapshot"/>）を順位付きで表示し、
    /// ホストには「もう一度（同じ設定で）」「ロビーへ戻る」、それ以外には待機表示を出す。
    /// </summary>
    /// <remarks>
    /// 順位表は <see cref="LobbyState"/> の名簿（<see cref="LobbyState.GetPlayersSnapshot"/>）を基準に組み立てる
    /// （H4）。司会専任のホスト（<c>IsHost &amp;&amp; IsModerator</c>）はプレイヤーではないため除外し
    /// （<c>LobbyView.RenderRoster</c> と同じ扱い）、得点は <see cref="GameSession.GetScore"/>
    /// （未登録なら 0）を使う。得点表（<see cref="GameSession.GetScoreSnapshot"/>）にだけ載っていて
    /// 名簿に無い ID（稀なケース）は末尾に補う。切断中のプレイヤーは
    /// <see cref="PlayerEntry.DisconnectedNameMask"/> で表示する。
    /// 順位計算そのものは <see cref="ResultRanking"/>（<c>TsumugiQuiz.Core</c>、純 C#）に委譲する。
    /// </remarks>
    public sealed class ResultView : IView
    {
        private const string RowClass = "result-rank-row";
        private const string RowDisconnectedClass = "result-rank-row--disconnected";
        private const string RankClass = "result-rank-badge";
        private const string NameClass = "result-rank-name";
        private const string ScoreClass = "result-rank-score";
        private const string YouBadgeClass = "lobby-player-badge";

        private const string NoSessionMessage = "結果を取得できませんでした（進行中のセッションが見つかりません）。";
        private const string RestartFailedMessage = "セッションを開始できませんでした。";
        private const string ReturnFailedMessage = "ロビーへ戻れませんでした。";
        private const string YouBadgeText = "あなた";

        private ViewRouter _router;
        private NetworkService _networkService;
        private GameSession _session;
        private LobbyState _lobby;

        private Label _summaryLabel;
        private VisualElement _rankList;
        private Label _waitingLabel;
        private Label _statusLabel;
        private Button _restartButton;
        private Button _returnButton;

        private bool _isHost;

        /// <inheritdoc />
        public void OnShow(ViewContext context)
        {
            _router = context.Router;

            if (!BindElements(context.Root))
            {
                Debug.LogError("[ResultView] 必要な UI 要素が見つかりません。result-view.uxml を確認してください。");
                return;
            }

            var bootstrap = NetworkBootstrap.Instance;
            _networkService = bootstrap != null ? bootstrap.Service : null;
            _isHost = _networkService != null && _networkService.IsHost;
            _lobby = bootstrap != null ? bootstrap.Lobby : null;
            _session = ResolveSession(bootstrap);

            // 通常の経路（#95 の LobbyView「ゲーム開始」→ GameSession.StartSession）では
            // ActiveSessionSettings が確定しているのでボタンを出す。単問経路（StartQuestion を
            // 直接呼ぶ #12 / #18 のテスト経路、ActiveSessionSettings == null）では
            // 「もう一度（同じ設定で）」に引き継ぐ進行設定が無く、StartSession() が代わりに
            // ルーム設定の出題数（既定 10 問）でセッションを始めてしまうため、
            // 設定の引き継ぎが成立する場合（ActiveSessionSettings != null）だけボタンを出す。
            var canRestartWithSameSettings = _session != null && _session.ActiveSessionSettings != null;
            _restartButton.style.display = _isHost && canRestartWithSameSettings
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _restartButton.clicked += OnRestartClicked;
            _returnButton.style.display = _isHost ? DisplayStyle.Flex : DisplayStyle.None;
            _returnButton.clicked += OnReturnToLobbyClicked;
            _waitingLabel.style.display = _isHost ? DisplayStyle.None : DisplayStyle.Flex;

            if (_session != null)
            {
                _session.ReturnedToLobby += HandleReturnedToLobby;
                _session.ScoreTableChanged += HandleScoreTableChanged;
                _session.Phase.OnValueChanged += HandlePhaseChanged;
            }

            if (_lobby != null)
            {
                _lobby.RosterChanged += HandleRosterChanged;
            }

            if (_networkService != null)
            {
                _networkService.DisconnectedFromHost += HandleDisconnectedFromHost;
            }

            RenderResults();
        }

        /// <inheritdoc />
        public void OnHide()
        {
            if (_session != null)
            {
                _session.ReturnedToLobby -= HandleReturnedToLobby;
                _session.ScoreTableChanged -= HandleScoreTableChanged;
                _session.Phase.OnValueChanged -= HandlePhaseChanged;
            }

            if (_lobby != null)
            {
                _lobby.RosterChanged -= HandleRosterChanged;
            }

            if (_networkService != null)
            {
                _networkService.DisconnectedFromHost -= HandleDisconnectedFromHost;
            }

            if (_restartButton != null)
            {
                _restartButton.clicked -= OnRestartClicked;
            }

            if (_returnButton != null)
            {
                _returnButton.clicked -= OnReturnToLobbyClicked;
            }

            _session = null;
            _lobby = null;
            _networkService = null;
            _router = null;
            _summaryLabel = null;
            _rankList = null;
            _waitingLabel = null;
            _statusLabel = null;
            _restartButton = null;
            _returnButton = null;
        }

        private bool BindElements(VisualElement root)
        {
            _summaryLabel = root.Q<Label>("result-summary-label");
            _rankList = root.Q<VisualElement>("result-rank-list");
            _waitingLabel = root.Q<Label>("result-waiting-label");
            _statusLabel = root.Q<Label>("result-status-label");
            _restartButton = root.Q<Button>("result-restart-button");
            _returnButton = root.Q<Button>("result-return-to-lobby-button");

            return _summaryLabel != null && _rankList != null && _waitingLabel != null
                   && _statusLabel != null && _restartButton != null && _returnButton != null;
        }

        /// <summary>順位表示を組み立て直す。最大人数も少ないため、毎回作り直す（<c>LobbyView.RenderRoster</c> と同じ作法）。</summary>
        private void RenderResults()
        {
            _rankList.Clear();

            if (_session == null || !_session.IsSpawned)
            {
                ShowStatus(NoSessionMessage);
                _summaryLabel.text = string.Empty;
                return;
            }

            ShowStatus(string.Empty);

            var total = _session.TotalQuestions.Value;
            _summaryLabel.text = total > 0 ? $"全 {total} 問終了" : string.Empty;

            var roster = _lobby != null && _lobby.IsSpawned ? _lobby.GetPlayersSnapshot() : null;
            var entries = BuildRankingEntries(roster, _session.GetScore, _session.GetScoreSnapshot());
            var ranked = ResultRanking.Compute(entries);
            var localClientId = _session.NetworkManager != null
                ? _session.NetworkManager.LocalClientId
                : ulong.MaxValue;

            foreach (var rankedEntry in ranked)
            {
                _rankList.Add(CreateRow(rankedEntry, localClientId));
            }
        }

        private VisualElement CreateRow(RankedScore entry, ulong localClientId)
        {
            var (name, isConnected) = ResolvePlayer(entry.ClientId);

            var row = new VisualElement();
            row.AddToClassList(RowClass);
            if (!isConnected)
            {
                row.AddToClassList(RowDisconnectedClass);
            }

            var rankLabel = new Label($"{entry.Rank} 位");
            rankLabel.AddToClassList(RankClass);
            row.Add(rankLabel);

            // #206: 名前は参加者が決める文字列なので、リッチテキストとして解釈させない。
            var nameLabel = PlainText.CreateLabel(isConnected ? name : PlayerEntry.DisconnectedNameMask);
            nameLabel.AddToClassList(NameClass);
            row.Add(nameLabel);

            if (entry.ClientId == localClientId)
            {
                var youBadge = new Label(YouBadgeText);
                youBadge.AddToClassList(YouBadgeClass);
                row.Add(youBadge);
            }

            var scoreLabel = new Label($"{entry.Score} 点");
            scoreLabel.AddToClassList(ScoreClass);
            row.Add(scoreLabel);

            return row;
        }

        /// <summary>
        /// 名簿（<see cref="LobbyState"/>）を基準に、順位計算へ渡す (ClientId, Score) の一覧を組み立てる（H4）。
        /// 司会専任のホスト（<c>IsHost &amp;&amp; IsModerator</c>）は除外し、得点は <paramref name="getScore"/>
        /// （未登録なら 0 を返す想定）で解決する。名簿に無いが得点表にはいる ID（退出済み等の稀なケース）は
        /// 末尾に補う。Unity API に依存しないため EditMode から直接テストできる（<c>internal</c>）。
        /// </summary>
        /// <param name="roster">名簿のスナップショット。null なら名簿からは 1 件も追加しない。</param>
        /// <param name="getScore">クライアント ID から累計得点を引く関数（未登録なら 0 を返すこと）。</param>
        /// <param name="scoreSnapshot">得点表のスナップショット。null なら名簿からの分だけになる。</param>
        /// <returns>順位計算（<see cref="ResultRanking.Compute"/>）にそのまま渡せる一覧。</returns>
        internal static List<(ulong ClientId, int Score)> BuildRankingEntries(
            IReadOnlyList<PlayerEntry> roster,
            Func<ulong, int> getScore,
            IReadOnlyList<ScoreEntry> scoreSnapshot)
        {
            var entries = new List<(ulong ClientId, int Score)>();
            var seen = new HashSet<ulong>();

            if (roster != null)
            {
                foreach (var player in roster)
                {
                    // 司会専任のホストはプレイヤーではないので順位に含めない（LobbyView.RenderRoster と同じ扱い）。
                    if (player.IsHost && player.IsModerator)
                    {
                        continue;
                    }

                    entries.Add((player.ClientId, getScore != null ? getScore(player.ClientId) : 0));
                    seen.Add(player.ClientId);
                }
            }

            if (scoreSnapshot != null)
            {
                foreach (var scoreEntry in scoreSnapshot)
                {
                    if (seen.Add(scoreEntry.ClientId))
                    {
                        entries.Add((scoreEntry.ClientId, scoreEntry.Score));
                    }
                }
            }

            return entries;
        }

        /// <summary>クライアント ID から表示名と接続状態を解決する。名簿に無ければ仮の名前を返す。</summary>
        private (string Name, bool IsConnected) ResolvePlayer(ulong clientId)
        {
            if (_lobby != null && _lobby.IsSpawned)
            {
                foreach (var player in _lobby.GetPlayersSnapshot())
                {
                    if (player.ClientId == clientId)
                    {
                        return (player.GetDisplayName(), player.IsConnected); // #206: 表示用に整えた名前
                    }
                }
            }

            return (PlayerDisplayNameSanitizer.FallbackName(clientId), true);
        }

        private void OnRestartClicked()
        {
            if (!_isHost || _session == null || _session.ActiveSessionSettings == null)
            {
                // ボタンは非表示のはずだが、念のため単問経路（設定の引き継ぎ不可）では
                // 既定セッションを始めてしまわないよう防御する。
                return;
            }

            // 引数なしで呼ぶと、#27 以降は「ロビーで確定したルーム設定（RoomSettingsSync.Current）→
            // 前回の進行設定（ActiveSessionSettings）→ 既定値」の順で解決される
            // （docs/network.md §12.2）。この経路はロビーへ戻らない＝ルーム設定がロックされたままなので、
            // Current はゲーム開始時に確定した値のまま＝「もう一度（同じ設定で）」になる。
            // StartSession はホスト自身の Phase.Value を同期的に Reading へ書き換えるため、
            // ここで OnValueChanged（HandlePhaseChanged）が即座に発火して Game へ遷移し、
            // ResultView.OnHide() が _router を null 化してしまう。Game への遷移は
            // HandlePhaseChanged 一本に寄せ、ここでは重ねて ShowView を呼ばない
            // （二重に呼ぶと OnHide 済みの _router が null で NullReferenceException になる）。
            if (!_session.StartSession())
            {
                ShowStatus(RestartFailedMessage);
            }
        }

        private void OnReturnToLobbyClicked()
        {
            if (!_isHost || _session == null)
            {
                return;
            }

            if (!_session.ReturnToLobby())
            {
                ShowStatus(ReturnFailedMessage);
                return;
            }

            // 実際の画面遷移は ReturnedToLobby イベント（HandleReturnedToLobby）で行う。
            // ホスト自身にも同じ合図が届くため、他クライアントと同じ経路で Lobby へ切り替わる。
        }

        private void HandleReturnedToLobby() => _router?.ShowView(ViewNames.Lobby);

        private void HandleScoreTableChanged() => RenderResults();

        private void HandleRosterChanged() => RenderResults();

        /// <summary>
        /// ホストの「もう一度（同じ設定で）」で次のセッションが出題（Reading）まで進んだら、
        /// クライアント側もそれに合わせて Game へ追従する（M2）。
        /// </summary>
        /// <remarks>
        /// <c>QuizPhase.Reading</c> になったときだけを見る（<c>!= Finished</c> のような広い条件にすると、
        /// 「ロビーへ戻る」（<see cref="GameSession.ReturnToLobby"/> が内部で Phase を一旦 Lobby に戻す）と
        /// 競合し、<see cref="HandleReturnedToLobby"/> より先に Game へ遷移してしまってから
        /// ロビーへ戻るという意図しない二重遷移が起きるため）。
        /// </remarks>
        private void HandlePhaseChanged(QuizPhase previous, QuizPhase current)
        {
            if (current == QuizPhase.Reading)
            {
                _router?.ShowView(ViewNames.Game);
            }
        }

        /// <summary>ホストから切断されたら Title へ戻る（<c>GameView</c> と同じ作法、M3）。</summary>
        /// <param name="reason">日本語に対応づけた切断理由（<c>NetworkService.DisconnectedFromHost</c>、#208）。</param>
        private void HandleDisconnectedFromHost(string reason)
        {
            Debug.LogWarning($"[ResultView] ホストから切断されたため Title へ戻ります: {reason}");

            _router?.ShowView(ViewNames.Title);
        }

        private void ShowStatus(string message)
        {
            if (_statusLabel == null)
            {
                return;
            }

            _statusLabel.text = message ?? string.Empty;
            _statusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// クライアント側の <see cref="GameSession"/> を探す（<see cref="Views.Game.GameView"/> と同じ、
        /// <see cref="NetworkService.FindActiveGameSession"/> 経由の作法、#14）。
        /// </summary>
        private static GameSession ResolveSession(NetworkBootstrap bootstrap) =>
            bootstrap?.Service?.FindActiveGameSession();
    }
}
