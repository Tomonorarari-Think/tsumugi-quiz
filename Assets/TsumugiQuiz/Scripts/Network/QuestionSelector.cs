using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// ルーム設定（<see cref="QuestionSelectionSettings"/>）に従って出題列を確定させるロジック（#19、
    /// docs/room-settings.md §1「問題選択」・§5）。除外の警告ログ以外は Unity API に依存しない。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 手順は「候補プールの平坦化 → <c>setIds</c> → <c>typeFilter</c> → <c>imageOnly</c> →
    /// <c>tagFilter</c> → <c>shuffleOrder</c>（シード固定）→ <c>count</c> で切り詰め」の順。
    /// <c>count</c> が候補数を超える場合は候補数に丸める（docs/room-settings.md §5）。
    /// </para>
    /// <para>
    /// 選択式（<see cref="QuestionType.Choice"/>）の進行は #17 で実装したため、<c>typeFilter</c> が
    /// 許していれば freeText と同様に候補へ含める（以前は #17 まで一律で除外していた）。
    /// </para>
    /// <para>
    /// シャッフルは <see cref="SeededRandom"/> による Fisher-Yates なので、
    /// <b>同じシード・同じ候補なら必ず同じ並び</b>になる（再現・検証のため）。
    /// 出題順はサーバー内にのみ置き、クライアントへは配らない（docs/network.md §1.2）。
    /// </para>
    /// <para>
    /// 本クラスを <c>TsumugiQuiz.Core</c> ではなく <c>TsumugiQuiz.Network</c> に置いているのは、
    /// <see cref="Question"/>（Questions）と <see cref="QuestionSelectionSettings"/>（Room）の
    /// 両方を参照する必要があり、両者を同時に参照できる層が Network だけであるため
    /// （docs/architecture.md §3 の依存方向。issue #19 の作業内容とも一致）。
    /// </para>
    /// </remarks>
    public static class QuestionSelector
    {
        /// <summary>
        /// 問題セットの一覧から出題列を確定させる（<c>questions.setIds</c> が効く通常の経路）。
        /// </summary>
        /// <param name="sets">読み込み済みの問題セット（<c>QuestionLibrary</c> / <c>QuestionRepository</c> 由来）。</param>
        /// <param name="settings">問題選択の設定。null なら既定値。</param>
        /// <param name="seed">シャッフルのシード。同じシードなら同じ並びになる。</param>
        /// <param name="logExclusions">
        /// 除外の警告（出題形式が無い問題）をログに出すか。false にすると同じ候補プールに対して
        /// 2 回選択する場合（ホスト UI が開始前に候補数だけを確かめる「下見」、#95 の
        /// <c>GameStartPlanner</c>）に同じ警告が二重に出るのを避けられる。
        /// </param>
        /// <returns>出題列。候補が無ければ <see cref="QuestionSelection.Empty"/> と同等の空の結果。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="sets"/> が null のとき。</exception>
        public static QuestionSelection SelectFromSets(
            IReadOnlyList<QuestionSet> sets, QuestionSelectionSettings settings, int seed, bool logExclusions = true)
        {
            if (sets == null)
            {
                throw new ArgumentNullException(nameof(sets));
            }

            var effective = settings ?? QuestionSelectionSettings.Default;
            var pool = new List<Question>();

            // 指定された setId のうち、実在したものを控えて「存在しない setId」を求める（§5）。
            var matchedSetIds = new List<string>();

            for (var i = 0; i < sets.Count; i++)
            {
                var set = sets[i];
                if (set?.Questions == null)
                {
                    continue;
                }

                if (!MatchesSetIds(set.SetId, effective.SetIds))
                {
                    continue;
                }

                if (set.SetId != null && !matchedSetIds.Contains(set.SetId))
                {
                    matchedSetIds.Add(set.SetId);
                }

                for (var j = 0; j < set.Questions.Count; j++)
                {
                    if (set.Questions[j] != null)
                    {
                        pool.Add(set.Questions[j]);
                    }
                }
            }

            var unknownSetIds = new List<string>();
            for (var i = 0; i < effective.SetIds.Count; i++)
            {
                if (!matchedSetIds.Contains(effective.SetIds[i]))
                {
                    unknownSetIds.Add(effective.SetIds[i]);
                }
            }

            return SelectFromPool(pool, effective, new SeededRandom(seed), unknownSetIds, logExclusions);
        }

        /// <summary>
        /// 平坦な問題リストから出題列を確定させる。
        /// </summary>
        /// <remarks>
        /// セット情報を持たないため <c>questions.setIds</c> は適用されない
        /// （<c>setIds</c> で絞りたい場合は <see cref="SelectFromSets"/> を使う）。
        /// </remarks>
        /// <param name="questions">候補プール（null 要素は無視する）。</param>
        /// <param name="settings">問題選択の設定。null なら既定値。</param>
        /// <param name="seed">シャッフルのシード。</param>
        /// <returns>出題列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="questions"/> が null のとき。</exception>
        public static QuestionSelection Select(
            IReadOnlyList<Question> questions, QuestionSelectionSettings settings, int seed) =>
            Select(questions, settings, new SeededRandom(seed));

        /// <summary>
        /// 乱数源を指定して平坦な問題リストから出題列を確定させる（テスト・差し替え用）。
        /// </summary>
        /// <param name="questions">候補プール（null 要素は無視する）。</param>
        /// <param name="settings">問題選択の設定。null なら既定値。</param>
        /// <param name="random">シャッフルに使う乱数源。null なら <see cref="SeededRandom"/>（シード 0）。</param>
        /// <returns>出題列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="questions"/> が null のとき。</exception>
        public static QuestionSelection Select(
            IReadOnlyList<Question> questions, QuestionSelectionSettings settings, IRandom random)
        {
            if (questions == null)
            {
                throw new ArgumentNullException(nameof(questions));
            }

            var pool = new List<Question>();
            for (var i = 0; i < questions.Count; i++)
            {
                if (questions[i] != null)
                {
                    pool.Add(questions[i]);
                }
            }

            return SelectFromPool(
                pool, settings ?? QuestionSelectionSettings.Default, random ?? new SeededRandom(0), null, true);
        }

        /// <summary>フィルタ → シャッフル → 出題数の切り詰めを順に適用する。</summary>
        private static QuestionSelection SelectFromPool(
            List<Question> pool,
            QuestionSelectionSettings settings,
            IRandom random,
            List<string> unknownSetIds,
            bool logExclusions)
        {
            var candidateIndices = new List<int>();
            var missingTypeCount = 0;
            for (var i = 0; i < pool.Count; i++)
            {
                if (Matches(pool[i], settings))
                {
                    candidateIndices.Add(i);
                    continue;
                }

                if (!pool[i].Type.HasValue)
                {
                    missingTypeCount++;
                }
            }

            if (missingTypeCount > 0 && logExclusions)
            {
                // QuestionSetValidator（読み込み時）が弾いているはずなので、ここに来る＝検証を通っていない。
                Debug.LogWarning(
                    $"[QuestionSelector] 出題形式（type）が設定されていない問題 {missingTypeCount} 件を"
                    + "出題候補から除外しました（読み込み時の検証を通っていない問題が混ざっています）。");
            }

            var candidateCount = candidateIndices.Count;
            if (candidateCount == 0)
            {
                // 条件に合う問題が 1 件も無い（呼び出し側が「出題できない」ことをユーザーに伝える）。
                return new QuestionSelection(
                    new List<Question>(),
                    new List<int>(),
                    pool.Count,
                    0,
                    missingTypeCount: missingTypeCount,
                    unknownSetIds: unknownSetIds);
            }

            if (settings.ShuffleOrder)
            {
                Shuffle(candidateIndices, random);
            }

            var take = settings.IsAllQuestions ? candidateCount : Math.Min(settings.Count, candidateCount);

            var orderedIndices = new List<int>(take);
            var orderedQuestions = new List<Question>(take);
            for (var i = 0; i < take; i++)
            {
                orderedIndices.Add(candidateIndices[i]);
                orderedQuestions.Add(pool[candidateIndices[i]]);
            }

            return new QuestionSelection(
                orderedQuestions,
                orderedIndices,
                pool.Count,
                candidateCount,
                missingTypeCount: missingTypeCount,
                unknownSetIds: unknownSetIds);
        }

        /// <summary>
        /// 1 件の問題がフィルタ条件（形式・画像・タグ）に合うか。
        /// </summary>
        /// <param name="question">判定する問題。</param>
        /// <param name="settings">問題選択の設定。</param>
        /// <returns>出題候補に入れるなら true。</returns>
        private static bool Matches(Question question, QuestionSelectionSettings settings)
        {
            if (!question.Type.HasValue)
            {
                // 出題形式が無い問題は配信できない（QuestionDistributor が弾く）ので、候補に入れない。
                return false;
            }

            if (!MatchesType(question.Type.Value, settings.TypeFilter))
            {
                return false;
            }

            if (settings.ImageOnly && string.IsNullOrWhiteSpace(question.ImagePath))
            {
                return false;
            }

            return MatchesTags(question.Tags, settings.TagFilter);
        }

        /// <summary><c>questions.typeFilter</c>（Room の列挙）と問題の出題形式を突き合わせる。</summary>
        private static bool MatchesType(QuestionType type, QuestionTypeFilter filter)
        {
            switch (filter)
            {
                case QuestionTypeFilter.FreeText:
                    return type == QuestionType.FreeText;

                case QuestionTypeFilter.Choice:
                    return type == QuestionType.Choice;

                default:
                    return true;
            }
        }

        /// <summary><c>questions.setIds</c>（空なら全セット）に含まれるセットか。</summary>
        private static bool MatchesSetIds(string setId, IReadOnlyList<string> setIds)
        {
            if (setIds.Count == 0)
            {
                return true;
            }

            for (var i = 0; i < setIds.Count; i++)
            {
                if (string.Equals(setIds[i], setId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary><c>questions.tagFilter</c>（空ならフィルタなし）のいずれかを持つか。</summary>
        private static bool MatchesTags(IReadOnlyList<string> tags, IReadOnlyList<string> tagFilter)
        {
            if (tagFilter.Count == 0)
            {
                return true;
            }

            if (tags == null)
            {
                return false;
            }

            for (var i = 0; i < tags.Count; i++)
            {
                for (var j = 0; j < tagFilter.Count; j++)
                {
                    if (string.Equals(tags[i], tagFilter[j], StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Fisher-Yates シャッフル（末尾から、<see cref="IRandom.NextInt"/> で交換相手を選ぶ）。</summary>
        private static void Shuffle(List<int> indices, IRandom random)
        {
            for (var i = indices.Count - 1; i > 0; i--)
            {
                var j = random.NextInt(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }
        }
    }
}
