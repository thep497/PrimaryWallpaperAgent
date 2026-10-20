using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrimaryWallpaperAgent.Tray;

internal static class BuildInfo
{
    internal static string Version => AboutForm.VersionText;
    internal static string Flavor => typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .SingleOrDefault(item => item.Key == "DistributionFlavor")?.Value ?? "full";
    internal static string PublicKey
    {
        get
        {
            using var stream = typeof(BuildInfo).Assembly.GetManifestResourceStream("PrimaryWallpaperAgent.UpdatePublicKey")
                ?? throw new InvalidDataException("Update public key is missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}

internal sealed record UpdateAsset(string Flavor, string FileName, long Size, string Sha256);
internal sealed record UpdateManifest(int SchemaVersion, string Version, string Channel, UpdateAsset[] Assets);
internal sealed record UpdateRelease(string Version, bool Preview, string Notes, UpdateManifest Manifest, byte[] ManifestBytes, byte[] Signature)
{
    internal UpdateAsset Asset(string flavor) => Manifest.Assets.Single(asset => asset.Flavor == flavor);
}

internal static partial class UpdateTrust
{
    internal const string Repository = "thep497/PrimaryWallpaperAgent";
    internal const string ApiUrl = "https://api.github.com/repos/" + Repository + "/releases?per_page=30";
    internal const string ManifestName = "update-manifest.json";
    internal const string SignatureName = "update-manifest.sig";
    internal const long MaxAssetBytes = 256L * 1024 * 1024;

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
    [GeneratedRegex(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    internal static Version ParseVersion(string value)
    {
        if (value.Length > 32 || !VersionPattern().IsMatch(value) || !System.Version.TryParse(value, out var version))
            throw new InvalidDataException("Invalid update version.");
        return version;
    }
    internal static string FileName(string flavor) => flavor switch
    {
        "full" => "PrimaryWallpaperAgent-Full-win-x64.exe",
        "slim" => "PrimaryWallpaperAgent-Slim-win-x64.exe",
        _ => throw new InvalidDataException("Unknown package flavor.")
    };
    internal static Uri AssetUrl(string version, string name)
    {
        _ = ParseVersion(version);
        if (name != ManifestName && name != SignatureName && name != FileName("full") && name != FileName("slim"))
            throw new InvalidDataException("Invalid update asset.");
        return new Uri("https://github.com/" + Repository + "/releases/download/v" + version + "/" + name);
    }
    internal static UpdateManifest Verify(byte[] bytes, byte[] signature, string publicKey)
    {
        if (bytes.Length is 0 or > 65536 || signature.Length is 0 or > 256) throw new InvalidDataException("Invalid signed manifest.");
        using var key = ECDsa.Create();
        key.ImportFromPem(publicKey);
        if (!key.VerifyData(bytes, signature, HashAlgorithmName.SHA256)) throw new InvalidDataException("Update signature does not match.");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes) ?? throw new InvalidDataException("Missing manifest.");
        _ = ParseVersion(manifest.Version);
        if (manifest.SchemaVersion != 1 || manifest.Channel is not ("stable" or "preview") ||
            manifest.Assets is null || manifest.Assets.Length != 2 || manifest.Assets.Select(a => a.Flavor).Distinct().Count() != 2)
            throw new InvalidDataException("Unsupported update manifest.");
        foreach (var asset in manifest.Assets)
            if (asset.FileName != FileName(asset.Flavor) || asset.Size is <= 0 or > MaxAssetBytes || !HashPattern().IsMatch(asset.Sha256))
                throw new InvalidDataException("Invalid update asset metadata.");
        return manifest;
    }
    internal static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    internal static void VerifyFile(string path, UpdateAsset asset)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != asset.Size ||
            !Hash(path).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded file does not match the signed manifest.");
    }
    internal static bool SamePath(string first, string second) =>
        Path.GetFullPath(first).Equals(Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
}
