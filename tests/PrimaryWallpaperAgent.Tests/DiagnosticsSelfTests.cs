using System.IO.Compression;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class DiagnosticsSelfTests
{
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "diagnostics-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var runtime = Path.Combine(folder, "runtime");
        var legacy = Path.Combine(runtime, "20261021_020000_000-aaaaaaaa");
        Directory.CreateDirectory(legacy);
        var now = DateTimeOffset.Now;
        File.WriteAllText(Path.Combine(legacy, "agent.log"), now.AddHours(-2).ToString("O") + " LEGACY_FIRST\n" +
            now.AddHours(-1).ToString("O") + @" LEGACY_ERROR C:\Users\private-user\secret\photo.jpg" + "\n   continuation");
        File.WriteAllText(Path.Combine(legacy, "config-aaaaaaaa-settings-before"), "{\"OriginalBytesBase64\":\"PRIVATE_SETTINGS\"}");
        File.WriteAllText(Path.Combine(legacy, "user-notes.txt"), "UNRELATED_PRIVATE_NOTE");
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        File.WriteAllText(Path.Combine(folder, "assets", "private.png"), "PRIVATE_IMAGE_BYTES");
        File.WriteAllText(Path.Combine(folder, "settings.json"), "PRIVATE_SETTINGS");

        string current;
        using (var first = new RuntimeLog(folder, "first-run"))
        using (var second = new RuntimeLog(folder, "second-run"))
        {
            first.Write("FIRST_NEW");
            second.Write("SECOND_NEW สวัสดี");
            Assert(first.CurrentFile == second.CurrentFile, "Restarts/processes append to one daily log");
            Parallel.For(0, 20, i => first.Write("CONCURRENT_" + i));
            for (var i = 0; i < 190; i++) first.Write("SIZE " + new string('x', 12000));
            first.Write("OVERSIZED " + new string('y', 100000));
            first.Write("LAST_NEW");
            current = first.CurrentFile;
            Assert(ReadShared(current).Contains("LAST_NEW"), "View log selects the latest size chunk");
        }
        var logs = Directory.GetFiles(Path.Combine(runtime, "logs"));
        Assert(logs.Length >= 2, "Size rolling creates additional chunks");
        var text = string.Join("\n", logs.Select(File.ReadAllText));
        Assert(text.Contains("first-run") && text.Contains("second-run") && text.Contains("สวัสดี") &&
            Enumerable.Range(0, 20).All(i => text.Contains("CONCURRENT_" + i)), "Runs and concurrent UTF-8 events retained");
        Assert(text.Contains("[truncated]"), "Oversized messages are bounded");

        var fake = new TrialSelfTests.FakeDesktop(2);
        fake.State = fake.State with { Wallpapers = fake.State.Wallpapers.Select(w => w with
            { WallpaperPath = @"\\server\private-share\personal.jpg", MonitorId = "PRIVATE-MONITOR-ID-" + w.Index }).ToArray() };
        var one = DiagnosticSnapshots.Save(legacy, "fixture", "attempt-failed-before", fake.State);
        var two = DiagnosticSnapshots.Save(legacy, "fixture", "attempt-failed-before", fake.State);
        Assert(one != two && File.Exists(one) && File.Exists(two), "Repeated failed attempts never overwrite evidence");
        DiagnosticSnapshots.Save(legacy, "fixture", "config-12345678-settings-before", new { OriginalBytesBase64 = "PRIVATE_SETTINGS" });
        var old = Path.Combine(runtime, "logs", "agent-20000101.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));
        LogRetention.Prune(folder, current, DateTime.UtcNow);
        Assert(!File.Exists(old) && File.Exists(one) && File.ReadAllText(Path.Combine(folder, "settings.json")) == "PRIVATE_SETTINGS",
            "New retention removes expired daily logs while preserving images/settings and current evidence");

        var destination = Path.Combine(folder, "support.zip");
        ExportResult exported;
        using (var active = new RuntimeLog(folder, "active-during-export"))
        {
            active.Write("EXPORT_WHILE_ACTIVE");
            exported = DiagnosticsExporter.Create(folder, destination);
        }
        using (var zip = ZipFile.OpenRead(destination))
        {
            var entries = zip.Entries.Select(e => e.FullName).ToArray();
            Assert(entries.Contains("timeline.log") && entries.Contains("manifest.json") &&
                entries.Any(e => e.StartsWith("diagnostics/")) && entries.Any(e => e.Contains("/logs/agent-")),
                "One ZIP includes legacy/new logs, timeline, manifest and display evidence");
            var all = string.Join("\n", zip.Entries.Select(Read));
            Assert(!all.Contains("PRIVATE_SETTINGS") && !all.Contains("PRIVATE_IMAGE_BYTES") &&
                !all.Contains("UNRELATED_PRIVATE_NOTE") && !all.Contains("PRIVATE-MONITOR-ID") &&
                !all.Contains("private-user") && !all.Contains("private-share"), "Support export excludes pictures/settings and redacts identities/paths");
            var timeline = Read(zip.GetEntry("timeline.log")!);
            Assert(timeline.IndexOf("LEGACY_FIRST", StringComparison.Ordinal) < timeline.IndexOf("FIRST_NEW", StringComparison.Ordinal) &&
                timeline.Contains("continuation"), "Timeline is sorted across runs and keeps exception context");
        }
        Assert(exported.Events >= 200 && exported.Skipped >= 2, "Export reports included events and intentional omissions");
        var original = File.ReadAllBytes(destination);
        try { DiagnosticsExporter.Create(folder, destination); throw new Exception("Expected existing destination rejection"); }
        catch (IOException) { }
        Assert(File.ReadAllBytes(destination).SequenceEqual(original), "Existing support ZIP is never overwritten/deleted");
        Assert(DiagnosticsExporter.Redact(@"\\server\private-share\file.jpg").StartsWith("<path:") &&
            DiagnosticsExporter.Redact(@"D:\Trial\FILE.jpg") == DiagnosticsExporter.Redact(@"d:\trial\file.jpg"),
            "UNC paths are masked and tokens remain consistent across case");
        TestSupportButtons(folder);
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new { Passed = true, exported.Files,
            exported.Events, exported.Skipped, NativeWallpaperCalls = 0, RegistryWrites = 0 }, SettingsStore.JsonOptions));
        Console.WriteLine("DIAGNOSTICS TESTS PASSED: daily/size rolling, restart/concurrent append, bounded messages, retention, unique failed-attempt evidence, legacy/new export, privacy, ordering and destination preservation.");
        Console.WriteLine("Evidence: " + folder);
        return 0;
    }
    internal static int ExportFixture(string root, string destination)
    {
        var result = DiagnosticsExporter.Create(root, destination);
        Console.WriteLine(JsonSerializer.Serialize(result)); return 0;
    }
    private static void TestSupportButtons(string folder)
    {
        foreach (var language in new[] { "en", "th" })
        {
            var targets = new List<string>(); var exports = 0;
            using var form = new AboutForm(language, folder, targets.Add, exportDiagnostics: () => exports++);
            form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
            var export = (Button)form.Controls.Find("ExportDiagnosticsButton", true).Single();
            export.PerformClick();
            ((Button)form.Controls.Find("OpenLogsButton", true).Single()).PerformClick();
            Assert(exports == 1 && targets.Single() == Path.Combine(folder, "runtime", "logs") &&
                export.Text == UiText.T("ExportDiagnostics", language), "Support actions are localized and use the app log directory");
            using (var image = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                image.Save(Path.Combine(folder, "about-support-" + language + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Close();
        }
    }
    private static string ReadShared(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var reader = new StreamReader(file); return reader.ReadToEnd(); }
    private static string Read(ZipArchiveEntry entry) { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException("DIAGNOSTICS TEST FAILED: " + message); }
}
