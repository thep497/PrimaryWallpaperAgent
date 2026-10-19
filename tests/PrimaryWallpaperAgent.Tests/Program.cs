using System.Text;
using PrimaryWallpaperAgent.Tray;

namespace PrimaryWallpaperAgent.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        try
        {
            return args switch
            {
                ["policy"] => TrialSelfTests.Run(),
                ["config"] => ConfigSelfTests.Run(),
                ["window"] => WindowSelfTests.Run(),
                ["localization"] => LocalizationSelfTests.Run(),
                ["polling"] => PollingSelfTests.Run(),
                ["branding"] => BrandingSelfTests.Run(),
                _ => Usage()
            };
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
    private static int Usage()
    {
        Console.WriteLine("PrimaryWallpaperAgent.Tests <policy|config|window|localization|polling|branding>");
        Console.WriteLine("Fake desktop/startup backends; window tests display only our fixture form.");
        return 2;
    }
}