using System.Runtime.InteropServices;

namespace PrimaryWallpaperAgent;

internal static class NativeMethods
{
    internal const uint MonitorInfoPrimary = 0x1;
    internal const uint DeviceInterfaceName = 0x1;
    internal const uint MonitorActive = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly Bounds ToBounds() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        public uint Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect rect, nint data);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
}

// Native COM slot order must include setters between getters. These declarations
// describe the ABI. The default probe never calls setters. Only explicit Phase 2
// trial commands call SetWallpaper for individual nonempty monitor IDs.
// Source: Microsoft's ShObjIdl_core.h, IDesktopWallpaper.
[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string path);
    [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string path);
    [PreserveSig] int GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
    [PreserveSig] int GetMonitorDevicePathCount(out uint count);
    [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out NativeMethods.Rect rect);
    [PreserveSig] int SetBackgroundColor(uint color);
    [PreserveSig] int GetBackgroundColor(out uint color);
    [PreserveSig] int SetPosition(uint position);
    [PreserveSig] int GetPosition(out uint position);
    [PreserveSig] int SetSlideshow(nint items);
    [PreserveSig] int GetSlideshow(out nint items);
    [PreserveSig] int SetSlideshowOptions(uint options, uint tick);
    [PreserveSig] int GetSlideshowOptions(out uint options, out uint tick);
    [PreserveSig] int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, uint direction);
    [PreserveSig] int GetStatus(out uint state);
}
