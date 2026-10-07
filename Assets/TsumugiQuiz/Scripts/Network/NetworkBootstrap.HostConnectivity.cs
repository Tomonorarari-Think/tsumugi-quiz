using System;
using TsumugiQuiz.Network.Nat;
using TsumugiQuiz.Room;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="NetworkBootstrap"/> のうち、<see cref="HostConnectivityService"/>（UPnP・グローバル IP・
    /// 参加コード）の生成・保持・解放を行う部分クラス（issue #5 レビュー C-1）。
    ///
    /// 以前は HostSetup View がこのサービスを生成・破棄していたが、View の Show/Hide（画面遷移）に
    /// ライフサイクルを結び付けると、ホストを開始したまま Lobby 等へ進んだ時点でポートマッピングの
    /// 更新ループ（<see cref="Nat.PortMappingService"/> の renew）が止まってしまう問題があった。
    /// Boot シーンに常駐する本クラスが保持することで、ホストが実際に停止する
    /// （<see cref="NetworkService.Stopped"/>）か、アプリが終了する
    /// （<see cref="HostConnectivityService"/> 自身が購読している <c>Application.quitting</c>）まで
    /// 生き続ける。View 側（<c>HostSetupView</c>）は <see cref="HostConnectivity"/> を参照して
    /// イベント購読・<c>ResolveAsync</c> の呼び出しを行うだけで、生成・破棄には関与しない。
    /// </summary>
    public sealed partial class NetworkBootstrap
    {
        private HostConnectivityService _hostConnectivity;

        /// <summary>
        /// <see cref="HostConnectivity"/> の生成方法。既定は <c>app-settings.json</c>（<see cref="AppSettings"/>）の
        /// <c>upnp.*</c> / <c>network.ipLookupUrls</c> を <see cref="NatOptionsAppSettingsAdapter"/> で
        /// <see cref="NatOptions"/> に変換して渡す（issue #28 H5。<c>AppPaths</c> が未設定・読み込み失敗時は
        /// <see cref="NatOptions.Default"/> にフォールバックする）。
        /// PlayMode テストでは Boot シーン読み込み後・ホスト開始前にこのプロパティへフェイクを積んだ
        /// ファクトリを差し替えることで、実ネットワークに一切出ずに検証できる。
        /// </summary>
        public Func<HostConnectivityService> HostConnectivityFactory { get; set; } = DefaultHostConnectivityFactory;

        private static HostConnectivityService DefaultHostConnectivityFactory()
            => new HostConnectivityService(ResolveNatOptionsFromAppSettings());

        /// <summary>
        /// 保存済みの <c>app-settings.json</c> から <see cref="NatOptions"/> を読む。
        /// <see cref="AppSettingsStore"/> の既定コンストラクタが投げる <see cref="InvalidOperationException"/>
        /// （<c>AppPaths</c> 未設定）は握りつぶし、<see cref="NatOptions.Default"/> にフォールバックする。
        /// </summary>
        private static NatOptions ResolveNatOptionsFromAppSettings()
        {
            try
            {
                var settings = AppSettingsStoreFactory().Load().Settings;
                return NatOptionsAppSettingsAdapter.FromAppSettings(settings);
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning($"[NetworkBootstrap] app-settings.json からの NAT 設定読み込みに失敗しました。既定値を使用します: {ex.Message}");
                return NatOptions.Default;
            }
        }

        /// <summary>
        /// 最新の <see cref="AppSettings"/> から <see cref="HostConnectivityFactory"/> を作り直す
        /// （issue #28 H5）。Settings 画面がアプリ設定を保存したときに呼ぶ想定。
        /// ホストが実行中（<see cref="NetworkService.IsListening"/> / <see cref="NetworkService.IsClient"/>）の
        /// 間は、キャッシュ済み <see cref="HostConnectivity"/> のポートマッピングを壊さないよう、
        /// 次回のホスト開始まで反映を遅らせる（ファクトリの差し替えだけ行い、既存インスタンスは解放しない）。
        /// </summary>
        public void RefreshHostConnectivityFactory(AppSettings appSettings)
        {
            var natOptions = NatOptionsAppSettingsAdapter.FromAppSettings(appSettings);
            HostConnectivityFactory = () => new HostConnectivityService(natOptions);

            if (Service != null && (Service.IsListening || Service.IsClient))
            {
                return;
            }

            if (_hostConnectivity != null)
            {
                var stale = _hostConnectivity;
                _hostConnectivity = null;
                stale.Dispose();
            }
        }

        /// <summary>
        /// 常駐している <see cref="HostConnectivityService"/>。初回アクセス時に
        /// <see cref="HostConnectivityFactory"/> で生成する（ホストを開始していない間は無駄に
        /// 生成しない）。<see cref="NetworkService.Stopped"/> で破棄されたあとに再度参照すると、
        /// 新しいインスタンスが作られる（次回のホスト開始に備える）。
        /// </summary>
        public HostConnectivityService HostConnectivity => _hostConnectivity ??= HostConnectivityFactory();

        /// <summary>
        /// <see cref="NetworkService.Stopped"/> のハンドラ。保持している
        /// <see cref="HostConnectivityService"/> のポートマッピングを解放してから破棄し、
        /// 次回のホスト開始で新しいインスタンスが作られるようにする（#5 C-1）。
        /// </summary>
        private async void HandleNetworkServiceStopped()
        {
            var connectivity = _hostConnectivity;
            _hostConnectivity = null;

            if (connectivity == null)
            {
                return;
            }

            try
            {
                await connectivity.ReleaseAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkBootstrap] ポートマッピングの解放に失敗しました: {ex.Message}");
            }
            finally
            {
                connectivity.Dispose();
            }
        }
    }
}
