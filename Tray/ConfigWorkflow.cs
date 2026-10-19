using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class ConfigWorkflow
{
    // Only Save and Apply uses this method. Preview/cancel/resume never do.
    internal static void Apply(TrayConfig candidate, PolicyImages images, byte[] settingsAtOpen,
        string? startupAtOpen, ISettingsPersistence settings, IStartupRegistration startup,
        ITrialDesktop desktop, Action<string> log, Action<string, object> saveEvidence)
    {
        if (!settings.Read().SequenceEqual(settingsAtOpen))
            throw new InvalidOperationException("Settings changed while Config was open. Close and reopen the app before saving.");
        var desiredStartup = candidate.StartWithWindows ? startup.ExpectedCommand : null;
        var changeStartup = !StringComparer.Ordinal.Equals(desiredStartup, startupAtOpen);
        if (changeStartup && !StringComparer.Ordinal.Equals(startup.Read(), startupAtOpen))
            throw new InvalidOperationException("Startup settings changed while Config was open. Reopen Config before saving.");
        var before = desktop.Capture();
        _ = TrialEngine.Active(before);
        if (before.Position != 4 || before.SlideshowStatus != 1)
            throw new InvalidOperationException("ตั้ง Windows Background เป็น Picture และ Fill ก่อนค่ะ รุ่นนี้ยังไม่รองรับ Spotlight/Slideshow");
        var plan = new TrialPlan(1, "config", images.PhotoPath, images.PhotoPath, images.PhotoSha256,
            images.BlackPath, images.BlackSha256, before);
        saveEvidence("settings-before", new { Config = SettingsStore.Parse(settingsAtOpen), OriginalBytesBase64 = Convert.ToBase64String(settingsAtOpen) });
        saveEvidence("plan-before", plan);
        var wallpaperApplied = false;
        var settingsAttempted = false;
        var startupAttempted = false;
        try
        {
            // Save and Apply is the user's explicit choice to replace current images.
            if (!TrialEngine.Matches(plan, before, restored: false))
            { TrialEngine.Apply(plan, desktop, log); wallpaperApplied = true; }
            settingsAttempted = true;
            var candidateBytes = JsonSerializer.SerializeToUtf8Bytes(candidate, SettingsStore.JsonOptions);
            settings.Write(candidateBytes);
            if (!settings.Read().SequenceEqual(candidateBytes))
                throw new InvalidOperationException("Settings readback failed.");
            if (changeStartup) { startupAttempted = true; startup.Write(desiredStartup); }
            if (changeStartup && !StringComparer.Ordinal.Equals(startup.Read(), desiredStartup))
                throw new InvalidOperationException("Startup registration readback failed.");
            saveEvidence("state-after", desktop.Capture());
            log("CONFIG_APPLIED; wallpaper readback verified; startupChanged=" + changeStartup);
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            if (startupAttempted) TryRestore(() => startup.Write(startupAtOpen), errors);
            if (settingsAttempted) TryRestore(() => settings.Write(settingsAtOpen), errors);
            if (wallpaperApplied) TryRestore(() => TrialEngine.Restore(plan, desktop), errors);
            if (errors.Count > 1 || failure is AggregateException)
                throw new AggregateException("บันทึกไม่สำเร็จและคืนค่าบางส่วนไม่ได้ โปรแกรมจะพัก กรุณาตรวจบันทึกค่ะ", errors);
            throw new InvalidOperationException("บันทึกไม่สำเร็จ ค่าที่เปลี่ยนในขั้นนี้ถูกคืนแล้วค่ะ", failure);
        }
    }
    private static void TryRestore(Action restore, List<Exception> errors)
    { try { restore(); } catch (Exception error) { errors.Add(error); } }
}
