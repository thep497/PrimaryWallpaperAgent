using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrimaryWallpaperAgent.Tray;

namespace PrimaryWallpaperAgent.Tests;

internal sealed record UpdateFixtureInstructions(string Full, string Slim, string PublicKey, string Manifest, string Signature, bool FailHealth);

internal static class UpdateProcessTests
{
    internal static int Fixture(string[] args)
    {
        var root = Path.GetDirectoryName(Environment.ProcessPath!)!;
        if (args is ["--update-preflight", var report]) { PrimaryWallpaperAgent.Tray.Program.CheckPreflight(report); return 0; }
        if (args is ["--apply-update", var path])
        {
            var ticket = UpdateInstaller.ReadTicket(path);
            var instructions = Instructions(ticket.DataRoot);
            return UpdateInstaller.RunHelper(path, instructions.PublicKey);
        }
        if (args is ["--post-update", var post])
        {
            var ticket = UpdateInstaller.ReadTicket(post);
            var instructions = Instructions(ticket.DataRoot);
            if (instructions.FailHealth) return 23;
            ticket = UpdateInstaller.ValidatePostUpdate(post, instructions.PublicKey);
            UpdateInstaller.MarkHealthy(ticket);
            Thread.Sleep(3000);
            return 0;
        }
        if (args is ["--config"]) { File.WriteAllText(Path.Combine(root, "rollback-started.txt"), "old fixture restarted"); return 0; }
        if (args is not ["update-fixture"]) return 2;
        var input = Instructions(root);
        using var client = new HttpClient(new FixtureTransport(input));
        using var controller = new UpdateController(root, clientOverride: client, publicKeyOverride: input.PublicKey);
        using var context = new ApplicationContext();
        using var timer = new System.Windows.Forms.Timer { Interval = 30 };
        var handedOff = false;
        controller.RestartRequested += () => { handedOff = true; context.ExitThread(); };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await controller.CheckAsync();
            await controller.InstallAsync();
            if (!handedOff)
            {
                File.WriteAllText(Path.Combine(root, "fixture-error.txt"), controller.StatusKey);
                context.ExitThread();
            }
        };
        timer.Start();
        Application.Run(context);
        return handedOff ? 0 : 1;
    }

    internal static int Run(string payloadRoot)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "runtime", "update-process-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var fullNew = Path.Combine(payloadRoot, "0.4.0", "full", "PrimaryWallpaperAgent.Tests.exe");
        var slimNew = Path.Combine(payloadRoot, "0.4.0", "slim", "PrimaryWallpaperAgent.Tests.exe");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = new UpdateManifest(1, "0.4.0", "stable", [
            Asset("full", fullNew), Asset("slim", slimNew)]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var signature = key.SignData(bytes, HashAlgorithmName.SHA256);
        var results = new List<object>();
        foreach (var flavor in new[] { "full", "slim" })
        foreach (var failure in new[] { false, true })
        {
            var scenario = Path.Combine(root, flavor + (failure ? "-rollback" : "-success"));
            Directory.CreateDirectory(scenario);
            var target = Path.Combine(scenario, "Renamed Agent.exe");
            File.Copy(Path.Combine(payloadRoot, "0.3.9", flavor, "PrimaryWallpaperAgent.Tests.exe"), target);
            var originalHash = UpdateTrust.Hash(target);
            var settings = FirstRun.Open(new SettingsStore(scenario));
            var instructions = new UpdateFixtureInstructions(fullNew, slimNew, key.ExportSubjectPublicKeyInfoPem(),
                Convert.ToBase64String(bytes), Convert.ToBase64String(signature), failure);
            File.WriteAllText(Path.Combine(scenario, "fixture-instructions.json"), JsonSerializer.Serialize(instructions));
            using (var parent = Process.Start(UpdateInstaller.StartInfo(target, "update-fixture"))!)
            {
                if (!parent.WaitForExit(60000)) { parent.Kill(); throw new TimeoutException("Parent fixture did not hand off."); }
                Assert(parent.ExitCode == 0, "Fixture parent handed off: " + scenario);
            }
            var resultPath = Path.Combine(scenario, "updates", "last-result.json");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(resultPath) && wait.Elapsed < TimeSpan.FromSeconds(45)) Thread.Sleep(100);
            Assert(File.Exists(resultPath), "Helper outcome exists: " + scenario);
            var result = JsonSerializer.Deserialize<UpdateOutcome>(File.ReadAllBytes(resultPath))!;
            Assert(result.Success != failure, "Expected process health outcome");
            Assert(File.ReadAllBytes(Path.Combine(scenario, "settings.json")).SequenceEqual(settings), "Settings bytes preserved");
            if (!failure)
            {
                Assert(UpdateTrust.Hash(target) == manifest.Assets.Single(asset => asset.Flavor == flavor).Sha256, "New EXE installed for " + flavor);
                Assert(result.BackupPath is not null && UpdateTrust.Hash(result.BackupPath) == originalHash, "Old backup preserved");
            }
            else
            {
                Assert(UpdateTrust.Hash(target) == originalHash && result.MessageKey == "UpdateRolledBack", "Original EXE restored");
                wait.Restart();
                while (!File.Exists(Path.Combine(scenario, "rollback-started.txt")) && wait.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(100);
                Assert(File.Exists(Path.Combine(scenario, "rollback-started.txt")), "Old EXE reopened");
            }
            results.Add(new { Flavor = flavor, Rollback = failure, Passed = true, NativeWallpaperCalls = 0, RegistryWrites = 0 });
        }
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(results, SettingsStore.JsonOptions));
        Console.WriteLine("UPDATE PROCESS TESTS PASSED: real copied parent/helper/candidate processes, 0.3.9 -> 0.4.0 Full/Slim, healthy startup and rollback; no native wallpaper/startup backend.");
        Console.WriteLine("Evidence: " + root);
        return 0;

        static UpdateAsset Asset(string flavor, string path) => new(flavor, UpdateTrust.FileName(flavor), new FileInfo(path).Length, UpdateTrust.Hash(path));
    }

    private static UpdateFixtureInstructions Instructions(string root) =>
        JsonSerializer.Deserialize<UpdateFixtureInstructions>(File.ReadAllBytes(Path.Combine(root, "fixture-instructions.json")))!;
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException("UPDATE PROCESS TEST FAILED: " + message); }

    private sealed class FixtureTransport(UpdateFixtureInstructions input) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            HttpContent content;
            var uri = request.RequestUri!;
            if (uri.Host == "api.github.com")
                content = new StringContent("[{\"tag_name\":\"v0.4.0\",\"draft\":false,\"prerelease\":false,\"body\":\"Fixture\",\"assets\":[{\"name\":\"update-manifest.json\"},{\"name\":\"update-manifest.sig\"}]}]");
            else if (uri.AbsolutePath.EndsWith(UpdateTrust.ManifestName)) content = new ByteArrayContent(Convert.FromBase64String(input.Manifest));
            else if (uri.AbsolutePath.EndsWith(UpdateTrust.SignatureName)) content = new StringContent(input.Signature);
            else content = new StreamContent(File.OpenRead(uri.AbsolutePath.EndsWith(UpdateTrust.FileName("full")) ? input.Full : input.Slim));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
