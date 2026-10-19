using System.Drawing.Imaging;
using System.Text.Json;

namespace PrimaryWallpaperAgent.Tray;

internal static class WindowSelfTests
{
    internal static int Run()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "runtime", "window-self-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var fixture = Path.Combine(folder, "preview.png");
        using (var bitmap = new Bitmap(320, 180))
        { using var drawing = Graphics.FromImage(bitmap); drawing.Clear(Color.Teal); bitmap.Save(fixture, ImageFormat.Png); }
        var selected = SelectedImage.Read(fixture);
        using var context = new ApplicationContext();
        using var trayIcons = new TrayIconSet();
        using var icon = new NotifyIcon { Icon = trayIcons.Running, Text = "Primary Wallpaper Agent · window test", Visible = true };
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        var results = new List<object>();
        Exception? failure = null;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                // Same tray-only ApplicationContext + modal Config as production.
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    using var form = new ConfigForm(selected, false, language => UiText.T("WindowTestStatus", language), () => fixture,
                        _ => throw new InvalidOperationException("Test must not save"), _ => { });
                    var currentAttempt = attempt;
                    icon.Icon = attempt == 1 ? trayIcons.Paused : trayIcons.Attention;
                    form.Load += (_, _) => form.BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            if (!WindowPresentation.IsVisible(form)) throw new InvalidOperationException("Config is hidden after ShowDialog");
                            form.WindowState = FormWindowState.Minimized;
                            WindowPresentation.Reveal(form);
                            if (form.WindowState == FormWindowState.Minimized) throw new InvalidOperationException("Config was not restored");
                            // A second request while already open must reveal the same form.
                            WindowPresentation.Reveal(form);
                            results.Add(new { Attempt = currentAttempt, NativeVisible = WindowPresentation.IsVisible(form), Restored = true });
                        }
                        catch (Exception error) { failure = error; }
                        finally { form.Close(); }
                    }));
                    form.ShowDialog();
                    if (failure is not null) break;
                }
            }
            catch (Exception error) { failure = error; }
            finally { icon.Visible = false; context.ExitThread(); }
        };
        timer.Start();
        Application.Run(context);
        File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(new
            { Passed = failure is null, Results = results, WallpaperSetters = 0, RegistryWrites = 0, Error = failure?.ToString() }, SettingsStore.JsonOptions));
        if (failure is not null) throw new InvalidOperationException("Window visibility test failed: " + folder, failure);
        return 0;
    }
}
