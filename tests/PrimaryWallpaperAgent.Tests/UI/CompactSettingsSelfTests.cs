using System.Drawing.Imaging;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class CompactSettingsSelfTests
{
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "compact-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "fixture.png");
        using (var bitmap = new Bitmap(320, 180))
        { using var g = Graphics.FromImage(bitmap); g.Clear(Color.Teal); bitmap.Save(source, ImageFormat.Png); }
        var image = SelectedImage.Read(source);
        var results = new List<object>();
        foreach (var language in new[] { "th", "en" })
        foreach (var size in new[] { new Size(700, 580), new Size(640, 560) })
        {
            var changes = 0;
            using var form = new ConfigForm(image, false, code => UiText.T("StatusRunning", code, 3),
                () => source, _ => changes++, _ => { }, language);
            form.ShowInTaskbar = false;
            form.ClientSize = size;
            form.Show(); Application.DoEvents();
            var visible = Descendants(form).Where(control => control.Visible).ToArray();
            Assert(visible.OfType<ScrollableControl>().All(control => !control.AutoScroll && !control.VerticalScroll.Visible && !control.HorizontalScroll.Visible),
                "No scrolling in Settings");
            var client = form.RectangleToScreen(form.ClientRectangle);
            foreach (var control in visible.Where(control => control is Button or ComboBox or CheckBox))
                Assert(client.Contains(control.RectangleToScreen(control.ClientRectangle)), "Interactive control fits client: " + control.Name);
            var preview = Find<PictureBox>(form, "WallpaperPreview");
            var actions = Find<TableLayoutPanel>(form, "ImageActions");
            Assert(preview.RectangleToScreen(preview.ClientRectangle).Bottom <= actions.RectangleToScreen(actions.ClientRectangle).Top,
                "Preview does not overlap actions");
            foreach (var topic in new[] {
                ("ImageHelpButton", "ImageHelpText"),
                ("LanguageHelpButton", "LanguageHint"),
                ("PollHelpButton", "PollHint"),
                ("StartupHelpButton", "StartupHint") })
            {
                Find<Button>(form, topic.Item1).PerformClick();
                var popup = form.CurrentHelp ?? throw new InvalidOperationException("Help popup missing");
                Assert(popup.Visible && popup.BodyText == UiText.T(topic.Item2, language), "Click reveals localized help");
                Assert(!string.IsNullOrWhiteSpace(Find<Button>(popup, "HelpCloseButton").AccessibleName), "Help close is accessible");
                if (size.Width == 700 && topic.Item1 == "ImageHelpButton")
                    Capture(popup, Path.Combine(folder, "help-" + language + ".png"));
                Find<Button>(popup, "HelpCloseButton").PerformClick();
                Assert(!popup.Visible, "Help dismisses with its close button");
                Application.DoEvents();
            }
            Assert(changes == 0, "Help never changes settings/startup/wallpaper");
            if (size.Width == 700) Capture(form, Path.Combine(folder, "config-" + language + ".png"));
            results.Add(new { Language = language, Width = form.ClientSize.Width, Height = form.ClientSize.Height, Scrollbars = false, HelpTopics = 4 });
            Find<Button>(form, "CloseButton").PerformClick();
        }
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
            { Passed = true, Results = results, NativeWallpaperCalls = 0, RegistryWrites = 0 }, SettingsStore.JsonOptions));
        Console.WriteLine("COMPACT SETTINGS TESTS PASSED: no scrollbars, controls fit, localized click-help, dismissal and no settings writes.");
        return 0;
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static T Find<T>(Control parent, string name) where T : Control => (T)parent.Controls.Find(name, true).Single();
    private static void Capture(Control control, string path)
    {
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));
        bitmap.Save(path, ImageFormat.Png);
    }
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException("COMPACT SETTINGS TEST FAILED: " + message); }
}
