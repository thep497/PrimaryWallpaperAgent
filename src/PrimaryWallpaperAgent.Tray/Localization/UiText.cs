using System.Globalization;
using System.Resources;

namespace PrimaryWallpaperAgent.Tray;

internal static class UiText
{
    internal const string AppName = "Primary Wallpaper Agent";
    private static readonly ResourceManager Resources = new("PrimaryWallpaperAgent.Tray.Resources.Strings", typeof(UiText).Assembly);

    internal static string Normalize(string? language) => language?.ToLowerInvariant() switch
    {
        null or "" or "th" or "th-th" => "th",
        _ => "en"
    };

    internal static string T(string key, string language = "th", params object[] args)
    {
        var culture = CultureInfo.GetCultureInfo(Normalize(language));
        var value = Resources.GetString(key, culture) ?? throw new InvalidOperationException("Missing resource: " + key);
        return args.Length == 0 ? value : string.Format(culture, value, args);
    }

    internal static string Error(Exception error, string language, string fallback = "GenericError") =>
        T(error is UiMessageException known ? known.Key : error is AggregateException ? "RestoreIncomplete" : fallback, language);

    internal static string Title(string key, string language) => AppName + " · " + T(key, language);
}

internal sealed class UiMessageException(string key, Exception? inner = null)
    : InvalidOperationException(UiText.T(key, "en"), inner)
{
    internal string Key { get; } = key;
}