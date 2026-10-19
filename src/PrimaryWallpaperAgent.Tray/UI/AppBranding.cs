namespace PrimaryWallpaperAgent.Tray;

internal static class AppBranding
{
    internal static Icon CreateApplicationIcon() => Load("Application", SystemInformation.IconSize);

    internal static Icon Load(string name, Size size)
    {
        using var stream = typeof(AppBranding).Assembly.GetManifestResourceStream("PrimaryWallpaperAgent.Tray.Branding." + name + ".ico")
            ?? throw new InvalidOperationException("Missing embedded icon: " + name);
        using var source = new Icon(stream, size);
        return (Icon)source.Clone(); // Icon owns its data after the resource stream closes.
    }
}

internal sealed class TrayIconSet : IDisposable
{
    internal Icon Running { get; } = AppBranding.Load("TrayRunning", SystemInformation.SmallIconSize);
    internal Icon Paused { get; } = AppBranding.Load("TrayPaused", SystemInformation.SmallIconSize);
    internal Icon Attention { get; } = AppBranding.Load("TrayAttention", SystemInformation.SmallIconSize);

    public void Dispose()
    {
        Running.Dispose();
        Paused.Dispose();
        Attention.Dispose();
    }
}
