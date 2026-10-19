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
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem status = new("กำลังตรวจจอ…") { Enabled = false };
    private readonly ToolStripMenuItem toggle = new("พักการทำงาน");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Stopwatch uptime = Stopwatch.StartNew();
    private readonly Queue<TimeSpan> repairs = new();
    private readonly string logFolder;
    private readonly string logPath;
    private bool paused;
    private bool needsConfig;
    private ConfigForm? configForm;
    private bool openOnFirstTick;
    private string currentStatus = "กำลังตรวจจอ…";
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
        logFolder = Path.Combine(AppContext.BaseDirectory, "runtime", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(logFolder);
        logPath = Path.Combine(logFolder, "agent.log");
        Log(smoke ? "START read-only tray smoke test; no setters" : "START tray v0.2; poll 1 second; two stable samples; startup writes only on Config Save");
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("ตั้งค่า…", null, (_, _) => ShowConfig());
        toggle.Click += (_, _) => Toggle();
        menu.Items.Add(toggle);
        menu.Items.Add("ตรวจภาพอีกครั้ง", null, (_, _) => { previous = null; if (!paused) Tick(); });
        menu.Items.Add("เปิดบันทึกการทำงาน", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("ออก (คงภาพปัจจุบัน)", null, (_, _) => ExitThread());
        icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "PrimaryWallpaperAgent — กำลังตรวจจอ", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => ShowConfig();
        icon.BalloonTipClicked += (_, _) => ShowConfig();
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    private void Tick()
    {
        if (smoke && uptime.Elapsed.TotalSeconds >= 5) { ExitThread(); return; }
        if (configForm is not null) return;
        if (openOnFirstTick) { openOnFirstTick = false; ShowConfig(); return; }
        if (paused) return;
        ProbeResult snapshot;
        try
        {
            snapshot = desktop.Capture();
            samples++;
            // Hotplug can expose a partial mapping briefly. No writes here.
            _ = TrialEngine.Active(snapshot);
            failures = 0;
        }
        catch (Exception error)
        {
            previous = null;
            if (++failures >= 10) Pause(error);
            else { Log("WAIT " + error.Message); SetStatus("รอ Windows จัดจอ…"); }
            return;
        }
        try
        {
            var fingerprint = WallpaperGuard.Fingerprint(snapshot);
            if (fingerprint != previous)
            {
                previous = fingerprint;
                SetStatus($"รอจอนิ่ง · {snapshot.Displays.Length} จอ");
                return;
            }
            if (smoke) { SetStatus($"ทดสอบอ่าน · {snapshot.Displays.Length} จอ"); return; }
            while (repairs.TryPeek(out var time) && uptime.Elapsed - time >= TimeSpan.FromMinutes(1)) repairs.Dequeue();
            // Count recent repairs, not all repairs since launch.
            var needsRepair = TrialEngine.Active(snapshot).Any(w => !StringComparer.OrdinalIgnoreCase.Equals(
                w.WallpaperPath, w.IsPrimary == true ? images.PhotoPath : images.BlackPath));
            if (needsRepair && repairs.Count >= 8)
                throw new PolicyConflictException("Wallpaper changed repeatedly (8 repairs in 60 seconds). Review the log before resuming.");
            if (needsRepair)
            {
                ValidateAssets();
                // Save the complete pre-write state before attempting any setter.
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
            SetStatus($"ทำงานอยู่ · {snapshot.Displays.Length} จอ");
        }
        catch (Exception error) { Pause(error, error is PolicyConflictException); }
    }

    private void ValidateAssets()
    {
        _ = store.Images(config);
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
                ValidateAssets();
                paused = false; previous = null; failures = 0; repairs.Clear(); Log("RESUMED by user");
            }
            catch (Exception error) { Pause(error); return; }
        }
        toggle.Text = paused ? "ทำงานต่อ" : "พักการทำงาน";
        SetStatus(paused ? "พักอยู่" : "กำลังตรวจจอ…");
    }

    private void Pause(Exception error, bool requireConfig = false)
    {
        paused = true;
        needsConfig = requireConfig;
        previous = null;
        toggle.Text = requireConfig ? "เลือกภาพและทำงานต่อ…" : "ทำงานต่อ";
        SetStatus(requireConfig ? "พักอยู่ · พบภาพหรือโหมดใหม่" : "พักอยู่ · ตรวจบันทึก");
        Log("PAUSED " + error);
        icon.ShowBalloonTip(8000, "PrimaryWallpaperAgent พักการทำงาน", requireConfig ?
            "พบภาพหรือโหมดใหม่ เปิดตั้งค่าเพื่อเลือกภาพและบันทึกใช้ค่ะ" : "ตรวจบันทึกจากเมนูไอคอน แล้วเลือกทำงานต่อเมื่อพร้อมค่ะ", ToolTipIcon.Warning);
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
            if (!store.Read().SequenceEqual(configBytes))
                throw new InvalidOperationException("ไฟล์ตั้งค่าถูกเปลี่ยนจากภายนอก กรุณาปิดและเปิดโปรแกรมใหม่ค่ะ");
            var startupAtOpen = startup.Read();
            var wasPaused = paused;
            var configurationStatus = currentStatus;
            try { configurationStatus += $" · {TrialEngine.Active(desktop.Capture()).Length} จอ\nระหว่างตั้งค่าจะพักการปรับภาพอัตโนมัติ"; }
            catch (Exception) { configurationStatus += " · รอ Windows จัดจอ"; }
            var initial = SelectedImage.Read(store.Asset(config.PhotoFile, config.PhotoSha256));
            if (config.PhotoLabel is not null) initial = initial with { FriendlyName = config.PhotoLabel };
            using var form = new ConfigForm(initial,
                startupAtOpen is not null, configurationStatus, CurrentPrimaryPath, (selected, enabled) =>
                {
                    try
                    {
                        var candidate = store.Prepare(config, selected, enabled);
                        var candidateImages = store.Images(candidate);
                        var id = "config-" + Guid.NewGuid().ToString("N")[..8];
                        ConfigWorkflow.Apply(candidate, candidateImages, configBytes, startupAtOpen, store, startup,
                            desktop, Log, (name, value) => Save(id + "-" + name + ".json", value));
                        config = candidate;
                        images = candidateImages;
                        configBytes = JsonSerializer.SerializeToUtf8Bytes(candidate, SettingsStore.JsonOptions);
                        paused = false; needsConfig = false; failures = 0; previous = null; repairs.Clear();
                        toggle.Text = "พักการทำงาน";
                        SetStatus("บันทึกแล้ว · กำลังตรวจจอ");
                    }
                    catch (Exception error) { Pause(error, requireConfig: true); throw; }
                }, OpenLog);
            configForm = form;
            form.Load += (_, _) => form.BeginInvoke((Action)(() =>
                Log("CONFIG_VISIBLE; nativeVisible=" + WindowPresentation.IsVisible(form))));
            Log("CONFIG_REQUESTED; preview only until Save");
            var result = form.ShowDialog();
            if (result != DialogResult.OK)
            {
                Log("CONFIG_CLOSED without a successful Save");
                // Preserve the prior pause state, including an apply failure in this dialog.
                if (wasPaused || needsConfig) paused = true;
            }
        }
        catch (Exception error) { MessageBox.Show(error.Message, "เปิดตั้งค่าไม่ได้", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { configForm = null; previous = null; }
    }

    private string CurrentPrimaryPath()
    {
        var current = TrialEngine.Active(desktop.Capture()).Single(w => w.IsPrimary == true);
        if (string.IsNullOrWhiteSpace(current.WallpaperPath)) throw new InvalidOperationException("จอหลักไม่มีไฟล์ภาพให้เลือกค่ะ");
        return current.WallpaperPath;
    }
    private void SetStatus(string text) { currentStatus = text; status.Text = text; icon.Text = "PrimaryWallpaperAgent — " + text; }
    private void Log(string message) => File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    private void Save(string name, object value) => File.WriteAllText(Path.Combine(logFolder, name),
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    private void OpenLog()
    {
        try { Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true }); }
        catch (Exception error) { MessageBox.Show(error.Message, "เปิดบันทึกไม่ได้"); }
    }
    protected override void ExitThreadCore()
    {
        timer.Stop();
        icon.Visible = false;
        try
        {
            Log($"EXIT samples={samples}; repairs={repairCount}; last wallpaper assignments remain");
            Save("summary.json", new { SmokeTest = smoke, Samples = samples, Repairs = repairCount,
                SecondsElapsed = uptime.Elapsed.TotalSeconds, Paused = paused, RestoredOnExit = false });
        }
        finally { base.ExitThreadCore(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); icon.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
}
