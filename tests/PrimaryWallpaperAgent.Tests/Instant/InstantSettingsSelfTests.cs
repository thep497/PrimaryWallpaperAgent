using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class InstantSettingsSelfTests
{
    internal static int Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "runtime", "instant-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        using (var fixture = new Fixture(Path.Combine(root, "edits")))
        {
            using (var form = fixture.OpenForm())
            {
                Assert(form.Controls.Find("SaveButton", true).Length == 0 && form.Controls.Find("CancelButton", true).Length == 0, "No Save/Cancel controls");
                Find<ComboBox>(form, "LanguageSelector").SelectedIndex = 0;
                Assert(fixture.Live.Config.Language == "th" && form.Visible, "Language persisted without closing");
                Capture(form, Path.Combine(root, "config-th.png"));
                Find<ComboBox>(form, "PollSelector").SelectedIndex = 2;
                Find<CheckBox>(form, "StartupCheck").Checked = true;
                Find<CheckBox>(form, "StartupCheck").Checked = false;
                Assert(fixture.Live.Config.PollIntervalMs == 2000 && fixture.Startup.Writes == 2 && fixture.Startup.Value is null, "Multiple startup/poll edits use the latest transaction baseline");
                Assert(fixture.Desktop.Captures == 0 && fixture.Desktop.Fake.Calls == 0, "Preferences never access desktop");
                Find<Button>(form, "UseCurrentButton").PerformClick(); // image B
                Assert(fixture.Live.Config.PhotoSha256 == fixture.B.Sha256 && fixture.Live.CanUndoImage, "Image command applies immediately");
                Find<ComboBox>(form, "LanguageSelector").SelectedIndex = 1;
                Find<ComboBox>(form, "PollSelector").SelectedIndex = 3;
                Find<CheckBox>(form, "StartupCheck").Checked = true;
                fixture.Source = fixture.A.SourcePath;
                Find<Button>(form, "UseCurrentButton").PerformClick(); // back to image A
                Assert(fixture.Live.Config.PhotoSha256 == fixture.A.Sha256, "A-B-A still adopts deliberate selections");
                Capture(form, Path.Combine(root, "config-en.png"));
                Find<Button>(form, "CloseButton").PerformClick();
            }
            using (var reopened = fixture.OpenForm())
            {
                Assert(fixture.Live.Config.Language == "en" && Find<ComboBox>(reopened, "PollSelector").SelectedIndex == 3, "Close/reopen retains edits");
                Assert(Find<Button>(reopened, "UndoImageButton").Enabled, "Undo survives window close/reopen");
                var bytes = fixture.Store.Read();
                fixture.Desktop.Fake.FailOnCall = fixture.Desktop.Fake.Calls + 2;
                Find<Button>(reopened, "UndoImageButton").PerformClick();
                Assert(fixture.Live.CanUndoImage && fixture.Store.Read().SequenceEqual(bytes), "Failed undo preserves previous settings and undo availability");
                Assert(Find<Label>(reopened, "ImageFilename").Text == fixture.A.FriendlyName, "Failed undo keeps current preview");
                fixture.Desktop.Fake.FailOnCall = 0;
                Find<Button>(reopened, "UndoImageButton").PerformClick();
                Assert(fixture.Live.Config.PhotoSha256 == fixture.B.Sha256 && !fixture.Live.CanUndoImage, "Undo restores one previous image");
                Assert(fixture.Live.Config.Language == "en" && fixture.Live.Config.PollIntervalMs == 5000 &&
                    fixture.Live.Config.StartWithWindows && fixture.Startup.Value == fixture.Startup.ExpectedCommand,
                    "Undo preserves later preferences and startup");
                Assert(fixture.Desktop.Fake.State.Wallpapers.All(w => w.WallpaperPath == (w.IsPrimary == true ? fixture.Live.Images.PhotoPath : fixture.Live.Images.BlackPath)), "Undo obeys current-primary policy");
                Assert(SelectedImage.Read(fixture.A.SourcePath).Bytes.SequenceEqual(fixture.A.Bytes) && SelectedImage.Read(fixture.B.SourcePath).Bytes.SequenceEqual(fixture.B.Bytes), "Source images unchanged");
            }
        }
        using (var fixture = new Fixture(Path.Combine(root, "failures")))
        using (var form = fixture.OpenForm())
        {
            var bytes = fixture.Store.Read();
            fixture.Persistence.FailNext = true;
            Find<ComboBox>(form, "LanguageSelector").SelectedIndex = 0;
            Assert(Find<ComboBox>(form, "LanguageSelector").SelectedIndex == 1 && fixture.Store.Read().SequenceEqual(bytes),
                "Failed preference change restores UI and exact bytes");
            Assert(fixture.Desktop.Captures == 0, "Failed preference change never touches desktop");
            fixture.Startup.FailNext = true;
            Find<CheckBox>(form, "StartupCheck").Checked = true;
            Assert(!Find<CheckBox>(form, "StartupCheck").Checked && fixture.Startup.Value is null &&
                fixture.Store.Read().SequenceEqual(bytes), "Failed startup toggle restores UI, registry fake and settings");
            Find<CheckBox>(form, "StartupCheck").Checked = true;
            Find<CheckBox>(form, "StartupCheck").Checked = false;
            Assert(fixture.Startup.Value is null, "A retried toggle uses the correct baseline after rollback");
            var good = fixture.Store.Read();
            var external = good.Concat(new byte[] { 32 }).ToArray();
            File.WriteAllBytes(Path.Combine(fixture.Folder, "settings.json"), external);
            Find<ComboBox>(form, "LanguageSelector").SelectedIndex = 0;
            Assert(fixture.Store.Read().SequenceEqual(external) && Find<ComboBox>(form, "LanguageSelector").SelectedIndex == 1,
                "External settings edits are not overwritten");
            File.WriteAllBytes(Path.Combine(fixture.Folder, "settings.json"), good);
            fixture.Source = Path.Combine(fixture.Folder, "bad.png");
            File.WriteAllText(fixture.Source, "invalid");
            Find<Button>(form, "UseCurrentButton").PerformClick();
            Assert(fixture.Live.Config.PhotoSha256 == fixture.A.Sha256 && !fixture.Live.CanUndoImage, "Invalid image does not replace selection/undo state");
        }
        TestWatcherWithOpenSettings(root);
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
        {
            Passed = true, RealWallpaperSetters = 0, RealRegistryWrites = 0,
            Tests = new[] { "immediate preferences", "rolling startup baseline", "A-B-A", "image undo only",
                "close/reopen", "rollback and UI reversion", "external edits", "invalid image", "watcher while Config open", "tray menu removal" }
        }, SettingsStore.JsonOptions));
        Console.WriteLine("INSTANT SETTINGS TESTS PASSED: immediate edits, startup baseline, image undo, UI rollback, live watcher and simplified tray.");
        return 0;
    }

    private static void TestWatcherWithOpenSettings(string root)
    {
        using var fixture = new Fixture(Path.Combine(root, "watcher"));
        fixture.Desktop.Fake.State = fixture.Desktop.Fake.State with
        { Wallpapers = fixture.Desktop.Fake.State.Wallpapers.Select(w => w with { WallpaperPath = fixture.Live.Images.PhotoPath }).ToArray() };
        using var context = new AgentContext(fixture.Store, fixture.Initial, fixture.Store.Read(), false, true, fixture.Desktop, fixture.Startup);
        using var monitor = new System.Windows.Forms.Timer { Interval = 50 };
        var elapsed = Stopwatch.StartNew();
        var stage = 0;
        var writesBeforeSwitch = 0;
        Exception? failure = null;
        monitor.Tick += (_, _) =>
        {
            var form = Application.OpenForms.OfType<ConfigForm>().SingleOrDefault();
            try
            {
                if (elapsed.Elapsed.TotalSeconds > 12) throw new InvalidOperationException("Live watcher test timed out");
                if (form is null) return;
                if (stage == 0 && fixture.Desktop.Fake.Calls >= 2)
                {
                    var items = context.TrayMenu.Items.OfType<ToolStripMenuItem>().ToArray();
                    Assert(items.Select(item => item.Name).SequenceEqual(new[] { "StatusItem", "SettingsItem", "PauseResumeItem", "LogItem", "ExitItem" }), "Tray contains only the intended five items");
                    var toggle = items.Single(item => item.Name == "PauseResumeItem");
                    toggle.PerformClick();
                    Find<ComboBox>(form, "PollSelector").SelectedIndex = 1; // 1 second, while paused
                    Find<ComboBox>(form, "LanguageSelector").SelectedIndex = 0;
                    Assert(Find<Label>(form, "AgentStatus").Text.Contains(UiText.T("StatusPaused", "th")), "Preference edits preserve user pause");
                    Assert(SettingsStore.Parse(fixture.Store.Read()).PollIntervalMs == 1000, "Live agent immediately persists new interval");
                    fixture.Desktop.Fake.SwitchPrimary();
                    writesBeforeSwitch = fixture.Desktop.Fake.Calls;
                    stage = 1;
                    toggle.PerformClick();
                }
                else if (stage == 1 && fixture.Desktop.Fake.Calls > writesBeforeSwitch &&
                    fixture.Desktop.Fake.State.Wallpapers.All(w => w.WallpaperPath == (w.IsPrimary == true ? fixture.Live.Images.PhotoPath : fixture.Live.Images.BlackPath)))
                {
                    Assert(form.Visible, "Config remains open during primary-follow repair");
                    Assert(fixture.Startup.Writes == 0, "Agent integration used no startup writes");
                    stage = 2;
                    monitor.Stop(); form.Close(); context.ExitThread();
                }
            }
            catch (Exception error)
            { failure = error; monitor.Stop(); form?.Close(); context.ExitThread(); }
        };
        monitor.Start();
        Application.Run(context);
        if (failure is not null) throw new InvalidOperationException("Watcher integration failed", failure);
        Assert(stage == 2, "Watcher repaired both initial secondary displays and primary switch while Config stayed open");
    }

    private static void Capture(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(path, ImageFormat.Png);
    }
    private static T Find<T>(Control form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException("INSTANT SETTINGS TEST FAILED: " + message); }

    private sealed class Fixture : IDisposable
    {
        internal string Folder { get; }
        internal SelectedImage A { get; }
        internal SelectedImage B { get; }
        internal string Source;
        internal SettingsStore Store { get; }
        internal TrayConfig Initial { get; }
        internal LiveSettings Live { get; }
        internal CountingDesktop Desktop { get; } = new();
        internal FakeStartup Startup { get; } = new();
        internal FailingPersistence Persistence { get; }
        internal Fixture(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(Path.Combine(folder, "assets"));
            var a = Path.Combine(folder, "assets", "a.png");
            var b = Path.Combine(folder, "assets", "b.png");
            var black = Path.Combine(folder, "assets", "black.png");
            using (var bitmap = new Bitmap(320, 180))
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.Teal); bitmap.Save(a, ImageFormat.Png);
                graphics.Clear(Color.CornflowerBlue); bitmap.Save(b, ImageFormat.Png);
                graphics.Clear(Color.Black); bitmap.Save(black, ImageFormat.Png);
            }
            A = SelectedImage.Read(a); B = SelectedImage.Read(b); Source = b;
            var blackImage = SelectedImage.Read(black);
            Initial = new(3, "assets/a.png", A.Sha256, "assets/black.png", blackImage.Sha256, [], false, A.FriendlyName, a, "en", 500);
            Store = new(folder);
            File.WriteAllBytes(Path.Combine(folder, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(Initial, SettingsStore.JsonOptions));
            Persistence = new(Store);
            Live = new(Store, Initial, Store.Read(), Startup, Desktop, _ => { }, (_, _) => { }, Persistence);
        }
        internal ConfigForm OpenForm()
        {
            var enabled = Live.BeginSession();
            var form = new ConfigForm(Live.CurrentImage(), enabled, code => UiText.T("StatusRunning", code, 3),
                () => Source, Live.Apply, _ => { }, Live.Config.Language, pollIntervalMs: Live.Config.PollIntervalMs,
                undoImage: Live.UndoImage, canUndo: () => Live.CanUndoImage);
            form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
            return form;
        }
        public void Dispose() { }
    }
    private sealed class CountingDesktop : ITrialDesktop
    {
        internal TrialSelfTests.FakeDesktop Fake { get; } = new(3);
        internal int Captures;
        public ProbeResult Capture() { Captures++; return Fake.Capture(); }
        public void SetWallpaper(string id, string path) => Fake.SetWallpaper(id, path);
    }
    private sealed class FakeStartup : IStartupRegistration
    {
        internal string? Value;
        internal int Writes;
        internal bool FailNext;
        public string ExpectedCommand => "\"fixture.exe\" --startup";
        public string? Read() => Value;
        public void Write(string? value) { Writes++; Value = value; if (FailNext) { FailNext = false; throw new IOException("Injected startup failure"); } }
    }
    private sealed class FailingPersistence(SettingsStore store) : ISettingsPersistence
    {
        internal bool FailNext;
        public byte[] Read() => store.Read();
        public void Write(byte[] bytes) { store.Write(bytes); if (FailNext) { FailNext = false; throw new IOException("Injected settings failure"); } }
    }
}
