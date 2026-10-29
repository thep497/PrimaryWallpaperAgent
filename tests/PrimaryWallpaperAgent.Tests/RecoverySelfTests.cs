using System.Drawing.Imaging;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class RecoverySelfTests
{
    internal static int Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "runtime", "recovery-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var primary = MakeImage(root, "primary", Color.Teal);
        var secondary = MakeImage(root, "secondary", Color.Navy);
        var external = MakeImage(root, "external", Color.Gold);
        var global = MakeImage(root, "global", Color.Crimson);
        var transient = MakeImage(root, "transient", Color.Gray);
        using (var cold = new Fixture(Path.Combine(root, "cold"), primary, secondary))
        {
            cold.SetPrimary(secondary.SourcePath);
            cold.Pump(5);
            Assert(cold.Config.PhotoSha256 == primary.Sha256, "A pre-first-poll role swap cannot import the saved secondary as primary");
            cold.AssertRoles();
        }
        using var fixture = new Fixture(root, primary, secondary);
        fixture.Pump(4);
        var initial = fixture.Store.Read();
        var originalConfig = fixture.Config;

        // Delayed Windows wallpaper changes on exactly the SAME monitor IDs,
        // including a readable, unfamiliar remembered image on the new primary.
        fixture.Desktop.Fake.SwitchPrimary();
        fixture.Pump(1);
        fixture.SetPrimary(transient.SourcePath);
        fixture.Pump(8);
        Assert(fixture.Config.PhotoSha256 == primary.Sha256 && fixture.Store.Read().SequenceEqual(initial),
            "Primary-only transition never adopts the old secondary or a remembered unfamiliar image");
        fixture.AssertRoles();
        Assert(!fixture.Context.IsDisplayTransition, "Transition finishes only after repaired roles settle");

        // Windows can change just primary; do not rewrite that monitor merely
        // to replace a verified external path with the application's cache path.
        fixture.SetPrimary(external.SourcePath);
        var writes = fixture.Desktop.Fake.Calls;
        fixture.Pump(5);
        Assert(fixture.Config.PhotoSha256 == external.Sha256 && fixture.Config.SecondaryImage == originalConfig.SecondaryImage,
            "Windows primary is imported without changing secondary");
        Assert(fixture.Desktop.Fake.Calls == writes, "Adoption does not write an already correct primary alias");
        Assert(fixture.Config.Theme == originalConfig.Theme && fixture.Config.PollIntervalMs == originalConfig.PollIntervalMs &&
            fixture.Config.Language == originalConfig.Language && fixture.Config.StartWithWindows,
            "Automatic import preserves every preference");
        Assert(File.Exists(fixture.Store.Asset(fixture.Config.PhotoFile, external.Sha256)), "Adoption creates a verified durable copy");

        File.Delete(external.SourcePath);
        var afterImport = fixture.Store.Read();
        fixture.SetSecondaries("secondary-drift-with-missing-primary-source.jpg");
        fixture.Pump(4);
        fixture.AssertRoles(external.SourcePath);
        Assert(fixture.Store.Read().SequenceEqual(afterImport), "Missing imported source still permits secondary repair from cached primary");
        external = MakeImage(root, "external", Color.Gold);

        fixture.SetAll(global.SourcePath);
        fixture.Pump(5);
        Assert(fixture.Config.PhotoSha256 == global.Sha256, "Windows set-for-all imports only the primary role");
        fixture.AssertRoles(global.SourcePath);
        var beforeSecondaryChange = fixture.Store.Read();
        fixture.SetSecondaries("user-secondary-only.jpg");
        fixture.Pump(4);
        fixture.AssertRoles(global.SourcePath);
        Assert(fixture.Store.Read().SequenceEqual(beforeSecondaryChange), "Secondary-only Windows choices are overwritten, never imported");

        // Windows may overwrite the contents of the same cache filename.
        var overwritten = MakeImage(root, "global", Color.LimeGreen);
        File.SetLastWriteTimeUtc(global.SourcePath, DateTime.UtcNow.AddSeconds(10));
        fixture.Pump(5);
        Assert(fixture.Config.PhotoSha256 == overwritten.Sha256, "Same-path changed bytes are imported");
        var alias = Path.Combine(root, "same-photo-other-path.png");
        File.Copy(global.SourcePath, alias);
        var beforeAlias = fixture.Store.Read();
        fixture.SetPrimary(alias); writes = fixture.Desktop.Fake.Calls;
        fixture.Pump(4);
        Assert(fixture.Store.Read().SequenceEqual(beforeAlias) && fixture.Desktop.Fake.Calls == writes,
            "Equal image bytes at another path cause neither adoption nor repair churn");
        File.Delete(alias); fixture.Pump(3); fixture.AssertNotPaused();

        // A saved image must still follow its ROLE on subsequent primary changes.
        fixture.Desktop.Fake.State = fixture.Desktop.Fake.State with
        {
            Displays = fixture.Desktop.Fake.State.Displays.Select((d, i) => d with { IsPrimary = i == 2 }).ToArray(),
            Wallpapers = fixture.Desktop.Fake.State.Wallpapers.Select((w, i) => w with { IsPrimary = i == 2 }).ToArray()
        };
        fixture.Pump(9); fixture.AssertRoles();
        Assert(fixture.Config.PhotoSha256 == overwritten.Sha256, "Imported primary follows a later role change");

        fixture.Desktop.CaptureFails = true;
        for (var i = 0; i < 12; i++) fixture.Pump(1, 31);
        Assert(fixture.Status == UiText.T("StatusWaiting", "en"), "More than ten mapping failures still retry");
        fixture.AssertNotPaused();
        fixture.Desktop.CaptureFails = false;
        fixture.Pump(4, 31); fixture.AssertRoles();

        fixture.SetSecondaries("setter-failure.jpg");
        fixture.Desktop.Fake.FailOnCall = fixture.Desktop.Fake.Calls + 1;
        fixture.Pump(3);
        fixture.AssertNotPaused();
        fixture.Desktop.Fake.FailOnCall = 0;
        fixture.Pump(6, 31); fixture.AssertRoles();

        writes = fixture.Desktop.Fake.Calls;
        fixture.Desktop.Fake.State = fixture.Desktop.Fake.State with { Position = 5, SlideshowStatus = 2 };
        fixture.Pump(8, 31); fixture.AssertNotPaused();
        Assert(fixture.Desktop.Fake.Calls == writes && fixture.Desktop.Fake.State.Position == 5,
            "Unsupported Windows mode stays untouched and is watched for recovery");
        fixture.Desktop.Fake.State = fixture.Desktop.Fake.State with { Position = 4, SlideshowStatus = 1 };
        fixture.Pump(4, 31); fixture.AssertRoles();

        // Repeated drift keeps a bounded write rate and resumes without a click.
        for (var i = 0; i < 9; i++)
        {
            fixture.SetSecondaries("repeated-" + i + ".jpg");
            fixture.Pump(3);
        }
        fixture.AssertNotPaused();
        Assert(fixture.Status == UiText.T("StatusRetry", "en"), "Repair limit enters a cooldown");
        fixture.Pump(4, 61); fixture.AssertRoles();

        var missing = Path.Combine(root, "not-yet-readable.png");
        fixture.SetPrimary(missing);
        var beforeMissing = fixture.Store.Read(); writes = fixture.Desktop.Fake.Calls;
        fixture.Pump(5, 31);
        Assert(fixture.Store.Read().SequenceEqual(beforeMissing) && fixture.Desktop.Fake.Calls == writes,
            "Unreadable candidate preserves both saved state and the observed primary");
        File.Copy(external.SourcePath, missing);
        fixture.Pump(5, 31);
        Assert(fixture.Config.PhotoSha256 == external.Sha256, "A candidate becoming readable recovers automatically");

        var toggle = fixture.Context.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Name == "PauseResumeItem");
        toggle.PerformClick();
        var captures = fixture.Desktop.Captures;
        var beforePause = fixture.Store.Read();
        fixture.SetPrimary(primary.SourcePath);
        fixture.Pump(20, 31);
        Assert(fixture.Desktop.Captures == captures && fixture.Store.Read().SequenceEqual(beforePause), "Manual Pause stays paused");
        toggle.PerformClick(); fixture.Pump(5);
        Assert(fixture.Config.PhotoSha256 == primary.Sha256, "Manual Resume starts watching Windows primary again");
        Assert(fixture.Startup.Reads == fixture.StartupReads && fixture.Startup.Writes == fixture.StartupWrites,
            "Watcher imports and recoveries never touch startup registration");

        TestTransactions(root, primary, secondary, external);
        var messages = ReadLogs(fixture.Store.Root);
        foreach (var code in new[] { "DISPLAY_TRANSITION_BEGIN", "DISPLAY_TRANSITION_END", "WINDOWS_PRIMARY_ADOPTED",
            "MAPPING_WAIT", "MAPPING_RECOVERED", "RECOVERY_RESUMED", "MODE_WAIT", "REPAIR_COOLDOWN" })
            Assert(messages.Contains(code), "Diagnostic timeline includes " + code);
        Assert(!messages.Contains("PAUSED System.") && !messages.Contains("PAUSED PrimaryWallpaperAgent"), "No automatic pause on runtime failures");
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
        {
            Passed = true, NativeWallpaperCalls = 0, NativeStartupWrites = 0,
            Tests = "primary transition races; Windows primary/global/secondary changes; same-path/alias bytes; durability; mapping/setter/mode recovery; cooldown; manual pause; transaction races/rollback"
        }, SettingsStore.JsonOptions));
        Console.WriteLine("RECOVERY TESTS PASSED: Windows primary adoption, app-only secondary, transition settling, automatic retries/cooldown, transaction rollback and manual pause.");
        Console.WriteLine("Evidence: " + root);
        return 0;
    }

    private static void TestTransactions(string root, SelectedImage primary, SelectedImage secondary, SelectedImage external)
    {
        var store = new SettingsStore(Path.Combine(root, "transactions"));
        var config = SettingsStore.Parse(FirstRun.Open(store));
        var desktop = new TestDesktop(); var startup = new Startup();
        var live = new LiveSettings(store, config, store.Read(), startup, desktop, _ => { }, (_, _) => { });
        live.BeginSession(); live.Apply(new(primary, true, "en", true, 500));
        live.Apply(new(secondary, true, "en", true, 500, Target: WallpaperTarget.Secondary));
        var original = store.Read();
        desktop.Fake.State = SetPrimary(desktop.Fake.State, external.SourcePath);
        var observed = desktop.Capture();
        desktop.Fake.SwitchPrimary();
        ExpectFailure(() => live.AdoptWindowsPrimary(external, observed));
        Assert(store.Read().SequenceEqual(original), "Primary changing before adoption rejects the stale candidate");
        desktop.Fake.State = observed;
        var failing = new FailingStore(store);
        var rollbackLive = new LiveSettings(store, live.Config, original, startup, desktop, _ => { }, (_, _) => { }, failing);
        ExpectFailure(() => rollbackLive.AdoptWindowsPrimary(external, observed));
        Assert(store.Read().SequenceEqual(original) && rollbackLive.Config.PhotoSha256 == primary.Sha256,
            "Failed import restores exact persisted bytes and leaves in-memory state unchanged");
        live.AdoptWindowsPrimary(external, observed);
        live.BeginSession(); live.UndoImage();
        Assert(live.Config.PhotoSha256 == primary.Sha256 && live.Config.SecondaryImage?.Sha256 == secondary.Sha256,
            "Undo restores the prior primary while preserving secondary");
        var stale = new LiveSettings(store, live.Config, store.Read(), startup, desktop, _ => { }, (_, _) => { });
        File.AppendAllText(Path.Combine(store.Root, "settings.json"), " ");
        var edited = store.Read();
        ExpectFailure(() => stale.AdoptWindowsPrimary(external, desktop.Capture()));
        Assert(store.Read().SequenceEqual(edited), "Externally edited settings are never overwritten");
    }

    private static SelectedImage MakeImage(string root, string name, Color color)
    {
        var path = Path.Combine(root, name + ".png");
        using var image = new Bitmap(64, 40);
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(color);
        image.Save(path, ImageFormat.Png);
        return SelectedImage.Read(path);
    }
    private static ProbeResult SetPrimary(ProbeResult state, string path) => state with
        { Wallpapers = state.Wallpapers.Select(w => w.IsPrimary == true ? w with { WallpaperPath = path } : w).ToArray() };
    private static string ReadLogs(string root) => string.Join("\n", Directory.GetFiles(Path.Combine(root, "runtime", "logs"), "*.log").Select(path =>
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }));
    private static void ExpectFailure(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; } catch (IOException) { return; }
        throw new InvalidOperationException("RECOVERY TEST FAILED: expected rejection");
    }
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException("RECOVERY TEST FAILED: " + message); }

    private sealed class Fixture : IDisposable
    {
        internal readonly SettingsStore Store;
        internal readonly TestDesktop Desktop = new();
        internal readonly Startup Startup = new();
        internal readonly int StartupReads;
        internal readonly int StartupWrites;
        internal readonly AgentContext Context;
        private TimeSpan now;
        internal TrayConfig Config => SettingsStore.Parse(Store.Read());
        internal string Status => Context.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Name == "StatusItem").Text ?? "";
        internal Fixture(string root, SelectedImage primary, SelectedImage secondary)
        {
            Store = new SettingsStore(Path.Combine(root, "watcher"));
            var initial = SettingsStore.Parse(FirstRun.Open(Store));
            var live = new LiveSettings(Store, initial, Store.Read(), Startup, Desktop, _ => { }, (_, _) => { });
            live.BeginSession(); live.Apply(new(primary, true, "en", true, 500, "dark"));
            live.Apply(new(secondary, true, "en", true, 500, "dark", WallpaperTarget.Secondary));
            StartupReads = Startup.Reads; StartupWrites = Startup.Writes;
            Context = new AgentContext(Store, live.Config, Store.Read(), false, false, Desktop, Startup, () => now);
        }
        internal void Pump(int count, int seconds = 1)
        { for (var i = 0; i < count; i++) { now += TimeSpan.FromSeconds(seconds); Context.Tick(); } }
        internal void SetPrimary(string path) => Desktop.Fake.State = RecoverySelfTests.SetPrimary(Desktop.Fake.State, path);
        internal void SetAll(string path) => Desktop.Fake.State = Desktop.Fake.State with
            { Wallpapers = Desktop.Fake.State.Wallpapers.Select(w => w with { WallpaperPath = path }).ToArray() };
        internal void SetSecondaries(string path) => Desktop.Fake.State = Desktop.Fake.State with
            { Wallpapers = Desktop.Fake.State.Wallpapers.Select(w => w.IsPrimary != true ? w with { WallpaperPath = path } : w).ToArray() };
        internal void AssertNotPaused() => Assert(Context.TrayMenu.Items.OfType<ToolStripMenuItem>()
            .Single(i => i.Name == "PauseResumeItem").Text == UiText.T("MenuPause", "en"), "Runtime failure must not latch Pause");
        internal void AssertRoles(string? primaryAlias = null)
        {
            var images = Store.Images(Config);
            Assert(TrialEngine.Active(Desktop.Fake.State).All(w => StringComparer.OrdinalIgnoreCase.Equals(w.WallpaperPath,
                w.IsPrimary == true ? primaryAlias ?? images.PhotoPath : images.SecondaryPath)), "Current monitor roles match selected images");
        }
        public void Dispose() => Context.Dispose();
    }
    private sealed class TestDesktop : ITrialDesktop
    {
        internal readonly TrialSelfTests.FakeDesktop Fake = new(3);
        internal bool CaptureFails;
        internal int Captures;
        public ProbeResult Capture()
        { Captures++; if (CaptureFails) throw new InvalidOperationException("Injected mapping failure"); return Fake.Capture(); }
        public void SetWallpaper(string id, string path) => Fake.SetWallpaper(id, path);
    }
    private sealed class Startup : IStartupRegistration
    {
        internal int Reads;
        internal int Writes;
        private string? value;
        public string ExpectedCommand => "fixture.exe --startup";
        public string? Read() { Reads++; return value; }
        public void Write(string? command) { Writes++; value = command; }
    }
    private sealed class FailingStore(SettingsStore store) : ISettingsPersistence
    {
        private bool fail = true;
        public byte[] Read() => store.Read();
        public void Write(byte[] bytes)
        { store.Write(bytes); if (fail) { fail = false; throw new IOException("Injected adoption save failure"); } }
    }
}
