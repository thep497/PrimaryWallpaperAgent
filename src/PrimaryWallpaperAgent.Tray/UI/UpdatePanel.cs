namespace PrimaryWallpaperAgent.Tray;

internal sealed class UpdatePanel : UserControl
{
    private readonly UpdateController controller;
    private readonly string language;
    private readonly CheckBox automatic = new() { Name = "AutoUpdateCheck", AutoSize = true };
    private readonly CheckBox preview = new() { Name = "PreviewUpdateCheck", AutoSize = true };
    private readonly Label status = new() { Name = "UpdateStatus", AutoSize = true, MaximumSize = new Size(450, 0), Margin = new Padding(0, 8, 0, 8) };
    private readonly Button check = new() { Name = "CheckUpdatesButton", AutoSize = true, Padding = new Padding(6, 3, 6, 3) };
    private readonly Button install = new() { Name = "InstallUpdateButton", AutoSize = true, Padding = new Padding(6, 3, 6, 3) };
    private readonly LinkLabel notes = new() { Name = "UpdateNotesLink", AutoSize = true, Margin = new Padding(0, 4, 0, 10) };
    private bool updating;

    internal UpdatePanel(UpdateController controller, string language, Action<string> openTarget)
    {
        this.controller = controller;
        this.language = language;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0, 8, 0, 10);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        Controls.Add(layout);
        automatic.Text = UiText.T("UpdateAutoCheck", language);
        preview.Text = UiText.T("UpdateIncludePreview", language);
        check.Text = UiText.T("UpdateCheckNow", language);
        install.Text = UiText.T("UpdateInstall", language);
        notes.Text = UiText.T("UpdateNotes", language);
        automatic.CheckedChanged += (_, _) => ChangePreferences();
        preview.CheckedChanged += (_, _) => ChangePreferences();
        check.Click += async (_, _) => await controller.CheckAsync();
        install.Click += async (_, _) => await controller.InstallAsync();
        notes.LinkClicked += (_, _) =>
        {
            if (controller.Available is { } release)
                openTarget("https://github.com/" + UpdateTrust.Repository + "/releases/tag/v" + release.Version);
        };
        layout.Controls.Add(automatic);
        layout.Controls.Add(preview);
        layout.Controls.Add(status);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        actions.Controls.AddRange([check, install]);
        layout.Controls.Add(actions);
        layout.Controls.Add(notes);
        controller.Changed += RefreshState;
        RefreshState();
    }
    private void ChangePreferences()
    {
        if (updating) return;
        try { controller.ChangePreferences(automatic.Checked, preview.Checked); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            RefreshState();
            MessageBox.Show(this, UiText.T("UpdatePreferencesFailed", language), UiText.T("AboutTitle", language), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void RefreshState()
    {
        if (IsDisposed) return;
        updating = true;
        try
        {
            automatic.Checked = controller.Preferences.AutoCheck;
            preview.Checked = controller.Preferences.IncludePreviews;
            automatic.Enabled = preview.Enabled = check.Enabled = !controller.IsBusy;
            install.Visible = controller.Available is not null;
            install.Enabled = !controller.IsBusy;
            notes.Visible = controller.Available is not null;
            status.Text = controller.Status(language);
        }
        finally { updating = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) controller.Changed -= RefreshState;
        base.Dispose(disposing);
    }
}
