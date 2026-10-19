using System.Drawing.Imaging;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class ConfigSelfTests
{
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "config-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        var photo = Path.Combine(folder, "assets", "first.png");
        var black = Path.Combine(folder, "assets", "black.bmp");
        var next = Path.Combine(folder, "next.png");
        using (var bitmap = new Bitmap(320, 180))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Teal); bitmap.Save(photo, ImageFormat.Png);
            graphics.Clear(Color.CornflowerBlue); bitmap.Save(next, ImageFormat.Png);
            graphics.Clear(Color.Black); bitmap.Save(black, ImageFormat.Bmp);
        }
        var firstImage = SelectedImage.Read(photo);
        var blackImage = SelectedImage.Read(black);
        var store = new SettingsStore(folder);
        var initial = new TrayConfig(1, "assets/first.png", firstImage.Sha256, "assets/black.bmp", blackImage.Sha256, ["original.jpg"]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(initial, SettingsStore.JsonOptions);
        Assert(SettingsStore.Parse(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray()).SchemaVersion == 1, "Legacy UTF-8 BOM migration");
        File.WriteAllBytes(Path.Combine(folder, "settings.json"), bytes);
        var selected = SelectedImage.Read(next);
        var candidate = store.Prepare(initial, selected, startup: false);
        var images = store.Images(candidate);
        var fake = new TrialSelfTests.FakeDesktop(3);
        var startup = new FakeStartup();
        ConfigWorkflow.Apply(candidate, images, bytes, null, store, startup, fake, _ => { }, (_, _) => { });
        Assert(SettingsStore.Parse(store.Read()).PhotoSha256 == selected.Sha256, "Save adopts the chosen image");
        Assert(fake.State.Wallpapers.All(w => w.WallpaperPath == (w.IsPrimary == true ? images.PhotoPath : images.BlackPath)), "Primary-only policy");
        Assert(startup.Writes == 0, "Unchecked startup does not write registry");
        Assert(File.ReadAllBytes(next).SequenceEqual(selected.Bytes), "Original image unchanged");

        // Enabling/disabling startup is tested only against this fake registry.
        var currentBytes = store.Read();
        ConfigWorkflow.Apply(candidate with { StartWithWindows = true }, images, currentBytes, null,
            store, startup, fake, _ => { }, (_, _) => { });
        Assert(startup.Value == startup.ExpectedCommand && startup.Writes == 1, "Explicit startup opt-in");
        ConfigWorkflow.Apply(candidate, images, store.Read(), startup.Value,
            store, startup, fake, _ => { }, (_, _) => { });
        Assert(startup.Value is null && startup.Writes == 2, "Explicit startup opt-out");

        Assert(candidate.SchemaVersion == 3 && candidate.AcceptedPaths.Length == 0 && candidate.CurrentSourcePath == next,
            "Only the current source alias is retained");
        Assert(store.Images(initial).AcceptedPaths.Length == 0, "Legacy history is not automatically trusted on upgrade");
        var preview = SelectedImage.Read(store.Asset(candidate.PhotoFile, candidate.PhotoSha256));
        Assert(store.Prepare(candidate, preview, false).CurrentSourcePath == next, "Saving a cached preview preserves the current source alias");
        var switchingConfig = candidate;
        for (var cycle = 0; cycle < 4; cycle++)
        {
            var chosen = cycle % 2 == 0 ? firstImage : selected;
            fake.State = fake.State with { Wallpapers = fake.State.Wallpapers.Select(w => w with { WallpaperPath = chosen.SourcePath }).ToArray() };
            var writesBeforeChoice = fake.Calls;
            _ = Catch(() => WallpaperPolicy.Repair(store.Images(switchingConfig), fake.State, fake, _ => { }));
            Assert(fake.Calls == writesBeforeChoice && fake.State.Wallpapers.All(w => w.WallpaperPath == chosen.SourcePath),
                "A previously used image is preserved until explicit adoption");
            var replacement = store.Prepare(switchingConfig, chosen, false);
            ConfigWorkflow.Apply(replacement, store.Images(replacement), store.Read(), null, store, startup, fake, _ => { }, (_, _) => { });
            switchingConfig = replacement;
        }
        // Reapplication of the CURRENT source on all monitors can still be repaired.
        var activeImages = store.Images(switchingConfig);
        fake.State = fake.State with { Wallpapers = fake.State.Wallpapers.Select(w => w with { WallpaperPath = switchingConfig.CurrentSourcePath }).ToArray() };
        Assert(WallpaperPolicy.Repair(activeImages, fake.State, fake, _ => { }), "Current-image reset still repairs black side monitors");

        foreach (var failure in new[] { "setter", "settings", "startup", "rollback" })
        {
            File.WriteAllBytes(Path.Combine(folder, "settings.json"), bytes);
            var desktop = new TrialSelfTests.FakeDesktop(3);
            var persistence = new FailingStore(store) { FailOnce = failure == "settings" };
            var registry = new FakeStartup { FailOnce = failure == "startup" };
            if (failure == "setter") desktop.FailOnCall = 2;
            if (failure == "rollback") desktop.FailFromCall = 2;
            var error = Catch(() => ConfigWorkflow.Apply(candidate with { StartWithWindows = failure == "startup" },
                images, bytes, null, persistence, registry, desktop, _ => { }, (_, _) => { }));
            if (failure == "rollback") Assert(error is AggregateException, "Failed rollback cannot be reported as restored");
            else Assert(desktop.State.Wallpapers.All(w => w.WallpaperPath == "original.jpg"), "Rollback after " + failure);
            Assert(store.Read().SequenceEqual(bytes), "Exact settings bytes restored after " + failure);
            Assert(registry.Value is null, "Startup restored after " + failure);
        }
        File.WriteAllBytes(Path.Combine(folder, "settings.json"), bytes);
        var external = new TrialSelfTests.FakeDesktop(3);
        var mismatch = bytes.Concat(new byte[] { 32 }).ToArray();
        _ = Catch(() => ConfigWorkflow.Apply(candidate, images, mismatch, null, store, startup, external, _ => { }, (_, _) => { }));
        Assert(external.Calls == 0, "External config change rejected before setters");
        var changedRegistry = new FakeStartup { Value = "changed-by-user" };
        _ = Catch(() => ConfigWorkflow.Apply(candidate with { StartWithWindows = true }, images, bytes, null,
            store, changedRegistry, external, _ => { }, (_, _) => { }));
        Assert(external.Calls == 0 && changedRegistry.Writes == 0, "External startup change preserved");
        _ = Catch(() => store.Asset("../outside.png", "unused"));
        File.AppendAllText(Path.Combine(folder, candidate.PhotoFile), "changed");
        _ = Catch(() => store.Images(candidate));
        var invalid = Path.Combine(folder, "invalid.png");
        File.WriteAllText(invalid, "not an image");
        _ = Catch(() => SelectedImage.Read(invalid));

        var saves = 0;
        using (var form = new ConfigForm(firstImage, false, language => UiText.T("ConfigStatus", language, UiText.T("StatusPausedNew", language), 3), () => next, _ => saves++, _ => { }))
        {
            // Render only our own form, without screen capture or interacting with other apps.
            form.CreateControl();
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(Path.Combine(folder, "config-preview.png"), ImageFormat.Png);
            form.Close();
        }
        Assert(saves == 0, "Preview and close do not invoke Save");
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
        {
            Passed = true, NativeWallpaperSetters = 0, NativeRegistryWrites = 0,
            Tests = new[] { "selected image", "primary only", "startup off", "original preserved", "A-B-A-B-A repeated adoption", "legacy history not trusted", "current-image reset", "setter/settings/startup failure rollback",
                "failed rollback reporting", "external changes", "asset boundary/hash", "invalid image", "preview and cancel" }
        }, SettingsStore.JsonOptions));
        return 0;
    }
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException("CONFIG TEST FAILED: " + message); }
    private static Exception Catch(Action action)
    {
        try { action(); } catch (Exception error) { return error; }
        throw new InvalidOperationException("CONFIG TEST FAILED: expected failure");
    }
    private sealed class FailingStore(SettingsStore inner) : ISettingsPersistence
    {
        internal bool FailOnce;
        public byte[] Read() => inner.Read();
        public void Write(byte[] value)
        {
            inner.Write(value);
            if (FailOnce) { FailOnce = false; throw new IOException("Injected failure after saving"); }
        }
    }
    private sealed class FakeStartup : IStartupRegistration
    {
        internal string? Value;
        internal bool FailOnce;
        internal int Writes;
        public string ExpectedCommand => "\"test.exe\" --startup";
        public string? Read() => Value;
        public void Write(string? command)
        {
            Writes++; Value = command;
            if (FailOnce) { FailOnce = false; throw new IOException("Injected failure after startup registration"); }
        }
    }
}
