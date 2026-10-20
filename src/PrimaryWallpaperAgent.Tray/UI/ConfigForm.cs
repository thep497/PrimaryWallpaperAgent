namespace PrimaryWallpaperAgent.Tray;

internal sealed class ConfigForm : Form
{
    private readonly Icon applicationIcon = AppBranding.CreateApplicationIcon();
    private readonly PictureBox preview = new() { Name = "WallpaperPreview", Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 29, 36), SizeMode = PictureBoxSizeMode.Zoom, MinimumSize = new Size(240, 80) };
    private readonly Label filename = new() { Name = "ImageFilename", AutoSize = false, Height = 30, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label feedback = new() { Name = "FeedbackLabel", AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 6) };
    private readonly LinkLabel about = new() { Name = "AboutLink", AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(12, 5, 0, 8) };
    private readonly Label heading = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
    private readonly Label status = new() { Name = "AgentStatus", AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(50, 85, 120), Margin = new Padding(0, 0, 0, 12) };
    private readonly Label languageLabel = new() { AutoSize = true, Margin = new Padding(0, 7, 10, 0) };
    private readonly ComboBox languageSelector = new() { Name = "LanguageSelector", DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly Label pollLabel = new() { AutoSize = true, Margin = new Padding(0, 12, 10, 0) };
    private readonly ComboBox pollSelector = new() { Name = "PollSelector", DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(3, 8, 3, 3) };
    private readonly CheckBox startup = new() { Name = "StartupCheck", AutoSize = true };
    private readonly Button browse = new() { Name = "BrowseButton", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
    private readonly Button useCurrent = new() { Name = "UseCurrentButton", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
    private readonly Button undo = new() { Name = "UndoImageButton", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
    private readonly Button close = new() { Name = "CloseButton", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(12, 6, 12, 6) };
    private readonly Button log = new() { AutoSize = true, Padding = new Padding(12, 6, 12, 6) };
    private readonly Func<string> currentPrimary;
    private readonly Button errorDetails;
    private readonly List<(Button Button, string Title, string Body)> helpTopics = [];
    private SettingsHelpPopup? helpPopup;
    private readonly string dataRoot;
    private readonly Action<string>? openAboutTarget;
    private readonly UpdateController? updates;
    private readonly Func<string, string> statusText;
    private readonly Action<ConfigSubmission> apply;
    private readonly Action<Exception>? reportError;
    private readonly Func<SelectedImage>? undoImage;
    private readonly Func<bool>? canUndo;
    private readonly Func<bool>? imageConfigured;
    private SelectedImage selected;
    private string language;
    private string committedLanguage;
    private bool committedStartup;
    private int committedPollIntervalMs;
    private bool updatingControls = true;
    private bool busy;
    private Exception? lastError;
    private string? feedbackKey;
    private string errorFallback = "GenericError";

    internal ConfigForm(SelectedImage initial, bool startupEnabled, Func<string, string> statusText,
        Func<string> currentPrimary, Action<ConfigSubmission> apply, Action<string> openLog,
        string language = "th", Action<Exception>? reportError = null, int pollIntervalMs = PollingOptions.DefaultIntervalMs,
        Func<SelectedImage>? undoImage = null, Func<bool>? canUndo = null, Func<bool>? imageConfigured = null, string dataRoot = "", Action<string>? openAboutTarget = null, UpdateController? updates = null)
    {
        selected = initial;
        this.dataRoot = dataRoot;
        this.openAboutTarget = openAboutTarget;
        this.updates = updates;
        this.currentPrimary = currentPrimary;
        this.statusText = statusText;
        this.apply = apply;
        this.reportError = reportError;
        this.undoImage = undoImage;
        this.canUndo = canUndo;
        this.imageConfigured = imageConfigured;
        this.language = committedLanguage = UiText.Normalize(language);
        committedStartup = startupEnabled;
        committedPollIntervalMs = PollingOptions.Validate(pollIntervalMs);
        Icon = applicationIcon;
        Font = new Font("Segoe UI", 10);
        heading.Font = new Font(Font.FontFamily, 16, FontStyle.Bold);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScroll = false;
        ClientSize = new Size(700, 580);
        MinimumSize = SizeFromClientSize(new Size(620, 520));
        var layout = new TableLayoutPanel { Name = "SettingsContent", Dock = DockStyle.Fill,
            ColumnCount = 1, RowCount = 8, Padding = new Padding(18), Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 8; i++)
            layout.RowStyles.Add(new RowStyle(i == 2 ? SizeType.Percent : SizeType.AutoSize, i == 2 ? 100 : 0));
        Controls.Add(layout);
        var header = new TableLayoutPanel { Name = "SettingsHeader", AutoSize = true, Dock = DockStyle.Fill,
            ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(heading, 0, 0);
        header.Controls.Add(about, 1, 0);
        about.LinkClicked += (_, _) =>
        {
            using var dialog = new AboutForm(this.language, this.dataRoot, this.openAboutTarget, this.updates);
            dialog.ShowDialog(this);
        };
        layout.Controls.Add(header, 0, 0);
        status.Height = Font.Height + 8;
        layout.Controls.Add(status, 0, 1);
        layout.Controls.Add(preview, 0, 2);
        filename.Height = Font.Height + 8;
        layout.Controls.Add(filename, 0, 3);
        var rowHeight = Font.Height + 16;
        var choose = new TableLayoutPanel { Name = "ImageActions", AutoSize = true, Dock = DockStyle.Fill,
            ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 4, 0, 10) };
        choose.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        choose.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        choose.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        browse.AutoSize = useCurrent.AutoSize = false;
        browse.Dock = useCurrent.Dock = DockStyle.Fill;
        browse.Height = useCurrent.Height = rowHeight;
        browse.Click += (_, _) => Browse();
        useCurrent.Click += (_, _) => SelectImage(currentPrimary);
        undo.Click += (_, _) => RunChange(() => { selected = undoImage!(); ShowSelected(); }, "ImageUndone");
        choose.Controls.Add(browse, 0, 0);
        choose.Controls.Add(useCurrent, 1, 0);
        choose.Controls.Add(CreateHelpButton("ImageHelpButton", "ImageHelpTitle", "ImageHelpText"), 2, 0);
        layout.Controls.Add(choose, 0, 4);
        var preferences = new TableLayoutPanel { Name = "Preferences", AutoSize = true, Dock = DockStyle.Fill,
            ColumnCount = 3, RowCount = 3, Margin = Padding.Empty };
        preferences.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        preferences.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        preferences.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        languageSelector.Items.AddRange([new LanguageOption("th", "ไทย (Thai)"), new LanguageOption("en", "English")]);
        languageSelector.SelectedIndex = this.language == "th" ? 0 : 1;
        languageSelector.SelectedIndexChanged += (_, _) => ChangePreferences();
        preferences.Controls.Add(languageLabel, 0, 0);
        preferences.Controls.Add(languageSelector, 1, 0);
        preferences.Controls.Add(CreateHelpButton("LanguageHelpButton", "LanguageLabel", "LanguageHint"), 2, 0);
        foreach (var interval in PollingOptions.SupportedIntervals)
            pollSelector.Items.Add(new PollOption(interval, PollOptionText(interval)));
        pollSelector.SelectedIndex = Array.IndexOf(PollingOptions.SupportedIntervals.ToArray(), committedPollIntervalMs);
        pollSelector.SelectedIndexChanged += (_, _) => ChangePreferences();
        preferences.Controls.Add(pollLabel, 0, 1);
        preferences.Controls.Add(pollSelector, 1, 1);
        preferences.Controls.Add(CreateHelpButton("PollHelpButton", "PollLabel", "PollHint"), 2, 1);
        startup.Checked = startupEnabled;
        startup.CheckedChanged += (_, _) => ChangePreferences();
        preferences.Controls.Add(startup, 0, 2);
        preferences.SetColumnSpan(startup, 2);
        preferences.Controls.Add(CreateHelpButton("StartupHelpButton", "StartupCheck", "StartupHint"), 2, 2);
        layout.Controls.Add(preferences, 0, 5);
        var response = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0, 4, 0, 4) };
        response.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        response.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        feedback.Height = Font.Height * 2 + 4;
        errorDetails = new Button { Name = "ErrorDetailsButton", Text = "?", Visible = false,
            Size = new Size(30, 30), FlatStyle = FlatStyle.Flat, Margin = new Padding(4, 0, 0, 0) };
        errorDetails.Click += (_, _) => ShowHelp(errorDetails, UiText.T("ChangeErrorTitle", this.language),
            lastError is null ? "" : UiText.Error(lastError, this.language, errorFallback));
        response.Controls.Add(feedback, 0, 0);
        response.Controls.Add(errorDetails, 1, 0);
        layout.Controls.Add(response, 0, 6);
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        log.Click += (_, _) => openLog(this.language);
        footer.Controls.Add(undo, 0, 0);
        footer.Controls.Add(log, 2, 0);
        footer.Controls.Add(close, 3, 0);
        layout.Controls.Add(footer, 0, 7);
        CancelButton = close;
        FormClosing += (_, args) => { if (busy) args.Cancel = true; };
        Load += (_, _) => BeginInvoke((Action)(() => WindowPresentation.Reveal(this)));
        ShowSelected();
        updatingControls = false;
        ApplyTexts();
    }

    internal SettingsHelpPopup? CurrentHelp => helpPopup;

    private Button CreateHelpButton(string name, string title, string body)
    {
        var button = new Button { Name = name, Text = "?", Size = new Size(30, 30),
            Margin = new Padding(8, 3, 0, 3), Padding = Padding.Empty, FlatStyle = FlatStyle.Flat };
        button.FlatAppearance.BorderColor = Color.FromArgb(190, 200, 210);
        helpTopics.Add((button, title, body));
        button.Click += (_, _) => ShowHelp(button, UiText.T(title, language), UiText.T(body, language));
        return button;
    }

    private void ShowHelp(Control anchor, string title, string body)
    {
        helpPopup?.Close();
        helpPopup?.Dispose();
        helpPopup = new SettingsHelpPopup(Font, title, body, UiText.T("Close", language));
        helpPopup.Show(anchor, new Point(0, anchor.Height + 4));
    }

    private int PollIntervalMs => ((PollOption)pollSelector.SelectedItem!).IntervalMs;
    private string PollOptionText(int intervalMs) => UiText.T("PollOption", language, intervalMs / 1000d);

    private void ChangePreferences()
    {
        if (updatingControls || busy) return;
        var nextLanguage = ((LanguageOption)languageSelector.SelectedItem!).Code;
        if (nextLanguage == committedLanguage && startup.Checked == committedStartup && PollIntervalMs == committedPollIntervalMs) return;
        RunChange(() =>
        {
            apply(new(selected, startup.Checked, nextLanguage, false, PollIntervalMs));
            language = committedLanguage = nextLanguage;
            committedStartup = startup.Checked;
            committedPollIntervalMs = PollIntervalMs;
        }, "SettingsSaved");
    }

    internal void RefreshAgentState()
    {
        if (IsDisposed || Disposing) return;
        status.Text = statusText(language);
        UpdateEnabled();
    }

    private void ApplyTexts()
    {
        string T(string key) => UiText.T(key, language);
        Text = UiText.Title("ConfigTitle", language);
        heading.Text = T("ConfigHeading");
        about.Text = UiText.T("AboutLink", language, AboutForm.VersionText);
        if (imageConfigured?.Invoke() == false) filename.Text = T("ChooseImageFirst");
        status.Text = statusText(language);
        languageLabel.Text = T("LanguageLabel");
        pollLabel.Text = T("PollLabel");
        var wasUpdating = updatingControls;
        updatingControls = true;
        try
        {
            var index = pollSelector.SelectedIndex;
            for (var i = 0; i < pollSelector.Items.Count; i++)
            {
                var option = (PollOption)pollSelector.Items[i]!;
                pollSelector.Items[i] = option with { Label = PollOptionText(option.IntervalMs) };
            }
            pollSelector.SelectedIndex = index;
        }
        finally { updatingControls = wasUpdating; }
        startup.Text = T("StartupCheck");
        browse.Text = T("Browse");
        useCurrent.Text = T("UseCurrent");
        undo.Text = T("UndoImage");
        close.Text = T("Close");
        log.Text = T("ViewLog");
        foreach (var topic in helpTopics) {
            topic.Button.AccessibleName = UiText.T("HelpFor", language, T(topic.Title));
            topic.Button.AccessibleDescription = T(topic.Body);
        }
        errorDetails.Visible = lastError is not null;
        errorDetails.AccessibleName = T("ChangeErrorTitle");
        feedback.ForeColor = lastError is null ? Color.FromArgb(25, 115, 65) : Color.Firebrick;
        feedback.Text = lastError is not null ? UiText.Error(lastError, language, errorFallback) : feedbackKey is null ? "" : T(feedbackKey);
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        about.Enabled = !busy;
        languageSelector.Enabled = pollSelector.Enabled = startup.Enabled = browse.Enabled = useCurrent.Enabled = !busy;
        undo.Enabled = !busy && undoImage is not null && canUndo?.Invoke() == true;
        close.Enabled = !busy;
    }

    private void Browse()
    {
        using var picker = new OpenFileDialog { Title = UiText.T("PickerTitle", language), Filter = UiText.T("PickerFilter", language), CheckFileExists = true, Multiselect = false, RestoreDirectory = true };
        if (picker.ShowDialog(this) == DialogResult.OK) SelectImage(() => picker.FileName);
    }

    private void SelectImage(Func<string> path) => RunChange(() =>
    {
        var source = path();
        SelectedImage next;
        Bitmap? nextPreview;
        try { next = SelectedImage.Read(source); nextPreview = next.Preview(); }
        catch (UiMessageException) { throw; }
        catch (Exception error) { throw new UiMessageException("ImageReadFailed", error); }
        try
        {
            apply(new(next, committedStartup, language, true, committedPollIntervalMs));
            selected = next;
            ShowSelected(nextPreview);
            nextPreview = null; // PictureBox owns this preview until replaced/disposed.
        }
        finally { nextPreview?.Dispose(); }
    }, "ImageApplied");

    private void ShowSelected(Bitmap? next = null)
    {
        next ??= selected.Preview();
        var old = preview.Image;
        preview.Image = next;
        old?.Dispose();
        filename.Text = selected.FriendlyName ?? Path.GetFileName(selected.SourcePath);
    }

    private void RunChange(Action action, string success, string fallback = "GenericError")
    {
        if (busy) return;
        busy = true;
        UseWaitCursor = true;
        UpdateEnabled();
        try { action(); lastError = null; feedbackKey = success; }
        catch (Exception error)
        {
            updatingControls = true;
            try
            {
                language = committedLanguage;
                languageSelector.SelectedIndex = language == "th" ? 0 : 1;
                pollSelector.SelectedIndex = Array.IndexOf(PollingOptions.SupportedIntervals.ToArray(), committedPollIntervalMs);
                startup.Checked = committedStartup;
            }
            finally { updatingControls = false; }
            lastError = error; feedbackKey = null; errorFallback = fallback;
            reportError?.Invoke(error);
        }
        finally { busy = false; UseWaitCursor = false; ApplyTexts(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { helpPopup?.Dispose(); preview.Image?.Dispose(); preview.Image = null; }
        base.Dispose(disposing);
        if (disposing) applicationIcon.Dispose();
    }
    private sealed record PollOption(int IntervalMs, string Label) { public override string ToString() => Label; }
    private sealed record LanguageOption(string Code, string Label) { public override string ToString() => Label; }
}
