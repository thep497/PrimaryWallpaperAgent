using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PrimaryWallpaperAgent.Tray;

internal sealed record ExportResult(int Files, int Events, int Skipped);
internal static partial class DiagnosticsExporter
{
    internal const long MaxInputBytes = 50L * 1024 * 1024;
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}T\S+)\s+(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex EventLine();
    [GeneratedRegex(@"(?i)(?:[a-z]:[\\/]|\\\\\?\\|\\\\(?![.?]\\))[^""\r\n<>]+", RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedPath();
    private static string Token(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToUpperInvariant())))[..12];
    internal static string Redact(string text) => EmbeddedPath().Replace(text, match => PathToken(match.Value));
    private static string PathToken(string path)
    {
        var location = path.Contains("primarywallpaperagent-v", StringComparison.OrdinalIgnoreCase) ? "legacy-app" :
            path.Contains("PrimaryWallpaperAgent", StringComparison.OrdinalIgnoreCase) && path.Contains("assets", StringComparison.OrdinalIgnoreCase) ? "app-assets" :
            path.Contains("Windows", StringComparison.OrdinalIgnoreCase) && path.Contains("Themes", StringComparison.OrdinalIgnoreCase) ? "windows-cache" : "path";
        return "<" + location + ":" + Token(path) + "/name:" + Token(Path.GetFileName(path)) + ">";
    }
    internal static ExportResult Create(string dataRoot, string destination, DateTimeOffset? at = null)
    {
        var now = at ?? DateTimeOffset.Now;
        var runtime = RuntimeFiles.Runtime(dataRoot);
        var cutoff = now.UtcDateTime.AddDays(-7);
        var candidates = RuntimeFiles.List(dataRoot).Where(f => f.LastWriteTimeUtc >= cutoff)
            .OrderByDescending(f => f.LastWriteTimeUtc).Take(501).ToArray();
        var skipped = 0; var added = 0; long used = 0;
        var timeline = new List<(DateTimeOffset At, long Order, string Text)>();
        var sources = new List<object>();
        var created = false;
        try
        {
            using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            using var zip = new ZipArchive(target, ZipArchiveMode.Create);
            foreach (var file in candidates)
            {
                if (added >= 500 || file.Length > 4 * 1024 * 1024 || used + file.Length > MaxInputBytes ||
                    file.Name.EndsWith("settings-before", StringComparison.Ordinal) || RuntimeFiles.IsLink(file.FullName))
                { skipped++; continue; }
                string body;
                try
                {
                    using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var length = (int)Math.Min(input.Length, Math.Min(4 * 1024 * 1024, MaxInputBytes - used));
                    if (length <= 0) { skipped++; continue; }
                    var bytes = new byte[length]; var count = 0;
                    while (count < length) { var read = input.Read(bytes, count, length - count); if (read == 0) break; count += read; }
                    body = Encoding.UTF8.GetString(bytes, 0, count); used += count;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; continue; }
                var relative = Path.GetRelativePath(runtime, file.FullName).Replace('\\', '/');
                if (file.Extension == ".log")
                {
                    Add(zip, "logs/" + relative, Redact(body));
                    Merge(body, relative);
                }
                else
                {
                    try
                    {
                        var record = JsonNode.Parse(body) ?? throw new JsonException();
                        var kind = record["Kind"]?.GetValue<string>() ?? file.Name;
                        if (kind.Contains("settings-before", StringComparison.Ordinal)) { skipped++; continue; }
                        var data = record["Data"] ?? record;
                        data = data["Before"] ?? data;
                        var safe = new JsonObject();
                        foreach (var key in new[] { "CapturedAt", "SessionId", "TopologyStable", "Position", "SlideshowStatus",
                            "Displays", "Wallpapers", "Issues", "Active", "GdiCount", "AttachedCount", "PrimaryCount",
                            "Samples", "Repairs", "SecondsElapsed", "Paused", "SmokeTest", "Reason" })
                            if (data[key] is { } value) safe[key] = Sanitize(value, key);
                        Add(zip, "diagnostics/" + relative + (file.Extension == ".json" ? "" : ".json"),
                            new JsonObject { ["Kind"] = kind, ["RunId"] = record["RunId"]?.DeepClone(), ["State"] = safe }.ToJsonString(SettingsStore.JsonOptions));
                    }
                    catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { skipped++; continue; }
                }
                added++; sources.Add(new { File = relative });
            }
            Add(zip, "timeline.log", string.Join(Environment.NewLine, timeline.OrderBy(e => e.At).ThenBy(e => e.Order).Select(e => e.Text)));
            using (var notices = typeof(RuntimeLog).Assembly.GetManifestResourceStream("PrimaryWallpaperAgent.ThirdPartyNotices") ?? throw new InvalidOperationException("Bundled notices missing"))
            using (var reader = new StreamReader(notices)) Add(zip, "third-party-notices.txt", reader.ReadToEnd());
            Add(zip, "manifest.json", JsonSerializer.Serialize(new
            {
                SchemaVersion = 1, ExportedAt = now, Days = 7, Version = BuildInfo.Version, Flavor = BuildInfo.Flavor,
                Files = sources, Skipped = skipped, Events = timeline.Count, InputBytes = used,
                Privacy = "No images, full settings backups, image hashes or original path/device identifiers. Paths use consistent diagnostic tokens.",
                Note = "Snapshot of active logs; legacy and current layouts included. Limits: 500 files, 4 MiB/file, 50 MiB input."
            }, SettingsStore.JsonOptions));
        }
        catch { if (created) File.Delete(destination); throw; }
        return new(added, timeline.Count, skipped);

        void Merge(string body, string source)
        {
            DateTimeOffset? time = null; var eventText = new StringBuilder();
            foreach (var raw in body.Split('\n'))
            {
                var line = raw.TrimEnd('\r'); var match = EventLine().Match(line);
                if (match.Success && DateTimeOffset.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
                { Flush(); time = stamp; eventText.Append(line); }
                else if (time is not null && line.Length > 0) eventText.AppendLine().Append(line);
            }
            Flush();
            void Flush()
            {
                if (time is not null && time.Value.UtcDateTime >= cutoff)
                    timeline.Add((time.Value, timeline.Count, Redact(eventText.ToString()) + " [source=" + source + "]"));
                eventText.Clear();
            }
        }
    }
    private static JsonNode? Sanitize(JsonNode? value, string key)
    {
        if (value is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var property in obj)
                if (property.Key is not ("OriginalBytesBase64" or "PhotoLabel" or "CurrentSourcePath" or "AcceptedPaths") &&
                    !property.Key.Contains("Sha256", StringComparison.OrdinalIgnoreCase))
                    result[property.Key] = Sanitize(property.Value, property.Key);
            return result;
        }
        if (value is JsonArray array) return new JsonArray(array.Select(item => Sanitize(item, key)).ToArray());
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text))
        {
            if (key is "MonitorId" or "InterfacePath") return JsonValue.Create("<monitor:" + Token(text) + ">");
            if (Path.IsPathRooted(text) && !text.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase))
                return JsonValue.Create(PathToken(text));
            return JsonValue.Create(Redact(text));
        }
        return value?.DeepClone();
    }
    private static void Add(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
