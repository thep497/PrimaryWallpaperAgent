using Microsoft.Win32;

namespace PrimaryWallpaperAgent.Tray;

internal interface IStartupRegistration
{
    string ExpectedCommand { get; }
    string? Read();
    void Write(string? command);
}

internal sealed class StartupRegistration : IStartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PrimaryWallpaperAgent";
    public string ExpectedCommand => "\"" + Path.Combine(AppContext.BaseDirectory, "PrimaryWallpaperAgent.Tray.exe") + "\" --startup";
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        var value = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is not null and not string) throw new UiMessageException("StartupValueType");
        return (string?)value;
    }
    public void Write(string? command)
    {
        if (command is { Length: > 260 }) throw new UiMessageException("StartupCommandTooLong");
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (command is null) key.DeleteValue(ValueName, throwOnMissingValue: false);
        else key.SetValue(ValueName, command, RegistryValueKind.String);
    }
}
