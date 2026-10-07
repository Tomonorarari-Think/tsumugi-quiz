using System.Collections.Generic;
using TsumugiQuiz.Core.Participants;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> のうち、参加者パネル（#194）へ配る「現在の問題の進行状態」
    /// （押下順・回答順・回答権・選択式の回答済み）をまとめた部分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 押下順は<b>集計窓の中で受理した押下だけ</b>に付け、勝者が確定した時点でまとめて確定する
    /// （統括判断 #194: 案 X。規則は変えない）。受付を開き直したら消す（新しい受付は別の競争のため）。
    /// 回答順（回答権を得た順番）は問題の間ずっと残す。
    /// </para>
    /// <para>
    /// <b>選択式の選んだ番号と正誤は、判定が確定するまで載せない</b>。判定前に分かるのは「選択を送ったか」だけ。
    /// 自由入力の正解（<see cref="ParticipantProgressFlags.Correct"/>）も、結果（<see cref="QuizPhase.Result"/>）に
    /// 入ってから立てる（回答を受理した直後の <see cref="QuizPhase.Judging"/> で先に漏らさないため）。
    /// </para>
    /// </remarks>
    public sealed partial class QuizStateMachine
    {
        /// <summary>直近の早押し（集計窓）の順位。受付を開き直す・次の問題で空にする。</summary>
        private readonly List<BuzzRankEntry> _buzzRanking = new List<BuzzRankEntry>();

        /// <summary>この問題で回答権を得た順（重複なし）。次の問題で空にする。</summary>
        private readonly List<ulong> _answerOrder = new List<ulong>();

        /// <summary>
        /// 現在の問題の進行状態を組み立てる（サーバーのみ意味を持つ）。問題が出ていなければ
        /// <see cref="QuestionProgress.Empty"/>。呼ぶたびに新しいインスタンスを返す。
        /// </summary>
        /// <returns>進行状態。</returns>
        public QuestionProgress BuildProgress()
        {
            if (QuestionIndex < 0)
            {
                return QuestionProgress.Empty;
            }

            var rows = new Dictionary<ulong, ProgressRow>();

            for (var i = 0; i < _buzzRanking.Count && i < ParticipantProgress.MaxRank; i++)
            {
                var entry = _buzzRanking[i];
                var row = GetRow(rows, entry.ClientId);
                row.BuzzRank = i + 1;
                if (entry.TiedWithWinner)
                {
                    row.Flags |= ParticipantProgressFlags.TiedWithWinner;
                }
            }

            for (var i = 0; i < _answerOrder.Count && i < ParticipantProgress.MaxRank; i++)
            {
                GetRow(rows, _answerOrder[i]).AnswerOrder = i + 1;
            }

            AddFlag(rows, _wrongAnswerers, ParticipantProgressFlags.WrongAnswered);
            AddFlag(rows, Penalties.SuspendedFor(QuestionIndex), ParticipantProgressFlags.SuspendedSkipNext);
            AddFlag(rows, _choiceSelections.Keys, ParticipantProgressFlags.ChoiceSubmitted);
            AddJudgedFlags(rows);

            var entries = new List<ParticipantProgress>(rows.Count);
            foreach (var pair in rows)
            {
                entries.Add(new ParticipantProgress(pair.Key, pair.Value.BuzzRank, pair.Value.AnswerOrder, pair.Value.Flags));
            }

            return QuestionProgress.Create(QuestionIndex, entries);
        }

        /// <summary>
        /// 勝者の確定を記録する（<see cref="QuizEvent.BuzzResolved"/> の直前に呼ぶ）。
        /// </summary>
        private void RecordBuzzResolution(BuzzResolution resolution)
        {
            _buzzRanking.Clear();
            _buzzRanking.AddRange(resolution.Ranking);

            if (!_answerOrder.Contains(resolution.WinnerClientId))
            {
                _answerOrder.Add(resolution.WinnerClientId);
            }
        }

        /// <summary>直近の早押しの順位を消す（受付を開き直すとき）。</summary>
        private void ClearBuzzRanking() => _buzzRanking.Clear();

        /// <summary>押下順と回答順を両方消す（次の問題・新しいセッション）。</summary>
        private void ClearProgress()
        {
            _buzzRanking.Clear();
            _answerOrder.Clear();
        }

        /// <summary>押下順・回答順のクライアント ID を付け替える（#84 の再接続）。</summary>
        /// <returns>付け替えたものがあれば true。</returns>
        private bool TransferProgress(ulong fromClientId, ulong toClientId)
        {
            var changed = false;
            for (var i = 0; i < _buzzRanking.Count; i++)
            {
                if (_buzzRanking[i].ClientId == fromClientId)
                {
                    _buzzRanking[i] = _buzzRanking[i].WithClientId(toClientId);
                    changed = true;
                }
            }

            var index = _answerOrder.IndexOf(fromClientId);
            if (index >= 0)
            {
                if (_answerOrder.Contains(toClientId))
                {
                    _answerOrder.RemoveAt(index);
                }
                else
                {
                    _answerOrder[index] = toClientId;
                }

                changed = true;
            }

            return changed;
        }

        /// <summary>席が無くなったクライアントを押下順・回答順から取り除く（#84）。</summary>
        private void ForgetProgress(ulong clientId)
        {
            _buzzRanking.RemoveAll(entry => entry.ClientId == clientId);
            _answerOrder.Remove(clientId);
        }

        /// <summary>判定が確定した後（結果・全問終了）だけ、正誤のビットを立てる。</summary>
        private void AddJudgedFlags(Dictionary<ulong, ProgressRow> rows)
        {
            if (Phase != QuizPhase.Result && Phase != QuizPhase.Finished)
            {
                return;
            }

            if (_isChoiceQuestion)
            {
                for (var i = 0; i < _lastChoiceResults.Count; i++)
                {
                    var result = _lastChoiceResults[i];
                    GetRow(rows, result.ClientId).Flags |= result.Judgement == QuizJudgement.Correct
                        ? ParticipantProgressFlags.Correct
                        : ParticipantProgressFlags.WrongAnswered;
                }

                return;
            }

            if (LastJudgement == QuizJudgement.Correct && LockedClientId != NoClientId)
            {
                GetRow(rows, LockedClientId).Flags |= ParticipantProgressFlags.Correct;
            }
        }

        private static void AddFlag(
            Dictionary<ulong, ProgressRow> rows, IEnumerable<ulong> clientIds, ParticipantProgressFlags flag)
        {
            foreach (var clientId in clientIds)
            {
                GetRow(rows, clientId).Flags |= flag;
            }
        }

        private static ProgressRow GetRow(Dictionary<ulong, ProgressRow> rows, ulong clientId)
        {
            if (!rows.TryGetValue(clientId, out var row))
            {
                row = new ProgressRow();
                rows[clientId] = row;
            }

            return row;
        }

        /// <summary><see cref="BuildProgress"/> の組み立て途中の 1 行（ローカル専用の可変な入れ物）。</summary>
        private sealed class ProgressRow
        {
            public int BuzzRank;
            public int AnswerOrder;
            public ParticipantProgressFlags Flags;
        }
    }
}
