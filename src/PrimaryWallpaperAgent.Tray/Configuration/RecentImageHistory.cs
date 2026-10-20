namespace PrimaryWallpaperAgent.Tray;

// Only explicit, successful selections enter settings. Historical paths are never
// added to the wallpaper policy's accepted aliases.
internal sealed record RecentImage(string PhotoFile, string Sha256, string? Label = null);

internal static class RecentImageHistory
{
    internal const int Limit = 4;

    internal static RecentImage[] List(TrayConfig config)
    {
        if (!config.HasChosenImage) return [];
        return new[] { new RecentImage(config.PhotoFile, config.PhotoSha256, config.PhotoLabel) }
            .Concat(config.RecentImages ?? [])
            .DistinctBy(image => image.Sha256, StringComparer.OrdinalIgnoreCase)
            .Take(Limit).ToArray();
    }

    internal static TrayConfig Remember(TrayConfig before, TrayConfig chosen) => chosen with
    {
        RecentImages = new[] { new RecentImage(chosen.PhotoFile, chosen.PhotoSha256, chosen.PhotoLabel) }
            .Concat(List(before))
            .DistinctBy(image => image.Sha256, StringComparer.OrdinalIgnoreCase)
            .Take(Limit).ToArray()
    };

    internal static SelectedImage Read(SettingsStore store, RecentImage image)
    {
        try
        {
            var selected = SelectedImage.Read(store.Asset(image.PhotoFile, image.Sha256));
            if (!selected.Sha256.Equals(image.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UiMessageException("AssetHashChanged");
            return selected with { FriendlyName = image.Label ?? selected.FriendlyName };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or UiMessageException or ArgumentException or OutOfMemoryException)
        { throw new UiMessageException("RecentImageUnavailable", error); }
    }
}
