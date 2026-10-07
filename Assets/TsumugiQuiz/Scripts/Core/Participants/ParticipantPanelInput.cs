using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core.Participants
{
    /// <summary><see cref="ParticipantPanelModel.Build"/> の入力（#194）。不変。</summary>
    public sealed class ParticipantPanelInput
    {
        private readonly IReadOnlyDictionary<ulong, int> _scores;

        /// <summary>値を指定して生成する。</summary>
        /// <param name="roster">名簿（名簿順）。null は空。</param>
        /// <param name="scores">クライアント ID → 累計得点。載っていない人は 0 点。null は空。</param>
        /// <param name="progress">現在の問題の進行状態。null は空。</param>
        /// <param name="questionIndex">現在の問題インデックス（未出題は -1）。</param>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="isChoiceQuestion">現在の問題が選択式か。</param>
        /// <param name="lockedClientId">回答権を持っているクライアント（居なければ <see cref="QuizStateMachine.NoClientId"/>）。</param>
        /// <param name="localClientId">自分のクライアント ID（不明なら null）。</param>
        /// <param name="showScores">得点を表示するか（司会なら設定に関わらず true を渡す）。</param>
        public ParticipantPanelInput(
            IReadOnlyList<ParticipantRosterEntry> roster,
            IReadOnlyDictionary<ulong, int> scores,
            QuestionProgress progress,
            int questionIndex,
            QuizPhase phase,
            bool isChoiceQuestion,
            ulong lockedClientId,
            ulong? localClientId,
            bool showScores)
        {
            // 呼び出し側が後から中身を書き換えても影響を受けないよう複製して持つ（不変、PR #201 レビュー L-10）。
            Roster = roster == null
                ? Array.Empty<ParticipantRosterEntry>()
                : new List<ParticipantRosterEntry>(roster).AsReadOnly();
            _scores = CopyScores(scores);
            Progress = progress ?? QuestionProgress.Empty;
            QuestionIndex = questionIndex;
            Phase = phase;
            IsChoiceQuestion = isChoiceQuestion;
            LockedClientId = lockedClientId;
            LocalClientId = localClientId;
            ShowScores = showScores;
        }

        /// <summary>名簿（名簿順）。</summary>
        public IReadOnlyList<ParticipantRosterEntry> Roster { get; }

        /// <summary>現在の問題の進行状態。</summary>
        public QuestionProgress Progress { get; }

        /// <summary>現在の問題インデックス。</summary>
        public int QuestionIndex { get; }

        /// <summary>現在のフェーズ。</summary>
        public QuizPhase Phase { get; }

        /// <summary>現在の問題が選択式か。</summary>
        public bool IsChoiceQuestion { get; }

        /// <summary>回答権を持っているクライアント。</summary>
        public ulong LockedClientId { get; }

        /// <summary>自分のクライアント ID。</summary>
        public ulong? LocalClientId { get; }

        /// <summary>得点を表示するか。</summary>
        public bool ShowScores { get; }

        /// <summary>累計得点を引く（載っていなければ 0）。</summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>累計得点。</returns>
        public int GetScore(ulong clientId) => _scores.TryGetValue(clientId, out var score) ? score : 0;

        private static IReadOnlyDictionary<ulong, int> CopyScores(IReadOnlyDictionary<ulong, int> scores)
        {
            var copy = new Dictionary<ulong, int>();
            if (scores != null)
            {
                foreach (var pair in scores)
                {
                    copy[pair.Key] = pair.Value;
                }
            }

            return copy;
        }
    }
}
