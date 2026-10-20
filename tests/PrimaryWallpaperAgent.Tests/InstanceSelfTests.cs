using System.Diagnostics;
using System.IO.Pipes;
using PrimaryWallpaperAgent.Tray;

namespace PrimaryWallpaperAgent.Tests;

internal static class InstanceSelfTests
{
    internal static int Client(string channel, string command)
    {
        using var instance = new InstanceCoordinator(channel);
        if (instance.IsPrimary) return 41;
        var result = instance.NotifyAsync(command == "show", timeoutMs: command == "unavailable" ? 300 : 5000).GetAwaiter().GetResult();
        if (result != (command != "unavailable")) Console.Error.WriteLine(instance.LastError?.ToString() ?? "Activation rejected");
        return result == (command != "unavailable") ? 0 : 42;
    }

    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "instance-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        var store = new SettingsStore(folder);
        var bytes = FirstRun.Open(store);
        var config = SettingsStore.Parse(bytes) with { PollIntervalMs = 500 };
        bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(config, SettingsStore.JsonOptions);
        store.Write(bytes);
        var desktop = new TrialSelfTests.FakeDesktop(3);
        var startup = new FakeStartup();
        var channel = "PrimaryWallpaperAgent.Tests." + Guid.NewGuid().ToString("N");
        using var instance = new InstanceCoordinator(channel);
        Assert(instance.IsPrimary, "First instance acquires isolated gate");
        RunClient("unavailable"); // Gate exists but listener is not ready; failure is bounded and creates no second agent.
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        using var context = new AgentContext(store, config, bytes, false, false, desktop, startup);
        instance.Listen(() => dispatcher.BeginInvoke((Action)context.RequestSettings));
        RunClient("startup");
        Application.DoEvents();
        Assert(!Application.OpenForms.OfType<ConfigForm>().Any(), "Duplicate startup does not open Settings");

        // Invalid/disconnected clients must not disable subsequent legitimate activation.
        using (var malformed = new NamedPipeClientStream(".", channel, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            malformed.Connect(2000);
            malformed.WriteByte(255);
            Assert(malformed.ReadByte() == 0, "Unknown command rejected");
        }
        using (var disconnected = new NamedPipeClientStream(".", channel, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            disconnected.Connect(2000);

        Exception? failure = null;
        var stage = 0;
        ConfigForm? original = null;
        var watch = Stopwatch.StartNew();
        using var monitor = new System.Windows.Forms.Timer { Interval = 80 };
        monitor.Tick += (_, _) =>
        {
            var form = Application.OpenForms.OfType<ConfigForm>().SingleOrDefault();
            try
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException("Settings activation timed out, stage " + stage);
                if (stage == 0 && form is { Visible: true })
                {
                    original = form;
                    form.WindowState = FormWindowState.Minimized;
                    stage = 1;
                    RunClient("show");
                }
                else if (stage == 1 && form is { WindowState: FormWindowState.Normal, Visible: true })
                {
                    Assert(ReferenceEquals(form, original), "Second activation restores the original Config rather than duplicating");
                    stage = 2;
                    form.Close();
                    dispatcher.BeginInvoke((Action)(() => RunClient("show")));
                }
                else if (stage == 2 && form is { Visible: true } && !ReferenceEquals(form, original))
                {
                    Assert(Application.OpenForms.OfType<ConfigForm>().Count() == 1, "Only one Settings form exists");
                    stage = 3;
                    monitor.Stop();
                    form.Close();
                    context.ExitThread();
                }
            }
            catch (Exception error) { failure = error; monitor.Stop(); form?.Close(); context.ExitThread(); }
        };
        monitor.Start();
        RunClient("show");
        Application.Run(context);
        if (failure is not null) throw failure;
        Assert(stage == 3 && desktop.Calls == 0 && startup.Writes == 0 && store.Read().SequenceEqual(bytes),
            "Cross-process activation preserves settings/wallpaper/startup");
        Console.WriteLine("INSTANCE TESTS PASSED: cross-process gate, startup silence, rejected/disconnected clients, open/restore/reopen Settings, no second agent or settings writes.");
        return 0;

        void RunClient(string command)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("instance-client");
            start.ArgumentList.Add(channel);
            start.ArgumentList.Add(command);
            using var child = Process.Start(start) ?? throw new IOException("Could not launch activation fixture.");
            if (!child.WaitForExit(7000)) { child.Kill(); throw new TimeoutException("Activation fixture timed out."); }
            Assert(child.ExitCode == 0, "Activation child returned " + child.ExitCode + " for " + command + "; client=" + child.StandardError.ReadToEnd() + "; server=" + instance.LastError);
        }
    }

    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("INSTANCE TEST FAILED: " + message); }
    private sealed class FakeStartup : IStartupRegistration
    {
        internal int Writes;
        public string ExpectedCommand => "\"fixture.exe\" --startup";
        public string? Read() => null;
        public void Write(string? command) { Writes++; throw new InvalidOperationException("Startup write forbidden."); }
    }
}
