using System;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 参加コードのエンコード・デコード・正規化が失敗したときに投げられる例外。
    /// <see cref="Message"/> はそのままユーザーに提示できる日本語の文言、
    /// <see cref="Error"/> は失敗理由の判別用（ログ・UI の出し分け）に使う。
    /// </summary>
    public sealed class JoinCodeException : Exception
    {
        public JoinCodeException(JoinCodeError error, string message)
            : base(message)
        {
            Error = error;
        }

        public JoinCodeException(JoinCodeError error, string message, Exception innerException)
            : base(message, innerException)
        {
            Error = error;
        }

        /// <summary>失敗理由。</summary>
        public JoinCodeError Error { get; }
    }
}
