namespace PrimaryWallpaperAgent.Tray;

internal enum WallpaperTarget { Primary, Secondary }
internal sealed record ConfigSubmission(SelectedImage Image, bool StartWithWindows, string Language, bool ApplyWallpaper,
    int PollIntervalMs = PollingOptions.DefaultIntervalMs, string? Theme = null,
    WallpaperTarget Target = WallpaperTarget.Primary, bool UseBlack = false);
internal sealed record WallpaperRoleActions(
    Func<WallpaperTarget, RecentImage?> Selection,
    Func<WallpaperTarget, RecentImage[]> Recent,
    Func<WallpaperTarget, bool> CanUndo,
    Func<WallpaperTarget, SelectedImage> Undo,
    Func<bool> SecondaryIsBlack);