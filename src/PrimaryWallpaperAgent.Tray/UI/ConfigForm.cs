namespace PrimaryWallpaperAgent.Tray;

internal sealed class ConfigForm : Form
{
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 29, 36), SizeMode = PictureBoxSizeMode.Zoom, MinimumSize = new Size(300, 180) };
    private readonly Label filename = new() { AutoSize = false, Height = 30, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label feedback = new() { Name = "FeedbackLabel", AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = Color.Firebrick, Padding = new Padding(0, 6, 0, 6) };
    private readonly Label heading = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
    private readonly Label policy = new() { AutoSize = true, MaximumSize = new Size(690, 0), Margin = new Padding(0, 0, 0, 8) };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = Color.FromArgb(50, 85, 120), Margin = new Padding(0, 0, 0, 12) };
    private readonly Label notes = new() { AutoSize = true, MaximumSize = new Size(690, 0), Margin = new Padding(0, 4, 0, 12) };
    private readonly Label languageLabel = new() { AutoSize = true, Margin = new Padding(0, 7, 10, 0) };
    private readonly Label languageHint = new() { AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = SystemColors.GrayText };
    private readonly ComboBox languageSelector = new() { Name = "LanguageSelector", DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly CheckBox startup = new() { Name = "StartupCheck", AutoSize = true };
    private readonly Button apply = new() { Name = "SaveButton", AutoSize = true, Padding = new Padding(14, 6, 14, 6) };
    private readonly Button browse = new() { AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
    private readonly Button useCurrent = new() { Name = "UseCurrentButton", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
    private readonly Button cancel = new() { Name = "CancelButton", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(12, 6, 12, 6) };
    private readonly Button log = new() { AutoSize = true, Padding = new Padding(12, 6, 12, 6) };
    private readonly Func<string> currentPrimary;
    private readonly Func<string, string> statusText;
    private readonly Action<ConfigSubmission> save;
    private readonly Action<Exception>? reportError;
    private readonly string initialLanguage;
    private readonly bool initialStartup;
    private SelectedImage selected;
    private string language;
    private bool imageChosen;
    private Exception? lastError;
    private string errorFallback = "GenericError";

    internal ConfigForm(SelectedImage initial, bool startupEnabled, Func<string, string> statusText,
        Func<string> currentPrimary, Action<ConfigSubmission> save, Action<string> openLog,
        string language = "th", Action<Exception>? reportError = null)
    {
        selected = initial;
        this.currentPrimary = currentPrimary;
        this.statusText = statusText;
        this.save = save;
        this.reportError = reportError;
        this.language = initialLanguage = UiText.Normalize(language);
        initialStartup = startupEnabled;
        Font = new Font("Segoe UI", 10);
        heading.Font = new Font(Font.FontFamily, 19, FontStyle.Bold);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 740);
        ClientSize = new Size(740, 790);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11, Padding = new Padding(22), AutoScroll = true };
        for (var i = 0; i < 11; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        Controls.Add(layout);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(policy, 0, 1);
        layout.Controls.Add(status, 0, 2);
        layout.Controls.Add(preview, 0, 3);
        var choose = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        browse.Click += (_, _) => Browse();
        useCurrent.Click += (_, _) => TrySelect(currentPrimary);
        choose.Controls.AddRange([browse, useCurrent]);
        layout.Controls.Add(choose, 0, 4);
        layout.Controls.Add(filename, 0, 5);
        layout.Controls.Add(notes, 0, 6);
        var languages = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 10) };
        languages.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        languages.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        languageSelector.Items.AddRange([new LanguageOption("th", "ไทย (Thai)"), new LanguageOption("en", "English")]);
        languageSelector.SelectedIndex = this.language == "th" ? 0 : 1;
        languageSelector.SelectedIndexChanged += (_, _) =>
        {
            this.language = ((LanguageOption)languageSelector.SelectedItem!).Code;
            ApplyTexts();
        };
        languages.Controls.Add(languageLabel, 0, 0);
        languages.Controls.Add(languageSelector, 1, 0);
        languages.Controls.Add(languageHint, 0, 1);
        languages.SetColumnSpan(languageHint, 2);
        layout.Controls.Add(languages, 0, 7);
        startup.Checked = startupEnabled;
        startup.CheckedChanged += (_, _) => ApplyTexts();
        layout.Controls.Add(startup, 0, 8);
        layout.Controls.Add(feedback, 0, 9);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 10, 0, 0) };
        log.Click += (_, _) => openLog(this.language);
        apply.Click += (_, _) => Save();
        footer.Controls.AddRange([cancel, apply, log]);
        layout.Controls.Add(footer, 0, 10);
        AcceptButton = apply;
        CancelButton = cancel;
        Load += (_, _) => BeginInvoke((Action)(() => WindowPresentation.Reveal(this)));
        ShowSelected();
        ApplyTexts();
    }

    private bool ApplyWallpaper => imageChosen || (language == initialLanguage && startup.Checked == initialStartup);

    private void ApplyTexts()
    {
        string T(string key) => UiText.T(key, language);
        Text = UiText.Title("ConfigTitle", language);
        heading.Text = T("ConfigHeading");
        policy.Text = T("ConfigPolicy");
        status.Text = statusText(language);
        notes.Text = T("ImageNotes");
        languageLabel.Text = T("LanguageLabel");
        languageHint.Text = T("LanguageHint");
        startup.Text = T("StartupCheck");
        apply.Text = T(ApplyWallpaper ? "SaveApply" : "SaveSettings");
        browse.Text = T("Browse");
        useCurrent.Text = T("UseCurrent");
        cancel.Text = T("Cancel");
        log.Text = T("ViewLog");
        feedback.Text = lastError is null ? "" : UiText.Error(lastError, language, errorFallback);
    }

    private void Browse()
    {
        using var picker = new OpenFileDialog { Title = UiText.T("PickerTitle", language), Filter = UiText.T("PickerFilter", language), CheckFileExists = true, Multiselect = false, RestoreDirectory = true };
        if (picker.ShowDialog(this) == DialogResult.OK) TrySelect(() => picker.FileName);
    }
    private void TrySelect(Func<string> path)
    {
        try { selected = SelectedImage.Read(path()); ShowSelected(); imageChosen = true; lastError = null; ApplyTexts(); }
        catch (Exception error) { ShowError(error, "ImageReadFailed"); }
    }
    private void ShowSelected()
    {
        var next = selected.Preview();
        var old = preview.Image;
        preview.Image = next;
        old?.Dispose();
        filename.Text = selected.FriendlyName ?? Path.GetFileName(selected.SourcePath);
    }
    private void ShowError(Exception error, string fallback)
    {
        lastError = error;
        errorFallback = fallback;
        reportError?.Invoke(error);
        ApplyTexts();
    }
    private void Save()
    {
        apply.Enabled = false;
        UseWaitCursor = true;
        try { save(new(selected, startup.Checked, language, ApplyWallpaper)); DialogResult = DialogResult.OK; Close(); }
        catch (Exception error) { ShowError(error, "GenericError"); }
        finally { apply.Enabled = true; UseWaitCursor = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { preview.Image?.Dispose(); preview.Image = null; }
        base.Dispose(disposing);
    }
    private sealed record LanguageOption(string Code, string Label)
    {
        public override string ToString() => Label;
    }
}