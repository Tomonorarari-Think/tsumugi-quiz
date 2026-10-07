using System.Collections.Generic;
using System.Collections.ObjectModel;
using TsumugiQuiz.Questions;

namespace TsumugiQuiz.UI.Views.Lobby
{
    /// <summary>
    /// <see cref="GameStartPlanner"/> が組み立てた「ゲーム開始」の下準備の結果（issue #95）。
    /// 不変で、<see cref="CanStart"/> が true のときだけ <see cref="PoolSource"/> / <see cref="Sets"/> が
    /// <c>GameSession.Configure</c> / <c>GameSession.SetQuestionSets</c> へ渡せる値になる。
    /// </summary>
    public sealed class GameStartPlan
    {
        private static readonly ReadOnlyCollection<QuestionSet> EmptySets =
            new ReadOnlyCollection<QuestionSet>(new List<QuestionSet>());

        private static readonly ReadOnlyCollection<string> EmptyWarnings =
            new ReadOnlyCollection<string>(new List<string>());

        private readonly ReadOnlyCollection<QuestionSet> _sets;
        private readonly ReadOnlyCollection<string> _warnings;

        private GameStartPlan(
            GameStartBlocker blocker,
            string failureMessage,
            ReadOnlyCollection<QuestionSet> sets,
            IQuestionSource poolSource,
            int poolCount,
            int candidateCount,
            ReadOnlyCollection<string> warnings)
        {
            Blocker = blocker;
            FailureMessage = failureMessage;
            _sets = sets ?? EmptySets;
            PoolSource = poolSource;
            PoolCount = poolCount;
            CandidateCount = candidateCount;
            _warnings = warnings ?? EmptyWarnings;
        }

        /// <summary>出題を開始できるか。</summary>
        public bool CanStart => Blocker == GameStartBlocker.None;

        /// <summary>開始できない理由の種別。</summary>
        public GameStartBlocker Blocker { get; }

        /// <summary>
        /// 開始できない理由のユーザー向け文言。<see cref="CanStart"/> が true のときは null。
        /// </summary>
        public string FailureMessage { get; }

        /// <summary>
        /// <c>questions.setIds</c> を適用するための問題セット
        /// （<c>GameSession.SetQuestionSets</c> に渡す）。開始できないときは空。
        /// </summary>
        public IReadOnlyList<QuestionSet> Sets => _sets;

        /// <summary>
        /// 候補プールの供給元（<c>GameSession.Configure</c> に渡す）。開始できないときは null。
        /// </summary>
        public IQuestionSource PoolSource { get; }

        /// <summary>読み込めた問題の総数（絞り込み前）。</summary>
        public int PoolCount { get; }

        /// <summary>ルーム設定の絞り込みを通過した問題数（<c>questions.count</c> で切り詰める前）。</summary>
        public int CandidateCount { get; }

        /// <summary>
        /// 開始は妨げないが記録しておきたい注意点（スキップされたセット・フォルダ単位のエラー・
        /// 存在しない <c>setId</c>・出題形式が無い問題）。
        /// </summary>
        /// <remarks>
        /// 現状の呼び出し側（<c>LobbyView.LogPlanWarnings</c>）は<b>ログに 1 本の警告として要約するだけ</b>で、
        /// 画面には出さない（読み込み状況の表示はホスト設定画面（#29）の役割）。
        /// </remarks>
        public IReadOnlyList<string> Warnings => _warnings;

        /// <summary>開始できる計画を作る。</summary>
        internal static GameStartPlan Ready(
            IReadOnlyList<QuestionSet> sets,
            IQuestionSource poolSource,
            int poolCount,
            int candidateCount,
            List<string> warnings) =>
            new GameStartPlan(
                GameStartBlocker.None,
                null,
                ToReadOnly(sets),
                poolSource,
                poolCount,
                candidateCount,
                new ReadOnlyCollection<string>(warnings ?? new List<string>()));

        /// <summary>開始できない計画を作る。</summary>
        internal static GameStartPlan Blocked(
            GameStartBlocker blocker, string failureMessage, int poolCount, List<string> warnings) =>
            new GameStartPlan(
                blocker,
                failureMessage,
                EmptySets,
                null,
                poolCount,
                0,
                new ReadOnlyCollection<string>(warnings ?? new List<string>()));

        private static ReadOnlyCollection<QuestionSet> ToReadOnly(IReadOnlyList<QuestionSet> sets)
        {
            var collected = new List<QuestionSet>();
            if (sets != null)
            {
                for (var i = 0; i < sets.Count; i++)
                {
                    if (sets[i] != null)
                    {
                        collected.Add(sets[i]);
                    }
                }
            }

            return new ReadOnlyCollection<QuestionSet>(collected);
        }
    }
}
