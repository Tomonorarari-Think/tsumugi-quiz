using System;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Room;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="NetworkBootstrap"/> のうち、アプリ設定（<c>app-settings.json</c>、<see cref="AppSettings"/>）を
    /// 起動時に読み、ネットワーク層へ反映する部分（issue #28 H5 / M1）。
    ///
    /// 反映先:
    /// <list type="bullet">
    ///   <item><description><c>upnp.*</c> / <c>network.ipLookupUrls</c> →
    ///     <see cref="HostConnectivityFactory"/>（<c>NetworkBootstrap.HostConnectivity.cs</c>）</description></item>
    ///   <item><description><c>question.prefetchCount</c> → <see cref="QuestionDistributor.PrefetchCount"/>
    ///     （スポーン済みのホストのみ。スポーン時の初期値は <see cref="CurrentAppSettings"/> を
    ///     <see cref="QuestionDistributor.OnNetworkSpawn"/> が読む）</description></item>
    /// </list>
    ///
    /// <c>network.port</c> は「ホスト開始時にどのポートで待ち受けるか」なので、起動時ではなく
    /// <c>HostSetupView</c>（<c>HostSetupPreferences.LoadPort</c>）が読む。
    /// <c>network.tickRate</c> は設定項目から外した（統括判断、PR #92 Phase 2。
    /// NGO の接続時ハッシュに含まれるため参加者間で値が揃わないと接続できない。docs/room-settings.md §2/§7）。
    /// </summary>
    public sealed partial class NetworkBootstrap
    {
        /// <summary>
        /// 使う <see cref="AppSettingsStore"/> の生成方法。既定は実ファイル（<c>app-settings.json</c>）を使う実装。
        /// テストではこのプロパティにテスト用パスを積んだファクトリを差し替えられる。
        /// </summary>
        internal static Func<AppSettingsStore> AppSettingsStoreFactory { get; set; } = () => new AppSettingsStore();

        /// <summary>
        /// 直近に反映したアプリ設定。<see cref="QuestionDistributor"/> がスポーン時の初期値として読む
        /// （Boot を経由していない構成では <see cref="NetworkBootstrap.Instance"/> 自体が null になる）。
        /// </summary>
        public AppSettings CurrentAppSettings { get; private set; } = AppSettings.Default;

        /// <summary>
        /// PlayMode テストが Boot シーンをロードする**前**に差し込む、<see cref="HostConnectivityFactory"/> の
        /// 上書き用ファクトリ（issue #161）。非 null の間、<see cref="ApplyAppSettings"/>
        /// （起動時の <c>Start()</c>・Settings 画面保存の両方から呼ばれる）が
        /// <see cref="RefreshHostConnectivityFactory"/> で本番実装へ上書きした直後に、
        /// 必ずこの値で再上書きする。
        ///
        /// 導入経緯: 以前は PlayMode テストが Boot シーン読み込み後・Main シーン表示前の「隙間」を
        /// 狙って <see cref="HostConnectivityFactory"/> に直接フェイクを差し込んでいたが、
        /// <c>NetworkBootstrap.Start()</c> 内の <c>SceneManager.LoadScene(Main)</c> 呼び出しが、
        /// 呼び出し元の <c>LoadSceneAsync(Boot)</c> の完了報告より前に（同一エンジンフレームの
        /// 処理中に連鎖的に）Main シーンの表示・<c>HostSetupView.OnShow</c>（→
        /// <see cref="HostConnectivity"/> への最初のアクセス）まで完了させてしまうことがあり
        /// （実測からの推測。公式文書での確認なし）、テストのフェイク差し込みが手遅れになる
        /// （実 UPnP・実 IP Lookup へ実際に出てしまう）レースが起きていた（実測: PlayMode 通し実行で
        /// 時々失敗し、失敗時のログに実グローバル IP・実 LAN IP が記録されていた）。このプロパティは、
        /// シーンロードそのものより前に静的に設定できるため、上記のようなフレーム内の実行順に
        /// 一切依存しない。
        ///
        /// 使い終わったら必ず null に戻すこと（他のテストに影響しないよう TearDown で必須）。
        /// </summary>
        internal static Func<HostConnectivityService> HostConnectivityFactoryOverrideForTesting { get; set; }

        /// <summary>
        /// 保存済みの <c>app-settings.json</c> を読んでネットワーク層へ反映する。
        /// <see cref="AppSettingsStore"/> が例外を投げた場合（<c>AppPaths</c> 未設定）は
        /// 何もしない（各既定値のまま動く）。
        /// </summary>
        private void ApplySavedAppSettings()
        {
            AppSettings settings;
            try
            {
                var loadResult = AppSettingsStoreFactory().Load();
                settings = loadResult.Settings;

                foreach (var warning in loadResult.Warnings)
                {
                    Debug.LogWarning($"[NetworkBootstrap] app-settings.json: {warning}");
                }
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning(
                    $"[NetworkBootstrap] app-settings.json を読み込めませんでした。既定値で起動します: {ex.Message}");
                return;
            }

            ApplyAppSettings(settings);
        }

        /// <summary>
        /// <paramref name="appSettings"/> をネットワーク層へ反映する（issue #28 H5 / M1）。
        /// 起動時（<c>Start</c>）と、Settings 画面がアプリ設定を保存したときに呼ぶ。
        /// </summary>
        /// <param name="appSettings">反映するアプリ設定。</param>
        public void ApplyAppSettings(AppSettings appSettings)
        {
            if (appSettings == null)
            {
                throw new ArgumentNullException(nameof(appSettings));
            }

            CurrentAppSettings = appSettings;

            RefreshHostConnectivityFactory(appSettings);

            // issue #161: テストが Boot シーンロード前に差し込んだ上書きがあれば、
            // 上記の本番実装への差し替えの直後に必ず勝たせる。
            if (HostConnectivityFactoryOverrideForTesting != null)
            {
                HostConnectivityFactory = HostConnectivityFactoryOverrideForTesting;
            }

            ApplyQuestionPrefetchCount(appSettings.QuestionPrefetchCount);

            Debug.Log(
                "[NetworkBootstrap] アプリ設定を反映しました"
                + $" upnpEnabled={appSettings.UpnpEnabled} ipLookupUrls={appSettings.IpLookupUrls.Count}"
                + $" questionPrefetchCount={appSettings.QuestionPrefetchCount}");
        }

        /// <summary>
        /// <c>question.prefetchCount</c> を、スポーン済みの <see cref="QuestionDistributor"/> へ反映する
        /// （issue #28 Phase 2）。先読みはサーバー（ホスト）側の挙動なので、ホストでないときは何もしない。
        /// まだ <see cref="GameSession"/> がスポーンされていない場合は、スポーン時に
        /// <see cref="CurrentAppSettings"/> から読まれる。
        /// </summary>
        private void ApplyQuestionPrefetchCount(int prefetchCount)
        {
            var session = Service != null ? Service.ActiveGameSession : null;
            if (session == null || !session.IsSpawned || !session.IsServer)
            {
                return;
            }

            var distributor = session.GetComponent<QuestionDistributor>();
            if (distributor == null)
            {
                return;
            }

            distributor.PrefetchCount = prefetchCount;
        }
    }
}
