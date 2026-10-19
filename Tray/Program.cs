using System.Security.Cryptography;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal sealed record TrayConfig(int SchemaVersion, string PhotoFile, string PhotoSha256,
    string BlackFile, string BlackSha256, string[] AcceptedPaths);

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var smoke = args is ["--smoke-test"];
        if (args.Length > 0 && !smoke) return 2;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        try
        {
            var config = JsonSerializer.Deserialize<TrayConfig>(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "settings.json")))
                ?? throw new InvalidOperationException("Missing settings.");
            if (config.SchemaVersion != 1 || config.AcceptedPaths is null)
                throw new InvalidOperationException("Unsupported settings.");
            var images = new PolicyImages(Asset(config.PhotoFile, config.PhotoSha256), config.PhotoSha256,
                Asset(config.BlackFile, config.BlackSha256), config.BlackSha256, config.AcceptedPaths);
            using var lease = new WallpaperWriteLease();
            using var context = new AgentContext(images, smoke);
            Application.Run(context);
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "PrimaryWallpaperAgent — เปิดไม่ได้", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static string Asset(string relative, string expectedHash)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "assets")) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image must be inside the application's assets folder.");
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image checksum changed; rebuild the package before starting.");
        return path;
    }
}
