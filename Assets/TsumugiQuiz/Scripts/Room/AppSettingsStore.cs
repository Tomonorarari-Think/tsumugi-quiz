using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// アプリ設定（<see cref="AppSettings"/>）の読み書き（docs/room-settings.md §0）。
    /// 保存先は既定で <see cref="AppPaths.DataRoot"/>/app-settings.json（#71）。
    /// <c>AppPaths</c> 自体は <c>Application.persistentDataPath</c> の値を受け取るだけの薄いラッパーで
    /// Unity API には依存しないため、Unity 非依存の <c>TsumugiQuiz.Room</c>（<c>TsumugiQuiz.Core</c> を参照）から
    /// 直接使える（実際の <c>Application.persistentDataPath</c> 解決・環境変数分離は呼び出し側・#71 が担う）。
    /// </summary>
    public sealed class AppSettingsStore
    {
        /// <summary><c>app-settings.json</c> の既定のファイル名（<see cref="AppPaths.Combine"/> と組み合わせて使う）。</summary>
        public const string FileName = "app-settings.json";

        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        private readonly string _filePath;
        private readonly IRoomFileSystem _fileSystem;

        /// <summary>
        /// ストアを生成する。
        /// </summary>
        /// <param name="filePath">
        /// <c>app-settings.json</c> の絶対パス。null / 空なら <see cref="GetDefaultFilePath"/>
        /// （<see cref="AppPaths.DataRoot"/>/app-settings.json）を使う。
        /// </param>
        /// <param name="fileSystem">ファイル I/O の実装。null なら <see cref="FileSystemRoomFileSystem"/>。</param>
        public AppSettingsStore(string filePath = null, IRoomFileSystem fileSystem = null)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath) ? GetDefaultFilePath() : filePath;
            _fileSystem = fileSystem ?? new FileSystemRoomFileSystem();
        }

        /// <summary>
        /// 既定の保存先（<see cref="AppPaths.DataRoot"/>/app-settings.json）。
        /// <see cref="AppPaths.DataRoot"/> が未設定（<see cref="AppPaths.Configure"/> / 環境変数 /
        /// <see cref="AppPaths.ConfigureDefault"/> のいずれも無い状態）で呼ぶと
        /// <see cref="InvalidOperationException"/> を投げる（#26 統括判断 L18）。
        /// 通常は Boot（<c>TsumugiQuiz.Network.AppPathsBootstrap</c>）が起動時に <c>ConfigureDefault</c> を
        /// 呼ぶため問題にならないが、Boot を経由しない単体テスト等では先に <see cref="AppPaths.Configure"/> /
        /// <c>ConfigureDefault</c> を呼ぶこと。
        /// </summary>
        public static string GetDefaultFilePath() => AppPaths.Combine(FileName);

        /// <summary>
        /// 設定ファイルを読み込む。ファイルが無い場合・JSON が壊れている場合・読み込み中に
        /// I/O 例外（<see cref="IOException"/> / <see cref="UnauthorizedAccessException"/>）が起きた場合も
        /// 例外を投げず、既定値ベースの <see cref="AppSettingsLoadResult"/> を返す
        /// （読み上げ・接続設定はゲームの必須要素ではないため、#26 統括判断 H4）。
        /// </summary>
        public AppSettingsLoadResult Load()
        {
            if (!_fileSystem.FileExists(_filePath))
            {
                return new AppSettingsLoadResult(AppSettings.Default, Array.Empty<string>());
            }

            try
            {
                var json = _fileSystem.ReadAllText(_filePath);
                var token = string.IsNullOrWhiteSpace(json) ? null : JToken.Parse(json);
                if (token == null || token.Type != JTokenType.Object)
                {
                    var emptyMessage = "app-settings.json の内容が空、またはオブジェクトではありません。既定値を使用しました。";
                    return new AppSettingsLoadResult(AppSettings.Default, new[] { emptyMessage });
                }

                var warnings = new List<string>();
                var input = AppSettingsInput.FromJson((JObject)token, warnings);
                var validation = AppSettingsValidator.Validate(input);
                warnings.AddRange(validation.Warnings);

                return new AppSettingsLoadResult(validation.Settings, warnings.AsReadOnly());
            }
            catch (JsonException ex)
            {
                var message = $"app-settings.json の解析に失敗しました。既定値を使用しました: {ex.GetType().Name}: {ex.Message}";
                return new AppSettingsLoadResult(AppSettings.Default, new[] { message });
            }
            catch (Exception ex) when (IsRecoverableIoException(ex))
            {
                var message = $"app-settings.json の読み込みに失敗しました。既定値を使用しました: {ex.GetType().Name}: {ex.Message}";
                return new AppSettingsLoadResult(AppSettings.Default, new[] { message });
            }
        }

        /// <summary>
        /// 設定ファイルを保存する（既存があれば上書き）。フォルダが無ければ作成する。
        /// I/O 例外（<see cref="IOException"/> / <see cref="UnauthorizedAccessException"/>）は投げず、
        /// <see cref="AppSettingsSaveResult.Success"/> = false として返す（#26 統括判断 H4）。
        /// </summary>
        public AppSettingsSaveResult Save(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var input = new AppSettingsInput
            {
                PlayerName = settings.PlayerName,
                NetworkPort = settings.NetworkPort,
                NetworkIpLookupUrls = new List<string>(settings.IpLookupUrls),
                UpnpEnabled = settings.UpnpEnabled,
                UpnpDiscoveryTimeoutMs = settings.UpnpDiscoveryTimeoutMs,
                UpnpMappingLifetimeSec = settings.UpnpMappingLifetimeSec,
                UpnpRenewIntervalMs = settings.UpnpRenewIntervalMs,
                QuestionPrefetchCount = settings.QuestionPrefetchCount,
                TtsSpeakerName = settings.TtsSpeakerName,
                TtsStyleName = settings.TtsStyleName,
                TtsCacheMaxBytes = settings.TtsCacheMaxBytes,
                TtsCacheMaxEntries = settings.TtsCacheMaxEntries,
                TtsAssetPathOverride = settings.TtsAssetPathOverride,
                CharacterEnabled = settings.CharacterEnabled,
                RoomLastApplied = settings.RoomLastApplied,
                HostRole = HostRoles.ToKey(settings.HostRole),
            };

            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    _fileSystem.EnsureDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(input, Formatting.Indented, SerializerSettings);
                _fileSystem.WriteAllText(_filePath, json);
                return AppSettingsSaveResult.Succeeded;
            }
            catch (Exception ex) when (IsRecoverableIoException(ex))
            {
                return AppSettingsSaveResult.Failed($"app-settings.json の保存に失敗しました: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 呼び出し元に投げず警告として扱ってよい I/O 例外か（ディスク由来の一時的な失敗のみを対象とする。
        /// プログラムの不具合を示す例外まで握りつぶさないよう種類を絞る）。
        /// </summary>
        private static bool IsRecoverableIoException(Exception ex) => ex is IOException || ex is UnauthorizedAccessException;
    }
}
