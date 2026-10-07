namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// 問題データの上限値（docs/question-data.md §1 / §2）の単一の出所。
    /// 読み込み時の検証（<see cref="QuestionSetValidator"/>）と配信用 DTO
    /// （<c>TsumugiQuiz.Network.QuestionDto</c>）の両方がここを参照することで、
    /// 「読み込めたのに配信できない問題」が生まれないようにする（#13）。
    /// </summary>
    /// <remarks>
    /// 依存方向は <c>Core ← Questions ← Network</c> の一方向なので（docs/architecture.md §3）、
    /// 定数の置き場は下位の <c>Questions</c> 側にする。
    /// </remarks>
    public static class QuestionLimits
    {
        /// <summary>問題セットのタイトルの最大文字数（docs/question-data.md §1）。</summary>
        public const int MaxTitleLength = 100;

        /// <summary>問題セットの説明の最大文字数（docs/question-data.md §1）。</summary>
        public const int MaxDescriptionLength = 500;

        /// <summary>問題 ID の最大文字数。</summary>
        public const int MaxIdLength = 100;

        /// <summary>画面表示用の問題文の最大文字数。</summary>
        public const int MaxTextLength = 500;

        /// <summary>読み上げ用テキストの最大文字数（<see cref="MaxTextLength"/> にそろえる）。</summary>
        public const int MaxReadingTextLength = 500;

        /// <summary>正解候補 1 件の最大文字数。</summary>
        public const int MaxAnswerLength = 100;

        /// <summary>
        /// 正解候補（<c>answers</c>）の最大件数（PR #93 レビュー L3、issue #32）。
        /// docs/question-data.md §2 は元々件数上限を定めていなかったが、編集フォームの
        /// 「正解候補を追加」ボタンを無制限に押せてしまう問題があったため新設した。
        /// <see cref="MaxChoiceCount"/>（8件）より緩い上限（自由記述の表記ゆれを吸収する用途のため）とし、20件とした。
        /// </summary>
        public const int MaxAnswerCount = 20;

        /// <summary>選択肢の最小件数（<c>choice</c> のとき）。</summary>
        public const int MinChoiceCount = 2;

        /// <summary>選択肢の最大件数。</summary>
        public const int MaxChoiceCount = 8;

        /// <summary>選択肢 1 件の最大文字数。</summary>
        public const int MaxChoiceLength = 100;

        /// <summary>タグの最大件数。</summary>
        public const int MaxTagCount = 20;

        /// <summary>タグ 1 件の最大文字数。</summary>
        public const int MaxTagLength = 100;

        /// <summary>難易度の下限。</summary>
        public const int MinDifficulty = 1;

        /// <summary>難易度の上限。</summary>
        public const int MaxDifficulty = 5;

        /// <summary>問題画像 1 枚の最大バイト数（docs/question-data.md §2: 2MB）。</summary>
        public const int MaxImageSizeBytes = 2 * 1024 * 1024;

        /// <summary>
        /// 問題画像の最大解像度（幅・高さそれぞれ）。受信側でデコードする前に header から判定し、
        /// 圧縮率の高い巨大画像（いわゆる decompression bomb）でメモリを食い潰さないようにする
        /// （docs/network.md §9、#16）。
        /// </summary>
        public const int MaxImageDimension = 4096;

        /// <summary>
        /// 問題フォルダ直下の *.json ファイル数の上限（docs/question-data.md §4、PR #42 統括申し送り L6）。
        /// <see cref="TsumugiQuiz.Questions.QuestionRepository"/> と問題エディタ用の
        /// <c>TsumugiQuiz.Questions.Editing.QuestionSetFileScanner</c> が共有する単一の出所
        /// （PR #88 レビュー LOW: 従来 <see cref="TsumugiQuiz.Questions.QuestionRepository"/> にのみ定義されていた）。
        /// </summary>
        public const int MaxQuestionSetFileCount = 200;

        /// <summary>
        /// 問題エディタの画像選択 UI（docs/question-data.md §8）が一覧表示する
        /// <c>Questions/images/</c> 直下の画像ファイル数の上限（PR #93 レビュー M3）。
        /// 大量のファイルが置かれたフォルダで UI Toolkit の行を無制限に生成しないための打ち切り。
        /// <see cref="MaxQuestionSetFileCount"/> と同じ200件にそろえる。
        /// </summary>
        public const int MaxQuestionImageFileCount = 200;

        /// <summary>
        /// 問題セットファイル1件あたりの最大バイト数（docs/question-data.md §4）。
        /// <see cref="MaxQuestionSetFileCount"/> と同様、<c>QuestionRepository</c> / <c>QuestionSetFileScanner</c>
        /// が共有する単一の出所。
        /// </summary>
        public const long MaxQuestionSetFileSizeBytes = 5L * 1024 * 1024;
    }
}
