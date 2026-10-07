using System;
using System.IO;

namespace TsumugiQuiz.Questions.Images
{
    /// <summary>
    /// <c>imagePath</c>（問題セットファイルからの相対パス）を実ファイルの絶対パスへ解決する
    /// （docs/question-data.md §2 の画像の規則）。
    /// </summary>
    /// <remarks>
    /// 読み込み時の検証（<see cref="QuestionSetValidator"/>）と配信時の読み出し
    /// （<c>TsumugiQuiz.Network.QuestionDistributor</c>、#16）が同じ規則を使うための単一の出所。
    /// 規則は「絶対パス禁止 / 拡張子は PNG・JPG / セットフォルダの外は禁止 / 実在 / 2MB 以下」。
    /// </remarks>
    public static class QuestionImagePathResolver
    {
        /// <summary>形式・解像度の判定のために先頭から読むバイト数。</summary>
        public const int HeaderProbeBytes = 64 * 1024;

        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>
        /// パスの形（絶対パスでないこと・拡張子）だけを検証する。ファイルシステムは見ない。
        /// </summary>
        /// <param name="imagePath">問題セットファイルからの相対パス。</param>
        /// <returns>問題が無ければ <see cref="ImagePathError.None"/>。</returns>
        public static ImagePathError ValidateFormat(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                return ImagePathError.Empty;
            }

            // 絶対パス（例: C:\Windows\x.png）はセットフォルダの外を直接指すため、拡張子より先に拒否する。
            if (IsPathRooted(imagePath))
            {
                return ImagePathError.Rooted;
            }

            if (!HasAllowedExtension(imagePath))
            {
                return ImagePathError.UnsupportedExtension;
            }

            return ImagePathError.None;
        }

        /// <summary>
        /// 基準フォルダからの相対パスを絶対パスへ解決し、実在とサイズを確かめる
        /// （パスの形は <see cref="ValidateFormat"/> で別に検証する）。
        /// </summary>
        /// <param name="baseDirectory">相対パス解決の基準（問題セットファイルの配置先）。</param>
        /// <param name="imagePath">問題セットファイルからの相対パス。</param>
        /// <param name="fullPath">解決した絶対パス。失敗時は null。</param>
        /// <param name="error">失敗理由。成功時は <see cref="ImagePathError.None"/>。</param>
        /// <returns>解決できたら true。</returns>
        public static bool TryResolveExisting(
            string baseDirectory, string imagePath, out string fullPath, out ImagePathError error)
        {
            return TryResolveExisting(baseDirectory, imagePath, out fullPath, out _, out error);
        }

        /// <summary>
        /// 相対パスを解決し、実在とサイズを確かめる（ファイルサイズも返す版）。
        /// </summary>
        /// <param name="baseDirectory">相対パス解決の基準（問題セットファイルの配置先）。</param>
        /// <param name="imagePath">問題セットファイルからの相対パス。</param>
        /// <param name="fullPath">解決した絶対パス。失敗時は null。</param>
        /// <param name="fileLengthBytes">
        /// ファイルのバイト数。<see cref="ImagePathError.TooLarge"/> のときも実際の値が入る
        /// （呼び出し側がエラーメッセージに載せられるようにするため）。それ以外の失敗時は 0。
        /// </param>
        /// <param name="error">失敗理由。成功時は <see cref="ImagePathError.None"/>。</param>
        /// <returns>解決できたら true。</returns>
        public static bool TryResolveExisting(
            string baseDirectory,
            string imagePath,
            out string fullPath,
            out long fileLengthBytes,
            out ImagePathError error)
        {
            fullPath = null;
            fileLengthBytes = 0L;

            if (string.IsNullOrEmpty(baseDirectory) || string.IsNullOrEmpty(imagePath))
            {
                error = ImagePathError.Empty;
                return false;
            }

            string normalizedBaseDirectory;
            string candidate;
            try
            {
                normalizedBaseDirectory = Path.GetFullPath(baseDirectory);
                if (!normalizedBaseDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    normalizedBaseDirectory += Path.DirectorySeparatorChar;
                }

                candidate = Path.GetFullPath(Path.Combine(baseDirectory, imagePath));
            }
            catch (Exception ex) when (ex is ArgumentException
                                       || ex is NotSupportedException
                                       || ex is PathTooLongException)
            {
                // 不正な文字・長すぎるパスなど。呼び出し側には「解決できない」とだけ伝える。
                error = ImagePathError.Invalid;
                return false;
            }

            // パストラバーサル対策: 「../」等でセットフォルダの外に出るパスを拒否する。
            if (!candidate.StartsWith(normalizedBaseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                error = ImagePathError.OutsideBaseDirectory;
                return false;
            }

            if (!File.Exists(candidate))
            {
                error = ImagePathError.NotFound;
                return false;
            }

            long length;
            try
            {
                length = new FileInfo(candidate).Length;
            }
            catch (IOException)
            {
                error = ImagePathError.NotFound;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                error = ImagePathError.NotFound;
                return false;
            }

            fileLengthBytes = length;

            if (length > QuestionLimits.MaxImageSizeBytes)
            {
                error = ImagePathError.TooLarge;
                return false;
            }

            fullPath = candidate;
            error = ImagePathError.None;
            return true;
        }

        /// <summary>
        /// パスの形の検証と実ファイルの解決をまとめて行う（配信側はこちらを使う）。
        /// </summary>
        /// <param name="baseDirectory">相対パス解決の基準。</param>
        /// <param name="imagePath">問題セットファイルからの相対パス。</param>
        /// <param name="fullPath">解決した絶対パス。失敗時は null。</param>
        /// <param name="error">失敗理由。成功時は <see cref="ImagePathError.None"/>。</param>
        /// <returns>解決できたら true。</returns>
        public static bool TryResolve(
            string baseDirectory, string imagePath, out string fullPath, out ImagePathError error)
        {
            fullPath = null;

            error = ValidateFormat(imagePath);
            if (error != ImagePathError.None)
            {
                return false;
            }

            return TryResolveExisting(baseDirectory, imagePath, out fullPath, out error);
        }

        /// <summary>
        /// 画像ファイルの先頭を読む（形式・解像度の判定に使う分だけ）。
        /// </summary>
        /// <remarks>
        /// 読み込み時の検証で 2MB のファイルを丸ごと読まないための措置。
        /// JPEG の <c>SOFn</c> が <see cref="HeaderProbeBytes"/> より後ろにある（巨大な EXIF を持つ）場合は
        /// 解像度を判定できないが、その場合は配信時（<c>QuestionDistributor</c>）に全体を読んで確かめる。
        /// </remarks>
        /// <param name="fullPath">画像ファイルの絶対パス。</param>
        /// <param name="header">読み取った先頭バイト列。失敗時は null。</param>
        /// <returns>読み取れたら true。</returns>
        public static bool TryReadHeader(string fullPath, out byte[] header)
        {
            header = null;

            if (string.IsNullOrEmpty(fullPath))
            {
                return false;
            }

            try
            {
                using (var stream = new FileStream(
                    fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var length = (int)Math.Min(HeaderProbeBytes, stream.Length);
                    if (length <= 0)
                    {
                        return false;
                    }

                    var buffer = new byte[length];
                    var read = 0;
                    while (read < length)
                    {
                        var chunk = stream.Read(buffer, read, length - read);
                        if (chunk <= 0)
                        {
                            break;
                        }

                        read += chunk;
                    }

                    if (read <= 0)
                    {
                        return false;
                    }

                    if (read != length)
                    {
                        Array.Resize(ref buffer, read);
                    }

                    header = buffer;
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException
                                       || ex is UnauthorizedAccessException
                                       || ex is NotSupportedException
                                       || ex is ArgumentException)
            {
                return false;
            }
        }

        /// <summary>ログ用の日本語の説明を返す。</summary>
        /// <param name="error">失敗理由。</param>
        /// <param name="imagePath">対象の相対パス。</param>
        /// <returns>説明文。</returns>
        public static string Describe(ImagePathError error, string imagePath)
        {
            switch (error)
            {
                case ImagePathError.None:
                    return string.Empty;
                case ImagePathError.Empty:
                    return "imagePath が空です。";
                case ImagePathError.Rooted:
                    return $"imagePath に絶対パスを指定することはできません: {imagePath}";
                case ImagePathError.UnsupportedExtension:
                    return $"imagePath の拡張子が PNG/JPG ではありません（実際: {imagePath}）。";
                case ImagePathError.OutsideBaseDirectory:
                    return $"imagePath がセットフォルダの外を指しています: {imagePath}";
                case ImagePathError.NotFound:
                    return $"imagePath の画像ファイルが見つかりません: {imagePath}";
                case ImagePathError.TooLarge:
                    return $"imagePath の画像ファイルが {QuestionLimits.MaxImageSizeBytes} バイトを超えています: {imagePath}";
                default:
                    return $"imagePath を解決できません: {imagePath}";
            }
        }

        private static bool IsPathRooted(string imagePath)
        {
            try
            {
                return Path.IsPathRooted(imagePath);
            }
            catch (ArgumentException)
            {
                // 不正な文字を含むパスは「絶対パス」と同じく受け付けない。
                return true;
            }
        }

        private static bool HasAllowedExtension(string imagePath)
        {
            string extension;
            try
            {
                extension = Path.GetExtension(imagePath);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            var lowered = extension.ToLowerInvariant();
            for (var i = 0; i < AllowedExtensions.Length; i++)
            {
                if (string.Equals(AllowedExtensions[i], lowered, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
