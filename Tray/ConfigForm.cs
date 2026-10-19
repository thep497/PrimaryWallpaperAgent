namespace PrimaryWallpaperAgent.Tray;

internal sealed class ConfigForm : Form
{
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 29, 36), SizeMode = PictureBoxSizeMode.Zoom, MinimumSize = new Size(300, 200) };
    private readonly Label filename = new() { AutoSize = false, Height = 30, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label feedback = new() { AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = Color.Firebrick, Padding = new Padding(0, 6, 0, 6) };
    private readonly CheckBox startup = new() { AutoSize = true, Text = "เปิดพร้อม Windows สำหรับบัญชีนี้" };
    private readonly Button apply = new() { AutoSize = true, Text = "บันทึกและใช้", Padding = new Padding(14, 6, 14, 6) };
    private readonly Func<string> currentPrimary;
    private readonly Action<SelectedImage, bool> save;
    private SelectedImage selected;

    internal ConfigForm(SelectedImage initial, bool startupEnabled, string status, Func<string> currentPrimary,
        Action<SelectedImage, bool> save, Action openLog)
    {
        selected = initial;
        this.currentPrimary = currentPrimary;
        this.save = save;
        Text = "PrimaryWallpaperAgent · ตั้งค่า";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 700);
        ClientSize = new Size(740, 710);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, Padding = new Padding(22) };
        for (var i = 0; i < 10; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "ตั้งค่าภาพพื้นหลัง", AutoSize = true, Font = new Font(Font.FontFamily, 19, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) }, 0, 0);
        layout.Controls.Add(new Label { Text = "ภาพตาม Windows Primary อัตโนมัติ · จออื่นทุกจอสีดำ", AutoSize = true, Margin = new Padding(0, 0, 0, 8) }, 0, 1);
        layout.Controls.Add(new Label { Text = status, AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = Color.FromArgb(50, 85, 120), Margin = new Padding(0, 0, 0, 12) }, 0, 2);
        layout.Controls.Add(preview, 0, 3);
        var choose = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        var browse = new Button { Text = "เลือกไฟล์ภาพ…", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        browse.Click += (_, _) => Browse();
        var useCurrent = new Button { Text = "ใช้ภาพบนจอหลักตอนนี้", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
        useCurrent.Click += (_, _) => TrySelect(() => currentPrimary());
        choose.Controls.AddRange([browse, useCurrent]);
        layout.Controls.Add(choose, 0, 4);
        layout.Controls.Add(filename, 0, 5);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(690, 0), Text = "รองรับ JPG / PNG / BMP · บนจอใช้ Fill ซึ่งอาจตัดขอบภาพ\nเลือกและดูตัวอย่างก่อนได้ ภาพจริงจะเปลี่ยนเมื่อกดบันทึกและใช้\nรุ่นนี้ใช้ภาพนิ่งร่วมกันทุก Virtual Desktop ยังไม่รองรับ Spotlight", Margin = new Padding(0, 4, 0, 14) }, 0, 6);
        startup.Checked = startupEnabled;
        layout.Controls.Add(startup, 0, 7);
        layout.Controls.Add(feedback, 0, 8);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 10, 0, 0) };
        var cancel = new Button { Text = "ยกเลิก", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(12, 6, 12, 6) };
        var log = new Button { Text = "ดูบันทึก", AutoSize = true, Padding = new Padding(12, 6, 12, 6) };
        log.Click += (_, _) => openLog();
        apply.Click += (_, _) => Save();
        footer.Controls.AddRange([cancel, apply, log]);
        layout.Controls.Add(footer, 0, 9);
        AcceptButton = apply;
        CancelButton = cancel;
        ShowSelected();
    }
    private void Browse()
    {
        using var picker = new OpenFileDialog { Title = "เลือกภาพหลัก", Filter = "ภาพนิ่ง (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp", CheckFileExists = true, Multiselect = false, RestoreDirectory = true };
        if (picker.ShowDialog(this) == DialogResult.OK) TrySelect(() => picker.FileName);
    }
    private void TrySelect(Func<string> path)
    {
        try { var next = SelectedImage.Read(path()); selected = next; ShowSelected(); feedback.Text = ""; }
        catch (Exception error) { feedback.Text = "อ่านภาพไม่ได้: " + error.Message; }
    }
    private void ShowSelected()
    {
        var next = selected.Preview();
        var old = preview.Image;
        preview.Image = next;
        old?.Dispose();
        filename.Text = selected.FriendlyName ?? Path.GetFileName(selected.SourcePath);
    }
    private void Save()
    {
        apply.Enabled = false;
        UseWaitCursor = true;
        try { save(selected, startup.Checked); DialogResult = DialogResult.OK; Close(); }
        catch (Exception error) { feedback.Text = error.Message; }
        finally { apply.Enabled = true; UseWaitCursor = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { preview.Image?.Dispose(); preview.Image = null; }
        base.Dispose(disposing);
    }
}
