namespace PrimaryWallpaperAgent;

// AcceptedPaths means aliases of the CURRENT image only, never image history.
// No monitor IDs or session number are persisted as policy inputs.
internal sealed record PolicyImages(string PhotoPath, string PhotoSha256, string BlackPath,
    string BlackSha256, string[] AcceptedPaths);

internal sealed class PolicyConflictException(string message) : InvalidOperationException(message);

internal static class WallpaperPolicy
{
    internal static bool Repair(PolicyImages images, ProbeResult current, ITrialDesktop desktop, Action<string> log)
    {
        var active = TrialEngine.Active(current);
        if (current.Position != 4 || current.SlideshowStatus != 1)
            throw new PolicyConflictException("Wallpaper mode changed. Select Picture / Fill before resuming.");
        var known = images.AcceptedPaths.Concat([images.PhotoPath, images.BlackPath]);
        if (active.Any(w => !known.Contains(w.WallpaperPath, StringComparer.OrdinalIgnoreCase)))
            throw new PolicyConflictException("Wallpaper differs from the current chosen image. Paused to preserve your choice, even if used previously.");
        var plan = new TrialPlan(1, "tray", images.PhotoPath, images.PhotoPath, images.PhotoSha256,
            images.BlackPath, images.BlackSha256, current);
        if (TrialEngine.Matches(plan, current, restored: false)) return false;
        // Apply captures and validates again before each write, verifies readback,
        // and rolls back to this fresh snapshot on failure.
        TrialEngine.Apply(plan, desktop, log);
        return true;
    }
}
