using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal sealed class UpdateFeed(HttpClient client)
{
    internal static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PrimaryWallpaperAgent/" + BuildInfo.Version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    internal async Task<UpdateRelease?> FindAsync(string currentVersion, bool includePreviews, string publicKey, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var bytes = await ReadAsync(new Uri(UpdateTrust.ApiUrl), 2 * 1024 * 1024, timeout.Token);
        using var json = JsonDocument.Parse(bytes);
        var releases = new List<(string Version, bool Preview, string Notes)>();
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var preview = release.GetProperty("prerelease").GetBoolean();
            if (preview && !includePreviews) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith('v')) continue;
            var version = tag[1..];
            try { if (UpdateTrust.ParseVersion(version) <= UpdateTrust.ParseVersion(currentVersion)) continue; }
            catch (InvalidDataException) { continue; }
            var names = release.GetProperty("assets").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray();
            if (!names.Contains(UpdateTrust.ManifestName) || !names.Contains(UpdateTrust.SignatureName)) continue;
            releases.Add((version, preview, release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : ""));
        }
        var latest = releases.OrderByDescending(release => UpdateTrust.ParseVersion(release.Version)).FirstOrDefault();
        if (latest.Version is null) return null;
        var manifestBytes = await ReadAsync(UpdateTrust.AssetUrl(latest.Version, UpdateTrust.ManifestName), 65536, timeout.Token);
        var signatureText = await ReadAsync(UpdateTrust.AssetUrl(latest.Version, UpdateTrust.SignatureName), 1024, timeout.Token);
        var signature = Convert.FromBase64String(Encoding.UTF8.GetString(signatureText).Trim());
        var manifest = UpdateTrust.Verify(manifestBytes, signature, publicKey);
        if (manifest.Version != latest.Version || (manifest.Channel == "preview") != latest.Preview)
            throw new InvalidDataException("Release and signed manifest do not match.");
        return new(latest.Version, latest.Preview, latest.Notes, manifest, manifestBytes, signature);
    }

    internal async Task DownloadAsync(UpdateRelease release, string flavor, string destination, Action<int> progress, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        token = deadline.Token;
        var asset = release.Asset(flavor);
        using var response = await GetAsync(UpdateTrust.AssetUrl(release.Version, asset.FileName), token);
        if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
            throw new InvalidDataException("Update content length mismatch.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            var buffer = new byte[65536];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                total += read;
                if (total > asset.Size) throw new InvalidDataException("Update exceeds signed size.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                progress((int)(total * 100 / asset.Size));
            }
            await output.FlushAsync(token);
            if (total != asset.Size) throw new InvalidDataException("Incomplete update download.");
        }
        UpdateTrust.VerifyFile(destination, asset);
    }

    private async Task<byte[]> ReadAsync(Uri uri, int limit, CancellationToken token)
    {
        using var response = await GetAsync(uri, token);
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update metadata too large.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Update metadata too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (var redirect = 0; redirect < 6; redirect++)
        {
            if (uri.Scheme != "https" || !(uri.Host is "api.github.com" or "github.com" or "objects.githubusercontent.com" or "release-assets.githubusercontent.com"))
                throw new InvalidDataException("Untrusted update URL.");
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new HttpRequestException("Too many update redirects.");
    }
}
