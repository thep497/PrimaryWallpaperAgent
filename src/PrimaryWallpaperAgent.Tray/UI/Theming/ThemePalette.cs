namespace PrimaryWallpaperAgent.Tray;

internal sealed record ThemePalette(bool Dark, Color Background, Color Surface, Color Text, Color Muted,
    Color Accent, Color Hover, Color Border, Color Success, Color Error)
{
    internal static ThemePalette Light { get; } = new(false, Color.FromArgb(246, 247, 249), Color.White,
        Color.FromArgb(32, 36, 42), Color.FromArgb(90, 99, 112), Color.FromArgb(18, 101, 170),
        Color.FromArgb(230, 237, 246), Color.FromArgb(185, 194, 206), Color.FromArgb(33, 99, 61), Color.FromArgb(175, 40, 58));
    internal static ThemePalette Night { get; } = new(true, Color.FromArgb(30, 32, 36), Color.FromArgb(46, 49, 55),
        Color.FromArgb(236, 239, 244), Color.FromArgb(171, 181, 194), Color.FromArgb(135, 191, 255),
        Color.FromArgb(61, 70, 83), Color.FromArgb(91, 103, 119), Color.FromArgb(133, 217, 160), Color.FromArgb(255, 173, 173));

    internal static ThemePalette Resolve(string mode, bool systemDark, bool highContrast)
    {
        ThemeOptions.Validate(mode);
        if (highContrast) return new(false, SystemColors.Window, SystemColors.Control, SystemColors.WindowText,
            SystemColors.GrayText, SystemColors.Highlight, SystemColors.Highlight, SystemColors.WindowText,
            SystemColors.WindowText, SystemColors.WindowText);
        return mode == "dark" || mode == "system" && systemDark ? Night : Light;
    }
}

internal sealed class ThemeMenuRenderer(ThemePalette palette) : ToolStripProfessionalRenderer(new ThemeMenuColors(palette))
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? palette.Text : palette.Muted;
        base.OnRenderItemText(e);
    }
}

internal sealed class ThemeMenuColors(ThemePalette p) : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => p.Background;
    public override Color MenuStripGradientBegin => p.Background;
    public override Color MenuStripGradientEnd => p.Background;
    public override Color ImageMarginGradientBegin => p.Background;
    public override Color ImageMarginGradientMiddle => p.Background;
    public override Color ImageMarginGradientEnd => p.Background;
    public override Color MenuItemSelected => p.Hover;
    public override Color MenuItemSelectedGradientBegin => p.Hover;
    public override Color MenuItemSelectedGradientEnd => p.Hover;
    public override Color MenuItemBorder => p.Border;
    public override Color MenuBorder => p.Border;
    public override Color SeparatorDark => p.Border;
    public override Color SeparatorLight => p.Border;
}
