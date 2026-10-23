using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal sealed record TrayConfig(int SchemaVersion, string PhotoFile, string PhotoSha256,
    string BlackFile, string BlackSha256, string[] AcceptedPaths, bool StartWithWindows = false,
    string? PhotoLabel = null, string? CurrentSourcePath = null, string Language = "th", int PollIntervalMs = PollingOptions.DefaultIntervalMs, bool HasChosenImage = true, RecentImage[]? RecentImages = null, string Theme = "system", SavedWallpaper? SecondaryImage = null);

internal sealed record SavedWallpaper(string PhotoFile, string Sha256, string? Label = null, string? SourcePath = null);

internal sealed record SelectedImage(string SourcePath, byte[] Bytes, string Extension, string Sha256, string? FriendlyName = null)
{
    internal static SelectedImage Read(string path)
    {
        // Read only the explicit file chosen by the user; never scan its directory.
        using var file = File.OpenRead(path);
        if (file.Length is <= 0 or > 32 * 1024 * 1024)
            throw new UiMessageException("ImageTooLarge");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
        if ((long)image.Width * image.Height > 40_000_000)
            throw new UiMessageException("ImageTooManyPixels");
        var extension = image.RawFormat.Guid == ImageFormat.Jpeg.Guid ? ".jpg" :
            image.RawFormat.Guid == ImageFormat.Png.Guid ? ".png" :
            image.RawFormat.Guid == ImageFormat.Bmp.Guid ? ".bmp" :
            throw new UiMessageException("ImageFormatUnsupported");
        return new(Path.GetFullPath(path), bytes, extension, Convert.ToHexString(SHA256.HashData(bytes)), Path.GetFileName(path));
    }
    internal Bitmap Preview()
    {
        using var stream = new MemoryStream(Bytes, writable: false);
        using var image = Image.FromStream(stream);
        return new Bitmap(image); // Releases the source file before previewing.
    }
}

internal interface ISettingsPersistence
{
    byte[] Read();
    void Write(byte[] bytes);
}

internal sealed class SettingsStore(string root) : ISettingsPersistence
{
    internal string Root { get; } = Path.GetFullPath(root);
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public byte[] Read() => File.ReadAllBytes(Path.Combine(Root, "settings.json"));
    internal static TrayConfig Parse(byte[] bytes)
    {
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        var config = JsonSerializer.Deserialize<TrayConfig>(bytes.AsSpan(offset)) ?? throw new UiMessageException("InvalidSettings");
        if (config.SchemaVersion is not (1 or 2 or 3 or 4) || config.AcceptedPaths is null ||
            config.AcceptedPaths.Any(string.IsNullOrWhiteSpace)) throw new UiMessageException("InvalidSettings");
        if (config.RecentImages is { } recent && (recent.Length > RecentImageHistory.Limit ||
            recent.Any(image => image is null || string.IsNullOrWhiteSpace(image.PhotoFile) ||
                string.IsNullOrWhiteSpace(image.Sha256) || image.Sha256.Length != 64 || !image.Sha256.All(Uri.IsHexDigit))))
            throw new UiMessageException("InvalidSettings");
        if (config.SecondaryImage is { } secondary && (config.SchemaVersion < 4 ||
            string.IsNullOrWhiteSpace(secondary.PhotoFile) || !ValidHash(secondary.Sha256)))
            throw new UiMessageException("InvalidSettings");
        _ = ThemeOptions.Validate(config.Theme);
        _ = PollingOptions.Validate(config.PollIntervalMs);
        return config with { Language = UiText.Normalize(config.Language) };
    }
    internal string Asset(string relative, string expectedHash)
    {
        var assetsRoot = Path.GetFullPath(Path.Combine(Root, "assets")) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            throw new UiMessageException("AssetLocationInvalid");
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new UiMessageException("AssetHashChanged");
        return path;
    }
    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    internal PolicyImages Images(TrayConfig config)
    {
        var secondary = config.SecondaryImage;
        var aliases = new[] { config.SchemaVersion >= 3 ? config.CurrentSourcePath : null, secondary?.SourcePath }
            .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(Asset(config.PhotoFile, config.PhotoSha256), config.PhotoSha256,
            Asset(secondary?.PhotoFile ?? config.BlackFile, secondary?.Sha256 ?? config.BlackSha256),
            secondary?.Sha256 ?? config.BlackSha256, aliases);
    }
    internal TrayConfig Prepare(TrayConfig current, SelectedImage selected, bool startup,
        WallpaperTarget target = WallpaperTarget.Primary, bool useBlack = false)
    {
        if (!Enum.IsDefined(target) || (useBlack && target != WallpaperTarget.Secondary))
            throw new UiMessageException("InvalidWallpaperTarget");
        if (useBlack)
            return current with { SchemaVersion = 4, SecondaryImage = null, StartWithWindows = startup };
        var relative = Cache(selected);
        var oldFile = target == WallpaperTarget.Primary ? current.PhotoFile : current.SecondaryImage?.PhotoFile;
        var oldHash = target == WallpaperTarget.Primary ? current.PhotoSha256 : current.SecondaryImage?.Sha256;
        var fromSavedPreview = oldFile is not null && StringComparer.OrdinalIgnoreCase.Equals(selected.SourcePath,
            Path.GetFullPath(Path.Combine(Root, oldFile))) && StringComparer.OrdinalIgnoreCase.Equals(selected.Sha256, oldHash);
        var source = fromSavedPreview ? (target == WallpaperTarget.Primary ? current.CurrentSourcePath : current.SecondaryImage?.SourcePath) : selected.SourcePath;
        var chosen = target == WallpaperTarget.Primary ? current with
        {
            SchemaVersion = 4, PhotoFile = relative, PhotoSha256 = selected.Sha256, PhotoLabel = selected.FriendlyName,
            CurrentSourcePath = source, AcceptedPaths = [], HasChosenImage = true, StartWithWindows = startup
        } : current with
        {
            SchemaVersion = 4, SecondaryImage = new(relative, selected.Sha256, selected.FriendlyName, source), StartWithWindows = startup
        };
        return RecentImageHistory.Remember(current, chosen, target);
    }
    private string Cache(SelectedImage selected)
    {
        var relative = "assets/photo-" + selected.Sha256.ToLowerInvariant() + selected.Extension;
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(selected.Bytes);
            file.Flush(flushToDisk: true);
        }
        _ = Asset(relative, selected.Sha256);
        return relative;
    }
    public void Write(byte[] bytes)
    {
        // Preserve the exact pre-upgrade settings outside rotating diagnostics.
        // The support exporter never inventories this root-level backup.
        if (Parse(bytes).SchemaVersion == 4)
        {
            var before = Read();
            var backup = Path.Combine(Root, "settings-before-schema4.json");
            if (Parse(before).SchemaVersion < 4 && !File.Exists(backup))
            {
                var temporaryBackup = Path.Combine(Root, "settings-schema4-backup-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var copy = new FileStream(temporaryBackup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { copy.Write(before); copy.Flush(flushToDisk: true); }
                    if (!File.ReadAllBytes(temporaryBackup).SequenceEqual(before)) throw new IOException("Settings backup readback failed.");
                    File.Move(temporaryBackup, backup, overwrite: false);
                }
                finally { if (File.Exists(temporaryBackup)) File.Delete(temporaryBackup); }
            }
        }
        var target = Path.Combine(Root, "settings.json");
        var temporary = Path.Combine(Root, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(bytes); file.Flush(flushToDisk: true); }
        File.Replace(temporary, target, destinationBackupFileName: null);
    }
}
