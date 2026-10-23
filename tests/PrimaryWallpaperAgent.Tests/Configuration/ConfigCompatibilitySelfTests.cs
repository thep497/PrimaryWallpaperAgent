using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PrimaryWallpaperAgent.Tray;

internal static class ConfigCompatibilitySelfTests
{
    internal static int Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "runtime", "compatibility-test-" + Guid.NewGuid().ToString("N")[..8]);
        var store = new SettingsStore(Path.Combine(root, "data"));
        var original = SettingsStore.Parse(FirstRun.Open(store));
        var primary = MakeImage(root, "primary.png", Color.Teal);
        var secondary = MakeImage(root, "secondary.png", Color.CornflowerBlue);
        var known = store.Prepare(original, primary, false);
        store.Write(Encode(known));
        known = store.Prepare(known, secondary, false, WallpaperTarget.Secondary);
        store.Write(Encode(known));
        var backup = File.ReadAllBytes(Path.Combine(store.Root, "settings-before-schema4.json"));
        var desktop = new TrialSelfTests.FakeDesktop(3);
        var startup = new NoStartup();
        byte[] futureBytes = [];
        foreach (var schema in new[] { 1, 2, 3, 4, 5, int.MaxValue })
        {
            var future = JsonSerializer.SerializeToNode(known)!.AsObject();
            future["SchemaVersion"] = schema;
            future["FuturePreference"] = JsonNode.Parse("{\"mode\":\"new\",\"items\":[1,null,{\"enabled\":true}]}");
            future["SecondaryImage"]!["FutureCrop"] = "center";
            future["Theme"] = "future-theme";
            future["PollIntervalMs"] = 60000;
            future["Language"] = "future-language";
            futureBytes = Encode(future);
            // Include the legacy UTF-8 BOM in the newest-schema rollback fixture.
            if (schema == int.MaxValue) futureBytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(futureBytes).ToArray();
            File.WriteAllBytes(Path.Combine(store.Root, "settings.json"), futureBytes);
            var read = FirstRun.Open(store);
            var parsed = SettingsStore.Parse(read);
            Assert(parsed.SchemaVersion == Math.Min(schema, SettingsStore.CurrentSchemaVersion), "Positive schema reads known fields");
            Assert(parsed.Theme == "system" && parsed.PollIntervalMs == PollingOptions.DefaultIntervalMs &&
                parsed.Language == UiText.Normalize("future-language"), "Unknown preference choices use supported defaults");
            Assert(parsed.PhotoSha256 == primary.Sha256 && parsed.SecondaryImage?.Sha256 == secondary.Sha256 &&
                parsed.RecentImages?.Length == known.RecentImages?.Length, "Known pictures and history survive newer metadata");
            var live = new LiveSettings(store, parsed, read, startup, desktop, _ => { }, (_, _) => { });
            live.BeginSession();
            Assert(live.CurrentImage().Sha256 == primary.Sha256 && live.CurrentImage(WallpaperTarget.Secondary).Sha256 == secondary.Sha256,
                "Live settings can use both cached pictures");
            Assert(store.Read().SequenceEqual(futureBytes), "Opening never rewrites or drops future fields");
        }
        Assert(desktop.Calls == 0 && startup.Writes == 0, "Opening/defaults have no wallpaper or startup writes");

        var optionalMissing = JsonSerializer.SerializeToNode(known)!.AsObject();
        foreach (var name in new[] { "StartWithWindows", "PhotoLabel", "CurrentSourcePath", "Language", "PollIntervalMs",
            "HasChosenImage", "RecentImages", "Theme", "SecondaryImage" }) optionalMissing.Remove(name);
        var defaults = SettingsStore.Parse(Encode(optionalMissing));
        Assert(!defaults.StartWithWindows && defaults.Theme == "system" && defaults.PollIntervalMs == PollingOptions.DefaultIntervalMs &&
            defaults.Language == "th" && defaults.HasChosenImage && defaults.RecentImages is null && defaults.SecondaryImage is null,
            "Missing optional fields recover this version's defaults");
        Assert(store.Images(defaults).SecondaryPath == store.Asset(known.BlackFile, known.BlackSha256) && defaults.PhotoSha256 == primary.Sha256,
            "Missing secondary selection uses black and retains the primary");

        var failed = new LiveSettings(store, SettingsStore.Parse(futureBytes), futureBytes, startup, desktop, _ => { }, (_, _) => { }, new FailOnceStore(store));
        failed.BeginSession();
        ExpectFailure(() => failed.Apply(new(failed.CurrentImage(), false, "en", false, 2000, "dark")));
        Assert(store.Read().SequenceEqual(futureBytes), "Failed edit restores exact future bytes including unknown fields and BOM");
        var saved = new LiveSettings(store, SettingsStore.Parse(futureBytes), futureBytes, startup, desktop, _ => { }, (_, _) => { });
        saved.BeginSession();
        saved.Apply(new(saved.CurrentImage(), false, "en", false, 2000, "dark"));
        var encoded = store.Read();
        var document = JsonNode.Parse(encoded)!.AsObject();
        Assert(document["SchemaVersion"]!.GetValue<int>() == SettingsStore.CurrentSchemaVersion && !document.ContainsKey("FuturePreference") &&
            !document["SecondaryImage"]!.AsObject().ContainsKey("FutureCrop"), "Successful edit writes this reader's schema and drops unknown fields");
        Assert(saved.Config.Theme == "dark" && saved.Config.PollIntervalMs == 2000 && saved.Config.PhotoSha256 == primary.Sha256 &&
            saved.Config.SecondaryImage?.Sha256 == secondary.Sha256, "Successful preferences edit keeps known role images");
        saved.Apply(new(saved.CurrentImage(), false, "th", false, 5000, "light"));
        Assert(SettingsStore.Parse(store.Read()).PollIntervalMs == 5000, "Next edit works after normalization without a false external-change error");
        Assert(FirstRun.Open(store).SequenceEqual(store.Read()), "Reopen after compatibility save remains unchanged");
        Assert(File.ReadAllBytes(Path.Combine(store.Root, "settings-before-schema4.json")).SequenceEqual(backup), "Existing pre-upgrade backup is never overwritten");
        Assert(desktop.Calls == 0 && startup.Writes == 0, "Preference saves and rollback never invoke wallpaper/startup writers");

        foreach (var schema in new[] { -1, 0 })
            ExpectFailure(() => SettingsStore.Parse(Encode(known with { SchemaVersion = schema })));
        ExpectFailure(() => SettingsStore.Parse("{broken"u8.ToArray()));
        ExpectFailure(() => SettingsStore.Parse(Encode(known with { AcceptedPaths = null! })));
        ExpectFailure(() => SettingsStore.Parse(Encode(known with { SecondaryImage = new("assets/test.png", "bad") })));
        ExpectFailure(() => store.Images(SettingsStore.Parse(Encode(known with { SchemaVersion = 99, PhotoFile = "../outside.png" }))));
        ExpectFailure(() => store.Images(SettingsStore.Parse(Encode(known with { SchemaVersion = 99, PhotoSha256 = new string('0', 64) }))));
        var wrongType = JsonSerializer.SerializeToNode(known)!.AsObject();
        wrongType["PollIntervalMs"] = "not-an-integer";
        ExpectFailure(() => SettingsStore.Parse(Encode(wrongType)));

        // Standalone package checks use these generated files, never actual user data.
        var packageData = Path.Combine(root, "package-future");
        Directory.CreateDirectory(Path.Combine(packageData, "assets"));
        foreach (var relative in new[] { known.PhotoFile, known.BlackFile, known.SecondaryImage!.PhotoFile }.Distinct())
            File.Copy(Path.Combine(store.Root, relative), Path.Combine(packageData, relative));
        File.WriteAllBytes(Path.Combine(packageData, "settings.json"), futureBytes);
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
        {
            Passed = true, NativeWallpaperCalls = 0, RegistryWrites = 0, PackageData = packageData,
            Tests = "legacy/future schemas, nested unknown fields, preference defaults, read-only open, exact failed-save rollback, explicit save drops future fields, reopen, optional defaults, asset/type validation"
        }, SettingsStore.JsonOptions));
        Console.WriteLine("CONFIG COMPATIBILITY TESTS PASSED: newer schema/fields, defaults, read-only opening, save/rollback and unchanged asset validation.");
        Console.WriteLine("Evidence: " + root);
        return 0;
    }
    private static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, SettingsStore.JsonOptions);
    private static SelectedImage MakeImage(string root, string name, Color color)
    {
        var path = Path.Combine(root, name);
        using var bitmap = new Bitmap(48, 32);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
        return SelectedImage.Read(path);
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("COMPATIBILITY TEST FAILED: " + message); }
    private static void ExpectFailure(Action action)
    {
        try { action(); } catch (Exception) { return; }
        throw new InvalidOperationException("COMPATIBILITY TEST FAILED: invalid data was accepted");
    }
    private sealed class NoStartup : IStartupRegistration
    {
        internal int Writes;
        public string ExpectedCommand => "\"fixture.exe\" --startup";
        public string? Read() => null;
        public void Write(string? command) { Writes++; throw new InvalidOperationException("Unexpected startup write"); }
    }
    private sealed class FailOnceStore(SettingsStore store) : ISettingsPersistence
    {
        private bool fail = true;
        public byte[] Read() => store.Read();
        public void Write(byte[] bytes)
        {
            store.Write(bytes);
            if (fail) { fail = false; throw new IOException("Injected save-after-write failure"); }
        }
    }
}
