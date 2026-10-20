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
            if (args is ["update-fixture"] or ["--apply-update", _] or ["--post-update", _] or ["--update-preflight", _] or ["--config"]) return UpdateProcessTests.Fixture(args);
            return args switch
            {
                ["policy"] => TrialSelfTests.Run(),
                ["config"] => ConfigSelfTests.Run(),
                ["window"] => WindowSelfTests.Run(),
                ["localization"] => LocalizationSelfTests.Run(),
                ["polling"] => PollingSelfTests.Run(),
                ["branding"] => BrandingSelfTests.Run(),
                ["instant"] => InstantSettingsSelfTests.Run(),
                ["compact"] => CompactSettingsSelfTests.Run(),
                ["recent"] => RecentImagesSelfTests.Run(),
                ["theme"] => ThemeSelfTests.Run(),
                ["release"] => ReleaseSelfTests.Run(),
                ["maintenance"] => MaintenanceSelfTests.Run(),
                ["instance"] => InstanceSelfTests.Run(),
                ["update"] => UpdateSelfTests.Run(),
                ["update-path", var checkedPath] => UpdatePaths.IsRedirectingLink(checkedPath) ? 10 : 0,
                ["verify-release", var releaseFolder] => UpdateSelfTests.VerifyPublishedFiles(releaseFolder),
                ["check-published"] => UpdateSelfTests.CheckLiveRelease(),
                ["update-process", var payloadRoot] => UpdateProcessTests.Run(payloadRoot),
                ["instance-client", var channel, var command] => InstanceSelfTests.Client(channel, command),
                ["retention-link", var root] => MaintenanceSelfTests.CheckJunction(root),
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
        Console.WriteLine("PrimaryWallpaperAgent.Tests <policy|config|window|localization|polling|branding|instant|compact|recent|theme|release|maintenance|instance|update>");
        Console.WriteLine("Fake desktop/startup backends; window tests display only our fixture form.");
        return 2;
    }
}