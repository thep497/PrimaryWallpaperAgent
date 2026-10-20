using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class RecentImagesSelfTests
{
    internal static int Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "runtime", "recent-images-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var store = new SettingsStore(Path.Combine(root, "data"));
        var initial = SettingsStore.Parse(FirstRun.Open(store));
        var desktop = new TrialSelfTests.FakeDesktop(3);
        var startup = new NoStartup();
        var live = new LiveSettings(store, initial, store.Read(), startup, desktop, _ => { }, (_, _) => { });
        live.BeginSession();
        Assert(live.RecentImages().Length == 0, "First-run black placeholder is not a recent picture");
        var sources = Enumerable.Range(0, 7).Select(index => MakeImage(root, index)).ToArray();
        for (var i = 0; i < 6; i++) live.Apply(new(sources[i], false, "en", true, 500));
        Assert(live.RecentImages().Select(image => image.Sha256).SequenceEqual(sources.Skip(2).Take(4).Reverse().Select(image => image.Sha256)),
            "Four distinct most recent successful pictures, including current");
        var legacy = live.Config with { RecentImages = null };
        Assert(RecentImageHistory.List(legacy).Length == 1, "Existing installations offer the current cached picture without scanning old files");
        Assert(SettingsStore.Parse(JsonSerializer.SerializeToUtf8Bytes(legacy)).PhotoSha256 == live.Config.PhotoSha256, "Additive settings remain backward readable");
        var reordered = live.ReadRecentImage(live.RecentImages()[2]);
        live.Apply(new(reordered, false, "en", true, 500));
        Assert(live.RecentImages().Length == 4 && live.RecentImages()[0].Sha256 == sources[3].Sha256 &&
            live.RecentImages()[1].Sha256 == sources[5].Sha256, "Selecting a previous picture moves it first without duplicates");

        var good = store.Read();
        var failing = new LiveSettings(store, live.Config, good, startup, desktop, _ => { }, (_, _) => { }, new FailOnceStore(store));
        failing.BeginSession();
        ExpectFailure(() => failing.Apply(new(sources[6], false, "en", true, 500)));
        Assert(store.Read().SequenceEqual(good) && failing.RecentImages().SequenceEqual(live.RecentImages()),
            "Failed image/settings transaction restores exact history and bytes");

        // Delete only known fixture source files. Recovery must use app-owned cached copies.
        foreach (var source in sources)
        {
            Assert(Path.GetFullPath(source.SourcePath).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Fixture deletion remains inside the test root");
            File.Delete(source.SourcePath);
        }
        var config = SettingsStore.Parse(store.Read());
        live = new LiveSettings(store, config, store.Read(), startup, desktop, _ => { }, (_, _) => { });
        live.BeginSession();
        Assert(live.RecentImages().Length == 4 && live.RecentImages().All(image => live.ReadRecentImage(image).Sha256 == image.Sha256),
            "History survives restart and original-file deletion");
        var outside = new RecentImage("../not-an-asset.png", sources[0].Sha256);
        ExpectFailure(() => RecentImageHistory.Read(store, outside));
        var brokenConfig = config with { RecentImages = Enumerable.Repeat(live.RecentImages()[0], 5).ToArray() };
        ExpectFailure(() => SettingsStore.Parse(JsonSerializer.SerializeToUtf8Bytes(brokenConfig)));

        foreach (var language in new[] { "en", "th" })
        foreach (var size in new[] { new Size(700, 640), new Size(640, 620), new Size(620, 620) })
        {
            using var form = OpenForm(live, language, size, Path.Combine(root, "IrisService", "missing.jpg"));
            var client = form.RectangleToScreen(form.ClientRectangle);
            foreach (var control in Descendants(form).Where(control => control.Visible && control is Button or ComboBox or CheckBox))
                Assert(client.Contains(control.RectangleToScreen(control.ClientRectangle)), "All controls fit with four thumbnails: " + control.Name);
            Assert(Descendants(form).OfType<ScrollableControl>().All(control => !control.AutoScroll && !control.VerticalScroll.Visible && !control.HorizontalScroll.Visible),
                "Four thumbnails require no scrollbars");
            var preview = Find<PictureBox>(form, "WallpaperPreview");
            var filename = Find<Label>(form, "ImageFilename");
            Assert(preview.Height >= 80 && preview.RectangleToScreen(preview.ClientRectangle).Bottom <= filename.RectangleToScreen(filename.ClientRectangle).Top,
                "Full image preview is not clipped: client=" + form.ClientSize + "; preview=" + preview.Bounds + "; filename=" + filename.Bounds + "; rows=" + string.Join(",", ((TableLayoutPanel)preview.Parent!).GetRowHeights()));
            var before = store.Read();
            Find<Button>(form, "UseCurrentButton").PerformClick();
            Assert(Find<Label>(form, "FeedbackLabel").Text == UiText.T("WindowsImageUnavailable", language) &&
                store.Read().SequenceEqual(before), "Missing Windows cache explains failure without changing settings");
            Find<Button>(form, "RecentImage0").PerformClick();
            Assert(live.Config.PhotoSha256 == config.PhotoSha256 &&
                Find<Label>(form, "FeedbackLabel").Text == UiText.T("ImageApplied", language),
                "Current cached choice: " + language + " " + size + "; feedback=" + Find<Label>(form, "FeedbackLabel").Text + "; current=" + live.Config.PhotoSha256 + "; expected=" + config.PhotoSha256);
            if (size.Width == 700)
            {
                using var screenshot = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(screenshot, new Rectangle(0, 0, form.Width, form.Height));
                screenshot.Save(Path.Combine(root, "recent-" + language + ".png"), ImageFormat.Png);
            }
            form.Close();
        }

        using (var form = OpenForm(live, "en", new Size(700, 640), "unused"))
        {
            var previous = live.Config.PhotoSha256;
            var chosen = live.RecentImages()[2];
            Find<Button>(form, "RecentImage2").PerformClick();
            Assert(live.Config.PhotoSha256 == chosen.Sha256 && live.RecentImages()[0].Sha256 == chosen.Sha256,
                "An older thumbnail applies its cached bytes and moves first");
            Find<Button>(form, "UndoImageButton").PerformClick();
            Assert(live.Config.PhotoSha256 == previous && !live.CanUndoImage,
                "Undo after a thumbnail choice restores the preceding picture and updates recency");
            var history = JsonSerializer.Serialize(live.RecentImages());
            var writes = desktop.Calls;
            Find<ComboBox>(form, "LanguageSelector").SelectedIndex = 0;
            Assert(JsonSerializer.Serialize(live.RecentImages()) == history && desktop.Calls == writes,
                "Preference-only edits preserve history and do not access wallpaper setters");
        }
        var older = live.RecentImages()[1];
        using (var form = OpenForm(live, "en", new Size(700, 640), "unused"))
        {
            File.AppendAllText(Path.Combine(store.Root, older.PhotoFile), "fixture corruption");
            var before = store.Read();
            Find<Button>(form, "RecentImage1").PerformClick();
            Assert(store.Read().SequenceEqual(before) && Find<Label>(form, "FeedbackLabel").Text == UiText.T("RecentImageUnavailable", "en"),
                "Changed cache is checked again on click and cannot become the new selection");
        }
        using (var form = OpenForm(live, "en", new Size(700, 640), "unused"))
            Assert(!Find<Button>(form, "RecentImage1").Enabled && Find<Button>(form, "RecentImage0").Enabled,
                "A damaged historical picture does not block Settings or healthy choices");
        // Restore fixture bytes for integration; originals remain absent.
        File.WriteAllBytes(Path.Combine(store.Root, older.PhotoFile), sources.Single(image => image.Sha256 == older.Sha256).Bytes);
        TestSessionRecovery(root, store, startup, live.Config);
        Assert(startup.Writes == 0, "Recent selection never opts in to startup");
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { Passed = true,
            Tests = "bounded/deduplicated persisted history, cache-only recovery, rollback, missing/corrupt sources, bilingual compact UI, paused 3-to-1-to-3 recovery",
            NativeWallpaperCalls = 0, RegistryWrites = 0 }, SettingsStore.JsonOptions));
        Console.WriteLine("RECENT IMAGES TESTS PASSED: four cached choices across restart, ordering, rollback, missing Windows cache, tamper protection, compact Thai/English UI and paused session recovery.");
        Console.WriteLine("Evidence: " + root);
        return 0;
    }

    private static void TestSessionRecovery(string root, SettingsStore store, NoStartup startup, TrayConfig configured)
    {
        var desktop = new TrialSelfTests.FakeDesktop(3);
        desktop.State = desktop.State with { Wallpapers = desktop.State.Wallpapers.Select(w => w with
            { WallpaperPath = w.IsPrimary == true ? store.Images(configured).PhotoPath : store.Images(configured).BlackPath }).ToArray() };
        using var context = new AgentContext(store, configured, store.Read(), false, true, desktop, startup);
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        var elapsed = Stopwatch.StartNew();
        var stage = 0;
        Exception? failure = null;
        timer.Tick += (_, _) =>
        {
            var form = Application.OpenForms.OfType<ConfigForm>().SingleOrDefault();
            try
            {
                if (elapsed.Elapsed.TotalSeconds > 15) throw new InvalidOperationException("Session recovery timed out at stage " + stage);
                if (form is null) return;
                var status = context.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == "StatusItem").Text;
                if (stage == 0)
                {
                    var one = new TrialSelfTests.FakeDesktop(1).State;
                    desktop.State = one with { Wallpapers = one.Wallpapers.Select(w => w with { WallpaperPath = Path.Combine(root, "gone-cache", "image.jpg") }).ToArray() };
                    stage = 1;
                }
                else if (stage == 1 && status == UiText.T("StatusPausedNew", configured.Language))
                {
                    var before = store.Read();
                    Find<Button>(form, "UseCurrentButton").PerformClick();
                    Assert(Find<Label>(form, "FeedbackLabel").Text == UiText.T("WindowsImageUnavailable", configured.Language) &&
                        store.Read().SequenceEqual(before), "Actual paused agent reproduces missing-cache UI failure");
                    Find<Button>(form, "RecentImage0").PerformClick();
                    Assert(desktop.State.Wallpapers.Single().WallpaperPath == store.Images(SettingsStore.Parse(store.Read())).PhotoPath,
                        "Explicit cached choice recovers one display");
                    stage = 2;
                }
                else if (stage == 2 && status == UiText.T("StatusRunning", configured.Language, 1))
                {
                    var three = new TrialSelfTests.FakeDesktop(3).State;
                    var photo = store.Images(SettingsStore.Parse(store.Read())).PhotoPath;
                    desktop.State = three with
                    {
                        Displays = three.Displays.Select((d, i) => d with { IsPrimary = i == 2 }).ToArray(),
                        Wallpapers = three.Wallpapers.Select((w, i) => w with { IsPrimary = i == 2, WallpaperPath = photo }).ToArray()
                    };
                    stage = 3;
                }
                else if (stage == 3 && status == UiText.T("StatusRunning", configured.Language, 3))
                {
                    var images = store.Images(SettingsStore.Parse(store.Read()));
                    if (!desktop.State.Wallpapers.All(w => w.WallpaperPath == (w.IsPrimary == true ? images.PhotoPath : images.BlackPath))) return;
                    stage = 4;
                    timer.Stop(); form.Close(); context.ExitThread();
                }
            }
            catch (Exception error) { failure = error; timer.Stop(); form?.Close(); context.ExitThread(); }
        };
        timer.Start();
        Application.Run(context);
        if (failure is not null) throw new InvalidOperationException("Paused session recovery failed", failure);
        Assert(stage == 4, "Cached choice resumes policy and follows three displays/new primary");
    }

    private static ConfigForm OpenForm(LiveSettings live, string language, Size size, string windowsPath)
    {
        var form = new ConfigForm(live.CurrentImage(), false, _ => "Paused", () => windowsPath,
            live.Apply, _ => { }, language, reportError: error => Console.WriteLine("EXPECTED FIXTURE ERROR: " + error.Message + " [" + error.InnerException?.GetType().Name + "]"), pollIntervalMs: live.Config.PollIntervalMs,
            undoImage: live.UndoImage, canUndo: () => live.CanUndoImage,
            recentImages: live.RecentImages, readRecentImage: live.ReadRecentImage);
        form.ClientSize = size;
        form.ShowInTaskbar = false;
        form.Show(); Application.DoEvents();
        return form;
    }
    private static SelectedImage MakeImage(string root, int index)
    {
        var path = Path.Combine(root, "picture-" + index + ".png");
        using var image = new Bitmap(320, 180);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.FromArgb(35 + index * 25, 65 + index * 20, 100 + index * 15));
            using var brush = new SolidBrush(Color.FromArgb(180, 210 - index * 15, 70 + index * 20));
            graphics.FillEllipse(brush, 35 + index * 8, 25, 170, 130);
        }
        image.Save(path, ImageFormat.Png);
        return SelectedImage.Read(path);
    }
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static T Find<T>(Control form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException("RECENT IMAGES TEST FAILED: " + message); }
    private static void ExpectFailure(Action action)
    {
        try { action(); } catch (Exception error) when (error is UiMessageException or IOException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected rejection");
    }
    private sealed class NoStartup : IStartupRegistration
    {
        internal int Writes;
        public string ExpectedCommand => "\"fixture.exe\" --startup";
        public string? Read() => null;
        public void Write(string? command) { Writes++; throw new InvalidOperationException("No startup writes expected"); }
    }
    private sealed class FailOnceStore(SettingsStore store) : ISettingsPersistence
    {
        private bool fail = true;
        public byte[] Read() => store.Read();
        public void Write(byte[] bytes) { store.Write(bytes); if (fail) { fail = false; throw new IOException("Injected settings failure"); } }
    }
}
