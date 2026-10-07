using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// 問題セット（1 JSON ファイル = 1 セット）の不変データ。docs/question-data.md §1 参照。
    /// </summary>
    public sealed class QuestionSet
    {
        public int SchemaVersion { get; }
        public string SetId { get; }
        public string Title { get; }
        public string Description { get; }
        public IReadOnlyList<Question> Questions { get; }

        [JsonConstructor]
        public QuestionSet(
            int schemaVersion,
            string setId,
            string title,
            string description = null,
            IReadOnlyList<Question> questions = null)
        {
            SchemaVersion = schemaVersion;
            SetId = setId;
            Title = title;
            Description = description ?? string.Empty;
            Questions = questions?.ToArray() ?? Array.Empty<Question>();
        }
    }
}
