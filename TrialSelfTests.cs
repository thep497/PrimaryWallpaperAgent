namespace PrimaryWallpaperAgent;

internal static class TrialSelfTests
{
    internal static int Run()
    {
        foreach (var count in new[] { 1, 3, 5 })
        {
            var desktop = new FakeDesktop(count);
            var plan = Plan(desktop.State);
            TrialEngine.Apply(plan, desktop, _ => { });
            Assert(TrialEngine.Matches(plan, desktop.State, false), $"Apply with {count} displays");
            TrialEngine.Restore(plan, desktop);
            Assert(TrialEngine.Matches(plan, desktop.State, true), $"Restore with {count} displays");
        }
        var partialFailure = new FakeDesktop(3) { FailOnCall = 2 };
        var partialPlan = Plan(partialFailure.State);
        MustThrow(() => TrialEngine.Apply(partialPlan, partialFailure, _ => { }));
        Assert(TrialEngine.Matches(partialPlan, partialFailure.State, true), "Rollback after a setter failure");

        var failedRestore = new FakeDesktop(3) { FailFromCall = 2 };
        var failedRestoreReported = false;
        try { TrialEngine.Apply(Plan(failedRestore.State), failedRestore, _ => { }); }
        catch (AggregateException) { failedRestoreReported = true; }
        Assert(failedRestoreReported, "A failed rollback must be reported rather than claimed successful");

        var ignoresMonitor = new FakeDesktop(3) { IgnoresMonitorId = true };
        var ignoredPlan = Plan(ignoresMonitor.State);
        MustThrow(() => TrialEngine.Apply(ignoredPlan, ignoresMonitor, _ => { }));
        Assert(TrialEngine.Matches(ignoredPlan, ignoresMonitor.State, true), "Detect global side effect and restore");

        var changedBefore = new FakeDesktop(3);
        var beforePlan = Plan(changedBefore.State);
        changedBefore.SwitchPrimary();
        MustThrow(() => TrialEngine.Apply(beforePlan, changedBefore, _ => { }));
        Assert(changedBefore.Calls == 0, "Changed primary rejected before any setter");

        var changedDuring = new FakeDesktop(3) { SwitchAfterFirstCall = true };
        var duringPlan = Plan(changedDuring.State);
        MustThrow(() => TrialEngine.Apply(duringPlan, changedDuring, _ => { }));
        Assert(TrialEngine.Matches(duringPlan, changedDuring.State, true), "Restore by monitor ID after primary changes mid-apply");

        var invalid = new FakeDesktop(3);
        var invalidPlan = Plan(invalid.State);
        invalid.State = invalid.State with { TopologyStable = false };
        MustThrow(() => TrialEngine.Apply(invalidPlan, invalid, _ => { }));
        Assert(invalid.Calls == 0, "Unstable topology cannot reach a setter");

        var changedImage = new FakeDesktop(3);
        var imagePlan = Plan(changedImage.State);
        changedImage.State = changedImage.State with { Wallpapers = changedImage.State.Wallpapers.Select(w => w with { WallpaperPath = "user-new.jpg" }).ToArray() };
        MustThrow(() => TrialEngine.Apply(imagePlan, changedImage, _ => { }));
        Assert(changedImage.Calls == 0, "User wallpaper change after preparation is preserved");

        var guardDesktop = new FakeDesktop(3);
        var guardPlan = Plan(guardDesktop.State);
        Assert(WallpaperGuard.RepairIfNeeded(guardPlan, guardDesktop.State, guardDesktop, _ => { }), "Guard repairs initial drift");
        var writesAfterRepair = guardDesktop.Calls;
        Assert(!WallpaperGuard.RepairIfNeeded(guardPlan, guardDesktop.State, guardDesktop, _ => { }) && guardDesktop.Calls == writesAfterRepair,
            "Guard must not write matching wallpapers repeatedly");
        guardDesktop.State = guardDesktop.State with { Wallpapers = guardDesktop.State.Wallpapers.Select(w => w with { WallpaperPath = "original.jpg" }).ToArray() };
        Assert(WallpaperGuard.RepairIfNeeded(guardPlan, guardDesktop.State, guardDesktop, _ => { }), "Guard repairs a desktop-switch reset");
        guardDesktop.SwitchPrimary();
        Assert(WallpaperGuard.RepairIfNeeded(guardPlan, guardDesktop.State, guardDesktop, _ => { }), "Guard follows a changed primary");
        Assert(TrialEngine.Matches(guardPlan with { Before = guardDesktop.State }, guardDesktop.State, false), "Guard uses the new primary rather than old index");
        var notebookOnly = new FakeDesktop(1);
        Assert(WallpaperGuard.RepairIfNeeded(guardPlan, notebookOnly.State, notebookOnly, _ => { }), "Guard accepts a changed display count");
        Assert(notebookOnly.State.Wallpapers[0].WallpaperPath == "photo.jpg", "Single display receives photograph");
        var unknownImage = new FakeDesktop(3);
        unknownImage.State = unknownImage.State with { Wallpapers = unknownImage.State.Wallpapers.Select(w => w with { WallpaperPath = "user-choice.jpg" }).ToArray() };
        MustThrow(() => WallpaperGuard.RepairIfNeeded(guardPlan, unknownImage.State, unknownImage, _ => { }));
        Assert(unknownImage.Calls == 0, "Guard preserves unknown user-selected images");
        TestTrayPolicy();
        Console.WriteLine("TRIAL SELF-TESTS PASSED: 1/3/5 displays, restore, partial failure, failed rollback reporting, ignored monitor ID, primary changes before/during apply, unstable topology, changed wallpaper.");
        Console.WriteLine("Tests used an in-memory desktop; no Windows settings were changed.");
        Console.WriteLine("GUARD SELF-TESTS PASSED: drift, no redundant writes, desktop reset, changed primary, display count, unknown-image stop.");
        return 0;
    }

    private static void TestTrayPolicy()
    {
        var images = new PolicyImages("pack-photo.jpg", "unused", "pack-black.bmp", "unused",
            ["original.jpg", "photo.jpg", "black.bmp"]);
        foreach (var count in new[] { 1, 3, 5 })
        {
            var desktop = new FakeDesktop(count);
            // A new logon session must not depend on the earlier trial session.
            desktop.State = desktop.State with { SessionId = 22 };
            Assert(WallpaperPolicy.Repair(images, desktop.State, desktop, _ => { }), "Tray accepts fresh topology/session");
            Assert(desktop.State.Wallpapers.All(w => w.WallpaperPath ==
                (w.IsPrimary == true ? images.PhotoPath : images.BlackPath)), "Tray targets only current primary");
            var writes = desktop.Calls;
            Assert(!WallpaperPolicy.Repair(images, desktop.State, desktop, _ => { }) && writes == desktop.Calls,
                "Tray does not repeat writes on matching state");
        }
        var legacy = new FakeDesktop(3);
        legacy.State = legacy.State with { Wallpapers = legacy.State.Wallpapers.Select(w => w with
            { WallpaperPath = w.IsPrimary == true ? "photo.jpg" : "black.bmp" }).ToArray() };
        Assert(WallpaperPolicy.Repair(images, legacy.State, legacy, _ => { }), "Accept known trial assets during migration");
        legacy.SwitchPrimary();
        Assert(WallpaperPolicy.Repair(images, legacy.State, legacy, _ => { }), "Tray follows changed primary");
        var selected = new FakeDesktop(3);
        selected.State = selected.State with { Wallpapers = selected.State.Wallpapers.Select(w => w with
            { WallpaperPath = "user-chosen.jpg" }).ToArray() };
        MustThrow(() => WallpaperPolicy.Repair(images, selected.State, selected, _ => { }));
        Assert(selected.Calls == 0, "Tray preserves unfamiliar selected image");
        var mode = new FakeDesktop(3);
        mode.State = mode.State with { Position = 5 };
        MustThrow(() => WallpaperPolicy.Repair(images, mode.State, mode, _ => { }));
        Assert(mode.Calls == 0, "Tray preserves changed wallpaper mode");
        var failure = new FakeDesktop(3) { FailOnCall = 2 };
        var original = Plan(failure.State);
        MustThrow(() => WallpaperPolicy.Repair(images, failure.State, failure, _ => { }));
        Assert(TrialEngine.Matches(original, failure.State, true), "Tray rolls back a partial setter failure");
        Console.WriteLine("TRAY POLICY TESTS PASSED: 1/3/5 displays, fresh session, legacy paths, primary change, no repeat writes, user image/mode, rollback.");
    }

    private static TrialPlan Plan(ProbeResult before) => new(1, "test", "original.jpg", "photo.jpg", "unused", "black.bmp", "unused", before);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }
    private static void MustThrow(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("TEST FAILED: expected the operation to reject unsafe state.");
    }

    private sealed class FakeDesktop : ITrialDesktop
    {
        internal ProbeResult State;
        internal int Calls;
        internal int FailOnCall;
        internal int FailFromCall;
        internal bool IgnoresMonitorId;
        internal bool SwitchAfterFirstCall;
        internal FakeDesktop(int count)
        {
            var displays = Enumerable.Range(0, count).Select(i => new DisplayInfo($"DISPLAY{i}", i == 0,
                new(i * 100, 0, (i + 1) * 100, 100), [new($"Monitor{i}", "test", $"id-{i}", 1)])).ToArray();
            var wallpapers = displays.Select((d, i) => new WallpaperInfo((uint)i, $"id-{i}", 0, d.Bounds,
                0, "original.jpg", "ExactDevicePath", d.GdiName, d.IsPrimary, true)).ToArray();
            State = new(DateTimeOffset.UtcNow, "test", "test", 1, true, true, "#000000", 4, 1, displays, wallpapers, []);
        }
        public ProbeResult Capture() => State;
        public void SetWallpaper(string monitorId, string path)
        {
            Calls++;
            if (Calls == FailOnCall || (FailFromCall > 0 && Calls >= FailFromCall)) throw new InvalidOperationException("Injected setter failure.");
            State = State with { Wallpapers = State.Wallpapers.Select(w => IgnoresMonitorId || w.MonitorId == monitorId ? w with { WallpaperPath = path } : w).ToArray() };
            if (Calls == 1 && SwitchAfterFirstCall) SwitchPrimary();
        }
        internal void SwitchPrimary()
        {
            State = State with
            {
                Displays = State.Displays.Select((d, i) => d with { IsPrimary = i == 1 }).ToArray(),
                Wallpapers = State.Wallpapers.Select((w, i) => w with { IsPrimary = i == 1 }).ToArray()
            };
        }
    }
}
