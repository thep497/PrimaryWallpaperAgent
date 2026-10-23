using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class SecondaryWallpaperSelfTests
{
    internal static int Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "runtime", "secondary-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var photos = Enumerable.Range(0, 7).Select(i => MakeImage(root, i)).ToArray();
        var store = new SettingsStore(Path.Combine(root, "data"));
        var initialBytes = FirstRun.Open(store);
        var initial = SettingsStore.Parse(initialBytes);
        foreach (var schema in new[] { 1, 2, 3 })
        {
            var legacy = initial with { SchemaVersion = schema, HasChosenImage = true };
            var parsed = SettingsStore.Parse(JsonSerializer.SerializeToUtf8Bytes(legacy));
            Assert(parsed.SecondaryImage is null && store.Images(parsed).SecondaryPath == store.Asset(initial.BlackFile, initial.BlackSha256),
                "Existing schemas default to black without replacing the primary");
        }
        Assert(FirstRun.Open(store).SequenceEqual(initialBytes), "Reading an installation does not rewrite its settings");
        var desktop = new RecordingDesktop(3);
        var startup = new NoStartup();
        var live = Open(store, desktop, startup);
        Apply(live, photos[1], WallpaperTarget.Secondary);
        Assert(File.ReadAllBytes(Path.Combine(store.Root, "settings-before-schema4.json")).SequenceEqual(initialBytes), "Schema upgrade keeps an exact permanent pre-upgrade backup");
        Assert(!live.Config.HasChosenImage && desktop.Writes.Count == 0 && live.Config.SecondaryImage?.Sha256 == photos[1].Sha256,
            "Secondary may be preconfigured before first primary without activating the watcher");
        live.UndoImage(WallpaperTarget.Secondary);
        Assert(live.Config.SecondaryImage is null && !live.Config.HasChosenImage && desktop.Writes.Count == 0,
            "Undo preconfiguration returns to black and stays inactive");
        Apply(live, photos[1], WallpaperTarget.Secondary);
        Apply(live, photos[0]);
        AssertPolicy(live, desktop);
        var primary = desktop.Fake.State.Wallpapers.Single(w => w.IsPrimary == true);
        desktop.Writes.Clear();
        Apply(live, photos[2], WallpaperTarget.Secondary);
        Assert(desktop.Writes.Count == 2 && desktop.Writes.All(w => w.Id != primary.MonitorId), "Secondary edit touches both secondaries and never rewrites primary");
        AssertPolicy(live, desktop);
        var baseline = desktop.Capture();
        desktop.Fake.SwitchPrimary(); desktop.Writes.Clear();
        Assert(WallpaperPolicy.Repair(live.Images, desktop.Capture(), desktop, _ => { }, baseline), "Primary switch repairs role assignments");
        AssertPolicy(live, desktop);
        Assert(desktop.Writes.Count == 2, "Only the two monitors changing role are written");
        baseline = desktop.Capture();
        desktop.Fake.State = new TrialSelfTests.FakeDesktop(5).State;
        Assert(WallpaperPolicy.Repair(live.Images, desktop.Capture(), desktop, _ => { }, baseline), "New monitors accept the secondary picture");
        AssertPolicy(live, desktop);
        var calls = desktop.Writes.Count;
        Assert(!WallpaperPolicy.Repair(live.Images, desktop.Capture(), desktop, _ => { }, desktop.Capture()) && desktop.Writes.Count == calls, "Stable policy has no repeat setters");

        Apply(live, photos[3]);
        Apply(live, photos[4], WallpaperTarget.Secondary);
        live.Apply(new(live.CurrentImage(), false, "th", false, 2000, "dark"));
        live.UndoImage();
        Assert(live.Config.PhotoSha256 == photos[0].Sha256 && live.Config.SecondaryImage?.Sha256 == photos[4].Sha256,
            "Primary Undo preserves newer secondary selection");
        live.UndoImage(WallpaperTarget.Secondary);
        Assert(live.Config.SecondaryImage?.Sha256 == photos[2].Sha256 && live.Config.PhotoSha256 == photos[0].Sha256 && live.Config.Theme == "dark" && live.Config.PollIntervalMs == 2000,
            "Secondary Undo preserves primary and preferences");
        Apply(live, photos[0], WallpaperTarget.Secondary, black: true);
        Assert(live.Config.SecondaryImage is null && live.Images.SecondaryPath == store.Asset(live.Config.BlackFile, live.Config.BlackSha256), "Black is a permanent default, not another history entry");
        live.UndoImage(WallpaperTarget.Secondary); AssertPolicy(live, desktop);
        Assert(live.Config.SecondaryImage?.Sha256 == photos[2].Sha256, "Secondary Undo may restore a picture after Use black");
        for (var i = 1; i < photos.Length; i++) Apply(live, photos[i], WallpaperTarget.Secondary);
        Assert(live.Config.RecentImages?.Length == 4 && live.Config.RecentImages.All(i => i.PhotoFile != live.Config.BlackFile), "One shared four-picture library excludes system black");
        Assert(live.RecentImages()[0].Sha256 == photos[0].Sha256, "Primary remains available after falling out of the shared library");
        live = Open(store, desktop, startup);
        Assert(live.CurrentImage().Sha256 == photos[0].Sha256 && live.CurrentImage(WallpaperTarget.Secondary).Sha256 == photos[6].Sha256,
            "Both selections survive restart independently of the bounded library");
        ExpectFailure(() => SettingsStore.Parse(JsonSerializer.SerializeToUtf8Bytes(live.Config with { SecondaryImage = new("assets/test.jpg", "bad") })));
        ExpectFailure(() => live.Apply(new(photos[0], false, "en", true, UseBlack: true)));
        TestFailures(store, desktop, startup, live, photos);
        TestOfflineAndManualChange(store, desktop, startup, photos);
        TestUi(root, store, desktop, startup, photos);
        live = Open(store, desktop, startup); Apply(live, photos[0]); Apply(live, photos[2], WallpaperTarget.Secondary);
        TestAgent(store, desktop, startup);
        Assert(startup.Writes == 0, "No startup registration is opted into");
        foreach (var photo in photos)
        {
            Assert(Path.GetFullPath(photo.SourcePath).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Fixture deletion stays inside the test root");
            File.Delete(photo.SourcePath);
        }
        live = Open(store, desktop, startup);
        Assert(live.CurrentImage().Bytes.Length > 0 && live.CurrentImage(WallpaperTarget.Secondary).Bytes.Length > 0,
            "Both roles use owned cached copies when original files disappear");
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { Passed = true, NativeWallpaperCalls = 0, RegistryWrites = 0,
            Tests = "legacy black defaults, first-run/offline, 1/3/5 monitors, primary switch/hotplug, shared history, scoped Undo, manual-change preservation, transaction rollback, bilingual/theme UI and actual AgentContext wiring" }));
        Console.WriteLine("SECONDARY WALLPAPER TESTS PASSED: role-based pictures, shared recents, independent Undo, offline/first-run, rollback and real UI/watcher wiring; no native setters.");
        Console.WriteLine("Evidence: " + root);
        return 0;
    }
    private static void TestFailures(SettingsStore store, RecordingDesktop desktop, NoStartup startup, LiveSettings live, SelectedImage[] photos)
    {
        var bytes = store.Read(); var before = desktop.Capture();
        desktop.Fake.FailOnCall = desktop.Fake.Calls + 2;
        ExpectFailure(() => Apply(live, photos[1], WallpaperTarget.Secondary));
        desktop.Fake.FailOnCall = 0;
        Assert(store.Read().SequenceEqual(bytes) && SameWallpapers(before, desktop.Capture()), "Setter failure restores assignments and exact settings/history");
        var failing = new LiveSettings(store, SettingsStore.Parse(bytes), bytes, startup, desktop, _ => { }, (_, _) => { }, new FailOnceStore(store));
        failing.BeginSession();
        ExpectFailure(() => Apply(failing, photos[1], WallpaperTarget.Secondary));
        Assert(store.Read().SequenceEqual(bytes) && SameWallpapers(before, desktop.Capture()) && !failing.CanUndo(WallpaperTarget.Secondary),
            "Save-after-write failure rolls back both roles and does not create Undo/history state");
    }
    private static void TestOfflineAndManualChange(SettingsStore store, RecordingDesktop desktop, NoStartup startup, SelectedImage[] photos)
    {
        var live = Open(store, desktop, startup);
        desktop.Fake.State = new TrialSelfTests.FakeDesktop(1).State;
        desktop.Fake.State = desktop.Fake.State with { Wallpapers = desktop.Fake.State.Wallpapers.Select(w => w with { WallpaperPath = live.Images.PhotoPath }).ToArray() };
        desktop.Writes.Clear();
        Apply(live, photos[2], WallpaperTarget.Secondary);
        Assert(desktop.Writes.Count == 0 && live.Config.SecondaryImage?.Sha256 == photos[2].Sha256, "One-monitor configuration saves the secondary without touching the primary");
        var one = desktop.Capture(); desktop.Fake.State = new TrialSelfTests.FakeDesktop(3).State;
        WallpaperPolicy.Repair(live.Images, desktop.Capture(), desktop, _ => { }, one); AssertPolicy(live, desktop);
        desktop.Fake.SwitchPrimary();
        Apply(live, photos[4], WallpaperTarget.Secondary);
        AssertPolicy(live, desktop);
        Assert(live.LastChangeMayResume, "Secondary edit during a pending primary swap rebases only trusted role images");
        desktop.Fake.State = desktop.Fake.State with { Wallpapers = desktop.Fake.State.Wallpapers.Select(w => w.IsPrimary == true ? w with { WallpaperPath = "user-changed-primary.jpg" } : w).ToArray() };
        var calls = desktop.Writes.Count;
        ExpectFailure(() => WallpaperPolicy.Repair(live.Images, desktop.Capture(), desktop, _ => { }, desktop.Capture()));
        Assert(desktop.Writes.Count == calls, "Unfamiliar Windows change on unchanged monitors still pauses");
        Apply(live, photos[3], WallpaperTarget.Secondary);
        Assert(desktop.Capture().Wallpapers.Single(w => w.IsPrimary == true).WallpaperPath == "user-changed-primary.jpg" && !live.LastChangeMayResume,
            "Secondary selection preserves unrelated primary change and cannot resume an unmatched policy");
        Apply(live, photos[0]); AssertPolicy(live, desktop);
        Assert(live.LastChangeMayResume, "Explicit primary adoption restores a complete policy");
                var historical = RecentImageHistory.Library(live.Config).First(i => i.Sha256 != live.Config.PhotoSha256 && i.Sha256 != live.Config.SecondaryImage?.Sha256);
        desktop.Fake.State = desktop.Fake.State with { Wallpapers = desktop.Fake.State.Wallpapers.Select(w => w.IsPrimary == true ? w :
            w with { WallpaperPath = store.Asset(historical.PhotoFile, historical.Sha256) }).ToArray() };
ExpectFailure(() => WallpaperPolicy.Repair(live.Images, desktop.Capture(), desktop, _ => { }, desktop.Capture()));
        Apply(live, photos[3], WallpaperTarget.Secondary); AssertPolicy(live, desktop);
    }
    private static void TestUi(string root, SettingsStore store, RecordingDesktop desktop, NoStartup startup, SelectedImage[] photos)
    {
        foreach (var language in new[] { "en", "th" })
        foreach (var theme in new[] { "light", "dark" })
        {
            var live = Open(store, desktop, startup); var submissions = 0;
            using var form = new ConfigForm(live.CurrentImage(), false, code => UiText.T("StatusRunning", code, 3), () => "unused",
                request => { submissions++; live.Apply(request); }, _ => { }, language, pollIntervalMs: live.Config.PollIntervalMs,
                undoImage: live.UndoImage, canUndo: () => live.CanUndoImage, imageConfigured: () => live.Config.HasChosenImage,
                recentImages: live.RecentImages, readRecentImage: live.ReadRecentImage, theme: theme,
                roles: new(t => RecentImageHistory.Current(live.Config, t), live.RecentImages, live.CanUndo, live.UndoImage, () => live.Config.SecondaryImage is null));
            form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
            var original = store.Read(); var calls = desktop.Writes.Count;
            Find<Button>(form, "WallpaperTargetSecondary").PerformClick();
            Assert(store.Read().SequenceEqual(original) && desktop.Writes.Count == calls && submissions == 0, "Viewing either role never applies or saves");
            Assert(Find<Button>(form, "UseCurrentButton").Text == UiText.T("UseBlack", language), "Secondary action clearly says Use black");
            Find<Button>(form, "UseCurrentButton").PerformClick();
            Assert(live.Config.SecondaryImage is null && Find<Label>(form, "ImageFilename").Text == UiText.T("BlackDefault", language), "Use black updates policy and shows a localized selected black tile");
            var primaryHash = live.Config.PhotoSha256;
            Capture(form, Path.Combine(root, "secondary-black-" + language + "-" + theme + ".png"));
            Find<Button>(form, "UndoImageButton").PerformClick();
            Assert(live.Config.SecondaryImage is not null && live.Config.PhotoSha256 == primaryHash, "Footer Undo is scoped to the viewed role");
            Find<Button>(form, "RecentImage1").PerformClick();
            Assert(live.Config.PhotoSha256 == primaryHash && live.Config.SecondaryImage is not null, "Recent picture applies only to the viewed role");
            Capture(form, Path.Combine(root, "secondary-picture-" + language + "-" + theme + ".png"));
            Find<Button>(form, "WallpaperTargetPrimary").PerformClick();
            Assert(Find<Button>(form, "UseCurrentButton").Text == UiText.T("UseCurrent", language), "Primary keeps the Windows-image action");
            var secondaryHash = live.Config.SecondaryImage?.Sha256;
            Find<Button>(form, "RecentImage1").PerformClick();
            Assert(live.Config.SecondaryImage?.Sha256 == secondaryHash, "Reusing a shared recent picture for primary preserves the secondary selection");
            var client = form.RectangleToScreen(form.ClientRectangle);
            foreach (var control in Descendants(form).Where(c => c.Visible && c is Button or ComboBox or CheckBox))
                Assert(client.Contains(control.RectangleToScreen(control.ClientRectangle)), "Bilingual role controls fit: " + control.Name);
            Capture(form, Path.Combine(root, "primary-" + language + "-" + theme + ".png"));
            form.Close();
        }
    }
    private static void TestAgent(SettingsStore store, RecordingDesktop desktop, NoStartup startup)
    {
        var config = SettingsStore.Parse(store.Read());
        using var context = new AgentContext(store, config, store.Read(), false, true, desktop, startup);
        using var timer = new System.Windows.Forms.Timer { Interval = 60 };
        var watch = Stopwatch.StartNew(); Exception? failure = null; var stage = 0;
        timer.Tick += (_, _) =>
        {
            var form = Application.OpenForms.OfType<ConfigForm>().SingleOrDefault();
            try
            {
                if (watch.Elapsed.TotalSeconds > 12) throw new InvalidOperationException("Agent UI timeout");
                if (form is null) return;
                if (stage == 0)
                {
                    var before = store.Read(); Find<Button>(form, "WallpaperTargetSecondary").PerformClick();
                    Assert(store.Read().SequenceEqual(before), "Actual AgentContext role navigation is read-only");
                    Find<Button>(form, "UseCurrentButton").PerformClick();
                    Assert(SettingsStore.Parse(store.Read()).SecondaryImage is null, "Actual AgentContext applies black to the secondary role");
                    Find<Button>(form, "UndoImageButton").PerformClick();
                    Assert(SettingsStore.Parse(store.Read()).SecondaryImage?.Sha256 == config.SecondaryImage?.Sha256, "Actual AgentContext wires scoped Undo");
                    desktop.Fake.SwitchPrimary(); stage = 1;
                }
                else if (stage == 1)
                {
                    var current = store.Images(SettingsStore.Parse(store.Read()));
                    if (!desktop.Capture().Wallpapers.All(w => w.WallpaperPath == (w.IsPrimary == true ? current.PhotoPath : current.SecondaryPath))) return;
                    stage = 2; timer.Stop(); form.Close(); context.ExitThread();
                }
            }
            catch (Exception error) { failure = error; timer.Stop(); form?.Close(); context.ExitThread(); }
        };
        timer.Start(); Application.Run(context);
        if (failure is not null) throw new InvalidOperationException("Agent role integration failed", failure);
        Assert(stage == 2, "Watcher follows primary changes with a custom secondary picture");
    }
    private static LiveSettings Open(SettingsStore store, RecordingDesktop desktop, NoStartup startup)
    { var live = new LiveSettings(store, SettingsStore.Parse(store.Read()), store.Read(), startup, desktop, _ => { }, (_, _) => { }); live.BeginSession(); return live; }
    private static void Apply(LiveSettings live, SelectedImage image, WallpaperTarget target = WallpaperTarget.Primary, bool black = false) =>
        live.Apply(new(image, false, live.Config.Language, true, live.Config.PollIntervalMs, live.Config.Theme, target, black));
    private static void AssertPolicy(LiveSettings live, RecordingDesktop desktop) => Assert(desktop.Capture().Wallpapers.All(w =>
        w.WallpaperPath == (w.IsPrimary == true ? live.Images.PhotoPath : live.Images.SecondaryPath)), "Only current primary uses primary picture; every secondary shares the secondary picture");
    private static bool SameWallpapers(ProbeResult a, ProbeResult b) => a.Wallpapers.Select(w => w.WallpaperPath).SequenceEqual(b.Wallpapers.Select(w => w.WallpaperPath));
    private static SelectedImage MakeImage(string root, int index)
    {
        var path = Path.Combine(root, "picture-" + index + ".png");
        using var image = new Bitmap(320, 180);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.FromArgb(30 + index * 25, 65 + index * 20, 90 + index * 15));
            using var brush = new SolidBrush(Color.Gold); graphics.FillEllipse(brush, 30 + index * 20, 20, 110, 130);
        }
        image.Save(path, ImageFormat.Png); return SelectedImage.Read(path);
    }
    private static void Capture(Form form, string path)
    { using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(path, ImageFormat.Png); }
    private static T Find<T>(Control form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void ExpectFailure(Action operation)
    { try { operation(); } catch (Exception e) when (e is UiMessageException or InvalidOperationException) { return; } throw new InvalidOperationException("Expected failure"); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException("SECONDARY TEST FAILED: " + message); }
    private sealed class NoStartup : IStartupRegistration
    { internal int Writes; public string ExpectedCommand => "fixture.exe --startup"; public string? Read() => null; public void Write(string? value) { Writes++; throw new InvalidOperationException("No registry writes"); } }
    private sealed class RecordingDesktop(int count) : ITrialDesktop
    {
        internal readonly TrialSelfTests.FakeDesktop Fake = new(count);
        internal readonly List<(string Id, string Path)> Writes = [];
        public ProbeResult Capture() => Fake.Capture();
        public void SetWallpaper(string monitorId, string path) { Writes.Add((monitorId, path)); Fake.SetWallpaper(monitorId, path); }
    }
    private sealed class FailOnceStore(SettingsStore store) : ISettingsPersistence
    { private bool fail = true; public byte[] Read() => store.Read(); public void Write(byte[] bytes) { store.Write(bytes); if (fail) { fail = false; throw new IOException("Injected persistence failure"); } } }
}