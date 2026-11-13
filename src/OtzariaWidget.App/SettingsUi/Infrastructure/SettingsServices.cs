using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.SettingsUi.Infrastructure;

/// <summary>The services the settings UI needs, obtained through <see cref="Composition"/> (or fakes in tests).</summary>
public sealed class SettingsServices
{
    public required ISettingsStore Store { get; init; }
    public required IOtzariaDataService Data { get; init; }
    public required ICalendarService Calendar { get; init; }
    public required IOtzariaPathResolver Resolver { get; init; }
    public required IOtzariaLauncher Launcher { get; init; }
    public required IShell Shell { get; init; }

    /// <summary>Builds the real services; any that fail to construct are replaced by "unavailable" stand-ins so the UI still opens.</summary>
    public static SettingsServices CreateDefault()
    {
        ISettingsStore store = Guard(Composition.CreateSettingsStore, ex => new UnavailableSettingsStore(ex));
        return new SettingsServices
        {
            Store = store,
            Data = Guard(() => Composition.CreateDataService(store), ex => new UnavailableDataService(ex)),
            Calendar = Guard(Composition.CreateCalendarService, ex => new UnavailableCalendar(ex)),
            Resolver = Guard(Composition.CreatePathResolver, ex => new UnavailableResolver(ex)),
            Launcher = Guard(Composition.CreateLauncher, ex => new UnavailableLauncher(ex)),
            Shell = new WindowsShell(),
        };
    }

    private static T Guard<T>(Func<T> create, Func<Exception, T> fallback)
    {
        try { return create(); }
        catch (Exception ex)
        {
            SettingsLog.Error("Service construction failed: " + typeof(T).Name, ex);
            return fallback(ex);
        }
    }
}

// ---- stand-ins used only when a real service cannot even be constructed ----

internal sealed class UnavailableSettingsStore(Exception why) : ISettingsStore
{
    public WidgetAppSettings Load() => throw why;
    public void Save(WidgetAppSettings settings) => throw why;
    public string SettingsFilePath => throw why;
#pragma warning disable CS0067
    public event EventHandler? Changed;
#pragma warning restore CS0067
}

internal sealed class UnavailableDataService(Exception why) : IOtzariaDataService
{
    public OtzariaSnapshot GetSnapshot() => throw why;
    public void Invalidate() { }
#pragma warning disable CS0067
    public event EventHandler? Changed;
#pragma warning restore CS0067
    public void Dispose() { }
}

internal sealed class UnavailableCalendar(Exception why) : ICalendarService
{
    public IReadOnlyList<CityInfo> Cities => throw why;
    public CityInfo? FindCity(string name) => throw why;
    public IReadOnlyList<ZmanDefinition> ZmanimRegistry => throw why;
    public IReadOnlyList<string> DefaultEnabledZmanim => throw why;
    public DayInfo GetDayInfo(DateOnly date, CityInfo city) => throw why;
    public IReadOnlyList<ZmanTime> GetZmanim(DateOnly date, CityInfo city, IReadOnlyList<string> ids) => throw why;
    public DateOnly GetEffectiveDate(DateTimeOffset now, CityInfo city, string dayTransition) => throw why;
}

internal sealed class UnavailableResolver(Exception why) : IOtzariaPathResolver
{
    public OtzariaPaths Resolve(WidgetAppSettings settings) => throw why;
}

internal sealed class UnavailableLauncher(Exception why) : IOtzariaLauncher
{
    public string? BuildLink(ReadingEntry entry) => throw why;
    public string BuildSearchLink(string query, string? mode) => throw why;
    public bool Launch(string? uri, OtzariaPaths paths) => throw why;
}
