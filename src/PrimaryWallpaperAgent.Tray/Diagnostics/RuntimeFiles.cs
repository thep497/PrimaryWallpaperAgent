using System.Text.RegularExpressions;

namespace PrimaryWallpaperAgent.Tray;

// One bounded inventory for retention and export. Never recurse or follow links.
internal static partial class RuntimeFiles
{
    [GeneratedRegex(@"^\d{8}_\d{6}_\d{3}-[0-9a-f]{8}$", RegexOptions.CultureInvariant)]
    internal static partial Regex SessionName();
    [GeneratedRegex(@"^(agent(\.previous)?\.log|summary\.json|(before|after)-\d+\.json|config-[0-9a-f]{8}-(settings-before|plan-before|state-after)|event-\d{8}T\d{9}Z-[0-9a-f]{32}-[a-z0-9-]+\.json)$", RegexOptions.CultureInvariant)]
    internal static partial Regex ManagedFile();
    [GeneratedRegex(@"^agent-\d{8}(_\d{3,})?\.log$", RegexOptions.CultureInvariant)]
    internal static partial Regex CentralLog();

    internal static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    internal static string Runtime(string dataRoot)
    {
        var runtime = Path.GetFullPath(Path.Combine(dataRoot, "runtime"));
        if (Directory.Exists(runtime) && IsLink(runtime)) throw new IOException("Runtime must not redirect to another directory.");
        return runtime;
    }
    internal static string Logs(string dataRoot)
    {
        var logs = Path.Combine(Runtime(dataRoot), "logs");
        if (Directory.Exists(logs) && IsLink(logs)) throw new IOException("Log directory must not redirect.");
        return logs;
    }
    internal static List<FileInfo> List(string dataRoot)
    {
        var root = Runtime(dataRoot);
        var files = new List<FileInfo>();
        if (!Directory.Exists(root)) return files;
        foreach (var file in Directory.EnumerateFiles(root, "startup-error*.log"))
            if (Path.GetFileName(file) is "startup-error.log" or "startup-error.previous.log") Add(file);
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (IsLink(folder)) continue;
            var name = Path.GetFileName(folder);
            if (name != "logs" && !SessionName().IsMatch(name)) continue;
            foreach (var file in Directory.EnumerateFiles(folder))
                if (name == "logs" ? CentralLog().IsMatch(Path.GetFileName(file)) : ManagedFile().IsMatch(Path.GetFileName(file))) Add(file);
        }
        return files;
        void Add(string file)
        {
            try { if (!IsLink(file)) files.Add(new FileInfo(file)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
