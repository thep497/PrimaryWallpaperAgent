namespace PrimaryWallpaperAgent.Tray;

internal sealed record ConfigSubmission(SelectedImage Image, bool StartWithWindows, string Language, bool ApplyWallpaper, int PollIntervalMs = PollingOptions.DefaultIntervalMs, string? Theme = null);