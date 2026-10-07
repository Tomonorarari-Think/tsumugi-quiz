using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Participants;
using TsumugiQuiz.Network;
using TsumugiQuiz.Room;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、左列の参加者パネル（#194）をまとめた部分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 読む同期値はすべてサーバー権威で、クライアントは読むだけ: 名簿（<c>LobbyState</c>）、得点表
    /// （<c>GameSession.GetScoreSnapshot</c>）、現在の問題の進行状態（<c>GameSession.GetQuestionProgress</c>）、
    /// ロック保持者・フェーズ・問題インデックス（<c>NetworkVariable</c>）、ルーム設定（<c>display.showScores</c>）。
    /// </para>
    /// <para>
    /// これらは別々の同期値・別々の <c>NetworkObject</c> なので届く順番が揃わない。変更の通知では印を付けるだけにして、
    /// 描き直しは <see cref="GameView"/> の Tick（100ms 間隔）で 1 回にまとめる（LobbyView の名簿と同じ作法）。
    /// </para>
    /// </remarks>
    public sealed partial class GameView
    {
        private ParticipantPanel _participantPanel;
        private bool _participantPanelDirty;
        private LobbyState _participantLobby;
        private RoomSettingsSync _participantSettingsSync;
        private GameSession _participantSession;

        /// <summary>参加者パネル（PlayMode テスト用）。</summary>
        internal ParticipantPanel ParticipantPanelForTests => _participantPanel;

        /// <summary>要素を探してパネルを用意する（<see cref="OnShow"/> から）。</summary>
        private void InitializeParticipantPanel(VisualElement root)
        {
            _participantPanel = ParticipantPanel.Create(root);
            if (_participantPanel == null)
            {
                // PR #201 レビュー L-5: パネルなしでも画面は動かすが、UXML の食い違いに気づけるよう 1 回だけ残す。
                Debug.LogWarning(
                    "[GameView] 参加者パネルの要素（participant-summary-label / participant-list）が見つからないため、"
                    + "参加者パネルを表示しません。game-view.uxml を確認してください。");
                return;
            }

            _participantPanel.Render(null);
            _participantPanelDirty = true;
        }

        /// <summary>セッションが手に入ったら、パネルの入力になる同期値を購読する（<see cref="TryAcquireSession"/> から）。</summary>
        private void BindParticipantPanelToSession()
        {
            UnbindParticipantPanel();
            if (_session == null)
            {
                return;
            }

            _participantSession = _session;
            _participantSession.QuestionProgressChanged += MarkParticipantPanelDirty;
            _participantSession.ScoreTableChanged += MarkParticipantPanelDirty;
            _participantSession.QuestionShown += HandleParticipantQuestionShown;
            _participantSession.Phase.OnValueChanged += HandleParticipantPhaseChanged;
            _participantSession.LockedClientId.OnValueChanged += HandleParticipantLockedChanged;
            _participantSession.QuestionIndex.OnValueChanged += HandleParticipantQuestionIndexChanged;

            _participantSettingsSync = _participantSession.SettingsSync;
            if (_participantSettingsSync != null)
            {
                _participantSettingsSync.SettingsChanged += HandleParticipantSettingsChanged;
            }

            TryBindParticipantLobby();
            _participantPanelDirty = true;
        }

        /// <summary>
        /// 名簿（<c>LobbyState</c>）をまだ購読していなければ、取り直して購読する（PR #201 レビュー L-3）。
        /// セッションより名簿のスポーン同期が遅れた場合でも、後から名簿の変化を拾えるようにする。
        /// </summary>
        private void TryBindParticipantLobby()
        {
            if (_participantLobby != null)
            {
                return;
            }

            _participantLobby = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Lobby : null;
            if (_participantLobby != null)
            {
                _participantLobby.RosterChanged += MarkParticipantPanelDirty;
                _participantPanelDirty = true;
            }
        }

        /// <summary>購読を外してパネルを手放す（<see cref="OnHide"/> から）。</summary>
        private void TeardownParticipantPanel()
        {
            UnbindParticipantPanel();
            _participantPanel = null;
            _participantPanelDirty = false;
        }

        private void UnbindParticipantPanel()
        {
            if (_participantSession != null)
            {
                _participantSession.QuestionProgressChanged -= MarkParticipantPanelDirty;
                _participantSession.ScoreTableChanged -= MarkParticipantPanelDirty;
                _participantSession.QuestionShown -= HandleParticipantQuestionShown;
                _participantSession.Phase.OnValueChanged -= HandleParticipantPhaseChanged;
                _participantSession.LockedClientId.OnValueChanged -= HandleParticipantLockedChanged;
                _participantSession.QuestionIndex.OnValueChanged -= HandleParticipantQuestionIndexChanged;
                _participantSession = null;
            }

            if (_participantSettingsSync != null)
            {
                _participantSettingsSync.SettingsChanged -= HandleParticipantSettingsChanged;
                _participantSettingsSync = null;
            }

            if (_participantLobby != null)
            {
                _participantLobby.RosterChanged -= MarkParticipantPanelDirty;
                _participantLobby = null;
            }
        }

        private void MarkParticipantPanelDirty() => _participantPanelDirty = true;

        private void HandleParticipantQuestionShown(int index, QuestionDto question, QuestionShownSource source) =>
            _participantPanelDirty = true;

        private void HandleParticipantPhaseChanged(QuizPhase previous, QuizPhase next) => _participantPanelDirty = true;

        private void HandleParticipantLockedChanged(ulong previous, ulong next) => _participantPanelDirty = true;

        private void HandleParticipantQuestionIndexChanged(int previous, int next) => _participantPanelDirty = true;

        private void HandleParticipantSettingsChanged(RoomSettings settings) => _participantPanelDirty = true;

        /// <summary>変更があれば 1 回だけ描き直す（<see cref="Tick"/> から）。</summary>
        private void FlushParticipantPanel()
        {
            if (_participantPanel == null || _session == null)
            {
                return;
            }

            if (_participantSession != null)
            {
                TryBindParticipantLobby();
            }

            if (!_participantPanelDirty)
            {
                return;
            }

            _participantPanelDirty = false;
            _participantPanel.Render(ParticipantPanelModel.Build(BuildParticipantPanelInput()));
        }

        /// <summary>同期値からパネルの入力を組み立てる。</summary>
        private ParticipantPanelInput BuildParticipantPanelInput()
        {
            var settings = _session.SettingsSync != null ? _session.SettingsSync.Current : null;
            var showScores = ParticipantPanelPresenter.ShouldShowScores(
                settings?.ShowScores ?? RoomSettings.DefaultShowScores, _isModerator);

            // PR #201 レビュー L-4: 手元の問題（_currentQuestion）が現在の問題と一致するときだけ形式を信じる。
            // 次の問題の提示が届く前は、前の問題の形式で現在の問題の状態を解釈しない。
            var isChoiceQuestion = ParticipantPanelPresenter.IsCurrentQuestionChoice(
                _currentQuestion?.Type, _displayedQuestionIndex, _session.QuestionIndex.Value);

            return new ParticipantPanelInput(
                BuildParticipantRoster(),
                BuildScoreTable(),
                _session.GetQuestionProgress(),
                _session.QuestionIndex.Value,
                _session.Phase.Value,
                isChoiceQuestion,
                _session.LockedClientId.Value,
                LocalClientIdOrNull,
                showScores);
        }

        /// <summary>名簿（名簿順）を Core の入力へ写す。表示名の連番（#85）は GameView の回答者表示と同じ規則。</summary>
        private static List<ParticipantRosterEntry> BuildParticipantRoster()
        {
            var roster = new List<ParticipantRosterEntry>();
            var lobby = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Lobby : null;
            if (lobby == null)
            {
                return roster;
            }

            var snapshot = lobby.GetPlayersSnapshot();
            var displayNames = PlayerEntryDisplayNames.Resolve(snapshot);
            for (var i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                var label = displayNames.GetLabel(i);
                roster.Add(new ParticipantRosterEntry(
                    entry.ClientId,
                    string.IsNullOrEmpty(label) ? entry.GetDisplayName() : label,
                    entry.IsConnected,
                    entry.IsHost && entry.IsModerator));
            }

            return roster;
        }

        private Dictionary<ulong, int> BuildScoreTable()
        {
            var scores = new Dictionary<ulong, int>();
            foreach (var entry in _session.GetScoreSnapshot())
            {
                scores[entry.ClientId] = entry.Score;
            }

            return scores;
        }
    }
}
