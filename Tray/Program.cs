namespace PrimaryWallpaperAgent.Tray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var smoke = args is ["--smoke-test"];
        var selfTest = args is ["--self-test-config"];
        var showConfig = args is ["--config"];
        if (args.Length > 0 && !smoke && !selfTest && !showConfig && args is not ["--startup"]) return 2;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        try
        {
            if (selfTest) return ConfigSelfTests.Run();
            var store = new SettingsStore(AppContext.BaseDirectory);
            var bytes = store.Read();
            var config = SettingsStore.Parse(bytes);
            _ = store.Images(config);
            using var lease = new WallpaperWriteLease();
            using var context = new AgentContext(store, config, bytes, smoke, showConfig);
            Application.Run(context);
            return 0;
        }
        catch (Exception error)
        {
            if (selfTest || smoke)
            {
                var folder = Path.Combine(AppContext.BaseDirectory, "runtime");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "last-test-error.txt"), error.ToString());
                return 1;
            }
            MessageBox.Show(error.Message, "PrimaryWallpaperAgent — เปิดไม่ได้", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

}
