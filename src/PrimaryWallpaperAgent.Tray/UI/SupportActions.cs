namespace PrimaryWallpaperAgent.Tray;

internal static class SupportActions
{
    internal static async Task ExportAsync(Form owner, string dataRoot, string language)
    {
        using var dialog = new SaveFileDialog
        {
            Title = UiText.T("ExportDiagnostics", language), Filter = "ZIP (*.zip)|*.zip",
            DefaultExt = "zip", AddExtension = true, OverwritePrompt = false,
            FileName = "PrimaryWallpaperAgent-support-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".zip"
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            await Task.Run(() => DiagnosticsExporter.Create(dataRoot, dialog.FileName));
            MessageBox.Show(owner, UiText.T("ExportComplete", language, dialog.FileName), UiText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { MessageBox.Show(owner, UiText.T("ExportFailed", language), UiText.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
