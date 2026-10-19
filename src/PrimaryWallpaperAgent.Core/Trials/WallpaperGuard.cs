using System.Diagnostics;
using System.Text.Json;

namespace PrimaryWallpaperAgent;

internal static class WallpaperGuard
{
    // Does not enumerate Virtual Desktop IDs. It observes the effective wallpaper
    // state Windows exposes and repairs drift on whichever desktop is active.
    internal static bool RepairIfNeeded(TrialPlan saved, ProbeResult current, ITrialDesktop desktop, Action<string> log)
    {
        var active = TrialEngine.Active(current);
        if (current.Position != 4 || current.SlideshowStatus != 1 || current.SessionId != saved.Before.SessionId)
            throw new InvalidOperationException("Guard stopped: Picture/Fill/session changed; user settings will not be overwritten.");
        if (active.Any(w => !KnownImage(saved, w.WallpaperPath)))
            throw new InvalidOperationException("Guard stopped: an unrecognized wallpaper was selected; review the user's change.");
        // Rebuild the plan from current IDs and primary, including a changed display
        // count. The old plan supplies image assets, never the new monitor ordering.
        var effective = saved with { Before = current };
        if (TrialEngine.Matches(effective, current, restored: false)) return false;
        TrialEngine.Apply(effective, desktop, log);
        return true;
    }

    private static bool KnownImage(TrialPlan plan, string? path) => new[]
        { plan.SourceOriginalPath, plan.PhotoPath, plan.BlackPath }
        .Contains(path, StringComparer.OrdinalIgnoreCase);

    internal static string Fingerprint(ProbeResult snapshot) => JsonSerializer.Serialize(new
    {
        snapshot.Displays, snapshot.Position, snapshot.BackgroundRgb, snapshot.SlideshowStatus,
        Images = snapshot.Wallpapers.Where(w => w.Attached == true).OrderBy(w => w.MonitorId, StringComparer.OrdinalIgnoreCase)
            .Select(w => new { w.MonitorId, w.WallpaperPath })
    });

    internal static int Run(TrialPlan plan, ITrialDesktop desktop, int seconds, string evidenceFolder)
    {
        if (seconds is < 10 or > 300) throw new ArgumentOutOfRangeException(nameof(seconds), "Use 10-300 seconds for this bounded experiment.");
        var runFolder = Path.Combine(evidenceFolder, "guard-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff",
            System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(runFolder);
        var journal = Path.Combine(runFolder, "journal.txt");
        void Log(string message)
        {
            var line = $"{DateTimeOffset.Now:O} {message}";
            File.AppendAllText(journal, line + Environment.NewLine);
            Console.WriteLine(line);
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        var clock = Stopwatch.StartNew();
        string? previous = null;
        var samples = 0;
        var repairs = 0;
        var reason = "Duration elapsed";
        var exitCode = 0;
        ProbeResult? latest = null;
        Console.WriteLine($"BOUNDED GUARD: {seconds} seconds; poll every 1 second; require two matching snapshots before repair.");
        Console.WriteLine("Switch desktops normally. A brief original-image flash can occur before detection. Press Ctrl+C to stop.");
        Console.WriteLine("No tray/autostart is installed. After exit the last assignment remains; use Restore-WallpaperTrial.ps1 if needed.");
        Console.WriteLine($"Evidence: {runFolder}");
        try
        {
            while (clock.Elapsed.TotalSeconds < seconds && !cancellation.IsCancellationRequested)
            {
                latest = desktop.Capture();
                samples++;
                if (!latest.TopologyStable) previous = null;
                else
                {
                    var fingerprint = Fingerprint(latest);
                    if (fingerprint == previous)
                    {
                        // Eight successful repairs is enough for this manual experiment.
                        // Stop rather than repeatedly compete with another component.
                        var effective = plan with { Before = latest };
                        if (repairs >= 8 && !TrialEngine.Matches(effective, latest, false))
                            throw new InvalidOperationException("Repair limit reached; inspect repeated wallpaper resets before continuing.");
                        if (RepairIfNeeded(plan, latest, desktop, Log))
                        {
                            repairs++;
                            latest = desktop.Capture();
                            File.WriteAllText(Path.Combine(runFolder, $"repair-{repairs}.json"),
                                JsonSerializer.Serialize(latest, new JsonSerializerOptions { WriteIndented = true }));
                            Log($"REPAIR {repairs}: {latest.Displays.Length} active displays; primary selected from current Windows flags.");
                            previous = null;
                        }
                    }
                    else previous = fingerprint;
                }
                try { Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { break; }
            }
            if (cancellation.IsCancellationRequested) reason = "Stopped with Ctrl+C";
        }
        catch (Exception error)
        {
            reason = error.ToString();
            exitCode = 1;
            Console.Error.WriteLine("GUARD STOPPED: " + reason);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            var summary = new { SecondsRequested = seconds, SecondsElapsed = clock.Elapsed.TotalSeconds, Samples = samples,
                Repairs = repairs, StopReason = reason, ExitCode = exitCode, LastSnapshot = latest,
                RestoredOnExit = false, NativeWritesOnlyWhenPolicyDiffers = true };
            File.WriteAllText(Path.Combine(runFolder, "summary.json"), JsonSerializer.Serialize(summary,
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"GUARD EXIT: repairs={repairs}; samples={samples}; last assignment remains. {reason}");
        }
        return exitCode;
    }
}

internal sealed class WallpaperWriteLease : IDisposable
{
    private readonly Mutex mutex = new(false, @"Local\PrimaryWallpaperAgent.WallpaperWrite");
    private readonly bool owns;
    internal WallpaperWriteLease()
    {
        try { owns = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns)
        {
            mutex.Dispose();
            throw new InvalidOperationException("Another wallpaper write/guard is running. Stop it before applying or restoring.");
        }
    }
    public void Dispose()
    {
        if (owns) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
