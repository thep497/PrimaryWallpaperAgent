using System.Drawing.Drawing2D;

namespace PrimaryWallpaperAgent.Tray;

internal sealed class RecentImageStrip : TableLayoutPanel
{
    private readonly Label heading = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 3) };
    private readonly Button[] buttons = new Button[RecentImageHistory.Limit];
    private readonly ToolTip tips = new();
    private RecentImage[] entries = [];
    private string language = "en";
    private string currentHash = "";
    internal event Action<RecentImage>? Chosen;

    internal RecentImageStrip()
    {
        Name = "RecentImages";
        AutoSize = true;
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 0, 0, 5);
        ColumnCount = RecentImageHistory.Limit;
        RowCount = 2;
        Controls.Add(heading, 0, 0);
        SetColumnSpan(heading, RecentImageHistory.Limit);
        for (var i = 0; i < buttons.Length; i++)
        {
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var index = i;
            var button = new Button { Name = "RecentImage" + i, Dock = DockStyle.Fill, Height = 60,
                FlatStyle = FlatStyle.Flat, BackgroundImageLayout = ImageLayout.Zoom,
                BackColor = Color.FromArgb(25, 29, 36), Margin = new Padding(i == 0 ? 0 : 4, 0, i == 3 ? 0 : 4, 0) };
            button.Click += (_, _) => { if (index < entries.Length) Chosen?.Invoke(entries[index]); };
            buttons[i] = button;
            Controls.Add(button, i, 1);
        }
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
                    button.BackgroundImage = CreateThumbnail(read(entries[i]), button.BackColor);
                    button.Enabled = true;
                }
                catch (Exception error) when (error is UiMessageException or ArgumentException or OutOfMemoryException) { } // One damaged historical asset cannot prevent Settings from opening.
            }
            Visible = entries.Length > 0;
            SetLanguage(language);
        }
        finally { ResumeLayout(performLayout: true); }
    }

    private static Bitmap CreateThumbnail(SelectedImage image, Color background)
    {
        using var source = image.Preview();
        var thumbnail = new Bitmap(160, 60);
        try
        {
            using var graphics = Graphics.FromImage(thumbnail);
            graphics.Clear(background);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var scale = Math.Min(154d / source.Width, 54d / source.Height);
            var width = Math.Max(1, (int)(source.Width * scale));
            var height = Math.Max(1, (int)(source.Height * scale));
            graphics.DrawImage(source, (160 - width) / 2, (60 - height) / 2, width, height);
            return thumbnail;
        }
        catch { thumbnail.Dispose(); throw; }
    }

    internal void SetLanguage(string value)
    {
        language = value;
        heading.Text = UiText.T("RecentImages", language);
        for (var i = 0; i < entries.Length; i++)
        {
            var button = buttons[i];
            var entry = entries[i];
            var label = entry.Label ?? Path.GetFileName(entry.PhotoFile);
            var isCurrent = entry.Sha256.Equals(currentHash, StringComparison.OrdinalIgnoreCase);
            var description = UiText.T("RecentImageChoice", language, i + 1, label) +
                (isCurrent ? " · " + UiText.T("RecentCurrent", language) : "");
            button.AccessibleName = description;
            button.AccessibleDescription = UiText.T(button.BackgroundImage is null ? "RecentImageUnavailable" : "RecentImageHint", language);
            button.Text = button.BackgroundImage is null ? UiText.T("RecentUnavailable", language) : "";
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = isCurrent ? 3 : 1;
            button.FlatAppearance.BorderColor = isCurrent ? Color.FromArgb(56, 143, 220) : Color.FromArgb(110, 120, 135);
            tips.SetToolTip(button, description + Environment.NewLine + button.AccessibleDescription);
        }
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
