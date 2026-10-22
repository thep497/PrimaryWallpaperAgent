using System.Drawing.Drawing2D;

namespace PrimaryWallpaperAgent.Tray;

internal sealed class RecentImageStrip : TableLayoutPanel
{
    private readonly Label heading = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 5) };
    private readonly Label filename = new() { Name = "ImageFilename", AutoSize = false, AutoEllipsis = true,
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 3, 4, 0) };
    private readonly Label empty = new() { Name = "RecentImagesEmpty", AutoSize = true, Dock = DockStyle.Fill,
        Margin = new Padding(0, 12, 0, 12), MaximumSize = new Size(560, 0) };
    private readonly Button[] buttons = new Button[RecentImageHistory.Limit];
    private readonly ToolTip tips = new();
    private RecentImage[] entries = [];
    private string language = "en";
    private string currentHash = "";
    private ThemePalette palette = ThemePalette.Light;
    internal event Action<RecentImage>? Chosen;

    internal RecentImageStrip()
    {
        Name = "RecentImages";
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, 5);
        ColumnCount = RecentImageHistory.Limit;
        RowCount = 3;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        RowStyles.Add(new RowStyle(SizeType.Absolute, Font.Height + 8));
        Controls.Add(heading, 0, 0);
        SetColumnSpan(heading, RecentImageHistory.Limit);
        for (var i = 0; i < buttons.Length; i++)
        {
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var index = i;
            var button = new Button { Name = "RecentImage" + i, Dock = DockStyle.Fill, Height = 76,
                FlatStyle = FlatStyle.Flat, BackgroundImageLayout = ImageLayout.Zoom,
                BackColor = Color.FromArgb(25, 29, 36), Margin = new Padding(i == 0 ? 0 : 4, 0, i == 3 ? 0 : 4, 0) };
            button.Click += (_, _) => { if (index < entries.Length) Chosen?.Invoke(entries[index]); };
            buttons[i] = button;
            Controls.Add(button, i, 1);
        }
        MeasureRows();
        Controls.Add(filename, 0, 2);
        Controls.Add(empty, 0, 1);
        SetColumnSpan(empty, RecentImageHistory.Limit);
    }

    internal void SetImages(RecentImage[] images, string hash, string language, Func<RecentImage, SelectedImage> read)
    {
        entries = images.Take(RecentImageHistory.Limit).ToArray();
        currentHash = hash;
        SuspendLayout();
        try
        {
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                var old = button.BackgroundImage;
                button.BackgroundImage = null;
                old?.Dispose();
                button.Visible = i < entries.Length;
                button.Enabled = false;
                if (i >= entries.Length) continue;
                try
                {
                    button.BackgroundImage = CreateThumbnail(read(entries[i]));
                    button.Enabled = true;
                }
                catch (Exception error) when (error is UiMessageException or ArgumentException or OutOfMemoryException) { }
            }
            filename.Visible = entries.Length > 0;
            empty.Visible = entries.Length == 0;
            SetLanguage(language);
        }
        finally { ResumeLayout(performLayout: true); }
    }

    private static Bitmap CreateThumbnail(SelectedImage image)
    {
        using var source = image.Preview();
        var thumbnail = new Bitmap(200, 88);
        try
        {
            using var graphics = Graphics.FromImage(thumbnail);
            graphics.Clear(Color.FromArgb(25, 29, 36));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var scale = Math.Min(194d / source.Width, 82d / source.Height);
            var width = Math.Max(1, (int)(source.Width * scale));
            var height = Math.Max(1, (int)(source.Height * scale));
            graphics.DrawImage(source, (200 - width) / 2, (88 - height) / 2, width, height);
            return thumbnail;
        }
        catch { thumbnail.Dispose(); throw; }
    }

    internal void SetLanguage(string value)
    {
        language = value;
        heading.Text = UiText.T("RecentImages", language);
        empty.Text = UiText.T("ChooseImageFirst", language);
        filename.Text = entries.FirstOrDefault() is { } first ? first.Label ?? Path.GetFileName(first.PhotoFile) : "";
        MeasureRows();
        filename.AccessibleName = UiText.T("RecentCurrent", language) + ": " + filename.Text;
        tips.SetToolTip(filename, filename.Text);
        for (var i = 0; i < entries.Length; i++)
        {
            var button = buttons[i];
            var entry = entries[i];
            var label = entry.Label ?? Path.GetFileName(entry.PhotoFile);
            var isCurrent = entry.Sha256.Equals(currentHash, StringComparison.OrdinalIgnoreCase);
            button.Tag = isCurrent;
            var description = UiText.T("RecentImageChoice", language, i + 1, label) +
                (isCurrent ? " · " + UiText.T("RecentCurrent", language) : "");
            button.AccessibleName = description;
            button.AccessibleDescription = UiText.T(button.BackgroundImage is null ? "RecentImageUnavailable" : "RecentImageHint", language);
            button.Text = button.BackgroundImage is null ? UiText.T("RecentUnavailable", language) : "";
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = isCurrent ? 3 : 1;
            button.FlatAppearance.BorderColor = isCurrent ? palette.Accent : palette.Border;
            tips.SetToolTip(button, description + Environment.NewLine + button.AccessibleDescription);
        }
    }

    // A caption in a percentage-width cell must not determine the strip height.
    // In particular, cached SHA-256 filenames must stay a single ellipsized line.
    internal void MeasureRows()
    {
        var scale = DeviceDpi / 96d;
        RowStyles[1].Height = (float)Math.Ceiling((entries.Length == 0 ? 48 : 76) * scale);
        RowStyles[2].Height = entries.Length == 0 ? 0 : filename.Font.Height + (float)Math.Ceiling(8 * scale);
    }

    internal void ApplyPalette(ThemePalette value)
    {
        palette = value;
        tips.BackColor = value.Surface;
        tips.ForeColor = value.Text;
        empty.ForeColor = value.Muted;
        foreach (var button in buttons)
            button.FlatAppearance.BorderColor = button.Tag is true ? value.Accent : value.Border;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tips.Dispose();
            foreach (var button in buttons) { button.BackgroundImage?.Dispose(); button.BackgroundImage = null; }
        }
        base.Dispose(disposing);
    }
}
