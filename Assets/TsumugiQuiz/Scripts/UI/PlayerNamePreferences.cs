using System;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Room;
using UnityEngine;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// <c>player.name</c>（アプリ設定、docs/room-settings.md §1）の読み書き窓口。
    /// <see cref="Views.JoinView"/> と <see cref="Views.HostSetup.HostSetupView"/> の双方から使う。
    /// </summary>
    /// <remarks>
    /// PR #92 レビュー H2: 従来は両 View が同じ <see cref="PlayerPrefs"/> キー
    /// （<see cref="Views.JoinView.PlayerNamePrefsKey"/>）へ直接読み書きしていたが、
    /// <see cref="AppSettingsStore"/>（<c>app-settings.json</c>）へ一本化した。
    /// <see cref="Load"/> は <see cref="AppSettings.PlayerName"/> が空で、かつ旧 <see cref="PlayerPrefs"/>
    /// キーに値が残っている場合にだけ、その値を一度きり <c>app-settings.json</c> へ書き写す
    /// （移行後は旧キーを削除するため、次回以降は素通りする）。
    /// </remarks>
    internal static class PlayerNamePreferences
    {
        /// <summary>移行元の旧 <see cref="PlayerPrefs"/> キー（<see cref="Views.JoinView.PlayerNamePrefsKey"/> と同じ値）。</summary>
        internal const string LegacyPlayerPrefsKey = "TsumugiQuiz.PlayerName";

        /// <summary>
        /// 使う <see cref="AppSettingsStore"/> の生成方法。既定は実ファイル（<c>app-settings.json</c>）を使う実装。
        /// PlayMode テストではこのプロパティにテスト用パスを積んだファクトリを差し替えられる。
        /// </summary>
        internal static Func<AppSettingsStore> AppSettingsStoreFactory { get; set; } = () => new AppSettingsStore();

        /// <summary>
        /// 保存済みのプレイヤー名を読む。旧 <see cref="PlayerPrefs"/> からの一度きりの移行もここで行う。
        /// <see cref="AppSettingsStore"/> の既定コンストラクタは <c>AppPaths.DataRoot</c> が未設定（Boot を
        /// 経由しない構成）だと <see cref="InvalidOperationException"/> を投げるため、ここで捕捉し空文字を返す
        /// （PR #92 レビュー M4）。
        /// </summary>
        public static string Load()
        {
            try
            {
                var store = AppSettingsStoreFactory();
                var settings = store.Load().Settings;

                if (string.IsNullOrEmpty(settings.PlayerName) && PlayerPrefs.HasKey(LegacyPlayerPrefsKey))
                {
                    var legacyName = PlayerPrefs.GetString(LegacyPlayerPrefsKey, string.Empty);
                    if (PlayerNameValidator.TryNormalize(legacyName, out var normalized))
                    {
                        settings = settings.WithPlayerName(normalized);
                        store.Save(settings);
                    }

                    PlayerPrefs.DeleteKey(LegacyPlayerPrefsKey);
                    PlayerPrefs.Save();
                }

                return settings.PlayerName;
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning($"[PlayerNamePreferences] player.name の読み込みに失敗しました: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 正規化済みのプレイヤー名を保存する。呼び出し側で <see cref="PlayerNameValidator"/> による検証済みであること。
        /// <see cref="AppSettingsStore"/> の既定コンストラクタが未設定の <c>AppPaths</c> で例外を投げた場合も
        /// 保存を諦めるだけで、呼び出し側には投げない（M4）。
        /// </summary>
        public static void Save(string normalizedPlayerName)
        {
            try
            {
                var store = AppSettingsStoreFactory();
                var current = store.Load().Settings;
                store.Save(current.WithPlayerName(normalizedPlayerName));
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning($"[PlayerNamePreferences] player.name の保存に失敗しました: {ex.Message}");
            }
        }
    }
}
