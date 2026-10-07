using System;
using System.Collections.Generic;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// 「編集中（＝最後に適用した）<see cref="RoomSettings"/>」の唯一の保持先
    /// （issue #28、PR #92 レビュー H4 / 再レビュー H-2）。
    /// プロセス内キャッシュと <c>app-settings.json</c> の <c>room.lastApplied</c>
    /// （<see cref="AppSettings.RoomLastApplied"/>）の双方に保持する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 読み手は 2 つある。
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     Settings View（<c>TsumugiQuiz.UI.Views.Settings.SettingsView</c>）: 画面を開いたときの初期値
    ///     （ホストを開始していないとき。開始後は <c>RoomSettingsSync.Current</c> が優先）
    ///   </description></item>
    ///   <item><description>
    ///     <c>TsumugiQuiz.Network.RoomSettingsSync.OnNetworkSpawn</c>: ホスト開始時のルーム設定の初期値。
    ///     <c>host.role</c> だけは HostSetup View の直前のトグルが勝つので、呼び出し側が
    ///     <c>RoomSettingsDraft.Current.WithHostRole(HostRolePreference.Load())</c> と重ねる
    ///   </description></item>
    /// </list>
    /// <para>
    /// 永続化フォーマットは <see cref="RoomPresetJson.Serialize"/> と同じ（プリセット名は
    /// <see cref="DraftPresetName"/> という予約名を使うが、読み込み時には使わない）。
    /// </para>
    /// <para>
    /// 本クラスは <c>TsumugiQuiz.Room</c>（Unity API 非依存を目標とする層）にあるため、
    /// 失敗はログではなく <c>out warnings</c> で呼び出し側へ返す（呼び出し側がログ・画面表示を決める）。
    /// </para>
    /// </remarks>
    public static class RoomSettingsDraft
    {
        /// <summary>シリアライズ時に使う予約名（読み込み時には参照しない）。</summary>
        internal const string DraftPresetName = "__settings-draft__";

        /// <summary>
        /// 使う <see cref="AppSettingsStore"/> の生成方法。既定は実ファイル（<c>app-settings.json</c>）を使う実装。
        /// テストではこのプロパティにテスト用パスを積んだファクトリを差し替えられる
        /// （null を代入すると既定へ戻る）。
        /// </summary>
        internal static Func<AppSettingsStore> AppSettingsStoreFactory
        {
            get => _appSettingsStoreFactory;
            set => _appSettingsStoreFactory = value ?? CreateDefaultStore;
        }

        private static Func<AppSettingsStore> _appSettingsStoreFactory = CreateDefaultStore;

        private static AppSettingsStore CreateDefaultStore() => new AppSettingsStore();

        private static RoomSettings _cached;

        /// <summary>
        /// 現在の下書き。初回アクセス時に <c>app-settings.json</c> から読み込む。
        /// 無い・壊れている場合は「標準」プリセット（docs/room-settings.md §3）。
        /// 警告を受け取りたい場合は <see cref="Load"/> を使う。
        /// </summary>
        public static RoomSettings Current => Load(out _);

        /// <summary>
        /// <see cref="Current"/> と同じ値を返しつつ、読み込み時の警告
        /// （<c>AppPaths</c> 未設定・<c>room.lastApplied</c> の値がクランプされた等）を返す。
        /// </summary>
        /// <param name="warnings">警告の一覧（無ければ空）。</param>
        /// <returns>現在の下書き。</returns>
        public static RoomSettings Load(out IReadOnlyList<string> warnings)
        {
            if (_cached != null)
            {
                warnings = Array.Empty<string>();
                return _cached;
            }

            _cached = LoadFromAppSettings(out warnings);
            return _cached;
        }

        /// <summary>
        /// 下書きを更新し、プロセス内キャッシュと <c>app-settings.json</c> の両方に反映する。
        /// 永続化に失敗しても、プロセス内キャッシュへの反映は行う
        /// （少なくとも同一セッション中は編集内容を保てるようにする）。
        /// </summary>
        /// <param name="settings">新しい下書き。</param>
        /// <param name="warnings">永続化に失敗した理由（成功時は空）。</param>
        /// <returns>永続化まで成功したら true。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> が null のとき。</exception>
        public static bool Set(RoomSettings settings, out IReadOnlyList<string> warnings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _cached = settings;

            try
            {
                var store = AppSettingsStoreFactory();
                var current = store.Load().Settings;
                var json = RoomPresetJson.Serialize(new RoomPreset(DraftPresetName, settings));
                var saveResult = store.Save(current.WithRoomLastApplied(json));

                warnings = saveResult.Success ? Array.Empty<string>() : saveResult.Warnings;
                return saveResult.Success;
            }
            catch (InvalidOperationException ex)
            {
                warnings = new[]
                {
                    $"編集中の設定を保存できませんでした（このセッション中は保持されます）: {ex.Message}",
                };
                return false;
            }
        }

        /// <summary>
        /// プロセス内キャッシュを破棄し、次回アクセス時に <c>app-settings.json</c> から読み直すようにする
        /// （テスト専用）。
        /// </summary>
        internal static void ResetCacheForTesting() => _cached = null;

        private static RoomSettings LoadFromAppSettings(out IReadOnlyList<string> warnings)
        {
            try
            {
                var appSettings = AppSettingsStoreFactory().Load().Settings;

                if (!string.IsNullOrWhiteSpace(appSettings.RoomLastApplied))
                {
                    var parseResult = RoomPresetJson.Parse(appSettings.RoomLastApplied, DraftPresetName);
                    if (parseResult.Found)
                    {
                        // parseResult.Warnings（クランプ等）は RoomSettingsValidator が既定値へ
                        // フォールバックしたうえでの警告なので、ここでは致命的として扱わず、
                        // 復元できた設定をそのまま使う（警告だけ呼び出し側へ返す）。
                        warnings = parseResult.Warnings;
                        return parseResult.Preset.Settings;
                    }

                    // 壊れていて読めなかった（Found == false）。標準プリセットから始める
                    // （PR #92 再レビュー L-9）。
                    warnings = parseResult.Warnings.Count > 0
                        ? parseResult.Warnings
                        : new[] { "room.lastApplied を読み取れませんでした。標準プリセットから始めます。" };
                    return RoomPreset.Standard.Settings;
                }
            }
            catch (InvalidOperationException ex)
            {
                warnings = new[]
                {
                    $"編集中の設定を読み込めませんでした。標準プリセットから始めます: {ex.Message}",
                };
                return RoomPreset.Standard.Settings;
            }

            warnings = Array.Empty<string>();
            return RoomPreset.Standard.Settings;
        }
    }
}
