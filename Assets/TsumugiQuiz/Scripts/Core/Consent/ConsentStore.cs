using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 利用規約同意の記録・判定・撤回を行う純 C# ロジック（requirements.md FR-71〜FR-76）。
    /// 実際の読み書き（Application.persistentDataPath/consent.json 等）は
    /// <see cref="IConsentStorage"/> を注入する呼び出し側（UI 層）の責務とし、この層は Unity API に依存しない。
    /// </summary>
    public sealed class ConsentStore
    {
        private readonly IConsentStorage _storage;

        public ConsentStore(IConsentStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        /// <summary>現在保存されている同意記録をすべて返す（設定/クレジット画面での確認用）。</summary>
        public IReadOnlyList<ConsentRecord> LoadRecords()
        {
            return _storage.Load() ?? Array.Empty<ConsentRecord>();
        }

        /// <summary>
        /// <paramref name="requiredTerms"/> のすべてについて、同意記録が存在し、かつそのハッシュが
        /// 現在の規約本文のハッシュと一致するかを判定する（FR-74: 未同意時の制限、FR-76: ハッシュ変化時の再同意）。
        /// 1件でも記録が無い、またはハッシュが異なれば false（=全体として未同意扱い）を返す。
        /// </summary>
        public bool HasAcceptedAll(IReadOnlyList<TermsDefinition> requiredTerms)
        {
            if (requiredTerms == null)
            {
                throw new ArgumentNullException(nameof(requiredTerms));
            }

            if (requiredTerms.Count == 0)
            {
                throw new ArgumentException("requiredTerms が空です。判定対象の規約を1件以上指定してください。", nameof(requiredTerms));
            }

            var records = LoadRecords();

            foreach (var required in requiredTerms)
            {
                if (!IsAccepted(records, required))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAccepted(IReadOnlyList<ConsentRecord> records, TermsDefinition required)
        {
            foreach (var record in records)
            {
                if (record.TermsId == required.TermsId && record.Sha256Hash == required.Sha256Hash)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// <paramref name="acceptedTerms"/> すべてに同意した事実を記録する（FR-73）。
        /// 既存の記録は上書きされる（同意 = そのときに提示した規約一式に対する一括の意思表示のため）。
        /// </summary>
        public void RecordConsent(IReadOnlyList<TermsDefinition> acceptedTerms, string appVersion, DateTime acceptedAtUtc)
        {
            if (acceptedTerms == null)
            {
                throw new ArgumentNullException(nameof(acceptedTerms));
            }

            if (acceptedTerms.Count == 0)
            {
                throw new ArgumentException("acceptedTerms が空です。同意対象の規約を1件以上指定してください。", nameof(acceptedTerms));
            }

            if (string.IsNullOrEmpty(appVersion))
            {
                throw new ArgumentException("appVersion を指定してください。", nameof(appVersion));
            }

            var records = new List<ConsentRecord>(acceptedTerms.Count);
            foreach (var terms in acceptedTerms)
            {
                records.Add(new ConsentRecord(terms.TermsId, terms.Sha256Hash, acceptedAtUtc, appVersion));
            }

            _storage.Save(records);
        }

        /// <summary>
        /// 同意を撤回する（FR-75）。撤回後は <see cref="HasAcceptedAll"/> が必ず false を返すようになり、
        /// 次回の判定（次回起動時等）で再び同意画面の提示が必要になる。
        /// </summary>
        public void Revoke()
        {
            _storage.Save(Array.Empty<ConsentRecord>());
        }
    }
}
