using System.Diagnostics;

namespace PrimaryWallpaperAgent.Tray;

internal sealed class AboutForm : Form
{
    internal const string ReleasesUrl = "https://github.com/thep497/PrimaryWallpaperAgent/releases";
    internal static string VersionText => typeof(AboutForm).Assembly.GetName().Version is { } version ?
        $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";
    private readonly Font headingFont;
    private readonly ThemeManager themes;
    private readonly bool ownsThemes;
    private readonly Icon applicationIcon = AppBranding.CreateApplicationIcon();

    internal AboutForm(string language, string dataRoot, Action<string>? openTarget = null, UpdateController? updates = null, ThemeManager? themes = null)
    {
        ownsThemes = themes is null;
        this.themes = themes ?? new ThemeManager();
        string T(string key) => UiText.T(key, language);
        Name = "AboutForm";
        Text = UiText.Title("AboutTitle", language);
        Icon = applicationIcon;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Padding = new Padding(20), MinimumSize = new Size(460, 0), MaximumSize = new Size(520, 0) };
        Controls.Add(layout);
        headingFont = new Font(Font.FontFamily, 15, FontStyle.Bold);
        var title = new Label { AutoSize = true, Text = UiText.AppName, Font = headingFont, Margin = new Padding(0, 0, 0, 6) };
        layout.Controls.Add(title);
        layout.Controls.Add(new Label { Name = "AboutVersion", AutoSize = true, Text = T("VersionLabel") + " " + VersionText + " · " + (BuildInfo.Flavor == "full" ? "Full" : "Slim"), Margin = new Padding(0, 0, 0, 14) });
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(450, 0), Text = T("AboutDescription"), Margin = new Padding(0, 0, 0, 14) });
        layout.Controls.Add(new Label { AutoSize = true, Text = T("DataFolderLabel"), Margin = new Padding(0, 0, 0, 4) });
        layout.Controls.Add(new TextBox { Name = "AboutDataPath", ReadOnly = true, Text = dataRoot, Width = 450,
            BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 16) });
        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, WrapContents = false };
        var releases = new Button { Name = "ReleasesButton", AutoSize = true, Text = T("GitHubReleases"), Padding = new Padding(6, 3, 6, 3) };
        var data = new Button { Name = "OpenDataButton", AutoSize = true, Text = T("OpenDataFolder"), Padding = new Padding(6, 3, 6, 3), Enabled = !string.IsNullOrWhiteSpace(dataRoot) };
        var close = new Button { Name = "AboutCloseButton", AutoSize = true, Text = T("Close"), DialogResult = DialogResult.Cancel, Padding = new Padding(6, 3, 6, 3) };
        releases.Click += (_, _) => Open(ReleasesUrl);
        data.Click += (_, _) => Open(dataRoot);
        actions.Controls.AddRange([releases, data, close]);
        if (updates is not null) layout.Controls.Add(new UpdatePanel(updates, language, Open));
        layout.Controls.Add(actions);
        CancelButton = close;
        AcceptButton = close;
        this.themes.Bind(this);


        void Open(string target)
        {
            try
            {
                if (openTarget is not null) openTarget(target);
                else Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            { MessageBox.Show(this, T("OpenLocationFailed"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { applicationIcon.Dispose(); headingFont.Dispose(); if (ownsThemes) themes.Dispose(); }
    }
}
