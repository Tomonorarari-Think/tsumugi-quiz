using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// <see cref="QuestionRepository"/> が読み込んだ問題セットを 1 本の出題リストに平坦化した
    /// <see cref="IQuestionSource"/>。読み込み順（ファイル名昇順 → セット内の並び順）を保つ。
    /// 出題形式で絞り込めるため、M2（freeText のみ）では
    /// <see cref="QuestionType.FreeText"/> を指定して使う。
    /// </summary>
    /// <remarks>
    /// 生成時にリストを固定するため、後から JSON を書き換えても本インスタンスは変化しない
    /// （1 ゲーム中に出題対象が変わらないようにするため）。
    /// </remarks>
    public sealed class RepositoryQuestionSource : IQuestionSource
    {
        private readonly Question[] _questions;

        /// <summary>
        /// 問題リストを指定して生成する。
        /// </summary>
        /// <param name="questions">出題対象の問題。null 要素は無視する。</param>
        /// <exception cref="ArgumentNullException"><paramref name="questions"/> が null のとき。</exception>
        public RepositoryQuestionSource(IEnumerable<Question> questions)
        {
            if (questions == null)
            {
                throw new ArgumentNullException(nameof(questions));
            }

            var collected = new List<Question>();
            foreach (var question in questions)
            {
                if (question != null)
                {
                    collected.Add(question);
                }
            }

            _questions = collected.ToArray();
        }

        /// <inheritdoc />
        public int Count => _questions.Length;

        /// <summary>
        /// 読み込み結果から生成する。
        /// </summary>
        /// <param name="result"><see cref="QuestionRepository.LoadAll"/> の結果。</param>
        /// <param name="typeFilter">出題形式の絞り込み。null なら全形式。</param>
        /// <returns>生成した供給元。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="result"/> が null のとき。</exception>
        public static RepositoryQuestionSource FromResult(QuestionRepositoryResult result, QuestionType? typeFilter = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return FromSets(result.Sets, typeFilter);
        }

        /// <summary>
        /// <see cref="QuestionLibrary"/> の読み込み結果から生成する（ホスト側の通常経路。#29 連携）。
        /// フォルダ単位のエラー（<see cref="QuestionLoadReport.FolderErrors"/>）や
        /// スキップされたセットは出題対象から外れるだけで、ここでは扱わない（UI が #5 / #28 で表示する）。
        /// </summary>
        /// <param name="report"><see cref="QuestionLibrary.Reload"/> / <c>CurrentReport</c> の結果。</param>
        /// <param name="typeFilter">出題形式の絞り込み。null なら全形式。</param>
        /// <returns>生成した供給元。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="report"/> が null のとき。</exception>
        public static RepositoryQuestionSource FromReport(QuestionLoadReport report, QuestionType? typeFilter = null)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            return FromSets(report.Sets, typeFilter);
        }

        /// <summary>
        /// リポジトリから読み込んで生成する（<see cref="QuestionLibrary"/> を使わない経路）。
        /// </summary>
        /// <param name="repository">問題リポジトリ。</param>
        /// <param name="typeFilter">出題形式の絞り込み。null なら全形式。</param>
        /// <returns>生成した供給元。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="repository"/> が null のとき。</exception>
        public static RepositoryQuestionSource FromRepository(QuestionRepository repository, QuestionType? typeFilter = null)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            return FromResult(repository.LoadAll(), typeFilter);
        }

        /// <inheritdoc />
        public bool TryGetQuestion(int index, out Question question)
        {
            if (index < 0 || index >= _questions.Length)
            {
                question = null;
                return false;
            }

            question = _questions[index];
            return true;
        }

        /// <summary>問題セットの一覧を平坦化して供給元を作る。</summary>
        private static RepositoryQuestionSource FromSets(IReadOnlyList<QuestionSet> sets, QuestionType? typeFilter)
        {
            var collected = new List<Question>();
            if (sets != null)
            {
                for (var i = 0; i < sets.Count; i++)
                {
                    AppendSet(sets[i], typeFilter, collected);
                }
            }

            return new RepositoryQuestionSource(collected);
        }

        private static void AppendSet(QuestionSet set, QuestionType? typeFilter, List<Question> collected)
        {
            var questions = set?.Questions;
            if (questions == null)
            {
                return;
            }

            for (var i = 0; i < questions.Count; i++)
            {
                var question = questions[i];
                if (question == null)
                {
                    continue;
                }

                if (typeFilter.HasValue && question.Type != typeFilter.Value)
                {
                    continue;
                }

                collected.Add(question);
            }
        }
    }
}
