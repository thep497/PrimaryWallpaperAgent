using System.Text;
using System.Text.RegularExpressions;

namespace PrimaryWallpaperAgent.Tray;

internal sealed record LogPruneResult(int DeletedFiles, long RemainingBytes, bool WithinBudget);

internal static partial class LogRetention
{
    internal const int RetentionDays = 30;
    internal const long MaxBytes = 50L * 1024 * 1024;
    internal const int MaxLogBytes = 2 * 1024 * 1024;

    [GeneratedRegex(@"^\d{8}_\d{6}_\d{3}-[0-9a-f]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex SessionName();
    [GeneratedRegex(@"^(agent(\.previous)?\.log|summary\.json|(before|after)-\d+\.json|config-[0-9a-f]{8}-(settings-before|plan-before|state-after))$", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedFile();

    internal static bool AppendBounded(string path, string text)
    {
        var rotated = false;
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxLogBytes) bytes = Encoding.UTF8.GetBytes("[Log entry exceeded limit]" + Environment.NewLine);
        var previous = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".previous.log");
        if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > MaxLogBytes)
        {
            // Never follow a replacement link, including one placed at the rotation target.
            if (IsLink(path) || File.Exists(previous) && IsLink(previous)) return false;
            File.Move(path, previous, overwrite: true);
            rotated = true;
        }
        if (File.Exists(path) && IsLink(path)) return false;
        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        file.Write(bytes);
        return rotated;
    }

    internal static LogPruneResult Prune(string dataRoot, string? activeLog, DateTime utcNow,
        long maxBytes = MaxBytes, int retentionDays = RetentionDays, string? activeSession = null)
    {
        List<FileInfo> files;
        string runtime;
        try { runtime = RuntimeFiles.Runtime(dataRoot); files = RuntimeFiles.List(dataRoot); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(0, 0, true); }
        if (!Directory.Exists(runtime)) return new(0, 0, true);
        var deleted = 0;
        var cutoff = utcNow.AddDays(-retentionDays);
        long total = files.Sum(file => file.Length);
        foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase))
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(file.FullName, activeLog)) continue;
            if (file.LastWriteTimeUtc >= cutoff && total <= maxBytes) continue;
            try
            {
                if (IsLink(file.FullName)) continue;
                File.Delete(file.FullName);
                total -= file.Length;
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        foreach (var path in Directory.EnumerateDirectories(runtime))
        {
            try
            {
                if (!RuntimeFiles.SessionName().IsMatch(Path.GetFileName(path)) || IsLink(path) ||
                    StringComparer.OrdinalIgnoreCase.Equals(path, activeSession) ||
                    StringComparer.OrdinalIgnoreCase.Equals(path, activeLog is null ? null : Path.GetDirectoryName(activeLog))) continue;
                if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, recursive: false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new(deleted, total, total <= maxBytes);
    }
    private static bool IsLink(string path) => RuntimeFiles.IsLink(path);
}
