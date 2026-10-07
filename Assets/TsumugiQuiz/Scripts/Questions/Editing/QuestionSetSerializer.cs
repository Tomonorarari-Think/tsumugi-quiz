using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace TsumugiQuiz.Questions.Editing
{
    /// <summary>
    /// <see cref="QuestionSet"/> を docs/question-data.md §1 のスキーマに従って JSON へ書き出す（issue #30）。
    /// Unity API に依存しない純 C#。
    /// </summary>
    /// <remarks>
    /// <see cref="QuestionSet"/> / <see cref="Question"/> の C# プロパティは PascalCase だが、
    /// JSON のキーは camelCase（CLAUDE.md）である必要があるため
    /// <see cref="QuestionSetContractResolver"/>（<see cref="CamelCasePropertyNamesContractResolver"/> 派生）を使う。
    /// インデントは半角スペース2個（docs/question-data.md §8「保存」）。<see cref="JsonTextWriter"/> の
    /// 既定値（<c>IndentChar = ' '</c> / <c>Indentation = 2</c>）と一致するが、既定値に依存せず
    /// <see cref="Serialize"/> 内で明示的に設定する（issue #32。将来 Newtonsoft.Json の既定値が
    /// 変わっても壊れないようにするため）。
    /// </remarks>
    public static class QuestionSetSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new QuestionSetContractResolver(),
            Formatting = Formatting.Indented,
            // readingText / imagePath 等、省略時 null の項目を書き出さない。
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static string Serialize(QuestionSet set)
        {
            if (set == null)
            {
                throw new ArgumentNullException(nameof(set));
            }

            var serializer = JsonSerializer.Create(Settings);
            var stringBuilder = new StringBuilder();

            using (var stringWriter = new StringWriter(stringBuilder, CultureInfo.InvariantCulture))
            using (var jsonWriter = new JsonTextWriter(stringWriter)
                   {
                       // Formatting.Indented を JsonSerializerSettings だけでなく JsonTextWriter 自身にも
                       // 明示する（JsonSerializer.Serialize(JsonWriter, ...) はライター自身の設定を使うため）。
                       Formatting = Formatting.Indented,
                       IndentChar = ' ',
                       Indentation = 2,
                   })
            {
                serializer.Serialize(jsonWriter, set);
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// docs/schemas/question-set.schema.json の <c>allOf</c>/<c>not</c>/<c>required</c> に違反しないよう、
        /// <c>answers</c> / <c>choices</c> / <c>tags</c> が空コレクションのときはキー自体を省略する
        /// （PR #88 レビュー H2。<see cref="Question"/> はこれらを常に空配列以上で保持するため
        /// <see cref="NullValueHandling.Ignore"/> だけでは対処できない）。
        /// </summary>
        private sealed class QuestionSetContractResolver : CamelCasePropertyNamesContractResolver
        {
            private static readonly HashSet<string> OmitWhenEmptyPropertyNames =
                new HashSet<string>(StringComparer.Ordinal) { "answers", "choices", "tags" };

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);

                // Question.Answers / Choices / Tags の宣言型は IReadOnlyList&lt;string&gt;（非ジェネリックの
                // ICollection を実装しない）なので、プロパティ型ではなく IEnumerable で判定する。
                if (OmitWhenEmptyPropertyNames.Contains(property.PropertyName)
                    && typeof(IEnumerable).IsAssignableFrom(property.PropertyType)
                    && property.PropertyType != typeof(string))
                {
                    property.ShouldSerialize = instance => GetElementCount(property, instance) > 0;
                }

                return property;
            }

            private static int GetElementCount(JsonProperty property, object instance)
            {
                var value = property.ValueProvider.GetValue(instance);
                switch (value)
                {
                    case null:
                        return 0;
                    case ICollection collection:
                        return collection.Count;
                    case IEnumerable enumerable:
                        var count = 0;
                        foreach (var _ in enumerable)
                        {
                            count++;
                        }

                        return count;
                    default:
                        return 0;
                }
            }
        }
    }
}
