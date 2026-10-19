using System.Text;
using System.Text.Json;

namespace PrimaryWallpaperAgent;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine("PrimaryWallpaperAgent - read-only probe and explicit Phase 2 one-shot commands");
            Console.WriteLine("Usage: PrimaryWallpaperAgent.exe [--json]");
            Console.WriteLine("Reads current-session GDI displays and IDesktopWallpaper. No settings are changed.");
            Console.WriteLine("Explicit write commands (run from project directory):");
            Console.WriteLine("  trial-prepare <approved-source.jpg> (files only, no wallpaper setters)");
            Console.WriteLine("  trial-restore-check <plan.json> (read only)");
            Console.WriteLine("  trial-apply <plan.json> | trial-restore <plan.json> (CHANGE WALLPAPER)");
            Console.WriteLine("  trial-status <plan.json> (reads settings and saves a snapshot)");
            Console.WriteLine("  trial-guard <plan.json> <10-300 seconds> (BOUNDED automatic repair, Ctrl+C to stop)");
            Console.WriteLine("  --self-test-trial (in-memory tests, no Windows setting changes)");
            Console.WriteLine("Exit codes: 0 = complete snapshot; 1 = fatal error; 2 = incomplete/ambiguous snapshot or invalid arguments.");
            return 0;
        }
        if (args.Length > 0 && args is not ["--json"] && args is not ["--self-test-trial"] && !args[0].StartsWith("trial-", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Unknown arguments. Use --help.");
            return 2;
        }
        try
        {
            if (args is ["--self-test-trial"]) return TrialSelfTests.Run();
            if (args.Length > 0 && args[0].StartsWith("trial-", StringComparison.Ordinal)) return WallpaperTrial.Run(args);
            var result = Probe.Capture();
            if (args is ["--json"])
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            else Print(result);
            return result.Complete ? 0 : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Probe failed ({Probe.Hex(exception.HResult)}): {exception}");
            return 1;
        }
    }

    private static void Print(ProbeResult result)
    {
        Console.WriteLine("PrimaryWallpaperAgent | PHASE 1 | READ ONLY");
        Console.WriteLine($"Captured: {result.CapturedAt:O} | Session: {result.SessionId}");
        Console.WriteLine($"{result.WindowsVersion} | {result.Runtime}");
        Console.WriteLine($"Active GDI displays: {result.Displays.Length} | Wallpaper entries: {result.Wallpapers.Length}");
        Console.WriteLine($"Topology stable during capture: {result.TopologyStable}");
        Console.WriteLine();
        foreach (var display in result.Displays)
        {
            Console.WriteLine($"{(display.IsPrimary ? "PRIMARY" : "SECONDARY")} {display.GdiName} | {display.Bounds}");
            foreach (var endpoint in display.Endpoints)
            {
                Console.WriteLine($"  Monitor: {endpoint.Description} | flags: 0x{endpoint.StateFlags:X8}");
                Console.WriteLine($"  Interface: {endpoint.InterfacePath}");
            }
        }
        Console.WriteLine();
        foreach (var wallpaper in result.Wallpapers)
        {
            var attached = wallpaper.Attached switch { true => "ATTACHED", false => "DETACHED/CACHED", null => "UNKNOWN" };
            Console.WriteLine($"Wallpaper entry {wallpaper.Index}: {attached} | {wallpaper.MatchStatus}");
            Console.WriteLine($"  ID: {wallpaper.MonitorId}");
            Console.WriteLine($"  GDI: {wallpaper.GdiName ?? "<unmapped>"} | Primary: {wallpaper.IsPrimary?.ToString() ?? "unknown"}");
            Console.WriteLine($"  Bounds: {wallpaper.Bounds?.ToString() ?? "<unavailable>"} | Rect agrees: {wallpaper.RectMatches?.ToString() ?? "n/a"}");
            Console.WriteLine($"  Wallpaper: {(wallpaper.WallpaperHResult is null ? "<not queried: invalid monitor ID>" : wallpaper.WallpaperHResult != 0 ? "<read failed>" : string.IsNullOrEmpty(wallpaper.WallpaperPath) ? "<empty: solid color/no image; not proof of black>" : wallpaper.WallpaperPath)}");
            Console.WriteLine($"  HRESULT: rect={Probe.Hex(wallpaper.RectHResult)} wallpaper={Probe.Hex(wallpaper.WallpaperHResult)}");
        }
        var position = result.Position switch { 0 => "Center", 1 => "Tile", 2 => "Stretch", 3 => "Fit", 4 => "Fill", 5 => "Span", _ => "Unknown" };
        Console.WriteLine();
        Console.WriteLine($"Background RGB: {result.BackgroundRgb ?? "unknown"} | Position: {position} ({result.Position}) | Slideshow status flags: {result.SlideshowStatus}");
        foreach (var issue in result.Issues) Console.WriteLine($"REVIEW: {issue}");
        Console.WriteLine(result.Complete ? "RESULT: PASS - all active displays mapped by device path." : "RESULT: REVIEW - snapshot is incomplete or ambiguous.");
        Console.WriteLine("Virtual Desktop behavior: see VIRTUAL-DESKTOP-TEST.md; this output is one read-only snapshot. No watcher/tray/autostart is installed.");
    }
}
