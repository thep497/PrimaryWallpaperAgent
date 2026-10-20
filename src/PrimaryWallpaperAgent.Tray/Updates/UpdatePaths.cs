namespace PrimaryWallpaperAgent.Tray;

internal static class UpdatePaths
{
    internal static void CheckUpdateRoot(string dataRoot)
    {
        var root = Path.Combine(dataRoot, "updates");
        if (Directory.Exists(root) && IsRedirectingLink(root))
            throw new InvalidDataException("The update directory must not redirect outside user data.");
    }
    internal static void CheckStaging(string dataRoot, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid update operation.");
        CheckUpdateRoot(dataRoot);
        foreach (var path in new[] { Path.Combine(dataRoot, "updates", "staging"), Path.Combine(dataRoot, "updates", "staging", id) })
            if (Directory.Exists(path) && IsRedirectingLink(path)) throw new InvalidDataException("The staging path is a redirecting link.");
    }

    // Cloud sync may mark ordinary directories as reparse points. Reject actual
    // name-redirection links; a non-link cloud placeholder remains in the same tree.
    internal static bool IsRedirectingLink(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) == 0) return false;
        FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
        return entry.LinkTarget is not null;
    }
}
