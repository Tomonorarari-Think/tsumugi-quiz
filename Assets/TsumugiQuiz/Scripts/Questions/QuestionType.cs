using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// 問題の出題形式。docs/question-data.md §1 参照。
    /// JSON 上は "freeText" / "choice" の文字列で表現される。
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum QuestionType
    {
        [EnumMember(Value = "freeText")]
        FreeText,

        [EnumMember(Value = "choice")]
        Choice,
    }
}
