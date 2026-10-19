using System.Collections;
using System.Drawing.Imaging;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PrimaryWallpaperAgent.Tray;

internal static class LocalizationSelfTests
{
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "localization-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        var fixture = Path.Combine(folder, "assets", "photo.png");
        using (var image = new Bitmap(320, 180))
        { using var graphics = Graphics.FromImage(image); graphics.Clear(Color.Teal); image.Save(fixture, ImageFormat.Png); }
        var selected = SelectedImage.Read(fixture);
        var config = new TrayConfig(3, "assets/photo.png", selected.Sha256, "assets/photo.png", selected.Sha256, [], false, "fixture.png", fixture);
        var original = JsonSerializer.SerializeToUtf8Bytes(config, SettingsStore.JsonOptions);
        var settingsPath = Path.Combine(folder, "settings.json");
        File.WriteAllBytes(settingsPath, original);
        var store = new SettingsStore(folder);
        var images = store.Images(config);
        var culture = CultureInfo.CurrentCulture.Name;
        var uiCulture = CultureInfo.CurrentUICulture.Name;

        var resourceManager = new ResourceManager("PrimaryWallpaperAgent.Tray.Resources.Strings", typeof(UiText).Assembly);
        var english = resourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var thai = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo("th"), true, false)
            ?? throw new InvalidOperationException("Thai satellite resources missing");
        var englishKeys = english.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).Order().ToArray();
        var thaiKeys = thai.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).Order().ToArray();
        Assert(englishKeys.SequenceEqual(thaiKeys), "English and Thai resource keys match");
        foreach (var key in englishKeys)
        {
            foreach (var language in new[] { "th", "en" })
                Assert(!string.IsNullOrWhiteSpace(UiText.T(key, language, "status", 3)), "Resource formats: " + key);
        }
        Assert(UiText.T("Cancel", "unsupported") == "Cancel", "Unsupported language uses English fallback");
        Assert(UiText.Normalize("th-TH") == "th" && UiText.Normalize("en-US") == "en", "Known language variants normalize");
        var legacy = JsonNode.Parse(original)!.AsObject();
        legacy.Remove("Language");
        Assert(SettingsStore.Parse(JsonSerializer.SerializeToUtf8Bytes(legacy)).Language == "th", "Existing settings default to Thai");
        Assert(UiText.Error(new UiMessageException("ImageTooLarge"), "en") == UiText.T("ImageTooLarge", "en"), "Known errors translate");
        Assert(UiText.Error(new IOException("technical detail"), "th") == UiText.T("GenericError", "th"), "Unexpected errors do not leak untranslated technical details");

        var forbidden = new ForbiddenDesktop();
        var startup = new NoStartup();
        ConfigWorkflow.Apply(config with { Language = "en" }, images, original, null, store, startup, forbidden,
            _ => { }, (_, _) => { }, applyWallpaper: false);
        Assert(SettingsStore.Parse(store.Read()).Language == "en", "English preference persists");
        Assert(SettingsStore.Parse(store.Read()).PhotoFile == config.PhotoFile &&
            SettingsStore.Parse(store.Read()).CurrentSourcePath == fixture, "Language save preserves image and source alias");
        Assert(forbidden.Calls == 0 && startup.Writes == 0, "Language-only save touches neither desktop nor startup");

        var englishBytes = store.Read();
        var failing = new FailingStore(store);
        try
        {
            ConfigWorkflow.Apply(config, images, englishBytes, null, failing, startup, forbidden,
                _ => { }, (_, _) => { }, applyWallpaper: false);
            throw new InvalidOperationException("Expected preference-save failure");
        }
        catch (UiMessageException error) { Assert(error.Key == "SaveRestored", "Preference failure reports verified restore"); }
        Assert(store.Read().SequenceEqual(englishBytes) && forbidden.Calls == 0, "Preference failure restores exact bytes without desktop access");

        foreach (var language in new[] { "th", "en" })
        {
            var saves = 0;
            using var form = Form(selected, language, _ => saves++, fixture);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            Assert(form.Text == UiText.Title("ConfigTitle", language), "Localized window title");
            using (var bitmap = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(folder, "config-" + language + ".png"), ImageFormat.Png); }
            var selector = Control<ComboBox>(form, "LanguageSelector");
            selector.SelectedIndex = language == "th" ? 1 : 0;
            Assert(Control<Button>(form, "SaveButton").Text == UiText.T("SaveSettings", language == "th" ? "en" : "th"),
                "Language-only action is labeled Save settings");
            Control<Button>(form, "CancelButton").PerformClick();
            Assert(saves == 0 && store.Read().SequenceEqual(englishBytes), "Preview and cancel do not persist language");
        }

        ConfigSubmission? submitted = null;
        using (var form = Form(selected, "th", request => submitted = request, fixture))
        {
            form.Show(); Application.DoEvents();
            Control<ComboBox>(form, "LanguageSelector").SelectedIndex = 1;
            Control<Button>(form, "SaveButton").PerformClick();
        }
        Assert(submitted is { Language: "en", ApplyWallpaper: false }, "Language-only UI save requests no wallpaper apply");
        using (var form = Form(selected, "th", request => submitted = request, fixture))
        {
            form.Show(); Application.DoEvents();
            Control<ComboBox>(form, "LanguageSelector").SelectedIndex = 1;
            Control<Button>(form, "UseCurrentButton").PerformClick();
            Assert(Control<Button>(form, "SaveButton").Text == UiText.T("SaveApply", "en"), "Explicit image selection requests apply");
            Control<Button>(form, "SaveButton").PerformClick();
        }
        Assert(submitted is { Language: "en", ApplyWallpaper: true }, "Image adoption still works with a language change");

        var invalid = Path.Combine(folder, "invalid.png");
        File.WriteAllText(invalid, "invalid");
        using (var form = Form(selected, "en", _ => { }, invalid))
        {
            form.Show(); Application.DoEvents();
            Control<Button>(form, "UseCurrentButton").PerformClick();
            Assert(Control<Label>(form, "FeedbackLabel").Text == UiText.T("ImageReadFailed", "en"), "Image errors use preview language");
            Control<ComboBox>(form, "LanguageSelector").SelectedIndex = 0;
            Assert(Control<Label>(form, "FeedbackLabel").Text == UiText.T("ImageReadFailed", "th"), "Visible errors switch language too");
            form.Close();
        }
        Assert(CultureInfo.CurrentCulture.Name == culture && CultureInfo.CurrentUICulture.Name == uiCulture,
            "UI choice does not mutate process culture or data formatting");
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
        {
            Passed = true, ResourceKeysPerLanguage = englishKeys.Length, NativeWallpaperCalls = forbidden.Calls,
            RegistryWrites = startup.Writes, ScreenshotLanguages = new[] { "th", "en" },
            Tests = new[] { "resource parity/fallback", "legacy Thai default", "persist English", "preference-only no desktop access",
                "rollback exact settings bytes", "preview/cancel", "UI request mode", "localized errors", "culture unchanged" }
        }, SettingsStore.JsonOptions));
        Console.WriteLine("LOCALIZATION TESTS PASSED: Thai/English resources, preview/cancel, preferences-only save, rollback, UI labels/errors and unchanged data culture.");
        return 0;
    }

    private static ConfigForm Form(SelectedImage image, string language, Action<ConfigSubmission> save, string current) =>
        new(image, false, code => UiText.T("ConfigStatus", code, UiText.T("StatusPausedNew", code), 3),
            () => current, save, _ => { }, language);

    private static T Control<T>(Control parent, string name) where T : Control =>
        (T)parent.Controls.Find(name, searchAllChildren: true).Single();

    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException("LOCALIZATION TEST FAILED: " + message); }

    private sealed class ForbiddenDesktop : ITrialDesktop
    {
        internal int Calls;
        public ProbeResult Capture() { Calls++; throw new InvalidOperationException("Preference save must not capture wallpaper"); }
        public void SetWallpaper(string monitorId, string path) { Calls++; throw new InvalidOperationException("Preference save must not set wallpaper"); }
    }
    private sealed class NoStartup : IStartupRegistration
    {
        internal int Writes;
        public string ExpectedCommand => "test";
        public string? Read() => null;
        public void Write(string? command) { Writes++; throw new InvalidOperationException("Language save must not write startup"); }
    }
    private sealed class FailingStore(SettingsStore inner) : ISettingsPersistence
    {
        private bool fail = true;
        public byte[] Read() => inner.Read();
        public void Write(byte[] value)
        {
            inner.Write(value);
            if (fail) { fail = false; throw new IOException("Injected write failure"); }
        }
    }
}