using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TsumugiQuiz.Core.Participants
{
    /// <summary>
    /// 現在の問題での参加者全員の進行状態（押下順・回答順・回答権・選択式の回答済み、#194）。不変。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 状態を持つ参加者だけを載せる。載っていない参加者は「まだ押せる」「未回答」を意味する。
    /// 並びはクライアント ID の昇順（同期した値を比べるとき・テストで並びがぶれないように）。
    /// </para>
    /// <para>
    /// サーバーは <see cref="QuizStateMachine.BuildProgress"/> で作り、同期用のペイロードへ写して配る。
    /// クライアントは受信したペイロードから <see cref="Create"/> で組み立て直す（重複・空の行はここで落とす）。
    /// </para>
    /// </remarks>
    public sealed class QuestionProgress : IEquatable<QuestionProgress>
    {
        /// <summary>問題が出ていないことを表す問題インデックス。</summary>
        public const int NoQuestion = -1;

        private readonly ReadOnlyCollection<ParticipantProgress> _entries;

        private QuestionProgress(int questionIndex, List<ParticipantProgress> entries)
        {
            QuestionIndex = questionIndex;
            _entries = entries.AsReadOnly();
        }

        /// <summary>問題が出ていない状態（行なし）。</summary>
        public static QuestionProgress Empty { get; } =
            new QuestionProgress(NoQuestion, new List<ParticipantProgress>());

        /// <summary>対象の問題インデックス。問題が出ていなければ <see cref="NoQuestion"/>。</summary>
        public int QuestionIndex { get; }

        /// <summary>状態を持つ参加者（クライアント ID の昇順）。</summary>
        public IReadOnlyList<ParticipantProgress> Entries => _entries;

        /// <summary>
        /// 行の集まりから組み立てる。状態を何も持たない行は捨て、同じクライアント ID は後の行を採用する。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。負の値は <see cref="NoQuestion"/> として扱う。</param>
        /// <param name="entries">行（null 可）。</param>
        /// <returns>新しいインスタンス。</returns>
        public static QuestionProgress Create(int questionIndex, IEnumerable<ParticipantProgress> entries)
        {
            var byClientId = new Dictionary<ulong, ParticipantProgress>();
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry.IsEmpty)
                    {
                        byClientId.Remove(entry.ClientId);
                        continue;
                    }

                    byClientId[entry.ClientId] = entry;
                }
            }

            var list = new List<ParticipantProgress>(byClientId.Values);
            list.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));
            return new QuestionProgress(questionIndex < 0 ? NoQuestion : questionIndex, list);
        }

        /// <summary>指定したクライアントの行を探す。</summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <param name="progress">見つかった行。無ければ状態なしの行。</param>
        /// <returns>行があれば true。</returns>
        public bool TryGet(ulong clientId, out ParticipantProgress progress)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].ClientId == clientId)
                {
                    progress = _entries[i];
                    return true;
                }
            }

            progress = new ParticipantProgress(clientId, ParticipantProgress.NoRank, ParticipantProgress.NoRank, ParticipantProgressFlags.None);
            return false;
        }

        /// <summary>
        /// 指定したクライアントがこの問題で早押しできない（誤答済み・次問休み）か。
        /// サーバーの <see cref="QuizStateMachine.IsPenalized"/> と同じ定義。
        /// </summary>
        /// <param name="clientId">クライアント ID。</param>
        /// <returns>押せないなら true。</returns>
        public bool IsExcludedFromBuzzing(ulong clientId) =>
            TryGet(clientId, out var progress)
            && (progress.Flags.Has(ParticipantProgressFlags.WrongAnswered)
                || progress.Flags.Has(ParticipantProgressFlags.SuspendedSkipNext));

        /// <inheritdoc />
        public bool Equals(QuestionProgress other)
        {
            if (other is null || QuestionIndex != other.QuestionIndex || _entries.Count != other._entries.Count)
            {
                return false;
            }

            for (var i = 0; i < _entries.Count; i++)
            {
                if (!_entries[i].Equals(other._entries[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => Equals(obj as QuestionProgress);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(QuestionIndex);
            for (var i = 0; i < _entries.Count; i++)
            {
                hash.Add(_entries[i]);
            }

            return hash.ToHashCode();
        }
    }
}
