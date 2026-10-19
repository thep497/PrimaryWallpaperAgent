using System.Diagnostics;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal sealed class AgentContext : ApplicationContext
{
    private readonly SettingsStore store;
    private readonly LiveSettings liveSettings;
    private TrayConfig config => liveSettings.Config;
    private PolicyImages images => liveSettings.Images;
    private readonly IStartupRegistration startup;
    private readonly bool smoke;
    private readonly ITrialDesktop desktop;
    private readonly NotifyIcon icon;
    private readonly TrayIconSet trayIcons = new();
    private readonly ContextMenuStrip menu = new();
    internal ContextMenuStrip TrayMenu => menu;
    private readonly ToolStripMenuItem status = new() { Name = "StatusItem", Enabled = false };
    private readonly ToolStripMenuItem toggle = new() { Name = "PauseResumeItem" };
    private readonly ToolStripMenuItem settingsItem = new() { Name = "SettingsItem" };
    private readonly ToolStripMenuItem logItem = new() { Name = "LogItem" };
    private readonly ToolStripMenuItem exitItem = new() { Name = "ExitItem" };
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
    private int? displayCount;

    internal AgentContext(SettingsStore store, TrayConfig config, byte[] configBytes, bool smoke, bool showConfig, ITrialDesktop? desktopOverride = null, IStartupRegistration? startupOverride = null)
    {
        this.store = store;
        desktop = desktopOverride ?? new NativeTrialDesktop();
        startup = startupOverride ?? new StartupRegistration();
        liveSettings = new LiveSettings(store, config, configBytes, startup, desktop, Log, Save);
        this.smoke = smoke;
        openOnFirstTick = showConfig;
        if (!config.HasChosenImage) statusKey = "StatusChooseImage";
        timer.Interval = PollingOptions.Validate(config.PollIntervalMs);
        logFolder = Path.Combine(store.Root, "runtime", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(logFolder);
        logPath = Path.Combine(logFolder, "agent.log");
        Log(smoke ? "START read-only tray smoke test; no setters" : "START tray v0.3; two stable samples; startup writes only on explicit setting changes");
        Log("UI_LANGUAGE_LOADED language=" + config.Language);
        Log("POLL_INTERVAL_LOADED milliseconds=" + timer.Interval);
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        settingsItem.Click += (_, _) => ShowConfig();
        menu.Items.Add(settingsItem);
        toggle.Click += (_, _) => Toggle();
        menu.Items.Add(toggle);
        logItem.Click += (_, _) => OpenLog();
        menu.Items.Add(logItem);
        menu.Items.Add(new ToolStripSeparator());
        exitItem.Click += (_, _) => { if (!liveSettings.IsApplying) ExitThread(); };
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
        if (liveSettings.IsApplying) return;
        if (openOnFirstTick) { openOnFirstTick = false; ShowConfig(); return; }
        if (paused || !config.HasChosenImage) return;
        ProbeResult snapshot;
        try
        {
            snapshot = desktop.Capture();
            samples++;
            displayCount = TrialEngine.Active(snapshot).Length;
            failures = 0;
        }
        catch (Exception error)
        {
            previous = null;
            displayCount = null;
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
        if (smoke || liveSettings.IsApplying) return;
        if (!config.HasChosenImage || paused && needsConfig) { ShowConfig(); return; }
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
            var startupEnabled = liveSettings.BeginSession();
            try { displayCount = TrialEngine.Active(desktop.Capture()).Length; }
            catch (Exception error) { displayCount = null; Log("CONFIG_DISPLAY_WAIT " + error.Message); }
            string ConfigurationStatus(string language)
            {
                var currentStatus = UiText.T(statusKey, language, statusArgs);
                return displayCount is { } count && statusArgs.Length == 0 ? UiText.T("ConfigStatus", language, currentStatus, count) :
                    currentStatus;
            }
            using var form = new ConfigForm(liveSettings.CurrentImage(), startupEnabled, ConfigurationStatus, CurrentPrimaryPath, request =>
            {
                var oldInterval = config.PollIntervalMs;
                try
                {
                    liveSettings.Apply(request);
                    AcceptSettingsChange(request.ApplyWallpaper, oldInterval);
                }
                catch (Exception error) { ConfigurationFailed(error); throw; }
            }, OpenLog, config.Language, error => Log("CONFIG_UI_ERROR " + error), config.PollIntervalMs,
            undoImage: () =>
            {
                var oldInterval = config.PollIntervalMs;
                try
                {
                    var image = liveSettings.UndoImage();
                    AcceptSettingsChange(wallpaperApplied: true, oldInterval);
                    return image;
                }
                catch (Exception error) { ConfigurationFailed(error); throw; }
            }, canUndo: () => liveSettings.CanUndoImage, imageConfigured: () => config.HasChosenImage);
            configForm = form;
            form.Load += (_, _) => form.BeginInvoke((Action)(() =>
                Log("CONFIG_VISIBLE; nativeVisible=" + WindowPresentation.IsVisible(form))));
            Log("CONFIG_REQUESTED; settings apply immediately; display checks continue");
            form.ShowDialog();
            Log("CONFIG_CLOSED; committed settings retained");
        }
        catch (Exception error)
        {
            Log("CONFIG_OPEN_ERROR " + error);
            MessageBox.Show(UiText.Error(error, config.Language), UiText.T("ConfigErrorTitle", config.Language), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { configForm = null; }
    }

    private void AcceptSettingsChange(bool wallpaperApplied, int oldInterval)
    {
        if (timer.Interval != config.PollIntervalMs) timer.Interval = config.PollIntervalMs;
        if (oldInterval != config.PollIntervalMs) previous = null;
        if (wallpaperApplied)
        {
            paused = false; needsConfig = false; failures = 0; previous = null; repairs.Clear();
            SetStatus("StatusSaved");
        }
        else RenderChrome(); // Preference edits preserve pause/error state.
        Log("SETTINGS_APPLIED language=" + config.Language + "; pollMilliseconds=" + timer.Interval + "; wallpaperApplied=" + wallpaperApplied);
    }

    private void ConfigurationFailed(Exception error)
    {
        Log("CONFIG_CHANGE_ERROR " + error);
        // A rolled-back preference/image failure does not itself stop a healthy watcher.
        // Incomplete recovery or an externally edited settings file needs intervention.
        if (error is AggregateException || error is UiMessageException { Key: "SettingsChanged" })
            Pause(error, requireConfig: true);
    }

    private string CurrentPrimaryPath()
    {
        WallpaperInfo current;
        try { current = TrialEngine.Active(desktop.Capture()).Single(w => w.IsPrimary == true); }
        catch (Exception error) { throw new UiMessageException("DisplayReadFailed", error); }
        if (string.IsNullOrWhiteSpace(current.WallpaperPath)) throw new UiMessageException("PrimaryImageMissing");
        return current.WallpaperPath;
    }
    private void SetStatus(string key, params object[] args) { statusKey = key; statusArgs = args; RenderChrome(); }
    private void RenderChrome()
    {
        string T(string key) => UiText.T(key, config.Language);
        settingsItem.Text = T("MenuSettings");
        logItem.Text = T("MenuLog");
        exitItem.Text = T("MenuExit");
        toggle.Text = T(!config.HasChosenImage ? "MenuChooseResume" : paused ? needsConfig ? "MenuChooseResume" : "MenuResume" : "MenuPause");
        status.Text = UiText.T(statusKey, config.Language, statusArgs);
        icon.Text = UiText.AppName + " — " + status.Text;
        icon.Icon = !config.HasChosenImage ? trayIcons.Attention : paused ? needsConfig || statusKey == "StatusPausedError" ? trayIcons.Attention : trayIcons.Paused : trayIcons.Running;
        configForm?.RefreshAgentState();
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