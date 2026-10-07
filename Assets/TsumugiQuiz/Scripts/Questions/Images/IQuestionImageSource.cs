namespace TsumugiQuiz.Questions.Images
{
    /// <summary>
    /// 問題に紐づく画像ファイルの供給元。サーバー（ホスト）だけが参照する（#16）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>imagePath</c> は問題セットファイルからの相対パスなので、解決には基準フォルダが要る。
    /// 配信器（<c>QuestionDistributor</c>）がファイルシステムの事情を知らずに済むよう、
    /// 解決はこのインターフェースへ分けている。
    /// </para>
    /// <para>
    /// 引数は問題インデックスではなく <see cref="Question"/> そのものにしている。
    /// セッション開始時に出題列がフィルタ・シャッフルされる（#19）ため、
    /// インデックスで引くと配信器側の並びとずれるおそれがあるため。
    /// </para>
    /// </remarks>
    public interface IQuestionImageSource
    {
        /// <summary>
        /// 指定した問題の画像ファイルの絶対パスを返す。
        /// </summary>
        /// <param name="question">対象の問題。</param>
        /// <param name="absolutePath">画像ファイルの絶対パス。画像が無い・解決できない場合は null。</param>
        /// <param name="error">
        /// 解決に失敗した理由（ログ用の日本語）。
        /// 「そもそも画像が指定されていない」場合は false を返しつつ null を入れる
        /// （警告を出すべきかどうかを呼び出し側が区別できるようにするため）。
        /// </param>
        /// <returns>画像があり、解決できたら true。</returns>
        bool TryGetImagePath(Question question, out string absolutePath, out string error);
    }
}
