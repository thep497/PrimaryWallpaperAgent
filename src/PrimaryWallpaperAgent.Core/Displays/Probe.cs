using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PrimaryWallpaperAgent;

internal sealed record Bounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public override string ToString() => $"{Width}x{Height} at ({Left},{Top})";
}

internal sealed record MonitorEndpoint(string Name, string Description, string InterfacePath, uint StateFlags);
internal sealed record DisplayInfo(string GdiName, bool IsPrimary, Bounds Bounds, MonitorEndpoint[] Endpoints);
internal sealed record WallpaperInfo(uint Index, string MonitorId, int? RectHResult, Bounds? Bounds,
    int? WallpaperHResult, string? WallpaperPath, string MatchStatus, string? GdiName,
    bool? IsPrimary, bool? RectMatches)
{
    public bool? Attached => RectHResult switch { 0 => true, 1 => false, _ => null };
}

internal sealed record ProbeResult(DateTimeOffset CapturedAt, string WindowsVersion, string Runtime,
    int SessionId, bool ReadOnly, bool TopologyStable, string? BackgroundRgb, uint? Position,
    uint? SlideshowStatus, DisplayInfo[] Displays, WallpaperInfo[] Wallpapers, string[] Issues)
{
    public bool Complete => Issues.Length == 0;
}

internal static class Probe
{
    internal static ProbeResult Capture()
    {
        if (Marshal.SizeOf<NativeMethods.MonitorInfoEx>() != 104 ||
            Marshal.SizeOf<NativeMethods.DisplayDevice>() != 840 || Marshal.SizeOf<NativeMethods.Rect>() != 16)
            throw new InvalidOperationException("Unexpected native struct sizes; refusing to query Windows.");

        var capturedAt = DateTimeOffset.Now;
        var issues = new List<string>();
        var displays = ReadDisplays();
        if (displays.Length == 0) issues.Add("No active GDI displays found in this session.");
        if (displays.Count(d => d.IsPrimary) != 1) issues.Add("Expected exactly one Windows primary display.");
        var wallpapers = new List<WallpaperInfo>();
        uint? position = null, status = null;
        string? background = null;
        object? comObject = null;
        try
        {
            var comType = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)!;
            comObject = Activator.CreateInstance(comType) ?? throw new InvalidOperationException("COM activation returned null.");
            var api = (IDesktopWallpaper)comObject;
            RequireOk(api.GetMonitorDevicePathCount(out var count), "GetMonitorDevicePathCount");
            for (uint index = 0; index < count; index++)
            {
                RequireOk(api.GetMonitorDevicePathAt(index, out var id), "GetMonitorDevicePathAt");
                if (string.IsNullOrWhiteSpace(id))
                {
                    issues.Add($"Monitor {index}: API returned an empty monitor ID; per-monitor queries were skipped.");
                    wallpapers.Add(new(index, id, null, null, null, null, "InvalidMonitorId", null, null, null));
                    continue;
                }
                var rectHr = api.GetMonitorRECT(id, out var rect);
                var wallpaperHr = api.GetWallpaper(id, out var path);
                if (rectHr is not (0 or 1)) issues.Add($"Monitor {index}: GetMonitorRECT returned {Hex(rectHr)}.");
                if (wallpaperHr != 0) issues.Add($"Monitor {index}: GetWallpaper returned {Hex(wallpaperHr)}.");

                // IDs are compared as device interface paths, never by index, brand or rectangle.
                var matches = displays.Where(d => d.Endpoints.Any(e =>
                    StringComparer.OrdinalIgnoreCase.Equals(e.InterfacePath, id))).ToArray();
                var match = matches.Length == 1 ? matches[0] : null;
                var matchStatus = matches.Length switch { 0 => "Unmatched", 1 => "ExactDevicePath", _ => "Ambiguous" };
                var bounds = rectHr == 0 ? rect.ToBounds() : null;
                bool? rectMatches = match is not null && bounds is not null ? match.Bounds == bounds : null;
                if (rectHr == 0 && match is null) issues.Add($"Attached wallpaper monitor {index}: {matchStatus} device-path mapping.");
                if (rectMatches == false) issues.Add($"Monitor {index}: GDI and wallpaper rectangles differ.");
                if (rectHr == 1 && match is not null) issues.Add($"Monitor {index}: detached in wallpaper API but present in GDI.");
                if (match is not null && !match.Endpoints.Any(e =>
                    StringComparer.OrdinalIgnoreCase.Equals(e.InterfacePath, id) &&
                    (e.StateFlags & NativeMethods.MonitorActive) != 0))
                    issues.Add($"Monitor {index}: matched endpoint is not marked active by GDI.");
                wallpapers.Add(new(index, id, rectHr, bounds, wallpaperHr,
                    wallpaperHr == 0 ? path : null, matchStatus, match?.GdiName, match?.IsPrimary, rectMatches));
            }
            var colorHr = api.GetBackgroundColor(out var color);
            if (colorHr == 0) background = $"#{color & 0xff:X2}{(color >> 8) & 0xff:X2}{(color >> 16) & 0xff:X2}";
            else issues.Add($"GetBackgroundColor returned {Hex(colorHr)}.");
            var positionHr = api.GetPosition(out var rawPosition);
            if (positionHr == 0) position = rawPosition;
            else issues.Add($"GetPosition returned {Hex(positionHr)}.");
            var statusHr = api.GetStatus(out var rawStatus);
            if (statusHr == 0) status = rawStatus;
            else issues.Add($"GetStatus returned {Hex(statusHr)}.");
        }
        finally
        {
            // Balance this activation only. FinalRelease would invalidate shared
            // RCWs held by another caller in the same apartment.
            if (comObject is not null && Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
        }

        foreach (var display in displays)
        {
            if (!wallpapers.Any(w => w.Attached == true && w.GdiName == display.GdiName))
                issues.Add($"{display.GdiName}: no attached wallpaper monitor mapped by device path.");
        }
        if (wallpapers.Count(w => w.Attached == true && w.IsPrimary == true) != 1)
            issues.Add("Expected exactly one attached primary wallpaper monitor; clone/ambiguous layouts need review.");
        if (wallpapers.Select(w => w.MonitorId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != wallpapers.Count)
            issues.Add("Wallpaper API returned duplicate monitor IDs.");

        var topologyStable = JsonSerializer.Serialize(displays) == JsonSerializer.Serialize(ReadDisplays());
        if (!topologyStable) issues.Add("Display topology changed during capture; wait for Windows to settle and rerun.");
        return new(capturedAt, RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
            System.Diagnostics.Process.GetCurrentProcess().SessionId, true, topologyStable,
            background, position, status, displays, wallpapers.ToArray(), issues.ToArray());
    }

    private static DisplayInfo[] ReadDisplays()
    {
        var displays = new List<DisplayInfo>();
        Exception? callbackError = null;
        NativeMethods.MonitorEnumProc callback = (nint monitor, nint hdc, ref NativeMethods.Rect rect, nint data) =>
        {
            try
            {
                var info = new NativeMethods.MonitorInfoEx { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfoEx>(), DeviceName = "" };
                if (!NativeMethods.GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var endpoints = new List<MonitorEndpoint>();
                for (uint index = 0; ; index++)
                {
                    var device = new NativeMethods.DisplayDevice { Size = (uint)Marshal.SizeOf<NativeMethods.DisplayDevice>(),
                        DeviceName = "", DeviceString = "", DeviceId = "", DeviceKey = "" };
                    if (!NativeMethods.EnumDisplayDevices(info.DeviceName, index, ref device, NativeMethods.DeviceInterfaceName)) break;
                    endpoints.Add(new(device.DeviceName, device.DeviceString, device.DeviceId, device.StateFlags));
                }
                displays.Add(new(info.DeviceName, (info.Flags & NativeMethods.MonitorInfoPrimary) != 0,
                    info.Monitor.ToBounds(), endpoints.OrderBy(e => e.InterfacePath, StringComparer.OrdinalIgnoreCase).ToArray()));
                return true;
            }
            catch (Exception exception)
            {
                callbackError = exception;
                return false; // Never let a managed exception cross the unmanaged callback boundary.
            }
        };
        var success = NativeMethods.EnumDisplayMonitors(0, 0, callback, 0);
        if (callbackError is not null) throw new InvalidOperationException("Display enumeration failed.", callbackError);
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors failed.");
        return displays.OrderBy(d => d.GdiName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void RequireOk(int hr, string method)
    {
        if (hr != 0) throw new COMException($"{method} returned {Hex(hr)}.", hr);
    }

    internal static string Hex(int hr) => $"0x{unchecked((uint)hr):X8}";
    internal static string Hex(int? hr) => hr.HasValue ? Hex(hr.Value) : "<not called>";
}
