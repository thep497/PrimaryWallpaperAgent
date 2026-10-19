namespace PrimaryWallpaperAgent.Tray;

internal sealed class SettingsHelpPopup : ToolStripDropDown
{
    private readonly Font titleFont;
    internal string BodyText { get; }

    internal SettingsHelpPopup(Font font, string title, string text, string closeText)
    {
        Name = "SettingsHelpPopup";
        Font = font;
        AccessibleName = title;
        AutoClose = true;
        Padding = Padding.Empty;
        Margin = Padding.Empty;
        DropShadowEnabled = true;
        Renderer = new ToolStripSystemRenderer();
        BodyText = text;
        titleFont = new Font(font, FontStyle.Bold);
        var width = Math.Clamp(font.Height * 19, 320, 440);
        var content = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2,
            Padding = new Padding(14), BackColor = SystemColors.Window, ForeColor = SystemColors.WindowText,
            MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var heading = new Label { AutoSize = true, Text = title, Font = titleFont,
            MaximumSize = new Size(width - 68, 0), Margin = new Padding(0, 3, 8, 10) };
        var close = new Button { Name = "HelpCloseButton", Text = "×", AccessibleName = closeText,
            Size = new Size(28, 28), Padding = Padding.Empty, Margin = Padding.Empty, FlatStyle = FlatStyle.Flat };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Close();
        var body = new Label { Name = "HelpBody", AutoSize = true, Text = text, Font = font,
            MaximumSize = new Size(width - 28, 0), Margin = Padding.Empty };
        content.Controls.Add(heading, 0, 0);
        content.Controls.Add(close, 1, 0);
        content.Controls.Add(body, 0, 1);
        content.SetColumnSpan(body, 2);
        Items.Add(new ToolStripControlHost(content) { AutoSize = true, Margin = Padding.Empty, Padding = Padding.Empty });
        Opened += (_, _) => close.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) titleFont.Dispose();
    }
}
