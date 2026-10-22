using System.Globalization;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class DiagnosticSnapshots
{
    internal static string Save(string folder, string runId, string kind, object data)
    {
        if (RuntimeFiles.IsLink(folder)) throw new IOException("Evidence directory is a link.");
        kind = string.Concat(kind.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-'));
        if (kind.Length > 100) kind = kind[..100];
        var at = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(folder, "event-" + at.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + id + "-" + kind + ".json");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(file, new { SchemaVersion = 1, RunId = runId, EventId = id, At = at, Kind = kind, Data = data }, SettingsStore.JsonOptions);
        file.Flush(flushToDisk: true);
        return path;
    }

    internal static object Mapping(ProbeResult? snapshot) => snapshot is null ? new { Reason = "CaptureFailed" } : new
    {
        snapshot.CapturedAt, snapshot.SessionId, snapshot.TopologyStable, GdiCount = snapshot.Displays.Length,
        AttachedCount = snapshot.Wallpapers.Count(w => w.Attached == true),
        PrimaryCount = snapshot.Wallpapers.Count(w => w.Attached == true && w.IsPrimary == true),
        Active = snapshot.Wallpapers.Where(w => w.Attached == true).Select(w => new
        { w.MonitorId, w.GdiName, w.MatchStatus, w.RectMatches, w.WallpaperHResult, w.IsPrimary, w.Bounds }),
        // Cached/disconnected API failures stay separate from active mapping rejection.
        snapshot.Displays, snapshot.Issues
    };
}
