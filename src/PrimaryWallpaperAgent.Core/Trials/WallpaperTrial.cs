using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PrimaryWallpaperAgent;

internal sealed record TrialPlan(int SchemaVersion, string RunId, string SourceOriginalPath,
    string PhotoPath, string PhotoSha256, string BlackPath, string BlackSha256, ProbeResult Before);

internal interface ITrialDesktop
{
    ProbeResult Capture();
    void SetWallpaper(string monitorId, string path);
}

internal sealed class NativeTrialDesktop : ITrialDesktop
{
    public ProbeResult Capture() => Probe.Capture();

    public void SetWallpaper(string monitorId, string path)
    {
        if (string.IsNullOrWhiteSpace(monitorId) || string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A specific monitor ID and image path are required; global writes are forbidden.");
        WithApi(api =>
        {
            var hr = api.SetWallpaper(monitorId, path);
            if (hr != 0) throw new COMException($"SetWallpaper returned {Probe.Hex(hr)}.", hr);
            return 0;
        });
    }

    internal void VerifyReadConnection() => WithApi(api =>
    {
        // Real COM regression check: a nested probe must not invalidate this RCW.
        // This is intentionally a getter-only operation, before any wallpaper write.
        var snapshot = Probe.Capture();
        var hr = api.GetPosition(out var position);
        if (hr != 0 || position != snapshot.Position)
            throw new InvalidOperationException("COM read connection did not survive a nested probe.");
        return 0;
    });

    private static T WithApi<T>(Func<IDesktopWallpaper, T> operation)
    {
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), true)!)!;
        try { return operation((IDesktopWallpaper)instance); }
        finally { Marshal.ReleaseComObject(instance); }
    }
}

internal static class TrialEngine
{
    internal static WallpaperInfo[] Active(ProbeResult snapshot)
    {
        var active = snapshot.Wallpapers.Where(w => w.Attached == true).ToArray();
        if (!snapshot.TopologyStable || active.Length == 0 || active.Length != snapshot.Displays.Length ||
            active.Count(w => w.IsPrimary == true) != 1 ||
            active.Any(w => string.IsNullOrWhiteSpace(w.MonitorId) || w.MatchStatus != "ExactDevicePath" ||
                w.RectMatches != true || w.WallpaperHResult != 0 || w.IsPrimary is null) ||
            active.Select(w => w.MonitorId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != active.Length ||
            snapshot.Displays.Any(d => active.Count(w => w.GdiName == d.GdiName) != 1))
            throw new InvalidOperationException("Active display mapping is incomplete, ambiguous or unstable; no apply is allowed.");
        // Unknown entries outside the active GDI set remain evidence, never write targets.
        return active;
    }

    internal static void RequireSameTopology(TrialPlan plan, ProbeResult current)
    {
        _ = Active(current);
        if (plan.Before.SessionId != current.SessionId ||
            JsonSerializer.Serialize(plan.Before.Displays) != JsonSerializer.Serialize(current.Displays))
            throw new InvalidOperationException("Session, topology or primary changed since preparation; aborting.");
    }

    internal static bool Matches(TrialPlan plan, ProbeResult current, bool restored)
    {
        var active = Active(current);
        var original = Active(plan.Before);
        return active.Length == original.Length && original.All(old =>
            active.Count(now => StringComparer.OrdinalIgnoreCase.Equals(now.MonitorId, old.MonitorId) &&
                StringComparer.OrdinalIgnoreCase.Equals(now.WallpaperPath,
                    restored ? old.WallpaperPath : old.IsPrimary == true ? plan.PhotoPath : plan.BlackPath)) == 1) &&
            current.Position == plan.Before.Position && current.BackgroundRgb == plan.Before.BackgroundRgb &&
            current.SlideshowStatus == plan.Before.SlideshowStatus;
    }

    internal static ProbeResult Apply(TrialPlan plan, ITrialDesktop desktop, Action<string> log)
    {
        var current = desktop.Capture();
        RequireSameTopology(plan, current);
        if (!Matches(plan, current, restored: true))
            throw new InvalidOperationException("Wallpaper state changed after preparation; refusing to overwrite it.");
        var attempted = false;
        try
        {
            // Primary is written last so an API that unexpectedly ignores monitor IDs
            // ends with the photograph rather than black, before verification/rollback.
            foreach (var monitor in Active(plan.Before).OrderBy(w => w.IsPrimary == true))
            {
                RequireSameTopology(plan, desktop.Capture());
                var path = monitor.IsPrimary == true ? plan.PhotoPath : plan.BlackPath;
                if (StringComparer.OrdinalIgnoreCase.Equals(monitor.WallpaperPath, path)) continue;
                log($"SetWallpaper requested for {monitor.MonitorId}");
                attempted = true; // A failing setter can still have side effects.
                desktop.SetWallpaper(monitor.MonitorId, path);
            }
            var after = desktop.Capture();
            RequireSameTopology(plan, after);
            if (!Matches(plan, after, restored: false))
                throw new InvalidOperationException("Readback does not match the per-monitor plan.");
            log("APPLY_READBACK_MATCHED; visual confirmation and desktop-switch test still required.");
            return after;
        }
        catch (Exception applyError)
        {
            if (!attempted) throw;
            try
            {
                Restore(plan, desktop);
                log("AUTO_RESTORE_READBACK_MATCHED after apply failure.");
            }
            catch (Exception restoreError)
            {
                throw new AggregateException("Apply failed and restoration was not fully verified. Use the saved plan to retry restore.", applyError, restoreError);
            }
            throw new InvalidOperationException("Apply failed; original monitor assignments were restored and read back.", applyError);
        }
    }

    internal static ProbeResult Restore(TrialPlan plan, ITrialDesktop desktop)
    {
        var active = Active(desktop.Capture());
        var errors = new List<Exception>();
        foreach (var original in Active(plan.Before))
        {
            // Restore known IDs still attached; do not write detached/cache entries.
            if (!active.Any(w => StringComparer.OrdinalIgnoreCase.Equals(w.MonitorId, original.MonitorId)))
            {
                errors.Add(new InvalidOperationException($"Original monitor is no longer attached: {original.MonitorId}"));
                continue;
            }
            try { desktop.SetWallpaper(original.MonitorId, original.WallpaperPath!); }
            catch (Exception error) { errors.Add(error); }
        }
        var after = desktop.Capture();
        if (!Matches(plan, after, restored: true)) errors.Add(new InvalidOperationException("Restore readback does not match the saved state."));
        if (errors.Count > 0) throw new AggregateException("Restoration is incomplete.", errors);
        return after;
    }
}

internal static class WallpaperTrial
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(string[] args)
    {
        var root = Path.GetFullPath(Directory.GetCurrentDirectory());
        if (!File.Exists(Path.Combine(root, "PrimaryWallpaperAgent.slnx")))
            throw new InvalidOperationException("Run trial commands from the PrimaryWallpaperAgent project directory.");
        if (args is ["trial-prepare", var source])
        {
            Prepare(root, source);
            return 0;
        }
        var guardCommand = args is ["trial-guard", _, _];
        if (!guardCommand && (args.Length != 2 || args[0] is not ("trial-apply" or "trial-restore-check" or "trial-restore" or "trial-status")))
            throw new ArgumentException("Usage: trial-prepare <source-image> | trial-apply|trial-restore-check|trial-restore|trial-status <plan.json>");

        var planPath = Path.GetFullPath(args[1]);
        RequireInside(planPath, Path.Combine(root, "snapshots"));
        var plan = JsonSerializer.Deserialize<TrialPlan>(File.ReadAllText(planPath)) ?? throw new InvalidDataException("Invalid trial plan.");
        ValidatePlan(root, plan);
        var desktop = new NativeTrialDesktop();
        using var writeLease = args[0] is "trial-apply" or "trial-restore" or "trial-guard" ? new WallpaperWriteLease() : null;
        if (guardCommand)
        {
            if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                throw new ArgumentException("Guard duration must be an integer from 10 to 300 seconds.");
            desktop.VerifyReadConnection();
            return WallpaperGuard.Run(plan, desktop, seconds, Path.GetDirectoryName(planPath)!);
        }
        if (args[0] == "trial-restore-check")
        {
            TrialEngine.RequireSameTopology(plan, desktop.Capture());
            desktop.VerifyReadConnection();
            if (!File.Exists(plan.SourceOriginalPath)) throw new FileNotFoundException("Original image is unavailable.");
            Console.WriteLine("RESTORE CHECK PASSED: assets, monitor mapping and nested COM lifetime regression check are valid; no setters executed.");
            return 0;
        }
        if (args[0] == "trial-status")
        {
            var current = desktop.Capture();
            SaveNew(Path.Combine(Path.GetDirectoryName(planPath)!, "status-" + Stamp() + ".json"), current);
            Console.WriteLine($"POLICY_READBACK_MATCH={TrialEngine.Matches(plan, current, restored: false)}");
            Console.WriteLine($"ORIGINAL_READBACK_MATCH={TrialEngine.Matches(plan, current, restored: true)}");
            return 0;
        }
        void Log(string message)
        {
            // Journal failures must be reported, and may trigger the engine's rollback.
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(planPath)!, "journal.txt"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        var result = args[0] == "trial-apply" ? TrialEngine.Apply(plan, desktop, Log) : TrialEngine.Restore(plan, desktop);
        SaveNew(Path.Combine(Path.GetDirectoryName(planPath)!, args[0] + "-" + Stamp() + ".json"), result);
        Console.WriteLine(args[0] == "trial-apply"
            ? "APPLY READBACK MATCHED. No watcher is running. Confirm visible images before switching desktops."
            : "RESTORE READBACK MATCHED. Confirm visible images on the selected desktop.");
        Console.WriteLine($"Plan: {planPath}");
        return 0;
    }

    private static void Prepare(string root, string sourceArgument)
    {
        var source = Path.GetFullPath(sourceArgument);
        var before = Probe.Capture();
        var active = TrialEngine.Active(before);
        if (before.Position != 4 || before.SlideshowStatus != 1)
            throw new InvalidOperationException("This controlled trial requires static Picture with Fill; global settings will not be changed.");
        if (active.Any(w => !StringComparer.OrdinalIgnoreCase.Equals(w.WallpaperPath, source)))
            throw new InvalidOperationException("Current images differ from the approved source; review before preparing.");

        var imageBytes = File.ReadAllBytes(source); // Only the exact path explicitly passed to this command.
        if (imageBytes.Length < 3 || imageBytes[0] != 0xff || imageBytes[1] != 0xd8 || imageBytes[2] != 0xff)
            throw new InvalidDataException("This experiment expects the approved JPEG source.");
        var runId = Stamp() + "-" + Guid.NewGuid().ToString("N")[..8];
        var assetFolder = Path.Combine(root, ".local-assets", runId);
        var runFolder = Path.Combine(root, "snapshots", "phase2-" + runId);
        Directory.CreateDirectory(assetFolder);
        Directory.CreateDirectory(runFolder);
        var photoPath = Path.Combine(assetFolder, "primary.jpg");
        var blackPath = Path.Combine(assetFolder, "black.bmp");
        File.WriteAllBytes(photoPath, imageBytes);
        WriteBlackBitmap(blackPath);
        var plan = new TrialPlan(1, runId, source, photoPath, Hash(photoPath), blackPath, Hash(blackPath), before);
        if (plan.PhotoSha256 != Convert.ToHexString(SHA256.HashData(imageBytes))) throw new IOException("Image copy hash mismatch.");
        TrialEngine.RequireSameTopology(plan, Probe.Capture());
        var planPath = Path.Combine(runFolder, "plan.json");
        SaveNew(planPath, plan);
        Console.WriteLine("PREPARED: photo copy, black bitmap and original state saved; no wallpaper setters executed.");
        Console.WriteLine($"Plan: {planPath}");
        Console.WriteLine($"Photo SHA256: {plan.PhotoSha256}");
        Console.WriteLine("Next: trial-restore-check <plan.json>, then trial-apply <plan.json>.");
    }

    private static void ValidatePlan(string root, TrialPlan plan)
    {
        if (plan.SchemaVersion != 1) throw new InvalidDataException("Unsupported plan schema.");
        _ = TrialEngine.Active(plan.Before);
        RequireInside(plan.PhotoPath, Path.Combine(root, ".local-assets"));
        RequireInside(plan.BlackPath, Path.Combine(root, ".local-assets"));
        if (Hash(plan.PhotoPath) != plan.PhotoSha256 || Hash(plan.BlackPath) != plan.BlackSha256)
            throw new InvalidDataException("Trial asset integrity check failed.");
        if (TrialEngine.Active(plan.Before).Any(w => string.IsNullOrWhiteSpace(w.WallpaperPath) ||
            !StringComparer.OrdinalIgnoreCase.Equals(w.WallpaperPath, plan.SourceOriginalPath)))
            throw new InvalidDataException("Original assignments are outside this controlled trial's approved image.");
    }

    private static void RequireInside(string path, string folder)
    {
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path is outside the trial's project directory.");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);

    private static void SaveNew<T>(string path, T value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, value, JsonOptions);
        stream.Flush(flushToDisk: true);
    }

    private static void WriteBlackBitmap(string path)
    {
        // A 1x1, 24-bit uncompressed BMP: 54-byte header + one padded black row.
        using var writer = new BinaryWriter(new FileStream(path, FileMode.CreateNew));
        writer.Write((ushort)0x4d42); writer.Write(58); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(1); writer.Write(1); writer.Write((ushort)1); writer.Write((ushort)24);
        writer.Write(0); writer.Write(4); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
        writer.Write(new byte[4]);
    }
}
