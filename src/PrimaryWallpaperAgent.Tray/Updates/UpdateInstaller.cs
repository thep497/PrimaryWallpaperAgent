using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal sealed record UpdateTicket(int SchemaVersion, string Id, string DataRoot, string TargetPath,
    string OriginalHash, string OriginalVersion, string Flavor, int ParentId, long ParentStartTicks,
    string ManifestBase64, string SignatureBase64, bool WasPaused)
{
    internal string OperationRoot => Path.Combine(DataRoot, "updates", "staging", Id);
    internal string Candidate => Path.Combine(OperationRoot, "candidate.exe");
    internal string Helper => Path.Combine(OperationRoot, "helper.exe");
    internal string ReadyFile => Path.Combine(Path.GetDirectoryName(TargetPath)!, ".pwa-update-" + Id + ".exe");
    internal string Backup => Path.Combine(Path.GetDirectoryName(TargetPath)!, ".pwa-backup-" + Id + ".exe");
    internal string FailedFile => Path.Combine(Path.GetDirectoryName(TargetPath)!, ".pwa-failed-" + Id + ".exe");
    internal string HealthFile => Path.Combine(OperationRoot, "health.json");
    internal string TicketFile => Path.Combine(OperationRoot, "ticket.json");
    internal string PipeName => "PrimaryWallpaperAgent.Update." + Id;
}
internal sealed record UpdateOutcome(bool Success, string Version, string MessageKey, string TargetPath, string OriginalHash,
    string? BackupPath, DateTime FinishedUtc);
internal sealed record UpdateHealth(string Id, string Version, string Flavor, int ProcessId);

internal static class UpdateInstaller
{
    internal static UpdateTicket ReadTicket(string path)
    {
        if (new FileInfo(path).Length > 131072 || IsLink(path)) throw new InvalidDataException("Invalid update ticket.");
        var ticket = JsonSerializer.Deserialize<UpdateTicket>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Missing ticket.");
        if (ticket.SchemaVersion != 1 || !Guid.TryParseExact(ticket.Id, "N", out _) ||
            !Path.IsPathFullyQualified(ticket.DataRoot) || !Path.IsPathFullyQualified(ticket.TargetPath) ||
            !UpdateTrust.SamePath(path, ticket.TicketFile) || IsLink(ticket.OperationRoot) ||
            !ticket.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid update paths.");
        UpdatePaths.CheckStaging(ticket.DataRoot, ticket.Id);
        _ = UpdateTrust.ParseVersion(ticket.OriginalVersion);
        _ = UpdateTrust.FileName(ticket.Flavor);
        return ticket;
    }

    internal static UpdateManifest VerifyTicket(UpdateTicket ticket, string publicKey)
    {
        var manifest = UpdateTrust.Verify(Convert.FromBase64String(ticket.ManifestBase64), Convert.FromBase64String(ticket.SignatureBase64), publicKey);
        if (UpdateTrust.ParseVersion(manifest.Version) <= UpdateTrust.ParseVersion(ticket.OriginalVersion))
            throw new InvalidDataException("Updates must move forward.");
        return manifest;
    }

    internal static async Task PreflightAsync(string candidate, string reportPath, string version, string flavor, CancellationToken token)
    {
        var start = StartInfo(candidate, "--update-preflight", reportPath);
        using var process = Process.Start(start) ?? throw new IOException("Could not check the new executable.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) process.Kill(); throw; }
        if (process.ExitCode != 0 || !File.Exists(reportPath))
            throw new UiMessageException(flavor == "slim" ? "UpdateRuntimeRequired" : "UpdatePreflightFailed");
        using var report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
        if (report.RootElement.GetProperty("Version").GetString() != version || report.RootElement.GetProperty("Flavor").GetString() != flavor)
            throw new InvalidDataException("New executable identity does not match the signed release.");
    }

    internal static async Task LaunchHelperAsync(UpdateTicket ticket, CancellationToken token)
    {
        using var pipe = new NamedPipeServerStream(ticket.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        var helperStart = StartInfo(ticket.Helper, "--apply-update", ticket.TicketFile);
        helperStart.RedirectStandardError = true;
        using var helper = Process.Start(helperStart)
            ?? throw new IOException("Could not start updater.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            Trace(ticket, "parent", "wait connection");
            await pipe.WaitForConnectionAsync(deadline.Token);
            Trace(ticket, "parent", "connected");
            var ready = new byte[1];
            await pipe.ReadExactlyAsync(ready, deadline.Token);
            Trace(ticket, "parent", "ready=" + ready[0]);
            if (ready[0] != 1) throw new IOException("Updater did not become ready.");
            await pipe.WriteAsync(new byte[] { 2 }, deadline.Token);
            // Wait for helper to consume the commit token before closing the pipe.
            await pipe.ReadExactlyAsync(ready, deadline.Token);
            Trace(ticket, "parent", "ack=" + ready[0]);
            if (ready[0] != 3) throw new IOException("Updater handoff failed.");
            await pipe.WriteAsync(new byte[] { 4 }, deadline.Token);
            await pipe.ReadExactlyAsync(ready, deadline.Token);
            Trace(ticket, "parent", "final=" + ready[0]);
            if (ready[0] != 5) throw new IOException("Final updater handoff failed.");
        }
        catch (Exception error)
        {
            if (!helper.HasExited) helper.Kill();
            helper.WaitForExit(3000);
            throw new IOException("Updater handoff failed. Helper diagnostics: " + helper.StandardError.ReadToEnd(), error);
        }
    }

    internal static int RunHelper(string ticketPath, string? publicKeyOverride = null) =>
        RunHelperAsync(ticketPath, publicKeyOverride).GetAwaiter().GetResult();

    private static async Task<int> RunHelperAsync(string ticketPath, string? publicKeyOverride)
    {
        UpdateTicket? ticket = null;
        try
        {
            ticket = ReadTicket(ticketPath);
            Trace(ticket, "helper", "ticket read");
            var manifest = VerifyTicket(ticket, publicKeyOverride ?? BuildInfo.PublicKey);
            if (BuildInfo.Version != ticket.OriginalVersion || BuildInfo.Flavor != ticket.Flavor ||
                !UpdateTrust.SamePath(Environment.ProcessPath!, ticket.Helper) ||
                !UpdateTrust.Hash(ticket.Helper).Equals(ticket.OriginalHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Updater helper identity mismatch.");
            using var parent = Process.GetProcessById(ticket.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != ticket.ParentStartTicks ||
                !UpdateTrust.SamePath(parent.MainModule!.FileName, ticket.TargetPath) ||
                !UpdateTrust.Hash(ticket.TargetPath).Equals(ticket.OriginalHash, StringComparison.OrdinalIgnoreCase) ||
                IsLink(ticket.TargetPath))
                throw new InvalidDataException("The parent executable no longer matches.");
            var expectedRoot = FirstRun.DataRoot(Path.GetDirectoryName(ticket.TargetPath)!,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            if (!UpdateTrust.SamePath(expectedRoot, ticket.DataRoot)) throw new InvalidDataException("Unexpected user data folder.");
            var asset = manifest.Assets.Single(item => item.Flavor == ticket.Flavor);
            UpdateTrust.VerifyFile(ticket.Candidate, asset);
            UpdateTrust.VerifyFile(ticket.ReadyFile, asset);
            using (var pipe = new NamedPipeClientStream(".", ticket.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
                Trace(ticket, "helper", "connected");
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
                Trace(ticket, "helper", "sent ready");
                var permission = new byte[1];
                await pipe.ReadExactlyAsync(permission, timeout.Token).ConfigureAwait(false);
                Trace(ticket, "helper", "permission=" + permission[0]);
                if (permission[0] != 2) throw new IOException("Update not committed.");
                await pipe.WriteAsync(new byte[] { 3 }, timeout.Token).ConfigureAwait(false);
                await pipe.ReadExactlyAsync(permission, timeout.Token).ConfigureAwait(false);
                Trace(ticket, "helper", "confirmation=" + permission[0]);
                if (permission[0] != 4) throw new IOException("Update handoff not confirmed.");
                await pipe.WriteAsync(new byte[] { 5 }, timeout.Token).ConfigureAwait(false);
                Trace(ticket, "helper", "sent final");
                // Keep the client handle alive until the parent consumes the final acknowledgment and closes its server.
                try
                {
                    if (await pipe.ReadAsync(permission, timeout.Token).ConfigureAwait(false) != 0)
                        throw new InvalidDataException("Unexpected data after update commit.");
                }
                catch (IOException) { }
            }
            Trace(ticket, "helper", "waiting parent exit");
            if (!parent.WaitForExit(30000)) throw new IOException("The app is still running; files were not replaced.");
            var outcome = Apply(ticket, manifest, () => LaunchAndCheck(ticket, manifest), () =>
            {
                Process.Start(StartInfo(ticket.TargetPath, "--config"))?.Dispose();
            });
            if (outcome.MessageKey is "UpdateRollbackFailed" or "UpdateTargetChanged" or "UpdateRestartFailed")
                MessageBox.Show(UiText.T(outcome.MessageKey, UiText.Normalize(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)),
                    UiText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return outcome.Success ? 0 : 1;
        }
        catch (Exception error)
        {
            if (ticket is not null)
            {
                try { File.WriteAllText(Path.Combine(ticket.OperationRoot, "failure.txt"), error.ToString()); } catch (IOException) { }
            }
            return 1;
        }
    }

    // File transaction has injectable lifecycle callbacks so failure paths never need a real wallpaper backend in tests.
    internal static UpdateOutcome Apply(UpdateTicket ticket, UpdateManifest manifest, Func<bool> startAndCheck, Action restartOld)
    {
        var replaced = false;
        try
        {
            var asset = manifest.Assets.Single(item => item.Flavor == ticket.Flavor);
            UpdateTrust.VerifyFile(ticket.ReadyFile, asset);
            if (IsLink(ticket.TargetPath) || !UpdateTrust.Hash(ticket.TargetPath).Equals(ticket.OriginalHash, StringComparison.OrdinalIgnoreCase) ||
                File.Exists(ticket.Backup)) throw new IOException("Installed executable changed before replacement.");
            ReplaceWithRetry(ticket.ReadyFile, ticket.TargetPath, ticket.Backup);
            replaced = true;
            if (!startAndCheck()) throw new IOException("New version did not report healthy startup.");
            var success = new UpdateOutcome(true, manifest.Version, "UpdateSucceeded", ticket.TargetPath, ticket.OriginalHash, ticket.Backup, DateTime.UtcNow);
            DeletePreviousBackup(ticket);
            try { WriteOutcome(ticket.DataRoot, success); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            return success;
        }
        catch (Exception error)
        {
            var restored = false;
            try
            {
                if (replaced) ReplaceWithRetry(ticket.Backup, ticket.TargetPath, ticket.FailedFile);
                restored = !IsLink(ticket.TargetPath) && UpdateTrust.Hash(ticket.TargetPath).Equals(ticket.OriginalHash, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException) { }
            var outcome = new UpdateOutcome(false, manifest.Version, restored ? "UpdateRolledBack" : replaced ? "UpdateRollbackFailed" : "UpdateTargetChanged",
                ticket.TargetPath, ticket.OriginalHash, File.Exists(ticket.Backup) ? ticket.Backup : null, DateTime.UtcNow);
            try { File.WriteAllText(Path.Combine(ticket.OperationRoot, "failure.txt"), error.ToString()); WriteOutcome(ticket.DataRoot, outcome); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            if (restored)
            {
                try { restartOld(); }
                catch (Exception restartError)
                {
                    outcome = outcome with { MessageKey = "UpdateRestartFailed" };
                    try { WriteOutcome(ticket.DataRoot, outcome); } catch (IOException) { }
                    _ = restartError;
                }
            }
            return outcome;
        }
    }

    internal static UpdateTicket ValidatePostUpdate(string ticketPath, string? publicKeyOverride = null)
    {
        var ticket = ReadTicket(ticketPath);
            Trace(ticket, "helper", "ticket read");
        var manifest = VerifyTicket(ticket, publicKeyOverride ?? BuildInfo.PublicKey);
        if (manifest.Version != BuildInfo.Version || ticket.Flavor != BuildInfo.Flavor ||
            !UpdateTrust.SamePath(ticket.TargetPath, Environment.ProcessPath!))
            throw new InvalidDataException("Post-update identity mismatch.");
        UpdateTrust.VerifyFile(ticket.TargetPath, manifest.Assets.Single(item => item.Flavor == ticket.Flavor));
        return ticket;
    }

    internal static void MarkHealthy(UpdateTicket ticket) => File.WriteAllText(ticket.HealthFile,
        JsonSerializer.Serialize(new UpdateHealth(ticket.Id, BuildInfo.Version, BuildInfo.Flavor, Environment.ProcessId)));

    private static bool LaunchAndCheck(UpdateTicket ticket, UpdateManifest manifest)
    {
        UpdateTrust.VerifyFile(ticket.TargetPath, manifest.Assets.Single(asset => asset.Flavor == ticket.Flavor));
        using var process = Process.Start(StartInfo(ticket.TargetPath, "--post-update", ticket.TicketFile))
            ?? throw new IOException("Could not open the updated app.");
        var healthy = false;
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(25))
            {
                if (process.HasExited) return false;
                if (File.Exists(ticket.HealthFile))
                {
                    try
                    {
                        var health = JsonSerializer.Deserialize<UpdateHealth>(File.ReadAllBytes(ticket.HealthFile));
                        if (health is not null && health.Id == ticket.Id && health.Version == manifest.Version &&
                            health.Flavor == ticket.Flavor && health.ProcessId == process.Id)
                        { healthy = true; return true; }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
                }
                Thread.Sleep(100);
            }
            return false;
        }
        finally
        {
            if (!healthy)
            {
                try { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
    }

    private static void ReplaceWithRetry(string source, string target, string backup)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { File.Replace(source, target, backup); return; }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(8)) { Thread.Sleep(200); }
        }
    }
    private static void WriteOutcome(string dataRoot, UpdateOutcome value) => File.WriteAllText(Path.Combine(dataRoot, "updates", "last-result.json"),
        JsonSerializer.Serialize(value, SettingsStore.JsonOptions));

    private static void DeletePreviousBackup(UpdateTicket ticket)
    {
        try
        {
            var resultPath = Path.Combine(ticket.DataRoot, "updates", "last-result.json");
            if (!File.Exists(resultPath)) return;
            var old = JsonSerializer.Deserialize<UpdateOutcome>(File.ReadAllBytes(resultPath));
            if (old is { Success: true, BackupPath: not null } && UpdateTrust.SamePath(old.TargetPath, ticket.TargetPath) &&
                UpdateTrust.SamePath(Path.GetDirectoryName(old.BackupPath)!, Path.GetDirectoryName(ticket.TargetPath)!) &&
                Path.GetFileName(old.BackupPath).StartsWith(".pwa-backup-", StringComparison.Ordinal) &&
                Guid.TryParseExact(Path.GetFileNameWithoutExtension(old.BackupPath)[".pwa-backup-".Length..], "N", out _) && File.Exists(old.BackupPath) &&
                !IsLink(old.BackupPath) && UpdateTrust.Hash(old.BackupPath).Equals(old.OriginalHash, StringComparison.OrdinalIgnoreCase))
                File.Delete(old.BackupPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }
    internal static ProcessStartInfo StartInfo(string executable, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        info.Environment["DOTNET_DISABLE_GUI_ERRORS"] = "1";
        foreach (var argument in args) info.ArgumentList.Add(argument);
        return info;
    }
    private static void Trace(UpdateTicket ticket, string side, string message) =>
        File.AppendAllText(Path.Combine(ticket.OperationRoot, side + "-trace.txt"), message + Environment.NewLine);

    private static bool IsLink(string path) => UpdatePaths.IsRedirectingLink(path);
}
