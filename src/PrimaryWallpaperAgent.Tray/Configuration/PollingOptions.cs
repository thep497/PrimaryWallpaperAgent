namespace PrimaryWallpaperAgent.Tray;

internal static class PollingOptions
{
    internal const int DefaultIntervalMs = 1000;
    internal static ReadOnlySpan<int> SupportedIntervals => [500, 1000, 2000, 5000];
    internal static bool IsSupported(int intervalMs) => SupportedIntervals.Contains(intervalMs);
    internal static int Validate(int intervalMs) => IsSupported(intervalMs) ? intervalMs :
        throw new UiMessageException("InvalidPollInterval");
    // Give every supported interval time to collect multiple read-only samples.
    internal static int SmokeDurationMs(int intervalMs) => Math.Max(5000, Validate(intervalMs) * 4 + 1000);
}
