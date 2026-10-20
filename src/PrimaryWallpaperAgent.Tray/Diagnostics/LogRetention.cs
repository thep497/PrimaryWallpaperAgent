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

    // Only direct managed files in runtime and recognized session directories. No recursive deletion or asset access.
    internal static LogPruneResult Prune(string dataRoot, string? activeLog, DateTime utcNow,
        long maxBytes = MaxBytes, int retentionDays = RetentionDays)
    {
        var runtime = Path.GetFullPath(Path.Combine(dataRoot, "runtime"));
        if (!Directory.Exists(runtime) || IsLink(runtime)) return new(0, 0, true);
        var files = new List<FileInfo>();
        var sessions = new List<DirectoryInfo>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(runtime, "startup-error*.log", SearchOption.TopDirectoryOnly))
                if (Path.GetFileName(path) is "startup-error.log" or "startup-error.previous.log") Add(path);
            foreach (var path in Directory.EnumerateDirectories(runtime, "*", SearchOption.TopDirectoryOnly))
            {
                var directory = new DirectoryInfo(path);
                if (!SessionName().IsMatch(directory.Name) || IsLink(path)) continue;
                sessions.Add(directory);
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
                    if (ManagedFile().IsMatch(Path.GetFileName(file))) Add(file);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

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
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        foreach (var directory in sessions)
        {
            // Empty-only removal. Unknown files/subdirectories are preserved.
            try
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(directory.FullName, activeLog is null ? null : Path.GetDirectoryName(activeLog)) &&
                    !IsLink(directory.FullName) && !Directory.EnumerateFileSystemEntries(directory.FullName).Any())
                    Directory.Delete(directory.FullName, recursive: false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return new(deleted, total, total <= maxBytes);

        void Add(string path)
        {
            try { if (!IsLink(path)) files.Add(new FileInfo(path)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
