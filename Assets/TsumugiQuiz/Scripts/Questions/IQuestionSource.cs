namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// 出題に使う問題の供給元。サーバー（ホスト）だけが参照する。
    /// <c>GameSession</c>（<c>TsumugiQuiz.Network</c>）はこのインターフェース越しに問題を取り、
    /// 読み込み元（JSON フォルダ・テスト用スタブ・将来の出題順シャッフル）を差し替えられるようにする。
    /// </summary>
    public interface IQuestionSource
    {
        /// <summary>出題対象の問題数。</summary>
        int Count { get; }

        /// <summary>
        /// 指定インデックスの問題を取得する。
        /// </summary>
        /// <param name="index">0 以上 <see cref="Count"/> 未満のインデックス。</param>
        /// <param name="question">取得した問題。範囲外なら null。</param>
        /// <returns>取得できたら true。</returns>
        bool TryGetQuestion(int index, out Question question);
    }
}
