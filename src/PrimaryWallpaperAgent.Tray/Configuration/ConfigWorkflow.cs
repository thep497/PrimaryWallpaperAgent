using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class ConfigWorkflow
{
    internal static void Apply(TrayConfig candidate, PolicyImages images, byte[] settingsAtOpen,
        string? startupAtOpen, ISettingsPersistence settings, IStartupRegistration startup,
        ITrialDesktop desktop, Action<string> log, Action<string, object> saveEvidence, bool applyWallpaper = true)
    {
        if (!settings.Read().SequenceEqual(settingsAtOpen)) throw new UiMessageException("SettingsChanged");
        var desiredStartup = candidate.StartWithWindows ? startup.ExpectedCommand : null;
        var changeStartup = !StringComparer.Ordinal.Equals(desiredStartup, startupAtOpen);
        if (changeStartup && !StringComparer.Ordinal.Equals(startup.Read(), startupAtOpen))
            throw new UiMessageException("StartupChanged");

        TrialPlan? plan = null;
        if (applyWallpaper)
        {
            ProbeResult before;
            try { before = desktop.Capture(); _ = TrialEngine.Active(before); }
            catch (Exception error) { throw new UiMessageException("DisplayReadFailed", error); }
            if (before.Position != 4 || before.SlideshowStatus != 1) throw new UiMessageException("PictureFillRequired");
            plan = new(1, "config", images.PhotoPath, images.PhotoPath, images.PhotoSha256, images.BlackPath, images.BlackSha256, before);
        }
        saveEvidence("settings-before", new { Config = SettingsStore.Parse(settingsAtOpen), OriginalBytesBase64 = Convert.ToBase64String(settingsAtOpen) });
        if (plan is not null) saveEvidence("plan-before", plan);
        var wallpaperApplied = false;
        var settingsAttempted = false;
        var startupAttempted = false;
        try
        {
            if (plan is not null && !TrialEngine.Matches(plan, plan.Before, restored: false))
            { TrialEngine.Apply(plan, desktop, log); wallpaperApplied = true; }
            settingsAttempted = true;
            var candidateBytes = JsonSerializer.SerializeToUtf8Bytes(candidate, SettingsStore.JsonOptions);
            settings.Write(candidateBytes);
            if (!settings.Read().SequenceEqual(candidateBytes)) throw new UiMessageException("SettingsReadbackFailed");
            if (changeStartup) { startupAttempted = true; startup.Write(desiredStartup); }
            if (changeStartup && !StringComparer.Ordinal.Equals(startup.Read(), desiredStartup)) throw new UiMessageException("StartupReadbackFailed");
            if (plan is not null) saveEvidence("state-after", desktop.Capture());
            log((plan is null ? "CONFIG_PREFERENCES_SAVED; wallpaperTouched=False" : "CONFIG_APPLIED; wallpaper readback verified") + "; startupChanged=" + changeStartup);
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            if (startupAttempted) TryRestore(() => startup.Write(startupAtOpen), errors);
            if (settingsAttempted) TryRestore(() => settings.Write(settingsAtOpen), errors);
            if (wallpaperApplied) TryRestore(() => TrialEngine.Restore(plan!, desktop), errors);
            if (errors.Count > 1 || failure is AggregateException)
                throw new AggregateException(UiText.T("RestoreIncomplete", "en"), errors);
            throw new UiMessageException("SaveRestored", failure);
        }
    }
    private static void TryRestore(Action restore, List<Exception> errors)
    { try { restore(); } catch (Exception error) { errors.Add(error); } }
}