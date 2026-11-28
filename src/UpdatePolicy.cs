namespace AudioTaskbar;

/// <summary>When the app looks for updates and when it installs them (v5.4: automatic on launch, one merged button).</summary>
internal static class UpdatePolicy
{
    /// <summary>Check on launch (and every 6 h) unless the user turned auto-update off or this is a Store build.</summary>
    public static bool ShouldCheckOnLaunch(AppConfig cfg, bool storeEdition) => !storeEdition && cfg.AutoUpdateEnabled;

    /// <summary>
    /// Install after a successful check that found a newer version. Launch checks (not manual) install only when silent
    /// install is on and this is not a developer checkout; the merged "Kiểm tra &amp; cập nhật" button always installs.
    /// </summary>
    public static bool ShouldInstall(bool manual, bool installRequested, bool silentInstall, bool devCheckout)
        => installRequested || (!manual && silentInstall && !devCheckout);
}
