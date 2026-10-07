using System;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 「現時点でアプリに同梱されている、ある1件の規約テキストが何であるか」を表す不変データ。
    /// 規約 ID と、その本文（<see cref="TermsHasher"/> で計算した）SHA-256 ハッシュの組。
    /// 同意記録（<see cref="ConsentRecord"/>）と突き合わせて、同意済みか・再同意が必要かを判定するために使う。
    /// </summary>
    public sealed class TermsDefinition
    {
        /// <summary>規約 ID（例: "voicevox-models-terms"）。Resources 上のファイル名と対応させる。</summary>
        public string TermsId { get; }

        /// <summary>現在のテキスト本文の SHA-256 ハッシュ（小文字16進数64桁）。</summary>
        public string Sha256Hash { get; }

        public TermsDefinition(string termsId, string sha256Hash)
        {
            if (string.IsNullOrEmpty(termsId))
            {
                throw new ArgumentException("termsId を指定してください。", nameof(termsId));
            }

            if (string.IsNullOrEmpty(sha256Hash))
            {
                throw new ArgumentException("sha256Hash を指定してください。", nameof(sha256Hash));
            }

            TermsId = termsId;
            Sha256Hash = sha256Hash;
        }
    }
}
