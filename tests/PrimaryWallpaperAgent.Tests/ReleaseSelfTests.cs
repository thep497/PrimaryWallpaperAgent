using System.Text.Json;
using PrimaryWallpaperAgent.Tray;

namespace PrimaryWallpaperAgent.Tests;

internal static class ReleaseSelfTests
{
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "release-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var store = new SettingsStore(Path.Combine(folder, "fresh"));
        var bytes = FirstRun.Open(store);
        var initial = SettingsStore.Parse(bytes);
        Assert(!initial.HasChosenImage && !initial.StartWithWindows, "Fresh launch is inactive and startup is opt-in");
        var images = store.Images(initial);
        using (var black = new Bitmap(images.SecondaryPath))
            Assert(black.GetPixel(0, 0).ToArgb() == Color.Black.ToArgb(), "Generated black asset decodes correctly");
        Assert(FirstRun.Open(store).SequenceEqual(bytes), "Second launch preserves initial bytes");
        Assert(FirstRun.DataRoot(Path.Combine(folder, "exe"), Path.Combine(folder, "user")) ==
            Path.Combine(folder, "user", "PrimaryWallpaperAgent"), "New copies use stable per-user data");
        Assert(FirstRun.DataRoot(store.Root, Path.Combine(folder, "user")) == store.Root, "Legacy folder remains explicit and intact");
        var renamed = Path.Combine(folder, "App with spaces", "Renamed Agent.exe");
        Assert(StartupRegistration.CommandFor(renamed) == "\"" + renamed + "\" --startup", "Startup quotes actual renamed executable");
        Assert(new StartupRegistration().ExpectedCommand == StartupRegistration.CommandFor(Environment.ProcessPath!), "Uses current process path");

        var desktop = new TrialSelfTests.FakeDesktop(3);
        var startup = new FakeStartup();
        var live = new LiveSettings(store, initial, bytes, startup, desktop, _ => { }, (_, _) => { });
        _ = live.BeginSession();
        var preview = live.CurrentImage();
        live.Apply(new(preview, false, "th", false, 500));
        Assert(!live.Config.HasChosenImage && desktop.Calls == 0 && startup.Writes == 0, "Preferences before first image cannot activate wallpaper");

        // Exercise the actual timer/context: an unconfigured installation must never repair to the preview.
        using (var context = new AgentContext(store, live.Config, store.Read(), false, false, desktop, startup))
        using (var stop = new System.Windows.Forms.Timer { Interval = 1700 })
        {
            stop.Tick += (_, _) => { stop.Stop(); context.ExitThread(); };
            stop.Start();
            Application.Run(context);
            Assert(desktop.Calls == 0, "Watcher stays inactive across multiple first-run timer ticks");
        }

        var source = Path.Combine(folder, "chosen.png");
        using (var photo = new Bitmap(32, 20))
        {
            using var graphics = Graphics.FromImage(photo);
            graphics.Clear(Color.Teal);
            photo.Save(source, System.Drawing.Imaging.ImageFormat.Png);
        }
        var selected = SelectedImage.Read(source);
        desktop.FailOnCall = 2;
        var beforeFailure = store.Read();
        try { live.Apply(new(selected, false, "en", true, 1000)); throw new InvalidOperationException("Expected injected apply failure"); }
        catch (UiMessageException) { }
        Assert(!live.Config.HasChosenImage && store.Read().SequenceEqual(beforeFailure) && !live.CanUndoImage,
            "Failed first image rolls back and remains inactive");
        desktop.FailOnCall = 0;
        live.Apply(new(selected, false, "en", true, 1000));
        Assert(live.Config.HasChosenImage && !live.CanUndoImage, "First accepted image activates; placeholder cannot become Undo target");
        Assert(desktop.State.Wallpapers.All(w => w.WallpaperPath ==
            (w.IsPrimary == true ? live.Images.PhotoPath : live.Images.SecondaryPath)), "First image applies current primary policy");
        var saved = store.Read();
        Assert(FirstRun.Open(store).SequenceEqual(saved), "Relaunch preserves configured image/preferences");
        Assert(SettingsStore.Parse(JsonSerializer.SerializeToUtf8Bytes(initial with { HasChosenImage = true })).HasChosenImage,
            "Configured state survives serialization");
        var legacyJson = JsonSerializer.Serialize(initial).Replace(",\"HasChosenImage\":false", "");
        Assert(SettingsStore.Parse(System.Text.Encoding.UTF8.GetBytes(legacyJson)).HasChosenImage, "Older settings default to configured");

        var invalid = new SettingsStore(Path.Combine(folder, "invalid"));
        Directory.CreateDirectory(invalid.Root);
        File.WriteAllText(Path.Combine(invalid.Root, "settings.json"), "{broken");
        Assert(System.Text.Encoding.UTF8.GetString(FirstRun.Open(invalid)) == "{broken", "Invalid existing settings are preserved, never silently reset");
        Console.WriteLine("RELEASE TESTS PASSED: first-run inactive watcher, no implicit startup, image adoption/rollback, no placeholder Undo, stable data, legacy settings and renamed executable.");
        return 0;
    }

    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("RELEASE TEST FAILED: " + message); }

    private sealed class FakeStartup : IStartupRegistration
    {
        internal int Writes;
        private string? value;
        public string ExpectedCommand => "\"fixture.exe\" --startup";
        public string? Read() => value;
        public void Write(string? command) { Writes++; value = command; }
    }
}
