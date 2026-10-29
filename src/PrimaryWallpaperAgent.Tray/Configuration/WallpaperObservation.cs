using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

// A topology transition owns wallpaper recovery until the saved roles have been
// observed quiet after readback. A stable path alone does not identify a user.
internal sealed class WallpaperObservation(Action<string> log)
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(2);
    private string? topology;
    private string? state;
    private TimeSpan stateSince;
    private TimeSpan transitionSince;
    private TimeSpan lastRepair;
    private string? cachedVersion;
    private SelectedImage? cachedImage;
    internal bool InTransition { get; private set; }
    internal bool HasBaseline { get; private set; }

    internal bool Observe(ProbeResult snapshot, TimeSpan now)
    {
        var key = JsonSerializer.Serialize(new
        {
            snapshot.SessionId,
            Active = TrialEngine.Active(snapshot).OrderBy(w => w.MonitorId, StringComparer.OrdinalIgnoreCase)
                .Select(w => new { w.MonitorId, w.IsPrimary, w.Bounds })
        });
        if (topology is not null && key != topology)
        {
            InTransition = true;
            transitionSince = lastRepair = now;
            log("DISPLAY_TRANSITION_BEGIN; saved role images retained; primary changes included");
        }
        topology = key;
        var primary = TrialEngine.Active(snapshot).Single(w => w.IsPrimary == true).WallpaperPath;
        var fingerprint = WallpaperGuard.Fingerprint(snapshot) + "|" + FileVersion(primary);
        if (state != fingerprint)
        {
            state = fingerprint; stateSince = now;
            return false;
        }
        return !InTransition || now - transitionSince >= QuietPeriod;
    }

    internal bool CanAdopt(TimeSpan now) => !InTransition && now - stateSince >= QuietPeriod;

    internal void InvalidateSample() => state = null;

    internal SelectedImage ReadPrimary(string path, PolicyImages images, string? knownSource)
    {
        var version = FileVersion(path);
        if (cachedImage is not null && version == cachedVersion) return cachedImage;
        SelectedImage image;
        if (!File.Exists(path) && (StringComparer.OrdinalIgnoreCase.Equals(path, knownSource) ||
            cachedImage is not null && StringComparer.OrdinalIgnoreCase.Equals(cachedImage.SourcePath, path) &&
            cachedImage.Sha256.Equals(images.PhotoSha256, StringComparison.OrdinalIgnoreCase)))
        {
            image = SelectedImage.Read(images.PhotoPath) with { SourcePath = path };
            log("WINDOWS_PRIMARY_SOURCE_UNAVAILABLE; verified cached primary retained");
        }
        else image = SelectedImage.Read(path);
        if (version != FileVersion(path)) throw new IOException("Primary image changed while being read; retrying.");
        cachedImage = image; cachedVersion = version;
        return image;
    }

    internal void Complete(TimeSpan now, bool repaired)
    {
        HasBaseline = true;
        if (repaired)
        {
            state = null;
            if (InTransition) lastRepair = now;
        }
        else if (InTransition && now - lastRepair >= QuietPeriod && now - stateSince >= QuietPeriod)
        {
            InTransition = false;
            log("DISPLAY_TRANSITION_END; saved role assignments observed settled");
        }
    }

    // Also observes an overwritten Windows cache file whose path stays the same.
    // No directory enumeration or import of other image files is performed.
    private static string FileVersion(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "<empty>";
        var file = new FileInfo(path);
        return file.Exists ? path + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks : path + "|missing";
    }

    internal void Reset()
    {
        topology = state = cachedVersion = null; cachedImage = null;
        InTransition = HasBaseline = false;
    }
}
