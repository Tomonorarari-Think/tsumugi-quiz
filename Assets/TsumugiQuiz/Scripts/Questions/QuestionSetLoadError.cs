using System.Collections.Generic;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// 読み込み・検証に失敗し、スキップされた問題セット1件分の情報。
    /// </summary>
    public sealed class QuestionSetLoadError
    {
        /// <summary>スキップされたファイルの絶対パス。</summary>
        public string FilePath { get; }

        /// <summary>エラーメッセージの一覧。</summary>
        public IReadOnlyList<string> Messages { get; }

        public QuestionSetLoadError(string filePath, IReadOnlyList<string> messages)
        {
            FilePath = filePath;
            Messages = messages;
        }
    }
}
