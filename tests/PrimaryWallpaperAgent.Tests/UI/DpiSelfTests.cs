using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PrimaryWallpaperAgent.Tray;

internal static class DpiSelfTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "dpi-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "picture.png");
        using (var bitmap = new Bitmap(100, 60)) { using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Color.Teal); bitmap.Save(path, ImageFormat.Png); }
        var selected = SelectedImage.Read(path);
        TestReportedLayout(folder, selected, path);
        foreach (var language in new[] { "en", "th" })
        {
            var work = new Rectangle(0, 0, 3000, 2000);
            var records = new List<string>();
            using var form = new ConfigForm(selected, false, _ => "Running", () => path, _ => throw new Exception("No settings writes"),
                _ => { }, language, reportLayout: records.Add, workingArea: () => work);
            form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
            foreach (var dpi in new[] { 144, 192, 96, 120, 96 })
            {
                SetDpi(form, dpi);
                form.FitSettings("native-message-test"); Application.DoEvents();
                Console.WriteLine(records.Last());
                Assert(form.DeviceDpi == dpi, "Native DPI transition reached the fixture");
                AssertControls(form);
                if (dpi is 96 or 192)
                {
                    using var image = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(folder, "settings-" + language + "-" + dpi + ".png"), ImageFormat.Png);
                }
            }
            work = new Rectangle(0, 0, 800, 520);
            form.FitSettings("small-workarea"); Application.DoEvents();
            var footer = form.Controls.Find("SettingsFooter", true).Single();
            Assert(form.RectangleToScreen(form.ClientRectangle).Contains(footer.RectangleToScreen(footer.ClientRectangle)),
                "Footer stays visible even on a screen too short for all content");
            Assert(work.Contains(form.Bounds), "Small-screen bounds respect the working area");
            Assert(records.Any(r => r.Contains("dpi=192")) && records.Any(r => r.Contains("small-workarea")), "Layout telemetry records DPI and working area");
            form.Close();
        }
        Console.WriteLine("DPI TESTS PASSED: native WM_DPICHANGED 96/120/144/192 transitions, bilingual button text/footer fit and small-screen fallback. Real Remote retest remains required.");
        Console.WriteLine("Evidence: " + folder);
        return 0;
    }
    private static void TestReportedLayout(string folder, SelectedImage selected, string path)
    {
        var work = new Rectangle(0, 0, 1920, 1128);
        var entries = Enumerable.Range(0, 3).Select(i => new RecentImage(path, i == 0 ? selected.Sha256 : new string((char)('b' + i), 64),
            i == 0 ? "photo-" + new string('a', 64) + ".jpg" : "Picture " + i)).ToArray();
        foreach (var language in new[] { "en", "th" })
        {
            using var form = new ConfigForm(selected, false, _ => "Running · displays: 1", () => path,
                _ => throw new Exception("No settings writes"), _ => { }, language,
                recentImages: () => entries, readRecentImage: _ => selected, workingArea: () => work,
                reportLayout: Console.WriteLine);
            form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
            SetDpi(form, 144);
            form.FitSettings("reported-150-percent"); Application.DoEvents();
            using (var image = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                image.Save(Path.Combine(folder, "reported-" + language + ".png"), ImageFormat.Png);
            }
            var geometry = Descendants(form).Where(c => c.Visible).Select(c =>
                $"{c.Name} {c.GetType().Name} bounds={c.Bounds}; preferred={c.PreferredSize}; dpi={c.DeviceDpi}").ToArray();
            File.WriteAllLines(Path.Combine(folder, "geometry-" + language + ".txt"), geometry);
            Console.WriteLine("Reported-layout evidence: " + folder);
            var recent = form.Controls.Find("RecentImages", true).Single();
            var caption = form.Controls.Find("ImageFilename", true).Single();
            var viewport = (Panel)form.Controls.Find("SettingsViewport", true).Single();
            Assert(recent.Height <= 160 * 1.5, "Recent strip stays bounded with a long cached filename; actual=" + recent.Height);
            Assert(caption.Height <= caption.Font.Height + 12, "Filename remains one line");
            Assert(!viewport.VerticalScroll.Visible, "Reported working area fits without scrolling");
            AssertControls(form);
            var originalRows = ((TableLayoutPanel)recent).GetRowHeights();
            var startHeight = form.Height;
            var startWidth = form.Width;
            form.Width = form.MinimumSize.Width; Application.DoEvents();
            Assert(form.Width < startWidth, "User can shrink horizontally");
            Assert(((TableLayoutPanel)recent).GetRowHeights().SequenceEqual(originalRows), "Narrowing cannot stretch the caption row");
            form.Width = startWidth; Application.DoEvents();
            form.Height -= 150; form.FitSettings("resize"); Application.DoEvents();
            Assert(form.Height <= startHeight - 140, "User can shrink the window vertically");
            var footer = form.Controls.Find("SettingsFooter", true).Single();
            Assert(form.RectangleToScreen(form.ClientRectangle).Contains(footer.RectangleToScreen(footer.ClientRectangle)),
                "Footer stays visible after shrinking");
            form.Height = startHeight; form.FitSettings("resize"); Application.DoEvents();
            Assert(!viewport.VerticalScroll.Visible, "Expanding restores compact content without scrollbars");
            form.Height = form.MinimumSize.Height; Application.DoEvents();
            Assert(viewport.VerticalScroll.Visible && !viewport.HorizontalScroll.Visible, "Minimum height scrolls content vertically only");
            Assert(form.RectangleToScreen(form.ClientRectangle).Contains(footer.RectangleToScreen(footer.ClientRectangle)),
                "Footer stays visible at minimum height");
            viewport.AutoScrollPosition = new Point(0, 10000); Application.DoEvents();
            var startup = form.Controls.Find("StartupCheck", true).Single();
            Assert(viewport.RectangleToScreen(viewport.ClientRectangle).Contains(startup.RectangleToScreen(startup.ClientRectangle)),
                "Last preference remains reachable by scrolling");
            form.Height = startHeight; Application.DoEvents();
            Assert(!viewport.VerticalScroll.Visible, "Returning from minimum height clears the scrollbar");
            form.Height = form.MinimumSize.Height; form.FitSettings("resize"); Application.DoEvents();
            SetDpi(form, 192); form.FitSettings("user-sized-dpi"); Application.DoEvents();
            Assert(form.ClientSize.Height < 900 && viewport.VerticalScroll.Visible, "DPI changes preserve a manually reduced viewport instead of growing it to full content");
            SetDpi(form, 144); startHeight += 6; form.Height = startHeight; Application.DoEvents(); // Allow native DPI rounding of margins.
            Assert(!viewport.VerticalScroll.Visible, "Restoring the original size after DPI changes fits content again");
            form.WindowState = FormWindowState.Minimized; Application.DoEvents();
            Assert(form.WindowState == FormWindowState.Minimized, "Live sizing permits minimizing");
            form.WindowState = FormWindowState.Normal; Application.DoEvents();
            Assert(form.Height == startHeight && !viewport.VerticalScroll.Visible, "Restoring preserves the useful window size");
            form.Close();
        }
    }
    private static void SetDpi(Form form, int dpi)
    {
        if (form.DeviceDpi == dpi) return;
        var scale = dpi / (double)form.DeviceDpi;
        var rect = new Rect { Left = 0, Top = 0, Right = (int)(form.Width * scale), Bottom = (int)(form.Height * scale) };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Rect>());
        try
        {
            Marshal.StructureToPtr(rect, memory, false);
            SendMessageW(form.Handle, 0x02E0, (nuint)((dpi << 16) | dpi), memory);
            Application.DoEvents();
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static void AssertControls(Form form)
    {
        var client = form.RectangleToScreen(form.ClientRectangle);
        foreach (var control in Descendants(form).Where(c => c.Visible && c is Button or ComboBox or CheckBox))
        {
            Assert(client.Contains(control.RectangleToScreen(control.ClientRectangle)), "Control remains inside client: " + control.Name + "; dpi=" + form.DeviceDpi + "; bounds=" + control.RectangleToScreen(control.ClientRectangle) + "; form=" + client);
            if (control is ComboBox combo) Assert(combo.Width >= 160, "Selector width survives DPI round trips: " + combo.Name);
            if (control is Button button && !string.IsNullOrWhiteSpace(button.Text))
            {
                using var graphics = button.CreateGraphics();
                var text = TextRenderer.MeasureText(graphics, button.Text, button.Font, new Size(10000, 10000), TextFormatFlags.SingleLine);
                Assert(button.ClientSize.Height >= text.Height + button.Padding.Vertical, "Button text is not vertically clipped: " + button.Name + "; dpi=" + form.DeviceDpi + "; client=" + button.ClientSize + "; text=" + text + "; font=" + button.Font.Size + "; padding=" + button.Padding);
            }
        }
    }
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException("DPI TEST FAILED: " + message); }
}
