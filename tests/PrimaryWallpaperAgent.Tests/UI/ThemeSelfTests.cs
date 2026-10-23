using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PrimaryWallpaperAgent.Tray;

internal static class ThemeSelfTests
{
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "theme-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var store = new SettingsStore(Path.Combine(folder, "data"));
        var first = SettingsStore.Parse(FirstRun.Open(store));
        var desktop = new TrialSelfTests.FakeDesktop(3);
        var startup = new NoStartup();
        var live = new LiveSettings(store, first, store.Read(), startup, desktop, _ => { }, (_, _) => { });
        live.BeginSession();
        var colors = new[] { Color.Teal, Color.CornflowerBlue, Color.IndianRed, Color.DarkGoldenrod };
        for (var i = 0; i < colors.Length; i++)
        {
            var path = Path.Combine(folder, "landscape-" + i + ".png");
            using var image = new Bitmap(320, 180);
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(colors[i]);
            image.Save(path, ImageFormat.Png);
            live.Apply(new(SelectedImage.Read(path), false, "en", true, 500));
        }
        var legacy = JsonNode.Parse(store.Read())!.AsObject();
        legacy.Remove("Theme");
        Assert(SettingsStore.Parse(JsonSerializer.SerializeToUtf8Bytes(legacy)).Theme == "system", "Old settings default to System");
        var invalid = live.Config with { Theme = "unsupported" };
        Assert(SettingsStore.Parse(JsonSerializer.SerializeToUtf8Bytes(invalid)).Theme == "system",
            "Unrecognized saved theme falls back to System");
        try { ThemeOptions.Validate(invalid.Theme); throw new Exception("Expected invalid UI theme"); }
        catch (UiMessageException error) { Assert(error.Key == "InvalidTheme", "New UI requests remain validated"); }
        foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Night })
        {
            Assert(Contrast(palette.Background, palette.Text) >= 7, "Body text contrast");
            Assert(Contrast(palette.Background, palette.Accent) >= 4.5 && Contrast(palette.Background, palette.Error) >= 4.5,
                "Status, links and error text remain readable");
        }

        foreach (var language in new[] { "en", "th" })
        {
            var systemDark = false;
            var highContrast = false;
            using var themes = new ThemeManager(live.Config.Theme, () => systemDark, () => highContrast, listen: false);
            var bytes = store.Read();
            var writes = desktop.Calls;
            using (var form = Open(live, language, themes))
            {
                SelectTheme(form, "dark");
                Assert(live.Config.Theme == "dark" && themes.Palette.Dark && form.BackColor == ThemePalette.Night.Background,
                    "Dark persists and applies without restart");
                Assert(desktop.Calls == writes && startup.Writes == 0, "Theme-only change never sets wallpaper/startup");
                Assert(live.RecentImages().Length == 4, "Theme change retains image history");
                var selected = Find<Button>(form, "RecentImage0");
                var caption = Find<Label>(form, "ImageFilename");
                Assert(form.Controls.Find("WallpaperPreview", true).Length == 0 &&
                    selected.RectangleToScreen(selected.ClientRectangle).Bottom <= caption.RectangleToScreen(caption.ClientRectangle).Top,
                    "Only thumbnails remain; current filename is below first image");
                using (var updates = new UpdateController(Path.Combine(folder, "updates-" + language)))
                using (var about = new AboutForm(language, store.Root, _ => { }, updates, themes))
                {
                    about.Show(form); Application.DoEvents();
                    Assert(about.BackColor == themes.Palette.Background &&
                        Find<TextBox>(about, "AboutDataPath").BackColor == themes.Palette.Surface,
                        "About and update controls share Dark palette");
                    themes.SetMode("light");
                    Assert(about.BackColor == ThemePalette.Light.Background && form.BackColor == ThemePalette.Light.Background,
                        "All open windows update together");
                    themes.SetMode("dark");
                    Capture(about, Path.Combine(folder, "about-dark-" + language + ".png"));
                    about.Close();
                }
                Find<Button>(form, "ThemeHelpButton").PerformClick();
                using (var help = form.CurrentHelp ?? throw new Exception("Theme help not shown"))
                {
                    Assert(help.BodyText == UiText.T("ThemeHint", language) &&
                        Find<Label>(help, "HelpBody").ForeColor == themes.Palette.Text, "Themed localized help");
                    help.Close();
                }
                foreach (var mode in new[] { "light", "dark", "system" })
                {
                    SelectTheme(form, mode);
                    Assert(live.Config.Theme == mode && themes.Mode == mode, "Theme choice remains committed: " + mode);
                    AssertControlsFit(form);
                    if (mode == "dark")
                    {
                        var selector = Find<ComboBox>(form, "ThemeSelector");
                        using var surface = new Bitmap(selector.Width, selector.Height);
                        selector.DrawToBitmap(surface, new Rectangle(Point.Empty, selector.Size));
                        Assert(surface.GetPixel(selector.Width - 6, 4).GetBrightness() < .5, "Dark ComboBox arrow has no white surface");
                    }
                    Capture(form, Path.Combine(folder, "settings-" + mode + "-" + language + ".png"));
                }
                var unchanged = store.Read();
                systemDark = true;
                NotifyAndPump(themes, () => themes.Palette.Dark);
                Assert(store.Read().SequenceEqual(unchanged), "System notification changes UI without saving preferences");
                systemDark = false;
                NotifyAndPump(themes, () => !themes.Palette.Dark);
                SelectTheme(form, "dark");
                systemDark = true;
                themes.Refresh();
                systemDark = false;
                themes.Refresh();
                Assert(themes.Palette.Dark, "Explicit Dark ignores Windows app mode");
                highContrast = true;
                themes.Refresh();
                Assert(themes.Palette.Background.ToArgb() == SystemColors.Window.ToArgb() &&
                    themes.Palette.Text.ToArgb() == SystemColors.WindowText.ToArgb(), "High Contrast takes precedence");
                highContrast = false;
                themes.Refresh();
                Assert(themes.Palette.Dark && live.Config.Theme == "dark", "Return from High Contrast preserves choice");
                form.Close();
            }
            Assert(themes.BoundCount == 0, "Disposed windows and hints release theme registrations");
            Assert(!store.Read().SequenceEqual(bytes), "Successful preference edits persist");
            var restored = new LiveSettings(store, SettingsStore.Parse(store.Read()), store.Read(), startup, desktop, _ => { }, (_, _) => { });
            restored.BeginSession();
            using (var reopened = Open(restored, language, themes))
            {
                Assert(Find<ComboBox>(reopened, "ThemeSelector").SelectedIndex == Array.IndexOf(ThemeOptions.Supported, "dark") &&
                    reopened.BackColor == ThemePalette.Night.Background, "Theme survives reopening/reloading settings");
                reopened.Close();
            }
            var beforeFailure = store.Read();
            var failing = new LiveSettings(store, SettingsStore.Parse(beforeFailure), beforeFailure, startup, desktop,
                _ => { }, (_, _) => { }, new FailOnceStore(store));
            failing.BeginSession();
            using (var form = Open(failing, language, themes))
            {
                SelectTheme(form, "light");
                Assert(store.Read().SequenceEqual(beforeFailure) && themes.Palette.Dark &&
                    Find<ComboBox>(form, "ThemeSelector").SelectedIndex == Array.IndexOf(ThemeOptions.Supported, "dark"),
                    "Failed save restores exact settings, selection and active colors");
                Assert(Find<Label>(form, "FeedbackLabel").ForeColor == themes.Palette.Error, "Error remains readable on Dark");
                form.Close();
            }
            // Image selection and Undo must retain a later appearance preference.
            var reloaded = new LiveSettings(store, SettingsStore.Parse(store.Read()), store.Read(), startup, desktop, _ => { }, (_, _) => { });
            reloaded.BeginSession();
            var previous = reloaded.Config.PhotoSha256;
            reloaded.Apply(new(reloaded.ReadRecentImage(reloaded.RecentImages()[1]), false, language, true, 500));
            Assert(reloaded.Config.Theme == "dark", "Image command with no theme override preserves existing preference");
            reloaded.UndoImage();
            Assert(reloaded.Config.Theme == "dark" && reloaded.Config.PhotoSha256 == previous, "Undo preserves appearance");
            live = reloaded;
        }
        Assert(startup.Writes == 0, "No startup registry writes");
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new { Passed = true, NativeWallpaperCalls = 0,
            RegistryWrites = 0, SystemPreferenceWrites = 0, Tests = "legacy defaults, immediate persistence, rollback, window propagation, system notifications, contrast, compact captions, image/Undo preference preservation" }, SettingsStore.JsonOptions));
        Console.WriteLine("THEME TESTS PASSED: Dark/Light/System, persistence/rollback, live system notifications, shared About/help colors, contrast and thumbnail-only layout; no system preference writes.");
        Console.WriteLine("Evidence: " + folder);
        return 0;
    }

    private static ConfigForm Open(LiveSettings live, string language, ThemeManager themes)
    {
        var form = new ConfigForm(live.CurrentImage(), false, code => UiText.T("StatusRunning", code, 3),
            () => live.Images.PhotoPath, live.Apply, _ => { }, language, pollIntervalMs: live.Config.PollIntervalMs,
            undoImage: live.UndoImage, canUndo: () => live.CanUndoImage, recentImages: live.RecentImages, readRecentImage: live.ReadRecentImage,
            theme: live.Config.Theme, themes: themes);
        form.ShowInTaskbar = false;
        form.Show(); Application.DoEvents();
        return form;
    }
    private static void SelectTheme(Control form, string mode)
    { Find<ComboBox>(form, "ThemeSelector").SelectedIndex = Array.IndexOf(ThemeOptions.Supported, mode); }
    private static void NotifyAndPump(ThemeManager themes, Func<bool> complete)
    {
        Task.Run(themes.SystemSettingChanged).GetAwaiter().GetResult();
        var timer = Stopwatch.StartNew();
        while (!complete() && timer.Elapsed < TimeSpan.FromSeconds(3)) { Application.DoEvents(); Thread.Sleep(10); }
        Assert(complete(), "Background notification is marshalled to UI thread");
    }
    private static void AssertControlsFit(Form form)
    {
        var client = form.RectangleToScreen(form.ClientRectangle);
        foreach (var control in Descendants(form).Where(c => c.Visible && c is Button or ComboBox or CheckBox or Label))
            Assert(client.Contains(control.RectangleToScreen(control.ClientRectangle)), "Control fits: " + control.Name);
        Assert(Descendants(form).OfType<ScrollableControl>().All(c => !c.AutoScroll && !c.VerticalScroll.Visible && !c.HorizontalScroll.Visible),
            "No scrollbars");
    }
    private static double Contrast(Color a, Color b)
    {
        static double L(Color color)
        {
            static double C(byte value) { var v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * C(color.R) + .7152 * C(color.G) + .0722 * C(color.B);
        }
        var left = L(a); var right = L(b);
        return (Math.Max(left, right) + .05) / (Math.Min(left, right) + .05);
    }
    private static void Capture(Control control, string path)
    {
        using var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size));
        image.Save(path, ImageFormat.Png);
    }
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException("THEME TEST FAILED: " + message); }
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
        public void Write(byte[] bytes) { store.Write(bytes); if (fail) { fail = false; throw new IOException("Injected save failure"); } }
    }
}
