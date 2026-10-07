using System;
using System.IO;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="AppPaths"/> / <see cref="DocumentsPaths"/> が受け取るルートパスの入力検証（#112）。
    /// 2 クラスで同じ検証・同じ文言を使うため、唯一の出所としてここに集約する。
    ///
    /// <para>
    /// PR #118 レビュー M-1: 「空でない」「絶対パス」だけでは <c>C:\a\..\b</c> のような未正規化の
    /// パスや <c>&lt;projectRoot&gt;\Assets</c> 配下を受理してしまう。ここで
    /// <see cref="Path.GetFullPath(string)"/> による正規化まで行い、正規化<b>後</b>の値を
    /// 呼び出し側へ返す（以降の比較・ログが一意になる）。<c>Assets/</c> 配下の拒否は
    /// <paramref name="forbiddenPrefix"/> を渡した呼び出し側でのみ行う
    /// （<c>Core</c> は Unity API に依存しないため <c>Application.dataPath</c> を自力で取得できない。
    /// <c>scripts/setup-external.ps1</c> の <c>Assert-PathOutsideAssets</c> と同じ方針）。
    /// </para>
    /// </summary>
    public static class RootPathValidator
    {
        /// <summary>
        /// ルートパスとして妥当なら、正規化した絶対パスを返す。
        /// </summary>
        /// <param name="root">検証する絶対パス。</param>
        /// <param name="label">エラーメッセージに出す呼び出し元の名前（例: "AppPaths.Configure の root"）。</param>
        /// <param name="parameterName">例外に載せる引数名。</param>
        /// <param name="forbiddenPrefix">
        /// 配下を拒否するフォルダ（省略可）。Unity プロジェクトの <c>Assets</c> フォルダ等、
        /// 実行時の書き込み先にしてはいけない場所を呼び出し側が指定する。
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="root"/> が空・絶対パスでない・正規化できない、
        /// または <paramref name="forbiddenPrefix"/> 配下のとき。
        /// </exception>
        public static string Validate(string root, string label, string parameterName, string forbiddenPrefix = null)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException($"{label} が空です。", parameterName);
            }

            if (!Path.IsPathRooted(root))
            {
                throw new ArgumentException($"{label} は絶対パスで指定してください: {root}", parameterName);
            }

            var normalized = Normalize(root, label, parameterName);

            if (!string.IsNullOrWhiteSpace(forbiddenPrefix))
            {
                var normalizedForbidden = Normalize(forbiddenPrefix, $"{label} の禁止フォルダ", nameof(forbiddenPrefix));
                if (IsSameOrUnder(normalized, normalizedForbidden))
                {
                    throw new ArgumentException(
                        $"{label} に {normalizedForbidden} 配下は指定できません: {normalized}", parameterName);
                }
            }

            return normalized;
        }

        /// <summary>
        /// <paramref name="path"/> が <paramref name="parent"/> と同じか、その配下かどうか
        /// （どちらも正規化済みの絶対パスであること）。Windows 前提で大文字小文字は区別しない。
        /// </summary>
        public static bool IsSameOrUnder(string path, string parent)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(parent))
            {
                return false;
            }

            var trimmedPath = TrimTrailingSeparators(path);
            var trimmedParent = TrimTrailingSeparators(parent);

            if (trimmedPath.Equals(trimmedParent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // issue #122-2: ドライブ直下（"C:\"）は TrimTrailingSeparators が末尾のセパレータを
            // 落とさない（"C:" は別の意味になるため）。この場合は既にセパレータで終わっているので、
            // そのまま前方一致に使う（セパレータを重ねて付けると "C:\\" になり、
            // "C:\Users\..." のような実在のパスと前方一致しなくなってしまう）。
            // issue #122 レビュー L-1: 判定対象は正規化済みの絶対パス想定だが、呼び出し側が
            // 未正規化の値（"/" 区切り）を渡す可能性もゼロではないため、両方のセパレータを見る。
            var trimmedParentEndsWithSeparator =
                trimmedParent.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                trimmedParent.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal);
            var prefix = trimmedParentEndsWithSeparator
                ? trimmedParent
                : trimmedParent + Path.DirectorySeparatorChar;

            return trimmedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path, string label, string parameterName)
        {
            try
            {
                return TrimTrailingSeparators(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException
                                       || ex is NotSupportedException
                                       || ex is PathTooLongException
                                       || ex is System.Security.SecurityException)
            {
                throw new ArgumentException($"{label} を絶対パスとして解決できません: {path}（{ex.Message}）", parameterName);
            }
        }

        private static string TrimTrailingSeparators(string path)
        {
            // ルートそのもの（"C:\"）は末尾のセパレータを落とすと別の意味になるため残す。
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 || trimmed.EndsWith(":", StringComparison.Ordinal) ? path : trimmed;
        }
    }
}
