namespace PrimaryWallpaperAgent.Tray;

internal static class ThemeOptions
{
    internal static readonly string[] Supported = ["system", "light", "dark"];
    internal static string Validate(string? value) => value is "system" or "light" or "dark" ? value :
        throw new UiMessageException("InvalidTheme");
}
