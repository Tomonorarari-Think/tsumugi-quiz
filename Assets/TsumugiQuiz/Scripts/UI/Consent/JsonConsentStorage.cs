using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TsumugiQuiz.Core;
using UnityEngine;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// <see cref="ConsentRecord"/> を <see cref="AppPaths.DataRoot"/>/consent.json に読み書きする
    /// <see cref="IConsentStorage"/> の実装（requirements.md FR-73）。
    /// 既定の保存先は <c>Application.persistentDataPath</c> 相当だが、実際の解決は
    /// <see cref="AppPaths"/>（#71）に委ねる。テスト実行時は worktree ごとに分離できる。
    /// JSON の形は Core 層に漏らさないよう、ここだけで完結する DTO（<see cref="ConsentRecordDto"/>）に変換する。
    /// </summary>
    public sealed class JsonConsentStorage : IConsentStorage
    {
        private const string FileName = "consent.json";

        // additionalProperties を許容する（未知フィールドがあっても同意記録全体を破棄しない）。
        // 将来フィールドを追加しても、古いバージョンが書いた JSON を読めるようにするため。
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        private readonly string _filePath;

        /// <summary>既定の保存先（<see cref="AppPaths.DataRoot"/>/consent.json）を使う。</summary>
        public JsonConsentStorage()
            : this(GetDefaultFilePath())
        {
        }

        /// <summary>保存先ファイルパスを明示的に指定する（テスト等で利用）。</summary>
        public JsonConsentStorage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("filePath を指定してください。", nameof(filePath));
            }

            _filePath = filePath;
        }

        public static string GetDefaultFilePath()
        {
            return AppPaths.Combine(FileName);
        }

        public IReadOnlyList<ConsentRecord> Load()
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<ConsentRecord>();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var dtos = JsonConvert.DeserializeObject<List<ConsentRecordDto>>(json, SerializerSettings);

                if (dtos == null)
                {
                    return Array.Empty<ConsentRecord>();
                }

                var records = new List<ConsentRecord>(dtos.Count);
                foreach (var dto in dtos)
                {
                    if (!dto.TryToRecord(out var record, out var error))
                    {
                        Debug.LogWarning($"[JsonConsentStorage] 不正なレコードをスキップしました（{_filePath}）: {error}");
                        continue;
                    }

                    records.Add(record);
                }

                return records.AsReadOnly();
            }
            catch (Exception ex)
            {
                // JSON の構文エラー・型不一致など。壊れた記録は「同意なし」として安全側に倒し、
                // 次回起動時に同意画面を再提示させる（詳細はログに残す。ユーザー向けには握りつぶさない）。
                Debug.LogError($"[JsonConsentStorage] 同意記録の読み込みに失敗しました（破損している可能性があります）: {_filePath}: {ex.Message}");
                return Array.Empty<ConsentRecord>();
            }
        }

        public void Save(IReadOnlyList<ConsentRecord> records)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            var dtos = records.Select(ConsentRecordDto.FromRecord).ToList();
            var json = JsonConvert.SerializeObject(dtos, Formatting.Indented, SerializerSettings);

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 書き込み途中でアプリが強制終了しても consent.json が壊れた状態のまま残らないよう、
            // 一時ファイルに書き切ってから置き換える（アトミックな更新。M-2）。
            var tempFilePath = _filePath + ".tmp";
            File.WriteAllText(tempFilePath, json);

            if (File.Exists(_filePath))
            {
                File.Replace(tempFilePath, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempFilePath, _filePath);
            }
        }

        /// <summary>consent.json 1レコード分の JSON 表現。camelCase のキーで保存する。</summary>
        private sealed class ConsentRecordDto
        {
            [JsonProperty("termsId")]
            public string TermsId { get; set; }

            [JsonProperty("sha256")]
            public string Sha256Hash { get; set; }

            [JsonProperty("acceptedAtUtc")]
            public DateTime AcceptedAtUtc { get; set; }

            [JsonProperty("appVersion")]
            public string AppVersion { get; set; }

            public static ConsentRecordDto FromRecord(ConsentRecord record)
            {
                return new ConsentRecordDto
                {
                    TermsId = record.TermsId,
                    Sha256Hash = record.Sha256Hash,
                    AcceptedAtUtc = record.AcceptedAtUtc,
                    AppVersion = record.AppVersion,
                };
            }

            public bool TryToRecord(out ConsentRecord record, out string error)
            {
                if (string.IsNullOrEmpty(TermsId) || string.IsNullOrEmpty(Sha256Hash) || string.IsNullOrEmpty(AppVersion))
                {
                    record = null;
                    error = "termsId / sha256 / appVersion のいずれかが欠けています。";
                    return false;
                }

                // Newtonsoft の既定設定（DateTimeZoneHandling.RoundtripKind）では "Z" 付き ISO 文字列は
                // Kind=Utc として復元されるはずだが、手編集・旧バージョンの記録などで Kind が
                // Unspecified になっているケースに備えて明示的に UTC 扱いへ寄せる。
                var acceptedAtUtc = AcceptedAtUtc.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(AcceptedAtUtc, DateTimeKind.Utc)
                    : AcceptedAtUtc.ToUniversalTime();

                record = new ConsentRecord(TermsId, Sha256Hash, acceptedAtUtc, AppVersion);
                error = null;
                return true;
            }
        }
    }
}
