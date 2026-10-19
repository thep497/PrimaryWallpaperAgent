using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PrimaryWallpaperAgent.Tray;

internal static class PollingSelfTests
{
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "polling-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        var imagePath = Path.Combine(folder, "assets", "photo.png");
        using (var bitmap = new Bitmap(320, 180))
        { using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Teal); bitmap.Save(imagePath, ImageFormat.Png); }
        var image = SelectedImage.Read(imagePath);
        var config = new TrayConfig(3, "assets/photo.png", image.Sha256, "assets/photo.png", image.Sha256, [], false, "fixture.png", imagePath, "en");
        var store = new SettingsStore(folder);
        File.WriteAllBytes(Path.Combine(folder, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(config, SettingsStore.JsonOptions));
        var legacy = JsonNode.Parse(store.Read())!.AsObject();
        legacy.Remove("PollIntervalMs");
        Assert(SettingsStore.Parse(JsonSerializer.SerializeToUtf8Bytes(legacy)).PollIntervalMs == 1000, "Old settings retain 1-second polling");

        foreach (var invalid in new[] { int.MinValue, -1, 0, 1, 499, 501, 1500, 5001, int.MaxValue })
        {
            var node = JsonNode.Parse(store.Read())!.AsObject();
            node["PollIntervalMs"] = invalid;
            try { _ = SettingsStore.Parse(JsonSerializer.SerializeToUtf8Bytes(node)); throw new Exception("Invalid interval accepted"); }
            catch (UiMessageException error) { Assert(error.Key == "InvalidPollInterval", "Invalid interval fails before timer startup"); }
        }

        var desktop = new ForbiddenDesktop();
        var startup = new ForbiddenStartup();
        var saves = 0;
        foreach (var interval in PollingOptions.SupportedIntervals)
        {
            var initial = SettingsStore.Parse(store.Read());
            ConfigSubmission? request = null;
            using (var form = Form(image, initial.Language, initial.PollIntervalMs, value => request = value, imagePath))
            {
                form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                var selector = Find<ComboBox>(form, "PollSelector");
                selector.SelectedIndex = Array.IndexOf(PollingOptions.SupportedIntervals.ToArray(), interval);
                // Switching language rebuilds poll labels without losing the interval.
                Find<ComboBox>(form, "LanguageSelector").SelectedIndex = 0;
                Find<ComboBox>(form, "LanguageSelector").SelectedIndex = 1;
                Assert(selector.SelectedIndex == Array.IndexOf(PollingOptions.SupportedIntervals.ToArray(), interval), "Language preview preserves interval");
                Assert(Find<Button>(form, "SaveButton").Text == UiText.T(interval == initial.PollIntervalMs ? "SaveApply" : "SaveSettings", "en"), "Poll-only save label");
                Find<Button>(form, "SaveButton").PerformClick();
            }
            Assert(request is not null && request.PollIntervalMs == interval, "UI submits selected interval");
            if (interval != initial.PollIntervalMs) Assert(!request!.ApplyWallpaper, "Poll change alone never applies wallpaper");
            var candidate = initial with { PollIntervalMs = request!.PollIntervalMs };
            ConfigWorkflow.Apply(candidate, store.Images(initial), store.Read(), null, store, startup, desktop,
                _ => { }, (_, _) => { }, applyWallpaper: false);
            saves++;
            var saved = SettingsStore.Parse(store.Read());
            Assert(saved.PollIntervalMs == interval && saved.Language == "en", "Preference persisted and language preserved");
            Assert(saved.PhotoFile == config.PhotoFile && saved.CurrentSourcePath == config.CurrentSourcePath, "Image references preserved");
            Assert(store.Prepare(saved, image, false).PollIntervalMs == interval, "Image adoption preserves poll preference");
            using var reopened = Form(image, saved.Language, saved.PollIntervalMs, _ => { }, imagePath);
            Assert(Find<ComboBox>(reopened, "PollSelector").SelectedIndex == Array.IndexOf(PollingOptions.SupportedIntervals.ToArray(), interval), "Reopened Config restores interval");
        }
        Assert(desktop.Calls == 0 && startup.Writes == 0, "Poll-only persistence makes no desktop or startup calls");

        var before = store.Read();
        foreach (var language in new[] { "th", "en" })
        {
            var calls = 0;
            using var form = Form(image, language, 5000, _ => calls++, imagePath);
            form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
            Find<ComboBox>(form, "PollSelector").SelectedIndex = 0;
            using (var bitmap = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(folder, "config-" + language + ".png"), ImageFormat.Png); }
            Find<Button>(form, "CancelButton").PerformClick();
            Assert(calls == 0 && store.Read().SequenceEqual(before), "Cancel leaves saved interval unchanged");
        }

        ConfigSubmission? adopted = null;
        using (var form = Form(image, "en", 1000, value => adopted = value, imagePath))
        {
            form.Show(); Application.DoEvents();
            Find<ComboBox>(form, "PollSelector").SelectedIndex = 3;
            Find<Button>(form, "UseCurrentButton").PerformClick();
            Find<Button>(form, "SaveButton").PerformClick();
        }
        Assert(adopted is { ApplyWallpaper: true, PollIntervalMs: 5000 }, "Explicit image adoption still applies when poll changes");

        var failing = new FailingStore(store);
        var savedConfig = SettingsStore.Parse(before);
        try
        {
            ConfigWorkflow.Apply(savedConfig with { PollIntervalMs = 500 }, store.Images(savedConfig), before, null,
                failing, startup, desktop, _ => { }, (_, _) => { }, applyWallpaper: false);
            throw new Exception("Expected injected persistence failure");
        }
        catch (UiMessageException error) { Assert(error.Key == "SaveRestored", "Polling save failure reported"); }
        Assert(store.Read().SequenceEqual(before) && desktop.Calls == 0, "Failure restores exact settings without wallpaper access");
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
        {
            Passed = true, SupportedIntervalsMs = PollingOptions.SupportedIntervals.ToArray(), PreferenceSaves = saves,
            NativeWallpaperCalls = desktop.Calls, StartupWrites = startup.Writes,
            Tests = new[] { "legacy default", "invalid settings", "poll-only save", "reopen", "cancel", "language preview", "image adoption", "rollback" }
        }, SettingsStore.JsonOptions));
        Console.WriteLine("POLLING TESTS PASSED: legacy default, supported intervals, no wallpaper/startup access, reopen/cancel, image adoption and rollback.");
        return 0;
    }
    private static ConfigForm Form(SelectedImage image, string language, int interval, Action<ConfigSubmission> save, string source) =>
        new(image, false, code => UiText.T("ConfigStatusWaiting", code, UiText.T("StatusWaiting", code)),
            () => source, save, _ => { }, language, pollIntervalMs: interval);
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException("POLLING TEST FAILED: " + message); }
    private sealed class ForbiddenDesktop : ITrialDesktop
    {
        internal int Calls;
        public ProbeResult Capture() { Calls++; throw new InvalidOperationException("Polling preference must not read desktop"); }
        public void SetWallpaper(string monitorId, string path) { Calls++; throw new InvalidOperationException("Polling preference must not write desktop"); }
    }
    private sealed class ForbiddenStartup : IStartupRegistration
    {
        internal int Writes;
        public string ExpectedCommand => "test";
        public string? Read() => null;
        public void Write(string? value) { Writes++; throw new InvalidOperationException("Polling preference must not write startup"); }
    }
    private sealed class FailingStore(SettingsStore inner) : ISettingsPersistence
    {
        private bool fail = true;
        public byte[] Read() => inner.Read();
        public void Write(byte[] bytes)
        { inner.Write(bytes); if (fail) { fail = false; throw new IOException("Injected poll settings write failure"); } }
    }
}
