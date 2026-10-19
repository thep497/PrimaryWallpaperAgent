using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PrimaryWallpaperAgent;

internal sealed record ObservedDesktopEvent(long Sequence, double ReceivedMs, string Source, uint Code,
    string WindowToken, uint? Action, int? ObjectId, int? ChildId, uint? EventThread,
    uint? GeneratedUptimeMs, uint? DeliveryAgeMs, bool Synthetic);

internal static class WallpaperEventProbe
{
    internal static int Run(int seconds, bool selfTest = false)
    {
        if (seconds is < 3 or > 300) throw new ArgumentOutOfRangeException(nameof(seconds), "Use 3-300 seconds.");
        var root = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(root, "PrimaryWallpaperAgent.slnx")))
            throw new InvalidOperationException("Run event-probe commands from the project directory.");
        // Exclude this application's writers during measurement without calling
        // any setter. Windows itself remains free to switch desktops/settings.
        using var writeExclusion = new WallpaperWriteLease();
        var runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        var folder = Path.Combine(root, "snapshots", "event-probe-" + runId);
        Directory.CreateDirectory(folder);
        using var eventWriter = NewWriter(Path.Combine(folder, "events.jsonl"));
        using var sampleWriter = NewWriter(Path.Combine(folder, "samples.jsonl"));
        using var stateWriter = NewWriter(Path.Combine(folder, "states.jsonl"));
        var clock = Stopwatch.StartNew();
        var startedAt = DateTimeOffset.Now;
        using var observer = new DesktopEventObserver(clock);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        var sourceCounts = new Dictionary<string, int>();
        var sequence = 0;
        var stateCount = 0;
        string? lastHash = null;
        var nextSampleAt = 0.0;
        var burstUntil = 0.0;
        var lastEventSequence = 0L;
        var lastEventReceivedMs = 0.0;
        var selfTestDelivered = false;
        var reason = "Duration elapsed";
        var exitCode = 0;
        ProbeResult? lastSnapshot = null;
        Console.WriteLine($"EVENT_PROBE_READY: {seconds} seconds; READ ONLY; hooks registered={observer.HookCount}.");
        Console.WriteLine("No wallpaper setters, registry reads or external DLLs. This process only observes events and writes project evidence.");
        Console.WriteLine("Switch desktops manually, wait a few seconds on each, then return. Ctrl+C stops the observer.");
        Console.WriteLine($"Evidence: {folder}");
        try
        {
            if (selfTest)
            {
                // Exercise our own hidden-window callback after a GC. The private
                // message is sent only to this process; no system event is broadcast.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                observer.PostLocalSelfTest();
            }
            while (clock.Elapsed.TotalSeconds < seconds && !cancellation.IsCancellationRequested)
            {
                if (!observer.Pump()) { reason = "WM_QUIT received"; break; }
                var sawEvent = false;
                while (observer.TryRead(out var desktopEvent))
                {
                    if (desktopEvent is null) continue;
                    eventWriter.WriteLine(JsonSerializer.Serialize(desktopEvent));
                    sourceCounts[desktopEvent.Source] = sourceCounts.GetValueOrDefault(desktopEvent.Source) + 1;
                    lastEventSequence = desktopEvent.Sequence;
                    lastEventReceivedMs = desktopEvent.ReceivedMs;
                    selfTestDelivered |= desktopEvent.Synthetic;
                    burstUntil = clock.Elapsed.TotalMilliseconds + 2000;
                    sawEvent = true;
                    Console.WriteLine($"EVENT {desktopEvent.Sequence}: {desktopEvent.Source} at {desktopEvent.ReceivedMs:F1} ms");
                }
                var now = clock.Elapsed.TotalMilliseconds;
                // Drain a burst into one immediate sample, then sample every 100 ms
                // for 2 seconds. Idle baseline is 1 second; all COM calls stay on STA.
                if (sawEvent) nextSampleAt = Math.Min(nextSampleAt, now);
                if (now >= nextSampleAt)
                {
                    var sampleStarted = clock.Elapsed.TotalMilliseconds;
                    var snapshot = Probe.Capture();
                    var sampleEnded = clock.Elapsed.TotalMilliseconds;
                    lastSnapshot = snapshot;
                    var fingerprint = WallpaperGuard.Fingerprint(snapshot);
                    var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint)));
                    var changed = lastHash is not null && hash != lastHash;
                    sequence++;
                    sampleWriter.WriteLine(JsonSerializer.Serialize(new { Sequence = sequence, CaptureStartedMs = sampleStarted,
                        CaptureCompletedMs = sampleEnded, StateHash = hash, snapshot.TopologyStable,
                        LastEventSequence = lastEventSequence, LastEventReceivedMs = lastEventSequence == 0 ? (double?)null : lastEventReceivedMs }));
                    if (hash != lastHash)
                    {
                        stateCount++;
                        stateWriter.WriteLine(JsonSerializer.Serialize(new { StateNumber = stateCount, SampleSequence = sequence,
                            CaptureStartedMs = sampleStarted, CaptureCompletedMs = sampleEnded, StateHash = hash, Snapshot = snapshot }));
                        Console.WriteLine($"{(changed ? "STATE_CHANGED" : "BASELINE")}: sample={sequence}; capture={sampleStarted:F1}-{sampleEnded:F1} ms; active={snapshot.Displays.Length}");
                        lastHash = hash;
                    }
                    nextSampleAt = sampleEnded + (sampleEnded < burstUntil ? 100 : 1000);
                }
                Thread.Sleep(10); // Pump on the same thread; never block a WinEvent callback with COM/file work.
            }
            if (cancellation.IsCancellationRequested) reason = "Stopped with Ctrl+C";
            if (selfTest && !selfTestDelivered) throw new InvalidOperationException("Hidden-window self-test message was not delivered.");
            if (selfTest && (observer.CallbackErrors != 0 || observer.DroppedEvents != 0))
                throw new InvalidOperationException("Observer self-test recorded callback errors or dropped events.");
        }
        catch (Exception error)
        {
            reason = error.ToString();
            exitCode = 1;
            Console.Error.WriteLine("EVENT PROBE ERROR: " + reason);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            var summary = new { StartedAt = startedAt, SecondsRequested = seconds, SecondsElapsed = clock.Elapsed.TotalSeconds,
                Samples = sequence, DistinctStates = stateCount, ObservedStateTransitions = Math.Max(0, stateCount - 1),
                HookCount = observer.HookCount, EventCounts = sourceCounts, DroppedEvents = observer.DroppedEvents,
                CallbackErrors = observer.CallbackErrors, ReadOnlyWindowsSettings = true, WallpaperSetterCalls = 0,
                VirtualDesktopIdDetected = false, SyntheticSelfTest = selfTest, SelfTestMessageDelivered = selfTestDelivered,
                TimingMeaning = "State changes occurred somewhere between sampled reads; events are candidates, not proof of causation or visible rendering completion",
                LastSnapshot = lastSnapshot, StopReason = reason, ExitCode = exitCode };
            File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"EVENT_PROBE_EXIT: samples={sequence}; state transitions={Math.Max(0, stateCount - 1)}; events={sourceCounts.Values.Sum()}; dropped={observer.DroppedEvents}; {reason}");
        }
        return exitCode;
    }

    private static StreamWriter NewWriter(string path) => new(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
}

internal sealed class DesktopEventObserver : IDisposable
{
    private readonly Stopwatch clock;
    private readonly ConcurrentQueue<ObservedDesktopEvent> events = new();
    private readonly List<nint> hooks = [];
    private readonly EventProbeNative.WinEventProc winEventCallback;
    private readonly EventProbeNative.WindowProc windowCallback;
    private GCHandle winEventRoot;
    private GCHandle windowRoot;
    private readonly string className = "PrimaryWallpaperAgent.EventProbe." + Guid.NewGuid().ToString("N");
    private readonly nint module;
    private nint window;
    private ushort classAtom;
    private long eventSequence;
    private bool disposed;
    internal int HookCount => hooks.Count;
    internal int DroppedEvents { get; private set; }
    internal int CallbackErrors { get; private set; }
    private const uint SelfTestMessage = 0x8001;

    internal DesktopEventObserver(Stopwatch clock)
    {
        this.clock = clock;
        winEventCallback = OnWinEvent;
        windowCallback = OnWindowMessage;
        winEventRoot = GCHandle.Alloc(winEventCallback);
        windowRoot = GCHandle.Alloc(windowCallback);
        module = EventProbeNative.GetModuleHandle(null);
        try
        {
            var windowClass = new EventProbeNative.WindowClass
            {
                Size = (uint)Marshal.SizeOf<EventProbeNative.WindowClass>(), Instance = module,
                Procedure = Marshal.GetFunctionPointerForDelegate(windowCallback), ClassName = className
            };
            classAtom = EventProbeNative.RegisterClassEx(ref windowClass);
            if (classAtom == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassExW failed.");
            // Hidden top-level window: message-only windows do not receive broadcasts.
            window = EventProbeNative.CreateWindowEx(0x08000080, className, "", 0, 0, 0, 0, 0, 0, 0, module, 0);
            if (window == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowExW failed.");
            AddHook(0x0003, 0x0003); // EVENT_SYSTEM_FOREGROUND: candidate only.
            AddHook(0x0020, 0x0020); // EVENT_SYSTEM_DESKTOPSWITCH: not assumed to mean Virtual Desktop.
            AddHook(0x8017, 0x8018); // EVENT_OBJECT_CLOAKED / UNCLOAKED.
        }
        catch { Dispose(); throw; }
    }

    private void AddHook(uint minimum, uint maximum)
    {
        var hook = EventProbeNative.SetWinEventHook(minimum, maximum, 0, winEventCallback, 0, 0, 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWinEventHook failed.");
        hooks.Add(hook);
    }

    private void OnWinEvent(nint hook, uint code, nint hwnd, int objectId, int childId, uint threadId, uint eventTime)
    {
        try
        {
            if (code is 0x8017 or 0x8018 && objectId != 0) return;
            var source = code switch { 0x0003 => "EVENT_SYSTEM_FOREGROUND", 0x0020 => "EVENT_SYSTEM_DESKTOPSWITCH",
                0x8017 => "EVENT_OBJECT_CLOAKED", 0x8018 => "EVENT_OBJECT_UNCLOAKED", _ => "UNKNOWN_WINEVENT" };
            var age = eventTime == 0 ? (uint?)null : unchecked((uint)Environment.TickCount - eventTime);
            Enqueue(source, code, hwnd, null, objectId, childId, threadId, eventTime, age, false);
        }
        catch { CallbackErrors++; } // No managed exception may cross the native callback boundary.
    }

    private nint OnWindowMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            var source = message switch { 0x001a => "WM_SETTINGCHANGE", 0x007e => "WM_DISPLAYCHANGE",
                0x031a => "WM_THEMECHANGED", SelfTestMessage => "LOCAL_SELF_TEST", _ => null };
            if (source is not null)
                Enqueue(source, message, hwnd, unchecked((uint)wParam), null, null, null, null, null, message == SelfTestMessage);
            // Do not read lParam strings or window titles/content.
        }
        catch { CallbackErrors++; }
        return EventProbeNative.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void Enqueue(string source, uint code, nint hwnd, uint? action, int? objectId, int? childId,
        uint? threadId, uint? generated, uint? age, bool synthetic)
    {
        if (events.Count >= 10000) { DroppedEvents++; return; }
        events.Enqueue(new(++eventSequence, clock.Elapsed.TotalMilliseconds, source, code,
            $"0x{unchecked((nuint)hwnd):X}", action, objectId, childId, threadId, generated, age, synthetic));
    }

    internal bool TryRead(out ObservedDesktopEvent? desktopEvent) => events.TryDequeue(out desktopEvent);

    internal bool Pump()
    {
        for (var count = 0; count < 200 && EventProbeNative.PeekMessage(out var message, 0, 0, 0, 1); count++)
        {
            if (message.Message == 0x0012) return false;
            EventProbeNative.DispatchMessage(ref message);
        }
        return true;
    }

    internal void PostLocalSelfTest()
    {
        if (!EventProbeNative.PostMessage(window, SelfTestMessage, 0, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Local self-test message failed.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var hook in hooks) EventProbeNative.UnhookWinEvent(hook);
        if (window != 0) EventProbeNative.DestroyWindow(window);
        if (classAtom != 0) EventProbeNative.UnregisterClass(className, module);
        if (winEventRoot.IsAllocated) winEventRoot.Free();
        if (windowRoot.IsAllocated) windowRoot.Free();
        GC.KeepAlive(winEventCallback);
        GC.KeepAlive(windowCallback);
    }
}

internal static class EventProbeNative
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WinEventProc(nint hook, uint code, nint hwnd, int objectId, int childId, uint threadId, uint eventTime);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MessageData
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", ExactSpelling = true)]
    internal static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    internal static extern nint SetWinEventHook(uint minimum, uint maximum, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(out MessageData message, nint hwnd, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", ExactSpelling = true)]
    internal static extern nint DispatchMessage(ref MessageData message);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string className, nint instance);
}
