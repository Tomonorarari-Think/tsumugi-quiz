using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 再接続トークン（<see cref="SessionTokenRecord"/>）を
    /// <c>AppPaths.DataRoot/session-token.json</c>（既定は <c>Application.persistentDataPath</c> 配下。#71）に読み書きする
    /// <see cref="ISessionTokenStorage"/> の実装（issue #69）。
    /// JSON の形は Core 層へ漏らさないよう、ここだけで完結する DTO に変換する
    /// （<c>JsonConsentStorage</c>（#8）と同じ作法）。
    /// </summary>
    /// <remarks>
    /// 保存されるのは「どのホストから、どのトークンを、いつまで有効なものとしてもらったか」だけ。
    /// トークンはホストのプロセス寿命の間しか意味を持たないため、この記録が漏れても
    /// 同じホストが立ち続けている間に限って席を要求できるだけだが、平文で置く以上は
    /// ユーザープロファイル配下（<see cref="AppPaths.DataRoot"/>）から出さないこと。
    /// </remarks>
    public sealed class JsonSessionTokenStorage : ISessionTokenStorage
    {
        /// <summary>保存ファイル名。</summary>
        public const string FileName = "session-token.json";

        /// <summary>保存形式のバージョン（形式を変えたら上げる）。</summary>
        private const int CurrentFormatVersion = 1;

        // 未知のフィールドがあっても全体を捨てない（将来の追加フィールドと前方互換にする）。
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        private readonly string _filePath;

        /// <summary>既定の保存先を使う。</summary>
        public JsonSessionTokenStorage()
            : this(GetDefaultFilePath())
        {
        }

        /// <summary>保存先ファイルパスを明示的に指定する（テスト等で利用）。</summary>
        /// <exception cref="ArgumentException"><paramref name="filePath"/> が空。</exception>
        public JsonSessionTokenStorage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("filePath を指定してください。", nameof(filePath));
            }

            _filePath = filePath;
        }

        /// <summary>
        /// 既定の保存先（<c>AppPaths.DataRoot/session-token.json</c>。#71）。
        /// </summary>
        public static string GetDefaultFilePath() => AppPaths.Combine(FileName);

        /// <inheritdoc />
        public IReadOnlyList<SessionTokenRecord> Load()
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<SessionTokenRecord>();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var file = JsonConvert.DeserializeObject<SessionTokenFileDto>(json, SerializerSettings);
                if (file?.Entries == null)
                {
                    return Array.Empty<SessionTokenRecord>();
                }

                var records = new List<SessionTokenRecord>(file.Entries.Count);
                foreach (var entry in file.Entries)
                {
                    if (entry == null || !entry.TryToRecord(out var record))
                    {
                        // 手編集・旧版・破損。1 件だけ捨てて残りは活かす（再接続できないだけで致命的ではない）。
                        Debug.LogWarning($"[JsonSessionTokenStorage] 不正な再接続トークンの記録をスキップしました（{_filePath}）。");
                        continue;
                    }

                    records.Add(record);
                }

                return records;
            }
            catch (Exception ex)
            {
                // 壊れていたら「トークンを持っていない」として扱う（新規参加になるだけで、進行は止めない）。
                Debug.LogWarning(
                    $"[JsonSessionTokenStorage] 再接続トークンの読み込みに失敗しました: {_filePath}: {ex.Message}");
                return Array.Empty<SessionTokenRecord>();
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// 失敗（権限不足・ディスク満杯など）は **例外のまま呼び出し側へ返す**（レビュー M1）。
        /// ここで握りつぶすと、呼び出し側（<c>NetworkService.HandleSessionTokenReceived</c>）が
        /// 「保存しました」と記録してしまい、ログが実態と食い違う。保存失敗をどう扱うか
        /// （接続は続ける・警告を出す）は呼び出し側の責務。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="records"/> が null。</exception>
        /// <exception cref="System.IO.IOException">書き込みに失敗したとき。</exception>
        /// <exception cref="System.UnauthorizedAccessException">書き込み権限が無いとき。</exception>
        public void Save(IReadOnlyList<SessionTokenRecord> records)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            var file = new SessionTokenFileDto
            {
                Version = CurrentFormatVersion,
                Entries = new List<SessionTokenEntryDto>(records.Count),
            };

            foreach (var record in records)
            {
                if (!record.IsValid)
                {
                    continue;
                }

                file.Entries.Add(SessionTokenEntryDto.FromRecord(record));
            }

            var json = JsonConvert.SerializeObject(file, Formatting.Indented, SerializerSettings);

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 書き込み途中で強制終了しても壊れたファイルが残らないよう、一時ファイル経由で置き換える。
            var tempFilePath = _filePath + ".tmp";
            File.WriteAllText(tempFilePath, json);

            try
            {
                if (File.Exists(_filePath))
                {
                    File.Replace(tempFilePath, _filePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempFilePath, _filePath);
                }
            }
            catch
            {
                // 置き換えに失敗したら中途半端な .tmp を残さない（レビュー L3）。
                // 後始末そのものが失敗しても、元の例外を覆い隠さないよう握りつぶす。
                TryDeleteTempFile(tempFilePath);
                throw;
            }
        }

        /// <summary>置き換えに失敗したときの一時ファイルの後始末（失敗しても何もしない）。</summary>
        private static void TryDeleteTempFile(string tempFilePath)
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[JsonSessionTokenStorage] 一時ファイルを削除できませんでした: {tempFilePath}: {ex.Message}");
            }
        }

        /// <summary>session-token.json 全体の JSON 表現（キーは camelCase）。</summary>
        private sealed class SessionTokenFileDto
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("entries")]
            public List<SessionTokenEntryDto> Entries { get; set; }
        }

        /// <summary>session-token.json の 1 レコード分。</summary>
        private sealed class SessionTokenEntryDto
        {
            [JsonProperty("hostKey")]
            public string HostKey { get; set; }

            [JsonProperty("token")]
            public string Token { get; set; }

            [JsonProperty("expiresAtUtc")]
            public DateTime ExpiresAtUtc { get; set; }

            public static SessionTokenEntryDto FromRecord(SessionTokenRecord record)
                => new SessionTokenEntryDto
                {
                    HostKey = record.HostKey,
                    Token = record.Token.ToHex(),
                    ExpiresAtUtc = record.ExpiresAtUtc,
                };

            /// <summary>DTO を検証してレコードへ変換する（ファイルの内容は信用しない）。</summary>
            public bool TryToRecord(out SessionTokenRecord record)
            {
                record = default;

                if (!SessionTokenHostKey.IsValid(HostKey) || !SessionToken.TryParseHex(Token, out var token))
                {
                    return false;
                }

                // Unspecified を UTC として扱う正規化は SessionTokenRecord に寄せてある（レビュー L4）。
                record = new SessionTokenRecord(HostKey, token, ExpiresAtUtc);
                return true;
            }
        }
    }
}
