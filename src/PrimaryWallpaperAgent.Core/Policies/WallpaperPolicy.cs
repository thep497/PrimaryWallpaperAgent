namespace PrimaryWallpaperAgent;

// AcceptedPaths means aliases of the CURRENT image only, never image history.
// No monitor IDs or session number are persisted as policy inputs.
internal sealed record PolicyImages(string PhotoPath, string PhotoSha256, string SecondaryPath,
    string SecondarySha256, string[] AcceptedPaths);

internal sealed class PolicyConflictException(string message) : InvalidOperationException(message);

internal static class WallpaperPolicy
{
    internal static bool Repair(PolicyImages images, ProbeResult current, ITrialDesktop desktop, Action<string> log, ProbeResult? acceptedTopology = null)
    {
        var active = TrialEngine.Active(current);
        if (current.Position != 4 || current.SlideshowStatus != 1)
            throw new PolicyConflictException("Wallpaper mode changed. Select Picture / Fill before resuming.");
        var known = images.AcceptedPaths.Concat([images.PhotoPath, images.SecondaryPath]);
        var unfamiliar = active.Any(w => !known.Contains(w.WallpaperPath, StringComparer.OrdinalIgnoreCase));
        // Only an observed change of attached monitor IDs in this session can
        // explain Windows restoring remembered assignments during hotplug.
        // Primary/position/order changes alone do not authorize another picture.
        var monitorSetChanged = acceptedTopology is not null && acceptedTopology.SessionId == current.SessionId &&
            !TrialEngine.Active(acceptedTopology).Select(w => w.MonitorId).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(active.Select(w => w.MonitorId));
        if (unfamiliar && !monitorSetChanged)
            throw new PolicyConflictException("Wallpaper differs from the current chosen image. Paused to preserve your choice, even if used previously.");
        if (monitorSetChanged)
            log($"HOTPLUG_REBASE; active={active.Length}; unfamiliarWallpaper={unfamiliar}; current Windows primary");
        var plan = new TrialPlan(1, "tray", images.PhotoPath, images.PhotoPath, images.PhotoSha256,
            images.SecondaryPath, images.SecondarySha256, current);
        if (TrialEngine.Matches(plan, current, restored: false)) return false;
        // Apply captures and validates again before each write, verifies readback,
        // and rolls back to this fresh snapshot on failure.
        TrialEngine.Apply(plan, desktop, log);
        return true;
    }
}
