using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class BrandingSelfTests
{
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "branding-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var frameCounts = new Dictionary<string, int>();
        foreach (var name in new[] { "Application", "TrayRunning", "TrayPaused", "TrayAttention" })
        {
            using var resource = typeof(AppBranding).Assembly.GetManifestResourceStream("PrimaryWallpaperAgent.Tray.Branding." + name + ".ico")
                ?? throw new InvalidOperationException("Missing icon resource: " + name);
            using var reader = new BinaryReader(resource);
            Assert(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "Valid ICO header");
            var count = reader.ReadUInt16();
            var expected = name == "Application" ? new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 } : new[] { 16, 20, 24, 32, 40, 48, 64 };
            Assert(count == expected.Length, "Required DPI frames embedded");
            var frames = new List<(int Size, uint Bytes, uint Offset)>();
            foreach (var size in expected)
            {
                var width = reader.ReadByte(); var height = reader.ReadByte();
                Assert((width == 0 ? 256 : width) == size && width == height, "ICO dimensions");
                reader.ReadByte(); reader.ReadByte();
                Assert(reader.ReadUInt16() == 1 && reader.ReadUInt16() == 32, "32-bit icon frames");
                frames.Add((size, reader.ReadUInt32(), reader.ReadUInt32()));
            }
            foreach (var frame in frames)
            {
                resource.Position = frame.Offset;
                var bytes = reader.ReadBytes(checked((int)frame.Bytes));
                Assert(bytes.Length == frame.Bytes, "Complete frame payload");
                using var data = new MemoryStream(bytes);
                using var image = new Bitmap(data);
                Assert(image.Width == frame.Size && image.Height == frame.Size, "PNG decode matches ICO directory");
                Assert(image.GetPixel(0, 0).A == 0, "Transparent background");
            }
            frameCounts.Add(name, count);
        }

        using var icons = new TrayIconSet();
        using var application = AppBranding.CreateApplicationIcon();
        var hashes = new[] { Hash(icons.Running), Hash(icons.Paused), Hash(icons.Attention) };
        Assert(hashes.Distinct().Count() == 3, "Tray states are visually distinct");
        Assert(icons.Running.Handle != IntPtr.Zero && application.Handle != IntPtr.Zero, "Native icon handles load after resource streams close");
        using (var tray = new NotifyIcon { Icon = icons.Running })
        { tray.Icon = icons.Paused; tray.Icon = icons.Attention; Assert(tray.Icon == icons.Attention, "Native NotifyIcon accepts each state"); }

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "PrimaryWallpaperAgent.Tray", configuration, "PrimaryWallpaperAgent.Tray.exe"));
        Assert(File.Exists(exe), "Expected apphost exists in sibling build output");
        var info = FileVersionInfo.GetVersionInfo(exe);
        Assert(info.ProductName == UiText.AppName && info.FileDescription == UiText.AppName, "Executable uses official display name");
        using var extracted = Icon.ExtractAssociatedIcon(exe) ?? throw new InvalidOperationException("Missing executable icon");
        using var generic = new Icon(SystemIcons.Application, extracted.Size);
        Assert(Hash(extracted) != Hash(generic), "Executable contains custom icon");
        using (var bitmap = extracted.ToBitmap()) bitmap.Save(Path.Combine(folder, "extracted-exe-icon.png"));
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
        { Passed = true, Frames = frameCounts, DistinctTrayStates = hashes.Length, ProductName = info.ProductName,
            NativeWallpaperCalls = 0, RegistryWrites = 0 }, SettingsStore.JsonOptions));
        Console.WriteLine("BRANDING TESTS PASSED: embedded ICO frames/alpha, distinct tray states, native handles and executable icon/product name.");
        return 0;
    }
    private static string Hash(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException("BRANDING TEST FAILED: " + message); }
}
