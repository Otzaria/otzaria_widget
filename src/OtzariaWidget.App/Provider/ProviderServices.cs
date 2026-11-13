// Agent D - lazily created, exception-tolerant access to the Core/Calendar services (wired by Composition).
// While a service is stubbed (NotImplementedException) or broken, every accessor degrades instead of throwing, so the
// provider process keeps running and renders error/"data missing" cards.
using System.IO;
using System.Diagnostics;
using OtzariaWidget.App.Provider.Cards;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Provider;

internal sealed class ProviderServices : IWidgetEnvironment, IDisposable
{
    private readonly object _gate = new();
    private readonly Func<ISettingsStore> _settingsFactory;
    private readonly Func<ISettingsStore, IOtzariaDataService> _dataFactory;
    private readonly Func<ICalendarService> _calendarFactory;
    private readonly Func<IOtzariaLauncher> _launcherFactory;

    private ISettingsStore? _settings;
    private IOtzariaDataService? _data;
    private ICalendarService? _calendar;
    private IOtzariaLauncher? _launcher;
    private bool _started;
    private bool _disposed;
    private Timer? _coalesce;
    private string _pendingSource = "data";

    /// <summary>The data service already reacts to widget-settings changes (it invalidates and raises Changed), and both it and the
    /// settings store may fire for one user action; bursts are merged into ONE refresh of the active widgets.</summary>
    public static readonly TimeSpan CoalesceDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Raised on a pool thread when Otzaria data or the widget settings changed (coalesced).</summary>
    public event Action<string>? SourceChanged;

    private void RaiseSourceChanged(string source)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pendingSource = source;
            _coalesce ??= new Timer(_ =>
            {
                string src;
                lock (_gate) { if (_disposed) return; src = _pendingSource; }
                try { SourceChanged?.Invoke(src); }
                catch (Exception ex) { ProviderLog.Error("SourceChanged handler", ex); }
            }, null, Timeout.Infinite, Timeout.Infinite);
            _coalesce.Change(CoalesceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    public ProviderServices(
        Func<ISettingsStore> settings, Func<ISettingsStore, IOtzariaDataService> data,
        Func<ICalendarService> calendar, Func<IOtzariaLauncher> launcher)
    {
        _settingsFactory = settings; _dataFactory = data; _calendarFactory = calendar; _launcherFactory = launcher;
    }

    public static ProviderServices CreateDefault() => new(
        Composition.CreateSettingsStore,
        Composition.CreateDataService,
        Composition.CreateCalendarService,
        Composition.CreateLauncher);

    /// <summary>Creates the settings store and data service and hooks their Changed events (idempotent, never throws).</summary>
    public void EnsureStarted()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            try { _settings = _settingsFactory(); }
            catch (Exception ex) { ProviderLog.Error("create settings store", ex); }
            try
            {
                if (_settings is not null)
                {
                    _data = _dataFactory(_settings);
                    _data.Changed += (_, _) => RaiseSourceChanged("data");
                }
            }
            catch (Exception ex) { ProviderLog.Error("create data service", ex); }
            // Only when there is no data service to relay settings changes do we listen to the store ourselves.
            if (_data is null && _settings is not null)
            {
                try { _settings.Changed += (_, _) => RaiseSourceChanged("settings"); }
                catch (Exception ex) { ProviderLog.Error("subscribe settings", ex); }
            }
        }
    }

    public WidgetAppSettings LoadSettings()
    {
        EnsureStarted();
        try { return _settings?.Load() ?? new WidgetAppSettings(); }
        catch (Exception ex) { ProviderLog.Error("settings.Load", ex); return new WidgetAppSettings(); }
    }

    /// <summary>Snapshot or null (with the exception type in <paramref name="error"/>) when the data service fails.</summary>
    public OtzariaSnapshot? TryGetSnapshot(out string? error)
    {
        EnsureStarted();
        error = null;
        var data = _data;   // Dispose may null the field concurrently
        if (data is null) { error = "data service unavailable"; return null; }
        try { return data.GetSnapshot(); }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            ProviderLog.Warn($"GetSnapshot failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public void InvalidateData()
    {
        EnsureStarted();
        var data = _data;
        try { data?.Invalidate(); }
        catch (Exception ex) { ProviderLog.Error("data.Invalidate", ex); }
    }

    public ICalendarService Calendar
    {
        get
        {
            lock (_gate)
            {
                if (_calendar is not null) return _calendar;
                try { return _calendar = _calendarFactory(); }
                catch (Exception ex)
                {
                    ProviderLog.Error("create calendar service", ex);
                    return _calendar = new UnavailableCalendarService();
                }
            }
        }
    }

    public IOtzariaLauncher? Launcher
    {
        get
        {
            lock (_gate)
            {
                if (_launcher is not null) return _launcher;
                try { return _launcher = _launcherFactory(); }
                catch (Exception ex) { ProviderLog.Error("create launcher", ex); return null; }
            }
        }
    }

    public DateTimeOffset Now => DateTimeOffset.Now;

    public bool LaunchOtzaria(string? uri, OtzariaPaths paths)
    {
        var launcher = Launcher;
        if (launcher is null) return false;
        try { return launcher.Launch(uri, paths); }
        catch (Exception ex) { ProviderLog.Error("launcher.Launch", ex); return false; }
    }

    /// <summary>Starts our own exe without arguments = the settings window (Program.cs dispatches on the arguments).</summary>
    public void StartSettingsWindow()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        // No arguments => Program.Main runs the settings window; a running instance is re-used (single-instance guard in SettingsApplication).
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _coalesce?.Dispose();
            _coalesce = null;
            try { _data?.Dispose(); } catch (Exception ex) { ProviderLog.Error("data.Dispose", ex); }
            _data = null;
        }
    }
}

/// <summary>Stand-in when the calendar service cannot even be constructed: every call fails, so cards show the error state.</summary>
internal sealed class UnavailableCalendarService : ICalendarService
{
    private static Exception Fail() => new InvalidOperationException("calendar service unavailable");
    public IReadOnlyList<CityInfo> Cities => throw Fail();
    public CityInfo? FindCity(string name) => throw Fail();
    public IReadOnlyList<ZmanDefinition> ZmanimRegistry => throw Fail();
    public IReadOnlyList<string> DefaultEnabledZmanim => throw Fail();
    public DayInfo GetDayInfo(DateOnly date, CityInfo city) => throw Fail();
    public IReadOnlyList<ZmanTime> GetZmanim(DateOnly date, CityInfo city, IReadOnlyList<string> ids) => throw Fail();
    public DateOnly GetEffectiveDate(DateTimeOffset now, CityInfo city, string dayTransition) => throw Fail();
}
