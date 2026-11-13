// Owned by the project lead. Entry point: dispatches between the two modes.
namespace OtzariaWidget.App;

public static class Program
{
    // NOTE: no [STAThread] — the COM provider must run MTA (widget-platform.md gotcha 4).
    public static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("-RegisterProcessAsComServer", StringComparison.OrdinalIgnoreCase)))
        {
            return Provider.ProviderHost.Run(args);
        }

        // Settings UI: WPF requires an STA thread.
        int exitCode = 0;
        var ui = new Thread(() => exitCode = SettingsUi.SettingsApplication.Run(args));
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        return exitCode;
    }
}
