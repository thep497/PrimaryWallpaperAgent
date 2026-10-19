using System.Diagnostics;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal sealed class AgentContext : ApplicationContext
{
    private readonly SettingsStore store;
    private TrayConfig config;
    private byte[] configBytes;
    private PolicyImages images;
    private readonly IStartupRegistration startup = new StartupRegistration();
    private readonly bool smoke;
    private readonly NativeTrialDesktop desktop = new();
    private readonly NotifyIcon icon;
    private readonly TrayIconSet trayIcons = new();
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem status = new() { Enabled = false };
    private readonly ToolStripMenuItem toggle = new();
    private readonly ToolStripMenuItem settingsItem = new();
    private readonly ToolStripMenuItem checkItem = new();
    private readonly ToolStripMenuItem logItem = new();
    private readonly ToolStripMenuItem exitItem = new();
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly Stopwatch uptime = Stopwatch.StartNew();
    private readonly Queue<TimeSpan> repairs = new();
    private readonly string logFolder;
    private readonly string logPath;
    private bool paused;
    private bool needsConfig;
    private ConfigForm? configForm;
    private bool openOnFirstTick;
    private string statusKey = "StatusChecking";
    private object[] statusArgs = [];
    private string? previous;
    private int failures;
    private int samples;
    private int repairCount;

    internal AgentContext(SettingsStore store, TrayConfig config, byte[] configBytes, bool smoke, bool showConfig)
    {
        this.store = store;
        this.config = config;
        this.configBytes = configBytes;
        images = store.Images(config);
        this.smoke = smoke;
        openOnFirstTick = showConfig;
        timer.Interval = PollingOptions.Validate(config.PollIntervalMs);
        logFolder = Path.Combine(AppContext.BaseDirectory, "runtime", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(logFolder);
        logPath = Path.Combine(logFolder, "agent.log");
        Log(smoke ? "START read-only tray smoke test; no setters" : "START tray v0.2; two stable samples; startup writes only on Config Save");
        Log("UI_LANGUAGE_LOADED language=" + config.Language);
        Log("POLL_INTERVAL_LOADED milliseconds=" + timer.Interval);
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        settingsItem.Click += (_, _) => ShowConfig();
        menu.Items.Add(settingsItem);
        toggle.Click += (_, _) => Toggle();
        menu.Items.Add(toggle);
        checkItem.Click += (_, _) => { previous = null; if (!paused) Tick(); };
        menu.Items.Add(checkItem);
        logItem.Click += (_, _) => OpenLog();
        menu.Items.Add(logItem);
        menu.Items.Add(new ToolStripSeparator());
        exitItem.Click += (_, _) => ExitThread();
        menu.Items.Add(exitItem);
        icon = new NotifyIcon { Icon = trayIcons.Running, ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => ShowConfig();
        icon.BalloonTipClicked += (_, _) => ShowConfig();
        RenderChrome();
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    private void Tick()
    {
        if (smoke && uptime.Elapsed.TotalMilliseconds >= PollingOptions.SmokeDurationMs(config.PollIntervalMs)) { ExitThread(); return; }
        if (configForm is not null) return;
        if (openOnFirstTick) { openOnFirstTick = false; ShowConfig(); return; }
        if (paused) return;
        ProbeResult snapshot;
        try
        {
            snapshot = desktop.Capture();
            samples++;
            _ = TrialEngine.Active(snapshot);
            failures = 0;
        }
        catch (Exception error)
        {
            previous = null;
            if (++failures >= 10) Pause(error);
            else { Log("WAIT " + error.Message); SetStatus("StatusWaiting"); }
            return;
        }
        try
        {
            var fingerprint = WallpaperGuard.Fingerprint(snapshot);
            if (fingerprint != previous)
            {
                previous = fingerprint;
                SetStatus("StatusStable", snapshot.Displays.Length);
                return;
            }
            if (smoke) { SetStatus("StatusReading", snapshot.Displays.Length); return; }
            while (repairs.TryPeek(out var time) && uptime.Elapsed - time >= TimeSpan.FromMinutes(1)) repairs.Dequeue();
            var needsRepair = TrialEngine.Active(snapshot).Any(w => !StringComparer.OrdinalIgnoreCase.Equals(
                w.WallpaperPath, w.IsPrimary == true ? images.PhotoPath : images.BlackPath));
            if (needsRepair && repairs.Count >= 8)
                throw new PolicyConflictException("Wallpaper changed repeatedly (8 repairs in 60 seconds). Review the log before resuming.");
            if (needsRepair)
            {
                _ = store.Images(config);
                Save($"before-{repairCount + 1}.json", snapshot);
            }
            if (WallpaperPolicy.Repair(images, snapshot, desktop, Log))
            {
                repairs.Enqueue(uptime.Elapsed);
                repairCount++;
                Save($"after-{repairCount}.json", desktop.Capture());
                Log($"REPAIR {repairCount}; active displays={snapshot.Displays.Length}");
                previous = null;
            }
            SetStatus("StatusRunning", snapshot.Displays.Length);
        }
        catch (Exception error) { Pause(error, error is PolicyConflictException); }
    }

    private void Toggle()
    {
        if (smoke) return;
        if (paused && needsConfig) { ShowConfig(); return; }
        if (!paused) { paused = true; Log("PAUSED by user"); }
        else
        {
            try
            {
                _ = store.Images(config);
                paused = false; previous = null; failures = 0; repairs.Clear(); Log("RESUMED by user");
            }
            catch (Exception error) { Pause(error); return; }
        }
        SetStatus(paused ? "StatusPaused" : "StatusChecking");
    }

    private void Pause(Exception error, bool requireConfig = false)
    {
        paused = true;
        needsConfig = requireConfig;
        previous = null;
        SetStatus(requireConfig ? "StatusPausedNew" : "StatusPausedError");
        Log("PAUSED " + error);
        icon.ShowBalloonTip(8000, UiText.Title("PausedTitle", config.Language),
            UiText.T(requireConfig ? "PausedNewMessage" : "PausedErrorMessage", config.Language), ToolTipIcon.Warning);
    }

    private void ShowConfig()
    {
        if (smoke) return;
        if (configForm is not null)
        {
            WindowPresentation.Reveal(configForm);
            Log("CONFIG_REVEALED; nativeVisible=" + WindowPresentation.IsVisible(configForm));
            return;
        }
        try
        {
            if (!store.Read().SequenceEqual(configBytes)) throw new UiMessageException("SettingsChanged");
            var startupAtOpen = startup.Read();
            var wasPaused = paused;
            int? displayCount = null;
            try { displayCount = TrialEngine.Active(desktop.Capture()).Length; }
            catch (Exception error) { Log("CONFIG_DISPLAY_WAIT " + error.Message); }
            string ConfigurationStatus(string language)
            {
                var currentStatus = UiText.T(statusKey, language, statusArgs);
                return displayCount is { } count ? UiText.T("ConfigStatus", language, currentStatus, count) :
                    UiText.T("ConfigStatusWaiting", language, currentStatus);
            }
            var initial = SelectedImage.Read(store.Asset(config.PhotoFile, config.PhotoSha256));
            if (config.PhotoLabel is not null) initial = initial with { FriendlyName = config.PhotoLabel };
            using var form = new ConfigForm(initial, startupAtOpen is not null, ConfigurationStatus, CurrentPrimaryPath, request =>
            {
                try
                {
                    var candidate = request.ApplyWallpaper ? store.Prepare(config, request.Image, request.StartWithWindows) :
                        config with { StartWithWindows = request.StartWithWindows };
                    candidate = candidate with { Language = UiText.Normalize(request.Language), PollIntervalMs = PollingOptions.Validate(request.PollIntervalMs) };
                    var candidateImages = request.ApplyWallpaper ? store.Images(candidate) : images;
                    var id = "config-" + Guid.NewGuid().ToString("N")[..8];
                    ConfigWorkflow.Apply(candidate, candidateImages, configBytes, startupAtOpen, store, startup,
                        desktop, Log, (name, value) => Save(id + "-" + name + ".json", value), request.ApplyWallpaper);
                    config = candidate;
                    timer.Interval = config.PollIntervalMs;
                    images = candidateImages;
                    configBytes = JsonSerializer.SerializeToUtf8Bytes(candidate, SettingsStore.JsonOptions);
                    if (request.ApplyWallpaper)
                    {
                        paused = false; needsConfig = false; failures = 0; previous = null; repairs.Clear();
                        SetStatus("StatusSaved");
                    }
                    else RenderChrome(); // Preserve pause state and the selected wallpaper.
                    Log("UI_LANGUAGE_SAVED language=" + config.Language + "; applyWallpaper=" + request.ApplyWallpaper);
                    Log("POLL_INTERVAL_SAVED milliseconds=" + timer.Interval);
                }
                catch (Exception error) { Pause(error, requireConfig: true); throw; }
            }, OpenLog, config.Language, error => Log("CONFIG_UI_ERROR " + error), config.PollIntervalMs);
            configForm = form;
            form.Load += (_, _) => form.BeginInvoke((Action)(() =>
                Log("CONFIG_VISIBLE; nativeVisible=" + WindowPresentation.IsVisible(form))));
            Log("CONFIG_REQUESTED; preview only until Save");
            var result = form.ShowDialog();
            if (result != DialogResult.OK)
            {
                Log("CONFIG_CLOSED without a successful Save");
                if (wasPaused || needsConfig) paused = true;
            }
        }
        catch (Exception error)
        {
            Log("CONFIG_OPEN_ERROR " + error);
            MessageBox.Show(UiText.Error(error, config.Language), UiText.T("ConfigErrorTitle", config.Language), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { configForm = null; previous = null; }
    }

    private string CurrentPrimaryPath()
    {
        var current = TrialEngine.Active(desktop.Capture()).Single(w => w.IsPrimary == true);
        if (string.IsNullOrWhiteSpace(current.WallpaperPath)) throw new UiMessageException("PrimaryImageMissing");
        return current.WallpaperPath;
    }
    private void SetStatus(string key, params object[] args) { statusKey = key; statusArgs = args; RenderChrome(); }
    private void RenderChrome()
    {
        string T(string key) => UiText.T(key, config.Language);
        settingsItem.Text = T("MenuSettings");
        checkItem.Text = T("MenuCheck");
        logItem.Text = T("MenuLog");
        exitItem.Text = T("MenuExit");
        toggle.Text = T(paused ? needsConfig ? "MenuChooseResume" : "MenuResume" : "MenuPause");
        status.Text = UiText.T(statusKey, config.Language, statusArgs);
        icon.Text = UiText.AppName + " — " + status.Text;
        icon.Icon = paused ? needsConfig || statusKey == "StatusPausedError" ? trayIcons.Attention : trayIcons.Paused : trayIcons.Running;
    }
    private void Log(string message) => File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    private void Save(string name, object value) => File.WriteAllText(Path.Combine(logFolder, name),
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    private void OpenLog(string? language = null)
    {
        language ??= config.Language;
        try { Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true }); }
        catch (Exception error)
        {
            Log("OPEN_LOG_ERROR " + error);
            MessageBox.Show(UiText.Error(error, language), UiText.T("LogErrorTitle", language));
        }
    }
    protected override void ExitThreadCore()
    {
        timer.Stop();
        icon.Visible = false;
        try
        {
            Log($"EXIT samples={samples}; repairs={repairCount}; last wallpaper assignments remain");
            Save("summary.json", new { SmokeTest = smoke, Samples = samples, Repairs = repairCount,
                SecondsElapsed = uptime.Elapsed.TotalSeconds, Paused = paused, RestoredOnExit = false, Language = config.Language, PollIntervalMs = timer.Interval });
        }
        finally { base.ExitThreadCore(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); icon.Dispose(); trayIcons.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
}