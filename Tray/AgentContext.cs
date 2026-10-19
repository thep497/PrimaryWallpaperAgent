using System.Diagnostics;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal sealed class AgentContext : ApplicationContext
{
    private readonly PolicyImages images;
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
    private string? previous;
    private int failures;
    private int samples;
    private int repairCount;

    internal AgentContext(PolicyImages images, bool smoke)
    {
        this.images = images;
        this.smoke = smoke;
        logFolder = Path.Combine(AppContext.BaseDirectory, "runtime", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(logFolder);
        logPath = Path.Combine(logFolder, "agent.log");
        Log(smoke ? "START read-only tray smoke test; no setters" : "START manual tray; poll 1 second; two stable samples; no autostart");
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        toggle.Click += (_, _) => Toggle();
        menu.Items.Add(toggle);
        menu.Items.Add("ตรวจภาพอีกครั้ง", null, (_, _) => { previous = null; if (!paused) Tick(); });
        menu.Items.Add("เปิดบันทึกการทำงาน", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("ออก (คงภาพปัจจุบัน)", null, (_, _) => ExitThread());
        icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "PrimaryWallpaperAgent — กำลังตรวจจอ", ContextMenuStrip = menu, Visible = true };
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    private void Tick()
    {
        if (smoke && uptime.Elapsed.TotalSeconds >= 5) { ExitThread(); return; }
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
        catch (Exception error) { Pause(error); }
    }

    private void ValidateAssets()
    {
        _ = Program.Asset("assets/primary.jpg", images.PhotoSha256);
        _ = Program.Asset("assets/black.bmp", images.BlackSha256);
    }

    private void Toggle()
    {
        if (smoke) return;
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

    private void Pause(Exception error)
    {
        paused = true;
        previous = null;
        toggle.Text = "ทำงานต่อ";
        SetStatus("พักอยู่ · ตรวจบันทึก");
        Log("PAUSED " + error);
        icon.ShowBalloonTip(8000, "PrimaryWallpaperAgent พักการทำงาน", "ตรวจบันทึกจากเมนูไอคอน แล้วเลือกทำงานต่อเมื่อพร้อมค่ะ", ToolTipIcon.Warning);
    }

    private void SetStatus(string text) { status.Text = text; icon.Text = "PrimaryWallpaperAgent — " + text; }
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
