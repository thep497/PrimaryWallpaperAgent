using Microsoft.Win32;

namespace PrimaryWallpaperAgent.Tray;

// Reserve the footer first. Re-measure text after native DPI changes; use a
// scrollable content viewport only when the current window cannot fit the measured content.
internal sealed class SettingsSizing : IDisposable
{
    private readonly Form form;
    private readonly Control content;
    private readonly Control footer;
    private readonly Panel viewport;
    private readonly Func<Rectangle> area;
    private readonly Action? measure;
    private readonly Action<string>? report;
    private bool adjusting;
    private bool queued;
    private bool opened;
    private bool fitHeight = true;
    internal Size RequiredClientSize { get; private set; }

    internal SettingsSizing(Form form, Control content, Control footer, Panel viewport,
        Func<Rectangle>? area = null, Action? measure = null, Action<string>? report = null)
    {
        this.form = form; this.content = content; this.footer = footer; this.viewport = viewport;
        this.area = area ?? (() => Screen.FromControl(form).WorkingArea);
        this.measure = measure; this.report = report;
        form.Load += Loaded; form.DpiChanged += DpiChanged; form.ResizeEnd += ResizeEnd; form.ResizeBegin += ResizeBegin; form.ClientSizeChanged += ClientSizeChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }
    private void Loaded(object? sender, EventArgs e) => Refresh("open");
    private void DpiChanged(object? sender, DpiChangedEventArgs e) => Queue("dpi-change");
    private void ResizeBegin(object? sender, EventArgs e) => fitHeight = false;
    private void ResizeEnd(object? sender, EventArgs e) => Refresh("resize");
    private void ClientSizeChanged(object? sender, EventArgs e) { if (opened && form.Visible) Refresh("resize-live"); }
    private void DisplayChanged(object? sender, EventArgs e) => Queue("display-change");
    private void Queue(string reason)
    {
        if (queued || form.IsDisposed || !form.IsHandleCreated) return;
        try
        {
            queued = true;
            form.BeginInvoke((Action)(() => { queued = false; if (!form.IsDisposed) Refresh(reason); }));
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { queued = false; }
    }
    internal void Refresh(string reason)
    {
        if (adjusting || form.IsDisposed || form.WindowState == FormWindowState.Minimized) return;
        adjusting = true;
        try
        {
            if (reason == "resize") fitHeight = false;
            var work = area();
            if (work.Width < 100 || work.Height < 100) return;
            measure?.Invoke();
            var chrome = new Size(form.Width - form.ClientSize.Width, form.Height - form.ClientSize.Height);
            var maxClient = new Size(Math.Max(100, work.Width - chrome.Width), Math.Max(100, work.Height - chrome.Height));
            var minimumWidth = Math.Min(maxClient.Width, Math.Max((int)Math.Ceiling(600d * form.DeviceDpi / 96), footer.GetPreferredSize(Size.Empty).Width));
            var width = Math.Clamp(form.ClientSize.Width, minimumWidth, maxClient.Width);
            viewport.AutoScroll = false;
            content.Width = width;
            content.PerformLayout();
            var footerHeight = footer.GetPreferredSize(new Size(width, 0)).Height;
            var needed = content.GetPreferredSize(new Size(width, 0)).Height + footerHeight;
            // The minimum is a usable viewport, not the preferred height of all
            // content. Fit once on open; subsequent resizes belong to the user.
            var minimumHeight = Math.Min(maxClient.Height, (int)Math.Ceiling(320d * form.DeviceDpi / 96));
            form.MinimumSize = new Size(minimumWidth + chrome.Width, minimumHeight + chrome.Height);
            var height = (!opened && reason == "open") || (fitHeight && reason is "dpi-change" or "display-change") ? needed : form.ClientSize.Height;
            form.ClientSize = new Size(width, Math.Clamp(height, minimumHeight, maxClient.Height));
            opened = true;
            viewport.AutoScroll = needed > form.ClientSize.Height;
            form.PerformLayout();
            content.Width = viewport.ClientSize.Width;
            content.PerformLayout();
            needed = content.GetPreferredSize(new Size(content.Width, 0)).Height + footerHeight;
            RequiredClientSize = new(width, needed);
            viewport.AutoScroll = needed > form.ClientSize.Height;
            form.Location = new Point(Math.Clamp(form.Left, work.Left, Math.Max(work.Left, work.Right - form.Width)),
                Math.Clamp(form.Top, work.Top, Math.Max(work.Top, work.Bottom - form.Height)));
            form.PerformLayout();
            if (reason != "resize-live") report?.Invoke($"UI_LAYOUT reason={reason}; dpi={form.DeviceDpi}; client={form.ClientSize.Width}x{form.ClientSize.Height}; required={width}x{needed}; minimum={form.MinimumSize}; content={content.Size}; work={work}; footer={footer.Bounds}; overflow={viewport.AutoScroll}; remote={SystemInformation.TerminalServerSession}");
        }
        finally { adjusting = false; }
    }
    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        form.Load -= Loaded; form.DpiChanged -= DpiChanged; form.ResizeEnd -= ResizeEnd; form.ResizeBegin -= ResizeBegin; form.ClientSizeChanged -= ClientSizeChanged;
    }
}
