namespace PrimaryWallpaperAgent.Tray;

// Retains the native ComboBox behavior/accessibility. Only the item surface,
// border and arrow are painted, including WM_PRINT used by UI verification.
internal sealed class ThemeComboBox : ComboBox
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color BorderColor { get; set; } = ThemePalette.Light.Border;
    internal ThemeComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
    }
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ItemHeight = Math.Max(22, Font.Height + 6);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var background = selected ? SystemColors.Highlight : BackColor;
        var foreground = selected ? SystemColors.HighlightText : ForeColor;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -4, 0),
            foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (!IsHandleCreated || Width < 10 || Height < 10) return;
        if (m.Msg == 0x000F /* WM_PAINT */)
        {
            using var graphics = Graphics.FromHwnd(Handle);
            PaintChrome(graphics);
        }
        else if (m.Msg is 0x0317 or 0x0318 /* WM_PRINT / WM_PRINTCLIENT */ && m.WParam != 0)
        {
            using var graphics = Graphics.FromHdc(m.WParam);
            PaintChrome(graphics);
        }
    }
    private void PaintChrome(Graphics graphics)
    {
        var arrowWidth = Math.Max(18, SystemInformation.VerticalScrollBarWidth + 2);
        var area = new Rectangle(Width - arrowWidth - 1, 1, arrowWidth, Height - 2);
        using (var brush = new SolidBrush(BackColor))
        {
            if (DropDownStyle == ComboBoxStyle.DropDownList)
            {
                graphics.FillRectangle(brush, ClientRectangle);
                var textBounds = new Rectangle(5, 1, Math.Max(0, area.Left - 7), Height - 2);
                TextRenderer.DrawText(graphics, GetItemText(SelectedItem), Font, textBounds,
                    Enabled ? ForeColor : BorderColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(textBounds, -1, -3), ForeColor, BackColor);
            }
            else graphics.FillRectangle(brush, area);
        }
        using (var border = new Pen(BorderColor)) graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var scale = DeviceDpi / 96f;
        var x = area.Left + area.Width / 2f;
        var y = area.Top + area.Height / 2f;
        using var pen = new Pen(Enabled ? ForeColor : BorderColor, Math.Max(1, scale));
        graphics.DrawLines(pen, [new PointF(x - 4 * scale, y - 2 * scale), new PointF(x, y + 2 * scale),
            new PointF(x + 4 * scale, y - 2 * scale)]);
    }
}
