// Owned by the project lead. The single place where concrete implementations are wired.
using OtzariaWidget.Calendar;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Launch;
using OtzariaWidget.Core.Paths;
using OtzariaWidget.Core.Services;
using OtzariaWidget.Core.Settings;

namespace OtzariaWidget.App;

public static class Composition
{
    public static ISettingsStore CreateSettingsStore() => new JsonSettingsStore();
    public static IOtzariaDataService CreateDataService(ISettingsStore store) => new OtzariaDataService(store);
    public static ICalendarService CreateCalendarService() => new CalendarService();
    public static IOtzariaPathResolver CreatePathResolver() => new OtzariaPathResolver();
    public static IOtzariaLauncher CreateLauncher() => new OtzariaLauncher();
}
