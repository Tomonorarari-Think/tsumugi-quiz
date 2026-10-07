using TsumugiQuiz.Room;

namespace TsumugiQuiz.Network.Nat
{
    /// <summary>
    /// <see cref="AppSettings"/>（<c>TsumugiQuiz.Room</c>、issue #26）の <c>upnp.*</c> / <c>network.ipLookupUrls</c>
    /// から <see cref="NatOptions"/> を組み立てるアダプタ（issue #28 H5）。
    /// <c>TsumugiQuiz.Network</c> asmdef は <c>TsumugiQuiz.Room</c> を参照済みのため、ここに置く
    /// （docs/room-settings.md §6 の「network.* / upnp.* 側のアダプタは #27/#28 のどちらでも実装可能」）。
    /// <see cref="NetworkBootstrap"/> の既定 <see cref="NetworkBootstrap.HostConnectivityFactory"/> が使うほか、
    /// UI 層の <c>TsumugiQuiz.UI.Views.Settings.AppSettingsAdapters.ToNatOptions</c> もここへ委譲する
    /// （変換ロジックを二重に持たない）。
    /// </summary>
    public static class NatOptionsAppSettingsAdapter
    {
        /// <summary><paramref name="settings"/> の <c>upnp.*</c> / <c>network.ipLookupUrls</c> を <see cref="NatOptions"/> に変換する。</summary>
        public static NatOptions FromAppSettings(AppSettings settings)
        {
            if (settings == null)
            {
                return NatOptions.Default;
            }

            return NatOptions.Default.With(
                settings.UpnpEnabled,
                settings.UpnpDiscoveryTimeoutMs,
                settings.UpnpMappingLifetimeSec,
                settings.UpnpRenewIntervalMs,
                settings.IpLookupUrls);
        }
    }
}
