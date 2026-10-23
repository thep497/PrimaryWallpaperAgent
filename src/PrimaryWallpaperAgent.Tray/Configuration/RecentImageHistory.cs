namespace PrimaryWallpaperAgent.Tray;

// One shared four-picture library. Current role images are referenced separately
// and remain available even after falling out of that library. Black is not history.
internal sealed record RecentImage(string PhotoFile, string Sha256, string? Label = null);

internal static class RecentImageHistory
{
    internal const int Limit = 4;
    internal static RecentImage? Current(TrayConfig config, WallpaperTarget target) => target == WallpaperTarget.Primary
        ? config.HasChosenImage ? new(config.PhotoFile, config.PhotoSha256, config.PhotoLabel) : null
        : config.SecondaryImage is { } image ? new(image.PhotoFile, image.Sha256, image.Label) : null;
    internal static RecentImage[] Library(TrayConfig config) => (config.RecentImages ?? [])
        .Concat(new[] { Current(config, WallpaperTarget.Primary), Current(config, WallpaperTarget.Secondary) }.OfType<RecentImage>())
        .DistinctBy(image => image.Sha256, StringComparer.OrdinalIgnoreCase).Take(Limit).ToArray();
    internal static RecentImage[] List(TrayConfig config) => List(config, WallpaperTarget.Primary);
    internal static RecentImage[] List(TrayConfig config, WallpaperTarget target)
    {
        var current = Current(config, target);
        return (current is null ? [] : new[] { current }).Concat(Library(config))
            .DistinctBy(image => image.Sha256, StringComparer.OrdinalIgnoreCase).Take(Limit).ToArray();
    }
    internal static TrayConfig Remember(TrayConfig before, TrayConfig chosen, WallpaperTarget target = WallpaperTarget.Primary) => chosen with
    {
        RecentImages = new[] { Current(chosen, target) }.OfType<RecentImage>().Concat(Library(before))
            .DistinctBy(image => image.Sha256, StringComparer.OrdinalIgnoreCase).Take(Limit).ToArray()
    };
    internal static SelectedImage Read(SettingsStore store, RecentImage image)
    {
        try
        {
            var selected = SelectedImage.Read(store.Asset(image.PhotoFile, image.Sha256));
            if (!selected.Sha256.Equals(image.Sha256, StringComparison.OrdinalIgnoreCase)) throw new UiMessageException("AssetHashChanged");
            return selected with { FriendlyName = image.Label ?? selected.FriendlyName };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or UiMessageException or ArgumentException or OutOfMemoryException)
        { throw new UiMessageException("RecentImageUnavailable", error); }
    }
}