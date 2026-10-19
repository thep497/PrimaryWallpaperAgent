using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

// One live state for all edits in the Settings window; no stale "at open" snapshot after a successful edit.
internal sealed class LiveSettings(SettingsStore store, TrayConfig config, byte[] bytes,
    IStartupRegistration startup, ITrialDesktop desktop, Action<string> log, Action<string, object> evidence,
    ISettingsPersistence? persistenceOverride = null)
{
    internal TrayConfig Config { get; private set; } = config;
    internal PolicyImages Images { get; private set; } = store.Images(config);
    internal bool IsApplying { get; private set; }
    internal bool CanUndoImage => previousImage is not null;
    private readonly ISettingsPersistence persistence = persistenceOverride ?? store;
    private byte[] currentBytes = bytes;
    private string? expectedStartup;
    private TrayConfig? previousImage;

    internal bool BeginSession()
    {
        EnsureUnchanged();
        expectedStartup = startup.Read();
        return expectedStartup is not null;
    }

    internal SelectedImage CurrentImage() => ReadImage(Config);

    internal void Apply(ConfigSubmission request)
    {
        if (IsApplying) throw new UiMessageException("SettingsBusy");
        IsApplying = true;
        try
        {
            EnsureUnchanged();
            var before = Config;
            var candidate = request.ApplyWallpaper ? store.Prepare(Config, request.Image, request.StartWithWindows) :
                Config with { StartWithWindows = request.StartWithWindows };
            candidate = candidate with { Language = UiText.Normalize(request.Language), PollIntervalMs = PollingOptions.Validate(request.PollIntervalMs) };
            Commit(candidate, request.ApplyWallpaper);
            if (request.ApplyWallpaper && before.HasChosenImage && !StringComparer.OrdinalIgnoreCase.Equals(before.PhotoSha256, candidate.PhotoSha256))
                previousImage = before;
        }
        finally { IsApplying = false; }
    }

    internal SelectedImage UndoImage()
    {
        if (IsApplying) throw new UiMessageException("SettingsBusy");
        var previous = previousImage ?? throw new UiMessageException("NoPreviousImage");
        IsApplying = true;
        try
        {
            EnsureUnchanged();
            // Undo only the image. Keep the current language, polling and startup preferences.
            var candidate = Config with
            {
                PhotoFile = previous.PhotoFile, PhotoSha256 = previous.PhotoSha256, PhotoLabel = previous.PhotoLabel,
                CurrentSourcePath = previous.SchemaVersion >= 3 ? previous.CurrentSourcePath : null, AcceptedPaths = []
            };
            var image = ReadImage(candidate); // Validate the cached copy before changing Windows.
            Commit(candidate, applyWallpaper: true);
            previousImage = null; // One undo, available across window reopen until app exit.
            return image;
        }
        finally { IsApplying = false; }
    }

    private void Commit(TrayConfig candidate, bool applyWallpaper)
    {
        var candidateImages = applyWallpaper ? store.Images(candidate) : Images;
        var operationId = "config-" + Guid.NewGuid().ToString("N")[..8];
        ConfigWorkflow.Apply(candidate, candidateImages, currentBytes, expectedStartup, persistence, startup, desktop,
            log, (name, value) => evidence(operationId + "-" + name, value), applyWallpaper);
        Config = candidate;
        Images = candidateImages;
        currentBytes = JsonSerializer.SerializeToUtf8Bytes(candidate, SettingsStore.JsonOptions);
        expectedStartup = candidate.StartWithWindows ? startup.ExpectedCommand : null;
    }

    private void EnsureUnchanged()
    {
        if (!persistence.Read().SequenceEqual(currentBytes)) throw new UiMessageException("SettingsChanged");
    }

    private SelectedImage ReadImage(TrayConfig value)
    {
        var image = SelectedImage.Read(store.Asset(value.PhotoFile, value.PhotoSha256));
        return value.PhotoLabel is null ? image : image with { FriendlyName = value.PhotoLabel };
    }
}
