using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

// Successful edits update one live state; role-specific Undo preserves the other
// role and all preferences, including changes committed after this window opened.
internal sealed class LiveSettings(SettingsStore store, TrayConfig config, byte[] bytes,
    IStartupRegistration startup, ITrialDesktop desktop, Action<string> log, Action<string, object> evidence,
    ISettingsPersistence? persistenceOverride = null)
{
    internal TrayConfig Config { get; private set; } = config;
    internal PolicyImages Images { get; private set; } = store.Images(config);
    internal bool IsApplying { get; private set; }
    internal bool LastChangeMayResume { get; private set; }
    internal bool CanUndoImage => CanUndo(WallpaperTarget.Primary);
    internal bool CanUndo(WallpaperTarget target) => previousImages.ContainsKey(target);
    private readonly ISettingsPersistence persistence = persistenceOverride ?? store;
    private readonly Dictionary<WallpaperTarget, TrayConfig> previousImages = [];
    private byte[] currentBytes = bytes;
    private string? expectedStartup;

    internal bool BeginSession()
    {
        EnsureUnchanged(); expectedStartup = startup.Read(); return expectedStartup is not null;
    }
    internal SelectedImage CurrentImage() => CurrentImage(WallpaperTarget.Primary);
    internal SelectedImage CurrentImage(WallpaperTarget target) => ReadImage(Config, target);
    internal RecentImage[] RecentImages() => RecentImages(WallpaperTarget.Primary);
    internal RecentImage[] RecentImages(WallpaperTarget target) => RecentImageHistory.List(Config, target);
    internal SelectedImage ReadRecentImage(RecentImage image)
    {
        if (!RecentImages(WallpaperTarget.Primary).Concat(RecentImages(WallpaperTarget.Secondary)).Contains(image))
            throw new UiMessageException("RecentImageUnavailable");
        return RecentImageHistory.Read(store, image);
    }
    internal void Apply(ConfigSubmission request)
    {
        if (IsApplying) throw new UiMessageException("SettingsBusy");
        if (!Enum.IsDefined(request.Target) || (request.UseBlack && request.Target != WallpaperTarget.Secondary))
            throw new UiMessageException("InvalidWallpaperTarget");
        IsApplying = true;
        try
        {
            EnsureUnchanged();
            var before = Config;
            var candidate = request.ApplyWallpaper ? store.Prepare(Config, request.Image, request.StartWithWindows, request.Target, request.UseBlack)
                : Config with { StartWithWindows = request.StartWithWindows };
            candidate = candidate with { Language = UiText.Normalize(request.Language), PollIntervalMs = PollingOptions.Validate(request.PollIntervalMs),
                Theme = ThemeOptions.Validate(request.Theme ?? Config.Theme) };
            Commit(candidate, request.ApplyWallpaper && candidate.HasChosenImage, request.Target);
            if (request.ApplyWallpaper && (request.Target == WallpaperTarget.Secondary || before.HasChosenImage) &&
                ImageKey(before, request.Target) != ImageKey(candidate, request.Target)) previousImages[request.Target] = before;
        }
        finally { IsApplying = false; }
    }
    // Import only an observed current-primary image. This transaction neither
    // writes wallpaper nor reads/writes startup registration.
    internal void AdoptWindowsPrimary(SelectedImage image, ProbeResult observed)
    {
        if (IsApplying) throw new UiMessageException("SettingsBusy");
        IsApplying = true;
        try
        {
            EnsureUnchanged();
            var before = Config;
            var candidate = store.Prepare(Config, image, Config.StartWithWindows);
            var candidateImages = store.Images(candidate);
            var fresh = desktop.Capture();
            _ = TrialEngine.Active(fresh);
            if (fresh.Position != 4 || fresh.SlideshowStatus != 1 ||
                fresh.SessionId != observed.SessionId || WallpaperGuard.Fingerprint(fresh) != WallpaperGuard.Fingerprint(observed))
                throw new InvalidOperationException("Display or wallpaper changed before primary adoption; retrying.");
            // Recheck both the file contents and settings immediately before commit.
            if (SelectedImage.Read(image.SourcePath).Sha256 != image.Sha256)
                throw new IOException("Primary image changed before adoption; retrying.");
            EnsureUnchanged();
            var encoded = JsonSerializer.SerializeToUtf8Bytes(candidate, SettingsStore.JsonOptions);
            try
            {
                persistence.Write(encoded);
                if (!persistence.Read().SequenceEqual(encoded)) throw new UiMessageException("SettingsReadbackFailed");
            }
            catch (Exception failure)
            {
                try
                {
                    persistence.Write(currentBytes);
                    if (!persistence.Read().SequenceEqual(currentBytes)) throw new IOException("Primary adoption rollback readback failed.");
                }
                catch (Exception restore) { throw new AggregateException("Primary adoption settings recovery is incomplete.", failure, restore); }
                throw;
            }
            previousImages[WallpaperTarget.Primary] = before;
            Config = candidate; Images = candidateImages; currentBytes = encoded;
            LastChangeMayResume = false;
            log("WINDOWS_PRIMARY_ADOPTED; sha256=" + image.Sha256 + "; secondary and preferences preserved; startupTouched=False");
        }
        finally { IsApplying = false; }
    }
    internal SelectedImage UndoImage() => UndoImage(WallpaperTarget.Primary);
    internal SelectedImage UndoImage(WallpaperTarget target)
    {
        if (IsApplying) throw new UiMessageException("SettingsBusy");
        if (!previousImages.TryGetValue(target, out var previous)) throw new UiMessageException("NoPreviousImage");
        IsApplying = true;
        try
        {
            EnsureUnchanged();
            var candidate = target == WallpaperTarget.Primary ? Config with
            {
                SchemaVersion = 4, PhotoFile = previous.PhotoFile, PhotoSha256 = previous.PhotoSha256, PhotoLabel = previous.PhotoLabel,
                CurrentSourcePath = previous.SchemaVersion >= 3 ? previous.CurrentSourcePath : null, AcceptedPaths = []
            } : Config with { SchemaVersion = 4, SecondaryImage = previous.SecondaryImage };
            candidate = RecentImageHistory.Remember(Config, candidate, target);
            var image = ReadImage(candidate, target);
            Commit(candidate, candidate.HasChosenImage, target);
            previousImages.Remove(target);
            return image;
        }
        finally { IsApplying = false; }
    }
    private static string ImageKey(TrayConfig config, WallpaperTarget target) =>
        target == WallpaperTarget.Primary ? config.PhotoSha256.ToUpperInvariant() : config.SecondaryImage?.Sha256.ToUpperInvariant() ?? "black";
    private void Commit(TrayConfig candidate, bool applyWallpaper, WallpaperTarget target)
    {
        var candidateImages = applyWallpaper ? store.Images(candidate) : Images;
        // Preconfigured secondary images must also become the next watcher's policy
        // even while first-run stays inactive. Validation reads only owned assets.
        if (!applyWallpaper && candidate.SecondaryImage != Config.SecondaryImage) candidateImages = store.Images(candidate);
        var operationId = "config-" + Guid.NewGuid().ToString("N")[..8];
        var mayResume = ConfigWorkflow.Apply(candidate, candidateImages, currentBytes, expectedStartup, persistence, startup, desktop,
            log, (name, value) => evidence(operationId + "-" + name, value), applyWallpaper, target, Images);
        Config = candidate; Images = candidateImages; LastChangeMayResume = mayResume;
        currentBytes = JsonSerializer.SerializeToUtf8Bytes(candidate, SettingsStore.JsonOptions);
        expectedStartup = candidate.StartWithWindows ? startup.ExpectedCommand : null;
    }
    private void EnsureUnchanged()
    { if (!persistence.Read().SequenceEqual(currentBytes)) throw new UiMessageException("SettingsChanged"); }
    private SelectedImage ReadImage(TrayConfig value, WallpaperTarget target)
    {
        var secondary = target == WallpaperTarget.Secondary ? value.SecondaryImage : null;
        var relative = target == WallpaperTarget.Primary ? value.PhotoFile : secondary?.PhotoFile ?? value.BlackFile;
        var hash = target == WallpaperTarget.Primary ? value.PhotoSha256 : secondary?.Sha256 ?? value.BlackSha256;
        var label = target == WallpaperTarget.Primary ? value.PhotoLabel : secondary?.Label;
        var image = SelectedImage.Read(store.Asset(relative, hash));
        return label is null ? image : image with { FriendlyName = label };
    }
}
