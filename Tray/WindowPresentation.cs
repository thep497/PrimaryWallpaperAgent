using System.Runtime.InteropServices;

namespace PrimaryWallpaperAgent.Tray;

internal static class WindowPresentation
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    internal static bool IsVisible(Form form) => form.IsHandleCreated && IsWindowVisible(form.Handle);

    internal static void Reveal(Form form)
    {
        // StartupInfo(SW_HIDE) may suppress WinForms' initial ShowDialog call.
        // This explicit later call must run after that first show was processed.
        ShowWindow(form.Handle, IsIconic(form.Handle) ? 9 /* SW_RESTORE */ : 5 /* SW_SHOW */);
        form.BringToFront();
        form.Activate();
        if (!IsVisible(form)) throw new InvalidOperationException("Windows ยังไม่แสดงหน้าตั้งค่า กรุณาปิดและเปิดโปรแกรมใหม่ค่ะ");
    }
}
