using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal sealed record TrayConfig(int SchemaVersion, string PhotoFile, string PhotoSha256,
    string BlackFile, string BlackSha256, string[] AcceptedPaths, bool StartWithWindows = false, string? PhotoLabel = null);

internal sealed record SelectedImage(string SourcePath, byte[] Bytes, string Extension, string Sha256, string? FriendlyName = null)
{
    internal static SelectedImage Read(string path)
    {
        // Read only the explicit file chosen by the user; never scan its directory.
        using var file = File.OpenRead(path);
        if (file.Length is <= 0 or > 32 * 1024 * 1024)
            throw new InvalidOperationException("เลือกไฟล์ภาพขนาดไม่เกิน 32 MB ค่ะ");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
        if ((long)image.Width * image.Height > 40_000_000)
            throw new InvalidOperationException("ภาพมีขนาดเกิน 40 ล้านพิกเซลค่ะ");
        var extension = image.RawFormat.Guid == ImageFormat.Jpeg.Guid ? ".jpg" :
            image.RawFormat.Guid == ImageFormat.Png.Guid ? ".png" :
            image.RawFormat.Guid == ImageFormat.Bmp.Guid ? ".bmp" :
            throw new InvalidOperationException("รุ่นนี้รองรับภาพนิ่ง JPG, PNG และ BMP ค่ะ");
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
        var config = JsonSerializer.Deserialize<TrayConfig>(bytes.AsSpan(offset)) ?? throw new InvalidOperationException("Missing settings.");
        if (config.SchemaVersion is not (1 or 2) || config.AcceptedPaths is null ||
            config.AcceptedPaths.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Unsupported settings.");
        return config;
    }
    internal string Asset(string relative, string expectedHash)
    {
        var assetsRoot = Path.GetFullPath(Path.Combine(Root, "assets")) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image must be inside this application's assets folder.");
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image checksum changed. Select the image again in Config.");
        return path;
    }
    internal PolicyImages Images(TrayConfig config) => new(Asset(config.PhotoFile, config.PhotoSha256), config.PhotoSha256,
        Asset(config.BlackFile, config.BlackSha256), config.BlackSha256, config.AcceptedPaths);
    internal TrayConfig Prepare(TrayConfig current, SelectedImage selected, bool startup)
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
        var accepted = current.AcceptedPaths.Concat([Path.Combine(Root, current.PhotoFile),
            Path.Combine(Root, current.BlackFile), selected.SourcePath]).Distinct(StringComparer.OrdinalIgnoreCase).TakeLast(64).ToArray();
        return new(2, relative, selected.Sha256, current.BlackFile, current.BlackSha256, accepted, startup, selected.FriendlyName);
    }
    public void Write(byte[] bytes)
    {
        var target = Path.Combine(Root, "settings.json");
        var temporary = Path.Combine(Root, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(bytes); file.Flush(flushToDisk: true); }
        File.Replace(temporary, target, destinationBackupFileName: null);
    }
}
