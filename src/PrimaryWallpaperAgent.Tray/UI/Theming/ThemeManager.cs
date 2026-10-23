using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security;

namespace PrimaryWallpaperAgent.Tray;

// Changes only this app's controls. Windows preference is read-only; system
// notifications are marshalled back to a live UI control and detached on dispose.
internal sealed class ThemeManager : IDisposable
{
    private readonly Func<bool> systemDark;
    private readonly Func<bool> highContrast;
    private readonly bool listening;
    private readonly List<Control> roots = [];
    private readonly object gate = new();
    private bool disposed;
    internal string Mode { get; private set; }
    internal ThemePalette Palette { get; private set; }
    internal int BoundCount { get { lock (gate) return roots.Count; } }

    internal ThemeManager(string mode = "system", Func<bool>? systemDark = null, Func<bool>? highContrast = null, bool listen = true)
    {
        Mode = ThemeOptions.Validate(mode);
        this.systemDark = systemDark ?? ReadSystemDark;
        this.highContrast = highContrast ?? (() => SystemInformation.HighContrast);
        listening = listen;
        Palette = Resolve();
        if (listening) SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
    }

    internal void SetMode(string mode)
    {
        Mode = ThemeOptions.Validate(mode);
        Refresh();
    }
    internal void Bind(Control root)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (gate)
        {
            if (roots.Contains(root)) return;
            roots.Add(root);
        }
        root.HandleCreated += RootHandleCreated;
        root.Disposed += RootDisposed;
        Refresh();
        Apply(root);
    }
    internal void Refresh()
    {
        if (disposed) return;
        var next = Resolve();
        if (next == Palette) return;
        Palette = next;
        foreach (var root in Snapshot()) if (!root.IsDisposed) Apply(root);
    }
    private ThemePalette Resolve() => ThemePalette.Resolve(Mode, Mode == "system" && systemDark(), highContrast());
    private Control[] Snapshot() { lock (gate) return roots.ToArray(); }
    private void RootHandleCreated(object? sender, EventArgs e)
    {
        Refresh();
        if (sender is Control root) Apply(root);
    }
    private void RootDisposed(object? sender, EventArgs e)
    {
        if (sender is not Control root) return;
        root.HandleCreated -= RootHandleCreated;
        root.Disposed -= RootDisposed;
        lock (gate) roots.Remove(root);
    }
    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => SystemSettingChanged();
    internal void SystemSettingChanged()
    {
        var root = Snapshot().FirstOrDefault(control => control.IsHandleCreated && !control.IsDisposed);
        if (root is null || disposed) return;
        try { root.BeginInvoke((Action)Refresh); }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { }
    }

    private void Apply(Control root)
    {
        ApplyControl(root);
        if (root is Form form && form.IsHandleCreated && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var dark = Palette.Dark ? 1 : 0;
            _ = DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        }
        root.Invalidate(invalidateChildren: true);
    }
    private void ApplyControl(Control control)
    {
        var p = Palette;
        control.BackColor = p.Background;
        control.ForeColor = control.Name is "AgentStatus" or "UpdateStatus" ? p.Accent :
            control.Name == "FeedbackLabel" ? control.Tag is true ? p.Error : p.Success : p.Text;
        switch (control)
        {
            case LinkLabel link:
                link.LinkColor = link.VisitedLinkColor = p.Accent;
                link.ActiveLinkColor = p.Text;
                break;
            case TextBoxBase:
            case ComboBox:
                control.BackColor = p.Surface;
                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.FlatAppearance.BorderColor = p.Border;
                button.FlatAppearance.MouseOverBackColor = p.Hover;
                button.FlatAppearance.MouseDownBackColor = p.Hover;
                button.BackColor = p.Surface;
                if (button.Name.StartsWith("WallpaperTarget", StringComparison.Ordinal))
                {
                    button.FlatAppearance.BorderSize = button.Tag is true ? 2 : 1;
                    button.FlatAppearance.BorderColor = button.Tag is true ? p.Accent : p.Border;
                }
                if (button.Name.StartsWith("RecentImage", StringComparison.Ordinal))
                {
                    button.BackColor = Color.FromArgb(25, 29, 36);
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = button.Tag is true ? p.Accent : p.Border;
                }
                break;
            case CheckBox check:
                check.FlatStyle = FlatStyle.Flat;
                check.UseVisualStyleBackColor = false;
                break;
            case ToolStrip strip:
                strip.Renderer = new ThemeMenuRenderer(p);
                foreach (ToolStripItem item in strip.Items)
                {
                    item.BackColor = p.Background;
                    item.ForeColor = item.Enabled ? p.Text : p.Muted;
                    if (item is ToolStripControlHost host) ApplyControl(host.Control);
                }
                break;
        }
        if (control is ThemeComboBox combo) combo.BorderColor = p.Border;
        if (control is RecentImageStrip recent) recent.ApplyPalette(p);
        foreach (Control child in control.Controls) ApplyControl(child);
    }
    private static bool ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (listening) SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        foreach (var root in Snapshot())
        {
            root.HandleCreated -= RootHandleCreated;
            root.Disposed -= RootDisposed;
        }
        lock (gate) roots.Clear();
    }
}
