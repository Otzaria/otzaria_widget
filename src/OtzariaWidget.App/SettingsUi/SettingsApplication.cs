// Settings UI entry point (called on a dedicated STA thread from Program.Main).
using System.Windows;
using System.Windows.Threading;
using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.App.SettingsUi.ViewModels;
using OtzariaWidget.App.SettingsUi.Views;

namespace OtzariaWidget.App.SettingsUi;

public static class SettingsApplication
{
    private static bool _crashDialogShown;

    public static int Run(string[] args)
    {
        using var guard = new SingleInstanceGuard();
        if (!guard.IsPrimary)
        {
            // another settings window is already open: ask it to come to the front and exit
            guard.SignalPrimary();
            return 0;
        }

        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            InstallGlobalHandlers(app);

            // .NET 9+/10 Fluent theme following the system light/dark setting (experimental API, WPF0001).
#pragma warning disable WPF0001
            try { app.ThemeMode = System.Windows.ThemeMode.System; }
            catch (Exception ex) { SettingsLog.Error("ThemeMode unavailable, using classic theme", ex); }
#pragma warning restore WPF0001

            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/OtzariaWidget;component/SettingsUi/Views/Styles.xaml", UriKind.Absolute),
            });

            var services = SettingsServices.CreateDefault();
            var vm = new MainViewModel(services);
            SelectPageFromArgs(vm, args);
            var window = new MainWindow(vm);

            var shot = ArgValue(args, "--screenshot");
            if (shot is not null)
            {
                if (double.TryParse(ArgValue(args, "--height"), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var hgt) && hgt >= 400)
                {
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Top = 0;
                    window.Height = hgt;
                }
                window.ScheduleScreenshot(shot, TimeSpan.FromSeconds(3));
            }

            guard.Listen(() => app.Dispatcher.BeginInvoke(new Action(window.BringToFront)));
            app.MainWindow = window;
            SettingsLog.Info("Settings window started, version " + AppEnvironment.Version);

            int code = app.Run(window);
            vm.Session.Flush();
            vm.Session.Dispose();
            try { services.Data.Dispose(); } catch { }
            return code;
        }
        catch (Exception ex)
        {
            SettingsLog.Error("Fatal error starting the settings UI", ex);
            ShowCrashDialog(ex);
            return 1;
        }
    }

    /// <summary>"--page N" (0-4) opens a specific page; handy for screenshots and for deep links from the widgets.</summary>
    private static void SelectPageFromArgs(MainViewModel vm, string[] args)
    {
        if (int.TryParse(ArgValue(args, "--page"), out int n) && n >= 0 && n < vm.Pages.Count)
            vm.SelectedPage = vm.Pages[n];
        else if (LaunchArguments.PageFromProtocolUri(args) is int p && p < vm.Pages.Count)
            vm.SelectedPage = vm.Pages[p];
    }

    private static string? ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static void InstallGlobalHandlers(Application app)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            SettingsLog.Error("Unhandled UI exception", e.Exception);
            e.Handled = true; // keep the window alive; the user's settings are not at risk
            ShowCrashDialog(e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            SettingsLog.Error("Unhandled exception (terminating=" + e.IsTerminating + ")", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            SettingsLog.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    private static void ShowCrashDialog(Exception ex)
    {
        if (_crashDialogShown) return; // avoid dialog storms; everything is in the log
        _crashDialogShown = true;
        try
        {
            MessageBox.Show(
                "אירעה שגיאה לא צפויה בחלון ההגדרות.\n\n" + ex.Message +
                "\n\nפרטים נשמרו בקובץ הלוג בתיקייה " + AppEnvironment.RealDataFolder,
                "הגדרות ווידג'ט אוצריא",
                MessageBoxButton.OK, MessageBoxImage.Warning,
                MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }
        catch { }
        finally { _crashDialogShown = false; }
    }
}
