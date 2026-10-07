using System.Collections.Generic;
using System.Linq;
using TsumugiQuiz.Core.Network;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// <see cref="AppSettingsInput"/>（設定ファイル由来の生の値）を検証し、範囲外・不正な値を既定値へ
    /// クランプ／破棄した <see cref="AppSettings"/> を組み立てる（#26 統括判断 H1/H2）。
    /// </summary>
    /// <remarks>
    /// 各数値・URL のクランプは <see cref="AppSettings.Create"/> にすでに実装されているため、
    /// ここでは「クランプ前後で値が変わったか」を比較して警告を組み立てる（クランプ処理の二重実装を避ける）。
    /// <c>player.name</c> のみ <see cref="PlayerNameValidator"/> による書式検証が必要なため個別に扱う。
    /// </remarks>
    public static class AppSettingsValidator
    {
        /// <summary>
        /// 生の入力値を検証・クランプし、<see cref="AppSettings"/> と警告一覧を返す。
        /// null が渡された場合はすべて既定値として扱う。
        /// </summary>
        public static AppSettingsValidationResult Validate(AppSettingsInput input)
        {
            input ??= new AppSettingsInput();
            var warnings = new List<string>();

            var hostRole = HostRoles.TryParse(input.HostRole, out var parsedHostRole)
                ? parsedHostRole
                : AppSettings.DefaultHostRole;

            var settings = AppSettings.Create(
                input.PlayerName,
                input.NetworkPort ?? AppSettings.DefaultNetworkPort,
                input.NetworkIpLookupUrls,
                input.UpnpEnabled ?? AppSettings.DefaultUpnpEnabled,
                input.UpnpDiscoveryTimeoutMs ?? AppSettings.DefaultUpnpDiscoveryTimeoutMs,
                input.UpnpMappingLifetimeSec ?? AppSettings.DefaultUpnpMappingLifetimeSec,
                input.UpnpRenewIntervalMs ?? AppSettings.DefaultUpnpRenewIntervalMs,
                input.QuestionPrefetchCount ?? AppSettings.DefaultQuestionPrefetchCount,
                input.TtsSpeakerName ?? AppSettings.DefaultTtsSpeakerName,
                input.TtsStyleName ?? AppSettings.DefaultTtsStyleName,
                input.TtsCacheMaxBytes ?? AppSettings.DefaultTtsCacheMaxBytes,
                input.TtsCacheMaxEntries ?? AppSettings.DefaultTtsCacheMaxEntries,
                input.TtsAssetPathOverride,
                input.CharacterEnabled ?? AppSettings.DefaultCharacterEnabled,
                input.RoomLastApplied ?? AppSettings.DefaultRoomLastApplied,
                hostRole);

            WarnIfInvalidPlayerName(warnings, input.PlayerName);
            WarnIfInvalidHostRole(warnings, input.HostRole);
            WarnIfClamped(warnings, "network.port", input.NetworkPort, settings.NetworkPort);
            WarnIfUnsupportedTickRate(warnings, input.NetworkTickRate);
            WarnIfClamped(warnings, "upnp.discoveryTimeoutMs", input.UpnpDiscoveryTimeoutMs, settings.UpnpDiscoveryTimeoutMs);
            WarnIfClamped(warnings, "upnp.mappingLifetimeSec", input.UpnpMappingLifetimeSec, settings.UpnpMappingLifetimeSec);
            WarnIfClamped(warnings, "upnp.renewIntervalMs", input.UpnpRenewIntervalMs, settings.UpnpRenewIntervalMs);
            WarnIfClamped(warnings, "question.prefetchCount", input.QuestionPrefetchCount, settings.QuestionPrefetchCount);
            WarnIfClampedLong(warnings, "tts.cacheMaxBytes", input.TtsCacheMaxBytes, settings.TtsCacheMaxBytes);
            WarnIfClamped(warnings, "tts.cacheMaxEntries", input.TtsCacheMaxEntries, settings.TtsCacheMaxEntries);
            WarnIfBlankString(warnings, "tts.speakerName", input.TtsSpeakerName);
            WarnIfBlankString(warnings, "tts.styleName", input.TtsStyleName);
            WarnInvalidIpLookupUrls(warnings, input.NetworkIpLookupUrls);

            return new AppSettingsValidationResult(settings, warnings.AsReadOnly());
        }

        /// <summary>
        /// <c>network.tickRate</c> は設定項目から外した（統括判断、PR #92 Phase 2）。
        /// 設定ファイルに残っていた場合は「未対応なので無視した」ことを警告で知らせる
        /// （未知のキーとして黙って捨てない）。
        /// </summary>
        /// <remarks>
        /// NGO 2.13.2 の <c>NetworkConfig.GetConfig()</c> は接続時の設定ハッシュに <c>TickRate</c> を含むため
        /// （<c>Runtime/Configuration/NetworkConfig.cs</c>）、各 PC ローカルのアプリ設定で変えられるようにすると
        /// 値の違う参加者が接続できなくなる。docs/room-settings.md §2 / §7 を参照。
        /// </remarks>
        private static void WarnIfUnsupportedTickRate(List<string> warnings, int? rawTickRate)
        {
            if (!rawTickRate.HasValue)
            {
                return;
            }

            warnings.Add(
                $"network.tickRate は現在サポートしていないため無視しました（常に {AppSettings.FixedNetworkTickRate}）。"
                + "参加者ごとに値が違うと接続できなくなるためです。");
        }

        /// <summary>
        /// <c>player.name</c> の書式検証の警告だけを追加する（実際の正規化・既定値へのフォールバックは
        /// <see cref="AppSettings.Create"/> がすでに行っているため、ここでは判定のみ、#26 統括判断 M2）。
        /// 未指定（null / 空）は既定値として扱う正常系なので警告は出さない。
        /// </summary>
        private static void WarnIfInvalidPlayerName(List<string> warnings, string rawPlayerName)
        {
            if (string.IsNullOrEmpty(rawPlayerName))
            {
                return;
            }

            if (!PlayerNameValidator.TryNormalize(rawPlayerName, out _))
            {
                warnings.Add(
                    $"player.name の値 \"{rawPlayerName}\" は無効です（{PlayerNameValidator.RuleSummary}）。既定値（空欄）に戻しました。");
            }
        }

        /// <summary>
        /// <c>host.role</c> が <c>"player"</c> / <c>"moderator"</c> のいずれとも一致しない場合に警告を追加する
        /// （実際の既定値へのフォールバックは <see cref="Validate"/> がすでに行っている）。未指定（null / 空）は
        /// 既定値として扱う正常系なので警告は出さない。
        /// </summary>
        private static void WarnIfInvalidHostRole(List<string> warnings, string rawHostRole)
        {
            if (string.IsNullOrEmpty(rawHostRole))
            {
                return;
            }

            if (!HostRoles.TryParse(rawHostRole, out _))
            {
                warnings.Add(
                    $"host.role の値 \"{rawHostRole}\" は不正なため既定値（{HostRoles.PlayerKey}）にクランプしました。");
            }
        }

        private static void WarnIfClamped(List<string> warnings, string keyName, int? rawValue, int finalValue)
        {
            if (rawValue.HasValue && rawValue.Value != finalValue)
            {
                warnings.Add($"{keyName} の値 {rawValue.Value} は許容範囲外のため {finalValue} にクランプしました。");
            }
        }

        private static void WarnIfClampedLong(List<string> warnings, string keyName, long? rawValue, long finalValue)
        {
            if (rawValue.HasValue && rawValue.Value != finalValue)
            {
                warnings.Add($"{keyName} の値 {rawValue.Value} は許容範囲外のため {finalValue} にクランプしました。");
            }
        }

        private static void WarnIfBlankString(List<string> warnings, string keyName, string rawValue)
        {
            if (rawValue != null && string.IsNullOrWhiteSpace(rawValue))
            {
                warnings.Add($"{keyName} が空白文字のみのため既定値を使用しました。");
            }
        }

        private static void WarnInvalidIpLookupUrls(List<string> warnings, List<string> rawUrls)
        {
            if (rawUrls == null)
            {
                return;
            }

            var invalidUrls = rawUrls.Where(url => !AppSettings.IsAllowedLookupUrl(url)).ToList();
            foreach (var invalidUrl in invalidUrls)
            {
                warnings.Add($"network.ipLookupUrls の値 \"{invalidUrl}\" は https の絶対 URL ではないため無視しました。");
            }

            if (invalidUrls.Count == rawUrls.Count && rawUrls.Count > 0)
            {
                warnings.Add("network.ipLookupUrls に有効な URL が1件も無かったため既定値を使用しました。");
            }
        }
    }
}
