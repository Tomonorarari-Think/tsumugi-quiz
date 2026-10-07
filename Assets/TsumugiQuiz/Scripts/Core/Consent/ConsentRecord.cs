using System;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 1件の規約に対する同意が成立した事実の不変記録（requirements.md FR-73）。
    /// 「いつ」「どのバージョンの規約本文に」「どのアプリバージョンで」同意したかを保持する。
    /// </summary>
    public sealed class ConsentRecord
    {
        /// <summary>同意対象の規約 ID（<see cref="TermsDefinition.TermsId"/> と対応）。</summary>
        public string TermsId { get; }

        /// <summary>同意時点で提示した規約本文の SHA-256 ハッシュ。</summary>
        public string Sha256Hash { get; }

        /// <summary>同意が成立した日時（UTC）。</summary>
        public DateTime AcceptedAtUtc { get; }

        /// <summary>同意時点のアプリバージョン（Unity の Application.version 相当）。</summary>
        public string AppVersion { get; }

        public ConsentRecord(string termsId, string sha256Hash, DateTime acceptedAtUtc, string appVersion)
        {
            if (string.IsNullOrEmpty(termsId))
            {
                throw new ArgumentException("termsId を指定してください。", nameof(termsId));
            }

            if (string.IsNullOrEmpty(sha256Hash))
            {
                throw new ArgumentException("sha256Hash を指定してください。", nameof(sha256Hash));
            }

            if (acceptedAtUtc.Kind == DateTimeKind.Unspecified)
            {
                throw new ArgumentException("acceptedAtUtc の Kind を DateTimeKind.Utc（または Local）で指定してください。", nameof(acceptedAtUtc));
            }

            if (string.IsNullOrEmpty(appVersion))
            {
                throw new ArgumentException("appVersion を指定してください。", nameof(appVersion));
            }

            TermsId = termsId;
            Sha256Hash = sha256Hash;
            AcceptedAtUtc = acceptedAtUtc.ToUniversalTime();
            AppVersion = appVersion;
        }
    }
}
