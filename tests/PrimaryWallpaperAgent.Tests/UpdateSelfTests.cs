using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrimaryWallpaperAgent.Tray;

namespace PrimaryWallpaperAgent.Tests;

internal static class UpdateSelfTests
{
    internal static int VerifyPublishedFiles(string folder)
    {
        var bytes = File.ReadAllBytes(Path.Combine(folder, UpdateTrust.ManifestName));
        var signature = Convert.FromBase64String(File.ReadAllText(Path.Combine(folder, UpdateTrust.SignatureName)));
        var manifest = UpdateTrust.Verify(bytes, signature, BuildInfo.PublicKey);
        foreach (var asset in manifest.Assets) UpdateTrust.VerifyFile(Path.Combine(folder, asset.FileName), asset);
        Console.WriteLine("PRODUCTION SIGNED RELEASE VERIFIED: " + manifest.Version);
        return 0;
    }
    internal static int CheckLiveRelease()
    {
        using var client = UpdateFeed.CreateClient();
        var release = new UpdateFeed(client).FindAsync("0.3.1", true, BuildInfo.PublicKey, CancellationToken.None).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Published updater release not found.");
        if (release.Version != BuildInfo.Version) throw new InvalidOperationException("Unexpected release selected.");
        Console.WriteLine("LIVE GITHUB FEED VERIFIED: " + release.Version + "; Full/Slim signature verified.");
        return 0;
    }

    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "update-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = key.ExportSubjectPublicKeyInfoPem();
        var content = Encoding.UTF8.GetBytes("signed candidate fixture");
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var manifest = new UpdateManifest(1, "99.0.0", "stable",
            [new("full", UpdateTrust.FileName("full"), content.Length, hash), new("slim", UpdateTrust.FileName("slim"), content.Length, hash)]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var signature = key.SignData(bytes, HashAlgorithmName.SHA256);
        Assert(UpdateTrust.Verify(bytes, signature, publicKey).Version == manifest.Version, "Valid signed manifest");
        var changed = bytes.ToArray(); changed[5] ^= 1;
        Reject(() => UpdateTrust.Verify(changed, signature, publicKey), "Tampered metadata rejected");
        Reject(() => UpdateTrust.Verify(bytes, signature.Reverse().ToArray(), publicKey), "Tampered signature rejected");
        Reject(() => UpdateTrust.ParseVersion("1.2.3/evil"), "Path-bearing tag rejected");
        Reject(() => UpdateTrust.AssetUrl("1.2.3", "../evil.exe"), "Unexpected asset name rejected");
        var invalid = manifest with { Assets = [manifest.Assets[0], manifest.Assets[0]] };
        var invalidBytes = JsonSerializer.SerializeToUtf8Bytes(invalid);
        Reject(() => UpdateTrust.Verify(invalidBytes, key.SignData(invalidBytes, HashAlgorithmName.SHA256), publicKey), "Duplicate flavor rejected");

        var transport = new FixtureFeed(manifest, bytes, signature, content);
        using (var client = new HttpClient(transport))
        {
            var feed = new UpdateFeed(client);
            var release = feed.FindAsync("0.4.0", false, publicKey, CancellationToken.None).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("Release not found");
            Assert(release.Asset("full").FileName != release.Asset("slim").FileName, "Flavor selects its own signed asset");
            foreach (var flavor in new[] { "full", "slim" })
            {
                var path = Path.Combine(folder, flavor + ".exe");
                feed.DownloadAsync(release, flavor, path, _ => { }, CancellationToken.None).GetAwaiter().GetResult();
                Assert(File.ReadAllBytes(path).SequenceEqual(content), "Complete hash-verified " + flavor + " download");
            }
            transport.Preview = true;
            Assert(feed.FindAsync("0.4.0", false, publicKey, CancellationToken.None).GetAwaiter().GetResult() is null, "Stable excludes preview");
        }
        var corrupt = new FixtureFeed(manifest, bytes, signature, content) { Truncate = true };
        using (var client = new HttpClient(corrupt))
        {
            var feed = new UpdateFeed(client);
            var release = feed.FindAsync("0.4.0", false, publicKey, CancellationToken.None).GetAwaiter().GetResult()!;
            Reject(() => feed.DownloadAsync(release, "full", Path.Combine(folder, "incomplete.exe"), _ => { }, CancellationToken.None).GetAwaiter().GetResult(),
                "Truncated executable rejected");
        }
        TestTransaction(folder, manifest, bytes, signature, content);
        TestPreferencesAndUi(folder, manifest, bytes, signature, content, publicKey);
        Console.WriteLine("UPDATE TESTS PASSED: signed metadata, tamper/version/flavor rejection, stable/preview feed, bounded verified downloads, success/rollback/conflict transaction, preferences and bilingual UI.");
        return 0;
    }

    private static void TestTransaction(string folder, UpdateManifest manifest, byte[] bytes, byte[] signature, byte[] content)
    {
        foreach (var failHealth in new[] { false, true })
        {
            var root = Path.Combine(folder, failHealth ? "rollback" : "success");
            Directory.CreateDirectory(root);
            var target = Path.Combine(root, "Renamed Agent.exe");
            File.WriteAllText(target, "original executable");
            var before = UpdateTrust.Hash(target);
            var id = Guid.NewGuid().ToString("N");
            var ticket = new UpdateTicket(1, id, root, target, before, "0.4.0", "full", 1, 1,
                Convert.ToBase64String(bytes), Convert.ToBase64String(signature), false);
            Directory.CreateDirectory(ticket.OperationRoot);
            Directory.CreateDirectory(Path.Combine(root, "assets"));
            File.WriteAllText(Path.Combine(root, "settings.json"), "settings unchanged");
            File.WriteAllText(Path.Combine(root, "assets", "image.png"), "image unchanged");
            File.WriteAllBytes(ticket.ReadyFile, content);
            var restarted = 0;
            var result = UpdateInstaller.Apply(ticket, manifest, () => !failHealth, () => restarted++);
            Assert(result.Success == !failHealth, "Health result controls transaction");
            Assert(failHealth ? UpdateTrust.Hash(target) == before : File.ReadAllBytes(target).SequenceEqual(content), "Target content after success/rollback");
            Assert(failHealth ? restarted == 1 : restarted == 0 && UpdateTrust.Hash(ticket.Backup) == before, "Previous version retained/restarted");
            Assert(File.ReadAllText(Path.Combine(root, "settings.json")) == "settings unchanged" &&
                File.ReadAllText(Path.Combine(root, "assets", "image.png")) == "image unchanged", "User data never replaced");
        }
        var conflictRoot = Path.Combine(folder, "changed-target");
        Directory.CreateDirectory(conflictRoot);
        var conflictTarget = Path.Combine(conflictRoot, "Agent.exe");
        File.WriteAllText(conflictTarget, "original");
        var conflict = new UpdateTicket(1, Guid.NewGuid().ToString("N"), conflictRoot, conflictTarget, UpdateTrust.Hash(conflictTarget),
            "0.4.0", "slim", 1, 1, Convert.ToBase64String(bytes), Convert.ToBase64String(signature), false);
        Directory.CreateDirectory(conflict.OperationRoot);
        File.WriteAllBytes(conflict.ReadyFile, content);
        File.WriteAllText(conflictTarget, "external change");
        var conflictRestarts = 0;
        var outcome = UpdateInstaller.Apply(conflict, manifest, () => throw new InvalidOperationException("Must not launch"), () => conflictRestarts++);
        Assert(!outcome.Success && conflictRestarts == 0 && File.ReadAllText(conflictTarget) == "external change", "Changed target is preserved before replacement");
    }

    private static void TestPreferencesAndUi(string folder, UpdateManifest manifest, byte[] bytes, byte[] signature, byte[] content, string publicKey)
    {
        var root = Path.Combine(folder, "controller");
        Directory.CreateDirectory(root);
        using var controller = new UpdateController(root, clientOverride: new HttpClient(new FixtureFeed(manifest, bytes, signature, content)), publicKeyOverride: publicKey);
        Assert(controller.Preferences.AutoCheck && !controller.Preferences.IncludePreviews, "Default daily checks and stable channel");
        controller.ChangePreferences(false, true);
        using (var reopen = new UpdateController(root))
            Assert(!reopen.Preferences.AutoCheck && reopen.Preferences.IncludePreviews, "Update preferences persist separately");
        var finished = false;
        using var shell = new Form { ShowInTaskbar = false };
        shell.Shown += async (_, _) => { await controller.CheckAsync(); finished = true; shell.Close(); };
        Application.Run(shell);
        Assert(finished && controller.Available?.Version == manifest.Version && controller.StatusKey == "UpdateAvailable", "Manual check updates UI model");
        foreach (var language in new[] { "th", "en" })
        {
            using var about = new AboutForm(language, root, _ => { }, controller);
            about.Show(); Application.DoEvents();
            Assert(about.Controls.Find("InstallUpdateButton", true).Single().Visible, "Install action appears for a verified update");
            var state = about.Controls.Find("UpdateStatus", true).Single();
            Assert(state.Text.Contains(manifest.Version), "Localized available version");
            var client = about.RectangleToScreen(about.ClientRectangle);
            foreach (var button in Walk(about).OfType<Button>().Where(button => button.Visible))
                Assert(client.Contains(button.RectangleToScreen(button.ClientRectangle)), "Update UI controls fit");
            using var screenshot = new Bitmap(about.Width, about.Height);
            about.DrawToBitmap(screenshot, new Rectangle(Point.Empty, about.Size));
            screenshot.Save(Path.Combine(folder, "updates-" + language + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            about.Close();
        }
    }

    internal sealed class FixtureFeed(UpdateManifest manifest, byte[] bytes, byte[] signature, byte[] content) : HttpMessageHandler
    {
        internal bool Preview;
        internal bool Truncate;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            byte[] result;
            if (uri.Host == "api.github.com")
            {
                result = JsonSerializer.SerializeToUtf8Bytes(new[] { new { tag_name = "v" + manifest.Version, draft = false, prerelease = Preview,
                    body = "Fixture notes", assets = new[] { new { name = UpdateTrust.ManifestName }, new { name = UpdateTrust.SignatureName } } } });
            }
            else if (uri.AbsolutePath.EndsWith(UpdateTrust.ManifestName)) result = bytes;
            else if (uri.AbsolutePath.EndsWith(UpdateTrust.SignatureName)) result = Encoding.UTF8.GetBytes(Convert.ToBase64String(signature));
            else result = Truncate ? content[..^1] : content;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(result) });
        }
    }
    private static IEnumerable<Control> Walk(Control root)
    { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Walk(child)) yield return nested; } }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or CryptographicException or HttpRequestException) { return; }
        throw new InvalidOperationException("UPDATE TEST FAILED: " + message);
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("UPDATE TEST FAILED: " + message); }
}
