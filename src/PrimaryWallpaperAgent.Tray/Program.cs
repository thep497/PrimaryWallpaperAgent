using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var packageCheck = args is ["--package-check", _];
        var smoke = args is ["--smoke-test"];
        var showConfig = args is ["--config"];
        if (args.Length > 0 && !smoke && !showConfig && !packageCheck && args is not ["--startup"]) return 2;
        var language = UiText.Normalize(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        string? dataRoot = null;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        try
        {
            dataRoot = packageCheck ? Path.GetFullPath(args[1]) :
                FirstRun.DataRoot(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var store = new SettingsStore(dataRoot);
            if (packageCheck) return CheckPackage(store);
            using var instance = new InstanceCoordinator();
            if (!instance.IsPrimary)
            {
                if (smoke) return 2;
                if (!instance.NotifyAsync(showSettings: args is not ["--startup"]).GetAwaiter().GetResult())
                    throw new UiMessageException("InstanceUnavailable");
                return 0;
            }
            using var lease = AcquireWallpaperLease();
            var bytes = FirstRun.Open(store);
            var config = SettingsStore.Parse(bytes);
            language = config.Language;
            _ = store.Images(config);
            using var context = new AgentContext(store, config, bytes, smoke, showConfig || !config.HasChosenImage);
            using var dispatcher = new Control();
            _ = dispatcher.Handle;
            instance.Listen(() =>
            {
                if (!dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)context.RequestSettings);
            });
            Application.Run(context);
            return 0;
        }
        catch (Exception error)
        {
            try
            {
                if (dataRoot is not null)
                {
                    var folder = Path.Combine(dataRoot, "runtime");
                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, "startup-error.log");
                    LogRetention.AppendBounded(path, error + Environment.NewLine);
                    LogRetention.Prune(dataRoot, path, DateTime.UtcNow);
                }
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            if (!smoke && !packageCheck)
                MessageBox.Show(UiText.Error(error, language), UiText.Title("StartupErrorTitle", language), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static WallpaperWriteLease AcquireWallpaperLease()
    {
        try { return new WallpaperWriteLease(); }
        catch (WallpaperLeaseBusyException error) { throw new UiMessageException("OlderInstanceRunning", error); }
    }

    // Explicit distribution diagnostic: no native desktop backend, writer lease or startup registration.
    private static int CheckPackage(SettingsStore store)
    {
        var bytes = FirstRun.Open(store);
        var config = SettingsStore.Parse(bytes);
        var images = store.Images(config);
        using var icons = new TrayIconSet();
        using var appIcon = AppBranding.CreateApplicationIcon();
        if (UiText.T("ConfigHeading", "th") == UiText.T("ConfigHeading", "en"))
            throw new InvalidOperationException("Thai satellite resources are missing.");
        foreach (var language in new[] { "en", "th" })
        {
            using var form = new ConfigForm(SelectedImage.Read(images.PhotoPath), false,
                code => UiText.T("StatusChooseImage", code), () => throw new InvalidOperationException("Read-only package check"),
                _ => throw new InvalidOperationException("Read-only package check"), _ => { }, language,
                imageConfigured: () => config.HasChosenImage);
            form.CreateControl();
            _ = UiText.T("ChooseImageFirst", language);
            using var about = new AboutForm(language, store.Root, _ => { });
            about.CreateControl();
        }
        var folder = Path.Combine(store.Root, "runtime");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "package-check.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Version = Application.ProductVersion, ProcessPath = Environment.ProcessPath,
            DataRoot = store.Root, HasChosenImage = config.HasChosenImage, Languages = new[] { "en", "th" },
            NativeWallpaperCalls = 0, RegistryWrites = 0, SettingsUnchanged = store.Read().SequenceEqual(bytes)
        }, SettingsStore.JsonOptions));
        return 0;
    }
}
