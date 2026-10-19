using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class FirstRun
{
    internal static byte[] Open(SettingsStore store)
    {
        var settings = Path.Combine(store.Root, "settings.json");
        if (File.Exists(settings)) return store.Read(); // Never replace existing or invalid user settings.
        Directory.CreateDirectory(Path.Combine(store.Root, "assets"));
        // A local preview placeholder only: the watcher stays inactive until explicit image selection.
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Black);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var bytes = stream.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var relative = "assets/black-" + hash.ToLowerInvariant() + ".png";
        var asset = Path.Combine(store.Root, relative);
        if (!File.Exists(asset))
        {
            using var target = new FileStream(asset, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            target.Write(bytes);
            target.Flush(flushToDisk: true);
        }
        _ = store.Asset(relative, hash);
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th" ? "th" : "en";
        var config = new TrayConfig(3, relative, hash, relative, hash, [], Language: language, HasChosenImage: false);
        var encoded = JsonSerializer.SerializeToUtf8Bytes(config, SettingsStore.JsonOptions);
        var temporary = Path.Combine(store.Root, "first-run-" + Guid.NewGuid().ToString("N") + ".tmp");
        using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { target.Write(encoded); target.Flush(flushToDisk: true); }
        try { File.Move(temporary, settings, overwrite: false); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return store.Read();
    }

    internal static string DataRoot(string executableFolder, string localApplicationData)
    {
        // Preserve explicit legacy folder installations; new downloads use per-user data.
        if (File.Exists(Path.Combine(executableFolder, "settings.json"))) return Path.GetFullPath(executableFolder);
        if (string.IsNullOrWhiteSpace(localApplicationData)) throw new IOException("Local application data is unavailable.");
        return Path.Combine(localApplicationData, "PrimaryWallpaperAgent");
    }
}
