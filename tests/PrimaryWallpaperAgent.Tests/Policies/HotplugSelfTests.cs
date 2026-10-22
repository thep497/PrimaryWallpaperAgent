using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class HotplugSelfTests
{
    internal static int Run()
    {
        TestPolicyBoundaries();
        TestRememberedLegacyPath();
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "hotplug-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var store = new SettingsStore(Path.Combine(folder, "data"));
        var initial = SettingsStore.Parse(FirstRun.Open(store));
        var path = Path.Combine(folder, "chosen.png");
        using (var image = new Bitmap(64, 40))
        {
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.Teal);
            image.Save(path, ImageFormat.Png);
        }
        var desktop = new TrialSelfTests.FakeDesktop(1);
        var startup = new NoStartup();
        var live = new LiveSettings(store, initial, store.Read(), startup, desktop, _ => { }, (_, _) => { });
        live.BeginSession();
        live.Apply(new(SelectedImage.Read(path), false, "en", true, 500));
        var bytes = store.Read();
        var images = live.Images;
        var before = desktop.Calls;
        using var context = new AgentContext(store, live.Config, bytes, false, false, desktop, startup);
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        var clock = Stopwatch.StartNew();
        var stage = 0;
        var writesBeforeManual = 0;
        Exception? failure = null;
        timer.Tick += (_, _) =>
        {
            try
            {
                var status = context.TrayMenu.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == "StatusItem").Text;
                if (clock.Elapsed.TotalSeconds > 20)
                    throw new InvalidOperationException("Watcher timed out at stage " + stage + "; status=" + status);
                if (stage < 5 && status == UiText.T("StatusPausedNew", "en"))
                    throw new InvalidOperationException("Hotplug incorrectly requested a new image at stage " + stage);
                if (stage == 0 && Running(1))
                {
                    Assert(desktop.Calls == before, "Matching baseline has no repeat writes");
                    desktop.State = ChangedSet(3, 0, "Windows-restored-new-display.jpg") with { TopologyStable = false };
                    stage = 1;
                }
                else if (stage == 1 && status == UiText.T("StatusWaiting", "en"))
                {
                    Assert(desktop.Calls == before, "Unstable hotplug has no setters");
                    desktop.State = desktop.State with { TopologyStable = true };
                }
                else if (stage == 1 && Running(3))
                {
                    Assert(desktop.Calls == before + 3 && Matches(), "New external displays use notebook primary automatically");
                    desktop.State = ChangedSet(1, 0, "Windows-restored-after-disconnect.jpg");
                    stage = 2;
                }
                else if (stage == 2 && Running(1))
                {
                    Assert(Matches(), "Disconnect keeps the remaining primary picture");
                    desktop.State = ChangedSet(3, 2, "Windows-restored-on-reconnect.jpg");
                    stage = 3;
                }
                else if (stage == 3 && Running(3))
                {
                    Assert(Matches(), "Reconnect follows an external Windows primary");
                    desktop.State = ReplaceLastMonitor(desktop.State);
                    desktop.State = WithWallpaper(desktop.State, "Windows-remembered-projector.jpg");
                    stage = 4;
                }
                else if (stage == 4 && Running(3))
                {
                    Assert(Matches(), "Same-count monitor replacement uses current IDs");
                    writesBeforeManual = desktop.Calls;
                    desktop.State = WithWallpaper(desktop.State, "user-manual-choice.jpg");
                    stage = 5;
                }
                else if (stage == 5 && status == UiText.T("StatusPausedNew", "en"))
                {
                    Assert(desktop.Calls == writesBeforeManual && desktop.State.Wallpapers.All(w => w.WallpaperPath == "user-manual-choice.jpg"),
                        "Unchanged display set still preserves manual image changes");
                    Assert(Application.OpenForms.OfType<ConfigForm>().Count() == 0, "Hotplug needs no Settings interaction");
                    stage = 6;
                    timer.Stop(); context.ExitThread();
                }
                bool Running(int count) => status == UiText.T("StatusRunning", "en", count) && Matches();
                bool Matches() => TrialEngine.Active(desktop.State).All(w => w.WallpaperPath ==
                    (w.IsPrimary == true ? images.PhotoPath : images.BlackPath));
            }
            catch (Exception error) { failure = error; timer.Stop(); context.ExitThread(); }
        };
        timer.Start();
        Application.Run(context);
        if (failure is not null) throw new InvalidOperationException("Hotplug watcher regression failed", failure);
        Assert(stage == 6 && store.Read().SequenceEqual(bytes), "Automatic repairs keep exact settings/history bytes");
        var messages = string.Join("\n", Directory.GetFiles(Path.Combine(store.Root, "runtime", "logs"), "*.log").Select(path =>
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file); return reader.ReadToEnd();
        }));
        Assert(messages.Contains("MAPPING_WAIT") && messages.Contains("MAPPING_RECOVERED") && messages.Contains("HOTPLUG_REBASE"),
            "Mapping wait, recovery and topology repair are all recorded");
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
        {
            Passed = true, NativeWallpaperCalls = 0, StartupWrites = 0, SettingsUnchanged = true,
            Tests = "observed monitor changes, 1-3-1-3, same-count replacement, current primary, manual wallpaper pause, mode/mapping/session guards"
        }, SettingsStore.JsonOptions));
        Console.WriteLine("HOTPLUG TESTS PASSED: Windows-restored unfamiliar images on observed monitor changes; 1-3-1-3, replacement, current primary and unchanged-set manual-image pause.");
        Console.WriteLine("Evidence: " + folder);
        return 0;
    }

    // Reconstructed from the user's 21 Oct 09:02 -> 09:07 evidence.
    // Synthetic IDs/paths only; no original monitor identifiers or pictures.
    private static void TestRememberedLegacyPath()
    {
        const string photo = @"C:\Fixture\AppData\PrimaryWallpaperAgent\assets\photo-same.jpg";
        const string black = @"C:\Fixture\AppData\PrimaryWallpaperAgent\assets\black.png";
        const string remembered = @"D:\Fixture\PrimaryWallpaperAgent-v0.2.0\assets\photo-same.jpg";
        var images = new PolicyImages(photo, "unused", black, "unused", []);
        var old = new TrialSelfTests.FakeDesktop(1);
        old.State = WithWallpaper(old.State, photo);
        var next = new TrialSelfTests.FakeDesktop(2);
        next.State = next.State with
        {
            Displays = next.State.Displays.Select((d, i) => d with
                { Bounds = i == 0 ? new(0, 0, 1920, 1200) : new(1920, 0, 5760, 2160) }).ToArray(),
            Wallpapers = next.State.Wallpapers.Select((w, i) => w with
            {
                Bounds = i == 0 ? new(0, 0, 1920, 1200) : new(1920, 0, 5760, 2160),
                WallpaperPath = i == 0 ? photo : remembered
            }).ToArray()
        };
        Reject(next, null, images, "Old strict policy reproduces the 09:06 unknown-path pause");
        var records = new List<string>();
        Assert(WallpaperPolicy.Repair(images, next.State, next, records.Add, old.State) &&
            next.State.Wallpapers[0].IsPrimary == true && next.State.Wallpapers[0].WallpaperPath == photo &&
            next.State.Wallpapers[1].WallpaperPath == black && records.Any(line => line.StartsWith("HOTPLUG_REBASE")),
            "One-to-two with a remembered legacy copy keeps the existing primary and blacks the new 4K monitor");
        var accepted = next.State;
        next.State = next.State with { Wallpapers = next.State.Wallpapers.Select((w, i) => i == 1 ? w with { WallpaperPath = remembered } : w).ToArray() };
        Reject(next, accepted, images, "Remembered aliases are not globally trusted after the transition");
    }

    private static void TestPolicyBoundaries()
    {
        var images = new PolicyImages("photo.jpg", "unused", "black.bmp", "unused", ["original.jpg"]);
        var old = new TrialSelfTests.FakeDesktop(1);
        WallpaperPolicy.Repair(images, old.State, old, _ => { });
        var baseline = old.State;
        var next = new TrialSelfTests.FakeDesktop(3) { State = ChangedSet(3, 0, "remembered.jpg") };
        Assert(WallpaperPolicy.Repair(images, next.State, next, _ => { }, baseline), "New set permits one rebase");
        var accepted = next.State;
        var calls = next.Calls;
        Assert(!WallpaperPolicy.Repair(images, next.State, next, _ => { }, accepted) && next.Calls == calls,
            "Matching state needs no extra writes");
        next.State = WithWallpaper(next.State, "user-choice.jpg");
        Reject(next, accepted, images, "Same IDs cannot repeatedly bypass the user-image guard");
        Reject(next, null, images, "Cold startup cannot infer a monitor change without a baseline");
        var otherSession = baseline with { SessionId = baseline.SessionId + 1 };
        Reject(next, otherSession, images, "An unrelated session is not evidence of hotplug");
        var reordered = next.State with { Wallpapers = next.State.Wallpapers.Reverse().ToArray(), Displays = next.State.Displays.Reverse().ToArray() };
        next.State = reordered;
        Reject(next, accepted, images, "Enumeration order is not a new monitor");
        next.State = WithWallpaper(accepted, "user-choice.jpg");
        next.SwitchPrimary();
        Reject(next, accepted, images, "A primary-only change does not authorize unfamiliar images");
        next.State = ChangedSet(3, 0, "remembered.jpg") with { Position = 5 };
        Reject(next, baseline, images, "Span/Slideshow remains an explicit conflict");
        next.State = ChangedSet(3, 0, "remembered.jpg") with { TopologyStable = false };
        Reject(next, baseline, images, "Unstable mapping has no setters");
        next.State = ChangedSet(3, 0, "remembered.jpg");
        next.State = next.State with { Wallpapers = next.State.Wallpapers.Select((w, i) => i == 1 ? w with { MatchStatus = "Unmatched" } : w).ToArray() };
        Reject(next, baseline, images, "Ambiguous mapping has no setters");
        next.State = ChangedSet(3, 0, "remembered.jpg");
        var originals = next.State.Wallpapers.Select(w => w.WallpaperPath).ToArray();
        next.FailOnCall = next.Calls + 2;
        try { WallpaperPolicy.Repair(images, next.State, next, _ => { }, baseline); throw new Exception("Expected setter failure"); }
        catch (InvalidOperationException) { }
        Assert(next.State.Wallpapers.Select(w => w.WallpaperPath).SequenceEqual(originals), "Partial hotplug failure restores the fresh current assignments");

        next = new TrialSelfTests.FakeDesktop(3) { State = ChangedSet(3, 2, "") };
        Assert(WallpaperPolicy.Repair(images, next.State, next, _ => { }, baseline), "A new monitor with no image receives policy");
        Assert(next.State.Wallpapers.All(w => w.WallpaperPath == (w.IsPrimary == true ? images.PhotoPath : images.BlackPath)), "Current primary determines targets");
        next = new TrialSelfTests.FakeDesktop(5) { State = ChangedSet(5, 4, "remembered-five.jpg") };
        var detached = next.State.Wallpapers[0] with { MonitorId = "detached-cache", RectHResult = 1, WallpaperPath = "unrelated-cache.jpg" };
        next.State = next.State with { Wallpapers = next.State.Wallpapers.Append(detached).ToArray() };
        Assert(WallpaperPolicy.Repair(images, next.State, next, _ => { }, baseline) && next.Calls == 5 &&
            next.State.Wallpapers.Single(w => w.MonitorId == "detached-cache").WallpaperPath == "unrelated-cache.jpg",
            "Five active displays are repaired without writing detached cache entries");
        var fiveAccepted = next.State;
        next.State = WithWallpaper(fiveAccepted, "manual-after-resize.jpg") with
        {
            Displays = fiveAccepted.Displays.Select(d => d with { Bounds = new(d.Bounds.Left, 0, d.Bounds.Right, 120) }).ToArray()
        };
        Reject(next, fiveAccepted, images, "Resolution changes alone do not authorize another picture");
        next.State = ChangedSet(3, 0, "remembered.jpg") with { SlideshowStatus = 2 };
        Reject(next, baseline, images, "A slideshow change is preserved even during hotplug");

    }

    private static ProbeResult ChangedSet(int count, int primary, string wallpaper)
    {
        var state = new TrialSelfTests.FakeDesktop(count).State;
        return state with
        {
            Displays = state.Displays.Select((d, i) => d with { IsPrimary = i == primary }).ToArray(),
            Wallpapers = state.Wallpapers.Select((w, i) => w with { IsPrimary = i == primary, WallpaperPath = wallpaper }).ToArray()
        };
    }
    private static ProbeResult WithWallpaper(ProbeResult state, string path) =>
        state with { Wallpapers = state.Wallpapers.Select(w => w with { WallpaperPath = path }).ToArray() };
    private static ProbeResult ReplaceLastMonitor(ProbeResult state) => state with
    {
        Displays = state.Displays.Select((d, i) => i != 2 ? d : d with
            { Endpoints = [new("Projector", "test", "replacement-id", 1)] }).ToArray(),
        Wallpapers = state.Wallpapers.Select((w, i) => i != 2 ? w : w with { MonitorId = "replacement-id" }).ToArray()
    };
    private static void Reject(TrialSelfTests.FakeDesktop desktop, ProbeResult? previous, PolicyImages images, string reason)
    {
        var calls = desktop.Calls;
        try { WallpaperPolicy.Repair(images, desktop.State, desktop, _ => { }, previous); }
        catch (InvalidOperationException) { Assert(desktop.Calls == calls, reason); return; }
        throw new InvalidOperationException("Expected rejection: " + reason);
    }
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException("HOTPLUG TEST FAILED: " + message); }
    private sealed class NoStartup : IStartupRegistration
    {
        public string ExpectedCommand => "\"fixture.exe\" --startup";
        public string? Read() => null;
        public void Write(string? command) => throw new InvalidOperationException("No startup writes expected");
    }
}
