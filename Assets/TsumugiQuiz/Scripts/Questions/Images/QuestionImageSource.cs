using System;

namespace TsumugiQuiz.Questions.Images
{
    /// <summary>
    /// 問題フォルダを基準に <c>imagePath</c> を解決する既定の実装（#16）。
    /// </summary>
    /// <remarks>
    /// 基準フォルダは問題フォルダ（<c>QuestionRepository.GetDefaultQuestionsFolderPath()</c>）で、
    /// 問題セットファイルと同じ階層に置かれている前提（docs/question-data.md §4:
    /// 問題ファイルは <c>Questions/</c> 直下 1 階層のみ）。
    /// 解決の規則は <see cref="QuestionImagePathResolver"/>（読み込み時の検証と同じ）に従う。
    /// </remarks>
    public sealed class QuestionImageSource : IQuestionImageSource
    {
        private readonly string _baseDirectory;

        /// <summary>
        /// 基準フォルダを指定して生成する。
        /// </summary>
        /// <param name="baseDirectory">
        /// <c>imagePath</c> の相対パス解決の基準フォルダ（問題セットファイルの配置先）。
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="baseDirectory"/> が空のとき。</exception>
        public QuestionImageSource(string baseDirectory)
        {
            if (string.IsNullOrEmpty(baseDirectory))
            {
                throw new ArgumentException("基準フォルダを指定してください。", nameof(baseDirectory));
            }

            _baseDirectory = baseDirectory;
        }

        /// <summary>解決の基準フォルダ。</summary>
        public string BaseDirectory => _baseDirectory;

        /// <inheritdoc />
        public bool TryGetImagePath(Question question, out string absolutePath, out string error)
        {
            absolutePath = null;
            error = null;

            if (question == null)
            {
                error = "問題が指定されていません。";
                return false;
            }

            if (string.IsNullOrEmpty(question.ImagePath))
            {
                // 画像なしの問題。警告する必要はないので error は null のままにする。
                return false;
            }

            if (!QuestionImagePathResolver.TryResolve(
                    _baseDirectory, question.ImagePath, out var fullPath, out var pathError))
            {
                error = QuestionImagePathResolver.Describe(pathError, question.ImagePath);
                return false;
            }

            absolutePath = fullPath;
            return true;
        }
    }
}
