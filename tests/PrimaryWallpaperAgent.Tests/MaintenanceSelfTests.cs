using System.Drawing.Imaging;
using PrimaryWallpaperAgent.Tray;

namespace PrimaryWallpaperAgent.Tests;

internal static class MaintenanceSelfTests
{
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "maintenance-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        TestRetention(Path.Combine(folder, "retention"));
        TestAbout(Path.Combine(folder, "about"));
        Console.WriteLine("MAINTENANCE TESTS PASSED: managed-log age/size pruning, locked/unknown/current data protection, bounded rotation, About localization/actions and no settings changes.");
        return 0;
    }

    private static void TestRetention(string folder)
    {
        var now = DateTime.UtcNow;
        var runtime = Path.Combine(folder, "runtime");
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        File.WriteAllText(Path.Combine(folder, "settings.json"), "settings sentinel");
        File.WriteAllText(Path.Combine(folder, "assets", "photo.png"), "image sentinel");
        var emptyActiveFolder = Path.Combine(runtime, "20261020_090000_000-ffffffff");
        Directory.CreateDirectory(emptyActiveFolder);
        LogRetention.Prune(folder, Path.Combine(emptyActiveFolder, "agent.log"), now);
        Assert(Directory.Exists(emptyActiveFolder), "An active session is preserved before its first log write");
        var active = Create("20261020_100000_000-aaaaaaaa", "agent.log", 600, now.AddDays(-40));
        var old = Create("20260801_100000_000-bbbbbbbb", "agent.log", 100, now.AddDays(-40));
        var olderRecent = Create("20261018_100000_000-cccccccc", "before-1.json", 700, now.AddDays(-2));
        var newest = Create("20261019_100000_000-dddddddd", "config-1234abcd-settings-before", 700, now.AddDays(-1));
        var unknown = Create("20261019_100000_000-dddddddd", "user-notes.txt", 2000, now.AddDays(-60));
        var unrecognized = Create("unrecognized", "agent.log", 2000, now.AddDays(-60));
        var nested = Path.Combine(runtime, "20261019_100000_000-dddddddd", "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "agent.log"), "nested sentinel");
        var result = LogRetention.Prune(folder, active, now, maxBytes: 1500);
        Assert(result.DeletedFiles == 2 && result.WithinBudget && result.RemainingBytes == 1300, "Age and oldest-first budget apply together");
        Assert(!File.Exists(old) && !File.Exists(olderRecent) && File.Exists(newest), "Newest retained; old/over-budget data removed");
        Assert(File.Exists(active) && File.Exists(unknown) && File.Exists(unrecognized) && File.Exists(Path.Combine(nested, "agent.log")), "Current/unknown/nested files preserved");
        Assert(File.ReadAllText(Path.Combine(folder, "settings.json")) == "settings sentinel" &&
            File.ReadAllText(Path.Combine(folder, "assets", "photo.png")) == "image sentinel", "Settings and images untouched");
        var locked = Create("20260802_100000_000-eeeeeeee", "agent.log", 2000, now.AddDays(-60));
        using (var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = LogRetention.Prune(folder, active, now, maxBytes: 1500);
            Assert(File.Exists(locked) && !result.WithinBudget, "Locked files are skipped and incomplete cleanup is reported");
        }
        result = LogRetention.Prune(folder, active, now, maxBytes: 1500);
        Assert(!File.Exists(locked) && result.WithinBudget, "Later cleanup retries unlocked files");
        var rotating = Path.Combine(runtime, "startup-error.log");
        LogRetention.AppendBounded(rotating, new string('a', LogRetention.MaxLogBytes - 20));
        Assert(LogRetention.AppendBounded(rotating, new string('b', 100)), "Size limit rotates before append");
        Assert(new FileInfo(Path.Combine(runtime, "startup-error.previous.log")).Length <= LogRetention.MaxLogBytes &&
            File.ReadAllText(rotating) == new string('b', 100), "Rotation preserves one bounded predecessor");
        LogRetention.AppendBounded(rotating, new string('x', LogRetention.MaxLogBytes + 1));
        Assert(new FileInfo(rotating).Length <= LogRetention.MaxLogBytes, "A single oversized entry cannot bypass log limit");

        string Create(string session, string name, int length, DateTime time)
        {
            var path = Path.Combine(runtime, session, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, new string('x', length));
            File.SetLastWriteTimeUtc(path, time);
            return path;
        }
    }

    private static void TestAbout(string folder)
    {
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "fixture.png");
        using (var bitmap = new Bitmap(320, 180))
        { using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Teal); bitmap.Save(source, ImageFormat.Png); }
        var image = SelectedImage.Read(source);
        foreach (var language in new[] { "en", "th" })
        {
            var opened = new List<string>();
            var saves = 0;
            using var form = new ConfigForm(image, false, code => UiText.T("StatusRunning", code, 3),
                () => source, _ => saves++, _ => { }, language, dataRoot: folder, openAboutTarget: opened.Add);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            var link = Find<LinkLabel>(form, "AboutLink");
            Assert(link.Text.Contains(AboutForm.VersionText), "Config displays running version");
            var checkedAbout = false;
            Exception? failure = null;
            using var monitor = new System.Windows.Forms.Timer { Interval = 50 };
            monitor.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.OfType<AboutForm>().SingleOrDefault();
                if (dialog is null) return;
                monitor.Stop();
                try
                {
                    Assert(dialog.Text == UiText.Title("AboutTitle", language), "Localized About title");
                    Assert(Find<Label>(dialog, "AboutVersion").Text.EndsWith(AboutForm.VersionText), "Actual assembly version");
                    Assert(Find<TextBox>(dialog, "AboutDataPath").Text == folder, "Shows the actual data folder");
                    Find<Button>(dialog, "ReleasesButton").PerformClick();
                    Find<Button>(dialog, "OpenDataButton").PerformClick();
                    Assert(opened.SequenceEqual(new[] { AboutForm.ReleasesUrl, folder }), "Actions use only the intended release URL/data root");
                    var client = dialog.RectangleToScreen(dialog.ClientRectangle);
                    foreach (var button in Descendants(dialog).OfType<Button>())
                        Assert(client.Contains(button.RectangleToScreen(button.ClientRectangle)), "About buttons fit");
                    using var screenshot = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(screenshot, new Rectangle(Point.Empty, dialog.Size));
                    screenshot.Save(Path.Combine(folder, "about-" + language + ".png"), ImageFormat.Png);
                    checkedAbout = true;
                }
                catch (Exception error) { failure = error; }
                finally { dialog.Close(); }
            };
            monitor.Start();
            // Invoke the same public LinkLabel accessibility action as a user click.
            (link.AccessibilityObject.GetChild(0) ?? throw new InvalidOperationException("About accessible link missing")).DoDefaultAction();
            if (failure is not null) throw failure;
            Assert(checkedAbout && saves == 0 && form.Visible, "About closes back to Settings without changing preferences");
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(folder, "settings-" + language + ".png"), ImageFormat.Png);
            form.Close();
        }
    }

    internal static int CheckJunction(string folder)
    {
        var target = Path.Combine(folder, "external", "agent.log");
        Assert(File.Exists(target), "Junction sentinel prepared");
        var result = LogRetention.Prune(folder, null, DateTime.UtcNow.AddDays(60), maxBytes: 0);
        Assert(File.ReadAllText(target) == "outside runtime sentinel" && result.DeletedFiles == 1, "Reparse target preserved while regular managed file is pruned");
        Console.WriteLine("RETENTION JUNCTION TEST PASSED: no link traversal.");
        return 0;
    }

    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("MAINTENANCE TEST FAILED: " + message); }
}
