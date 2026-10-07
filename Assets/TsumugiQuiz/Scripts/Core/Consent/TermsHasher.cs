using System;
using System.Security.Cryptography;
using System.Text;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 利用規約テキストの SHA-256 ハッシュ計算（requirements.md FR-73・FR-76）。
    /// 同梱テキスト（Assets/TsumugiQuiz/Resources/Terms/*.txt）の内容が変わればハッシュも変わるため、
    /// これを同意記録（<see cref="ConsentRecord"/>）と突き合わせることで再同意の要否を判定できる。
    /// </summary>
    public static class TermsHasher
    {
        /// <summary>ヘッダー（出典・取得日等の注記）と本文を区切る行。Resources/Terms/*.txt の規約。</summary>
        private const string HeaderBodySeparatorLine = "---";

        /// <summary>
        /// <paramref name="content"/> の SHA-256 を計算し、小文字16進数文字列で返す。
        /// 改行コード（CRLF/LF）の違いと先頭 BOM は結果に影響しないよう正規化してからハッシュ化する
        /// （同じ内容のファイルを別環境で保存し直しただけで再同意（FR-76）を誤って要求しないため）。
        /// </summary>
        public static string ComputeSha256Hex(string content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var normalized = NormalizeForHashing(content);
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(normalized);
            var hash = sha256.ComputeHash(bytes);

            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Resources/Terms/*.txt の形式（先頭に出典 URL・取得日等のヘッダー、"---" のみの行で区切ってから
        /// 規約本文が続く）を前提に、ヘッダーを除いた本文だけをハッシュ化する。
        /// 出典 URL の書き振りや取得日の追記など、規約そのものではない注記の変更で
        /// 誤って再同意（FR-76）が要求されないようにするため、本文のみを対象にする。
        /// </summary>
        public static string ComputeSha256HexForTermsBody(string fullTextWithHeader)
        {
            if (fullTextWithHeader == null)
            {
                throw new ArgumentNullException(nameof(fullTextWithHeader));
            }

            var body = ExtractBody(fullTextWithHeader);
            return ComputeSha256Hex(body);
        }

        private static string ExtractBody(string fullText)
        {
            var normalized = NormalizeLineEndings(fullText);
            var lines = normalized.Split('\n');

            var separatorIndex = Array.IndexOf(lines, HeaderBodySeparatorLine);
            if (separatorIndex < 0 || separatorIndex == lines.Length - 1)
            {
                // ヘッダー区切りが見つからない場合はフォーマット不備を握りつぶさず、全文を対象にする。
                return normalized.Trim();
            }

            var bodyLineCount = lines.Length - separatorIndex - 1;
            return string.Join("\n", lines, separatorIndex + 1, bodyLineCount).Trim();
        }

        private static string NormalizeForHashing(string content)
        {
            var normalized = NormalizeLineEndings(content);

            // BOM（U+FEFF）を除去する。File.ReadAllText 等は既定で BOM を取り除くが、
            // 呼び出し元・実行環境によっては BOM を含んだ文字列が渡ってくる場合に備える。
            const char byteOrderMark = '﻿';
            return normalized.TrimStart(byteOrderMark);
        }

        private static string NormalizeLineEndings(string text)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }
}
