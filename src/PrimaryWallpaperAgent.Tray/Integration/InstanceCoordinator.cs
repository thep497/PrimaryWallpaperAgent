using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PrimaryWallpaperAgent.Tray;

// One tray instance per user/session. WallpaperWriteLease still protects native writers and older tools.
internal sealed class InstanceCoordinator : IDisposable
{
    private readonly Mutex gate;
    private readonly CancellationTokenSource stop = new();
    private NamedPipeServerStream? server;
    private Task? listener;
    private bool disposed;
    internal bool IsPrimary { get; }
    internal Exception? LastError { get; private set; }
    internal string Channel { get; }

    internal static string DefaultChannel
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User?.Value ?? throw new IOException("User identity unavailable.");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..24];
            using var process = Process.GetCurrentProcess();
            return "PrimaryWallpaperAgent.Tray." + process.SessionId + "." + hash;
        }
    }

    internal InstanceCoordinator(string? channel = null)
    {
        Channel = channel ?? DefaultChannel;
        gate = new Mutex(false, @"Local\" + Channel);
        try { IsPrimary = gate.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    internal void Listen(Action requestSettings)
    {
        if (!IsPrimary || server is not null) throw new InvalidOperationException("Only the primary may listen once.");
        server = new NamedPipeServerStream(Channel, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 128, 128);
        var pipe = server;
        var token = stop.Token;
        listener = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    var command = new byte[1];
                    await pipe.ReadExactlyAsync(command, timeout.Token).ConfigureAwait(false);
                    if (command[0] == 1) requestSettings(); // Callback queues onto the UI thread; it never blocks for a modal form.
                    await pipe.WriteAsync(new byte[] { command[0] is 1 or 2 ? (byte)1 : (byte)0 }, timeout.Token).ConfigureAwait(false);
                    await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
                    // Disconnect discards unread pipe data on Windows. Wait for the client to consume the reply.
                    await pipe.ReadExactlyAsync(new byte[1], timeout.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
                { LastError = error; if (token.IsCancellationRequested) break; }
                finally
                {
                    try { pipe.Disconnect(); }
                    catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
                }
            }
        }, token);
    }

    internal async Task<bool> NotifyAsync(bool showSettings, int timeoutMs = 5000)
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        try
        {
            using var pipe = new NamedPipeClientStream(".", Channel, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (showSettings && GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
                _ = AllowSetForegroundWindow(processId);
            await pipe.WriteAsync(new byte[] { showSettings ? (byte)1 : (byte)2 }, timeout.Token).ConfigureAwait(false);
            var answer = new byte[1];
            await pipe.ReadExactlyAsync(answer, timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync(new byte[] { 0xAC }, timeout.Token).ConfigureAwait(false);
            return answer[0] == 1;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
        { LastError = error; return false; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Cancel();
        server?.Dispose();
        try { listener?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        stop.Dispose();
        if (IsPrimary) gate.ReleaseMutex();
        gate.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
