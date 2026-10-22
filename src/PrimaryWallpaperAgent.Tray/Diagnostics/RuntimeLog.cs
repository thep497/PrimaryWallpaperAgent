using System.Globalization;
using Serilog;
using Serilog.Events;

namespace PrimaryWallpaperAgent.Tray;

internal sealed class RuntimeLog : IDisposable
{
    private readonly Serilog.Core.Logger logger;
    internal string RunId { get; }
    internal string Folder { get; }
    internal RuntimeLog(string dataRoot, string runId, string component = "Agent")
    {
        RunId = runId;
        Folder = RuntimeFiles.Logs(dataRoot);
        Directory.CreateDirectory(Folder);
        // Refuse pre-existing file redirects before letting the file sink open them.
        if (Directory.EnumerateFiles(Folder).Any(p => RuntimeFiles.CentralLog().IsMatch(Path.GetFileName(p)) && RuntimeFiles.IsLink(p)))
            throw new IOException("A managed log file is a link.");
        logger = new LoggerConfiguration().MinimumLevel.Debug()
            .Enrich.WithProperty("RunId", runId).Enrich.WithProperty("Version", BuildInfo.Version)
            .Enrich.WithProperty("Component", component)
            .WriteTo.File(Path.Combine(Folder, "agent-.log"), rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: LogRetention.MaxLogBytes, rollOnFileSizeLimit: true,
                retainedFileCountLimit: null, retainedFileTimeLimit: TimeSpan.FromDays(LogRetention.RetentionDays),
                shared: true, buffered: false,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:O} [{Level:u3}] [v{Version} run={RunId} component={Component} event={EventId}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
    internal string CurrentFile => Directory.EnumerateFiles(Folder, "agent-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "*.log")
        .Where(p => RuntimeFiles.CentralLog().IsMatch(Path.GetFileName(p)) && !RuntimeFiles.IsLink(p))
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).LastOrDefault() ??
        Path.Combine(Folder, "agent-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
    internal void Write(string message)
    {
        if (message.Length > 16000) message = message[..16000] + " [truncated]";
        var code = message.Split([' ', ';', ':'], 2)[0];
        var level = code.Contains("ERROR", StringComparison.Ordinal) ? LogEventLevel.Error :
            code is "PAUSED" or "MAPPING_WAIT" or "MAPPING_STILL_WAITING" ? LogEventLevel.Warning : LogEventLevel.Information;
        logger.ForContext("EventId", code).Write(level, "{Detail}", message);
    }
    internal static void StartupError(string dataRoot, Exception error)
    {
        using var log = new RuntimeLog(dataRoot, Guid.NewGuid().ToString("N"), "Startup");
        log.Write("STARTUP_ERROR " + error);
        LogRetention.Prune(dataRoot, log.CurrentFile, DateTime.UtcNow);
    }
    public void Dispose() => logger.Dispose();
}
