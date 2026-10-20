using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--apply-update", var ticket]) return UpdateInstaller.RunHelper(ticket);
        var packageCheck = args is ["--package-check", _];
        var preflight = args is ["--update-preflight", _];
        var postUpdate = args is ["--post-update", _];
        var smoke = args is ["--smoke-test"];
        var showConfig = args is ["--config"];
        if (args.Length > 0 && !smoke && !showConfig && !packageCheck && !preflight && !postUpdate && args is not ["--startup"]) return 2;
        var language = UiText.Normalize(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        string? dataRoot = null;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        try
        {
            if (preflight) { CheckPreflight(args[1]); return 0; }
            var updateTicket = postUpdate ? UpdateInstaller.ValidatePostUpdate(args[1]) : null;
            dataRoot = packageCheck ? Path.GetFullPath(args[1]) :
                FirstRun.DataRoot(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            if (updateTicket is not null && !UpdateTrust.SamePath(dataRoot, updateTicket.DataRoot))
                throw new InvalidDataException("Update data folder mismatch.");
            var store = new SettingsStore(dataRoot);
            if (packageCheck) return CheckPackage(store);
            using var instance = new InstanceCoordinator();
            if (!instance.IsPrimary)
            {
                if (smoke || postUpdate) return 2;
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
            if (updateTicket is not null) context.SetInitialPause(updateTicket.WasPaused);
            using var dispatcher = new Control();
            _ = dispatcher.Handle;
            instance.Listen(() =>
            {
                if (!dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)context.RequestSettings);
            });
            EventHandler? ready = null;
            if (updateTicket is not null)
            {
                ready = (_, _) => { Application.Idle -= ready; UpdateInstaller.MarkHealthy(updateTicket); };
                Application.Idle += ready;
            }
            try { Application.Run(context); }
            finally { if (ready is not null) Application.Idle -= ready; }
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
            if (!smoke && !packageCheck && !preflight && !postUpdate)
                MessageBox.Show(UiText.Error(error, language), UiText.Title("StartupErrorTitle", language), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static WallpaperWriteLease AcquireWallpaperLease()
    {
        try { return new WallpaperWriteLease(); }
        catch (WallpaperLeaseBusyException error) { throw new UiMessageException("OlderInstanceRunning", error); }
    }

    // Read-only distribution check, including the actual bundled language/icon/UI resources.
    internal static void CheckPreflight(string reportPath)
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Black);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var bytes = stream.ToArray();
        var image = new SelectedImage(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "preview.png"),
            bytes, ".png", Convert.ToHexString(SHA256.HashData(bytes)));
        CheckUi(image, Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Version = BuildInfo.Version, Flavor = BuildInfo.Flavor,
            NativeWallpaperCalls = 0, RegistryWrites = 0 }));
    }

    private static void CheckUi(SelectedImage image, string dataRoot)
    {
        using var icons = new TrayIconSet();
        using var appIcon = AppBranding.CreateApplicationIcon();
        if (UiText.T("ConfigHeading", "th") == UiText.T("ConfigHeading", "en"))
            throw new InvalidOperationException("Thai satellite resources are missing.");
        foreach (var language in new[] { "en", "th" })
        {
            using var form = new ConfigForm(image, false, code => UiText.T("StatusChooseImage", code),
                () => throw new InvalidOperationException("Read-only check"), _ => throw new InvalidOperationException("Read-only check"),
                _ => { }, language, imageConfigured: () => false);
            form.CreateControl();
            using var about = new AboutForm(language, dataRoot, _ => { });
            about.CreateControl();
        }
    }

    private static int CheckPackage(SettingsStore store)
    {
        var bytes = FirstRun.Open(store);
        var config = SettingsStore.Parse(bytes);
        var images = store.Images(config);
        CheckUi(SelectedImage.Read(images.PhotoPath), store.Root);
        var folder = Path.Combine(store.Root, "runtime");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "package-check.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Version = Application.ProductVersion, Flavor = BuildInfo.Flavor, ProcessPath = Environment.ProcessPath,
            DataRoot = store.Root, HasChosenImage = config.HasChosenImage, Languages = new[] { "en", "th" },
            NativeWallpaperCalls = 0, RegistryWrites = 0, SettingsUnchanged = store.Read().SequenceEqual(bytes)
        }, SettingsStore.JsonOptions));
        return 0;
    }
}
