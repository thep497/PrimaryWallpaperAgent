using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal sealed record UpdatePreferences(bool AutoCheck = true, bool IncludePreviews = false,
    DateTime? LastAttemptUtc = null, DateTime? LastSuccessUtc = null, string? NotifiedVersion = null);

internal sealed class UpdateController : IDisposable
{
    private readonly string dataRoot;
    private readonly HttpClient client;
    private readonly UpdateFeed feed;
    private readonly string publicKey;
    private readonly CancellationTokenSource stop = new();
    private readonly Func<bool> wasPaused;
    private bool disposed;
    private bool handedOff;
    private int lastProgress = -1;
    internal UpdatePreferences Preferences { get; private set; }
    internal UpdateRelease? Available { get; private set; }
    internal string StatusKey { get; private set; } = "UpdateNotChecked";
    internal object[] StatusArgs { get; private set; } = [];
    internal bool IsBusy { get; private set; }
    internal event Action? Changed;
    internal event Action<string>? NewVersion;
    internal event Action? RestartRequested;

    internal UpdateController(string dataRoot, Func<bool>? wasPaused = null, HttpClient? clientOverride = null, string? publicKeyOverride = null)
    {
        this.dataRoot = dataRoot;
        this.wasPaused = wasPaused ?? (() => false);
        publicKey = publicKeyOverride ?? BuildInfo.PublicKey;
        client = clientOverride ?? UpdateFeed.CreateClient();
        feed = new UpdateFeed(client);
        Preferences = LoadPreferences();
        ReadPreviousOutcome();
        PruneStaging();
    }
    internal string Status(string language)
    {
        if (!IsBusy && StatusKey is "UpdateNotChecked" or "UpdateSucceeded" or "UpdateRolledBack" or "UpdateRollbackFailed" or "UpdateTargetChanged" or "UpdateRestartFailed")
            ReadPreviousOutcome();
        return UiText.T(StatusKey, language, StatusArgs);
    }

    internal void ChangePreferences(bool automatic, bool previews)
    {
        var next = Preferences with { AutoCheck = automatic, IncludePreviews = previews };
        if (previews != Preferences.IncludePreviews) next = next with { LastAttemptUtc = null, LastSuccessUtc = null };
        SavePreferences(next);
        Preferences = next;
        if (Available?.Preview == true && !previews) { Available = null; SetStatus("UpdateNotChecked"); }
        else Changed?.Invoke();
    }
    internal async Task AutoTickAsync()
    {
        var now = DateTime.UtcNow;
        if (!Preferences.AutoCheck || IsBusy || handedOff ||
            Preferences.LastSuccessUtc is { } success && now - success < TimeSpan.FromHours(24) ||
            Preferences.LastAttemptUtc is { } attempt && now - attempt < TimeSpan.FromHours(1)) return;
        await CheckAsync();
    }
    internal async Task CheckAsync()
    {
        if (IsBusy || disposed || handedOff) return;
        IsBusy = true;
        SetStatus("UpdateChecking");
        try
        {
            var attempt = Preferences with { LastAttemptUtc = DateTime.UtcNow };
            SavePreferences(attempt); Preferences = attempt;
            Available = await feed.FindAsync(BuildInfo.Version, Preferences.IncludePreviews, publicKey, stop.Token);
            var next = Preferences with { LastSuccessUtc = DateTime.UtcNow };
            var notify = Available is not null && next.NotifiedVersion != Available.Version;
            if (notify) next = next with { NotifiedVersion = Available!.Version };
            SavePreferences(next); Preferences = next;
            if (Available is { } release)
            {
                SetStatus("UpdateAvailable", release.Version);
                if (notify) NewVersion?.Invoke(release.Version);
            }
            else SetStatus("UpdateCurrent");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { Log(error); SetStatus("UpdateCheckFailed"); }
        finally { IsBusy = false; if (!disposed) Changed?.Invoke(); }
    }

    internal async Task InstallAsync()
    {
        if (IsBusy || disposed || handedOff || Available is not { } release) return;
        IsBusy = true;
        SetStatus("UpdateDownloading", 0);
        string? readyFile = null;
        try
        {
            var target = Environment.ProcessPath ?? throw new IOException("Executable path unavailable.");
            using var parent = Process.GetCurrentProcess();
            var id = Guid.NewGuid().ToString("N");
            var operation = Path.Combine(dataRoot, "updates", "staging", id);
            Directory.CreateDirectory(operation);
            var ticket = new UpdateTicket(1, id, dataRoot, target, UpdateTrust.Hash(target), BuildInfo.Version,
                BuildInfo.Flavor, parent.Id, parent.StartTime.ToUniversalTime().Ticks,
                Convert.ToBase64String(release.ManifestBytes), Convert.ToBase64String(release.Signature), wasPaused());
            _ = UpdateInstaller.VerifyTicket(ticket, publicKey);
            // Prove the install location is writable before downloading or stopping the current app.
            using (var check = new FileStream(ticket.ReadyFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            readyFile = ticket.ReadyFile;
            lastProgress = -1;
            await feed.DownloadAsync(release, ticket.Flavor, ticket.Candidate, progress =>
            {
                if (progress == lastProgress) return;
                lastProgress = progress;
                SetStatus("UpdateDownloading", progress);
            }, stop.Token);
            SetStatus("UpdateVerifying");
            await UpdateInstaller.PreflightAsync(ticket.Candidate, Path.Combine(operation, "preflight.json"),
                release.Version, ticket.Flavor, stop.Token);
            await Task.Run(() =>
            {
                File.Copy(target, ticket.Helper, overwrite: false);
                if (!UpdateTrust.Hash(ticket.Helper).Equals(ticket.OriginalHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Running executable changed.");
                File.Copy(ticket.Candidate, ticket.ReadyFile, overwrite: false);
                UpdateTrust.VerifyFile(ticket.ReadyFile, release.Asset(ticket.Flavor));
                File.WriteAllText(ticket.TicketFile, JsonSerializer.Serialize(ticket, SettingsStore.JsonOptions));
            }, stop.Token);
            stop.Token.ThrowIfCancellationRequested();
            await UpdateInstaller.LaunchHelperAsync(ticket, stop.Token);
            handedOff = true;
            SetStatus("UpdateRestarting");
            RestartRequested?.Invoke();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            Log(error);
            SetStatus(error is UiMessageException known ? known.Key : "UpdateInstallFailed");
        }
        finally
        {
            if (!handedOff && readyFile is not null)
            {
                try { if (File.Exists(readyFile) && (File.GetAttributes(readyFile) & FileAttributes.ReparsePoint) == 0) File.Delete(readyFile); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            IsBusy = false;
            if (!disposed) Changed?.Invoke();
        }
    }

    private UpdatePreferences LoadPreferences()
    {
        try
        {
            var path = Path.Combine(dataRoot, "updates", "preferences.json");
            if (File.Exists(path) && new FileInfo(path).Length < 65536)
                return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllBytes(path)) ?? new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    private void SavePreferences(UpdatePreferences value)
    {
        var folder = Path.Combine(dataRoot, "updates");
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, "preferences-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, SettingsStore.JsonOptions));
        File.Move(temporary, Path.Combine(folder, "preferences.json"), overwrite: true);
    }
    private void SetStatus(string key, params object[] args)
    {
        StatusKey = key; StatusArgs = args;
        if (!disposed) Changed?.Invoke();
    }
    private void Log(Exception error)
    {
        try
        {
            var folder = Path.Combine(dataRoot, "updates");
            Directory.CreateDirectory(folder);
            LogRetention.AppendBounded(Path.Combine(folder, "update.log"), DateTimeOffset.Now.ToString("O") + " " + error + Environment.NewLine);
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
    }
    private void ReadPreviousOutcome()
    {
        try
        {
            var path = Path.Combine(dataRoot, "updates", "last-result.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return;
            var result = JsonSerializer.Deserialize<UpdateOutcome>(File.ReadAllBytes(path));
            if (result is not null && result.MessageKey is "UpdateSucceeded" or "UpdateRolledBack" or "UpdateRollbackFailed" or "UpdateTargetChanged" or "UpdateRestartFailed")
            { _ = UpdateTrust.ParseVersion(result.Version); StatusKey = result.MessageKey; StatusArgs = [result.Version]; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
    }
    private void PruneStaging()
    {
        var staging = Path.Combine(dataRoot, "updates", "staging");
        try
        {
            if (!Directory.Exists(staging) || (File.GetAttributes(staging) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var operation in Directory.EnumerateDirectories(staging, "*", SearchOption.TopDirectoryOnly))
            {
                if (!Guid.TryParseExact(Path.GetFileName(operation), "N", out _) ||
                    (File.GetAttributes(operation) & FileAttributes.ReparsePoint) != 0 ||
                    Directory.GetLastWriteTimeUtc(operation) > DateTime.UtcNow.AddDays(-1)) continue;
                foreach (var name in new[] { "candidate.exe", "helper.exe", "ticket.json", "preflight.json", "health.json", "failure.txt", "parent-trace.txt", "helper-trace.txt" })
                {
                    var path = Path.Combine(operation, name);
                    if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
                }
                if (!Directory.EnumerateFileSystemEntries(operation).Any()) Directory.Delete(operation, recursive: false);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Cancel();
        client.Dispose();
        stop.Dispose();
    }
}
