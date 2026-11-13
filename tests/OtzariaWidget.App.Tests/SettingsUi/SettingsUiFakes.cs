using System.Runtime.CompilerServices;
using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests.SettingsUi;

/// <summary>Keeps the settings UI's log / data folder out of the real %LOCALAPPDATA% while tests run.</summary>
internal static class SettingsUiTestEnvironment
{
    [ModuleInitializer]
    internal static void Init()
    {
        var dir = Path.Combine(Path.GetTempPath(), "OtzariaWidget.App.Tests." + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        AppEnvironment.DataFolderOverride = dir;
    }
}

internal sealed class FakeSettingsStore : ISettingsStore
{
    public WidgetAppSettings Current = new();
    public int Saves;
    public bool Fail;
    public WidgetAppSettings Load() => Current;
    public void Save(WidgetAppSettings s) { if (Fail) throw new IOException("disk full"); Interlocked.Increment(ref Saves); Current = s; }
    public string SettingsFilePath => "fake-settings.json";
    public event EventHandler? Changed { add { } remove { } }
}

internal sealed class ThrowingSettingsStore : ISettingsStore
{
    public WidgetAppSettings Load() => throw new NotImplementedException();
    public void Save(WidgetAppSettings s) => throw new NotImplementedException();
    public string SettingsFilePath => throw new NotImplementedException();
    public event EventHandler? Changed { add { } remove { } }
}

internal sealed class FakeDataService : IOtzariaDataService
{
    public bool Throw;
    public OtzariaPreferences Prefs = new() { SelectedCity = "ירושלים", TwelveHourClock = true };
    public OtzariaSnapshot GetSnapshot() => Throw ? throw new NotImplementedException() : new OtzariaSnapshot
    {
        ReadAt = DateTimeOffset.Now,
        Paths = new OtzariaPaths { DataRoot = @"C:\nope", Diagnostics = ["note 1"] },
        Preferences = Prefs,
        Status = [new SourceStatus("user_state.db", DataHealth.NotFound, null)],
    };
    public void Invalidate() { }
    public event EventHandler? Changed { add { } remove { } }
    public void Dispose() { }
}

internal sealed class UiFakeCalendar : ICalendarService
{
    public bool Throw;
    private static readonly CityInfo A = new() { Name = "ירושלים", Country = "ארץ ישראל", TimeZoneId = "Asia/Jerusalem", IsInIsrael = true };
    private static readonly CityInfo B = new() { Name = "ניו יורק", Country = "ארצות הברית", TimeZoneId = "America/New_York" };
    private T Guard<T>(Func<T> f) => Throw ? throw new NotImplementedException() : f();
    public IReadOnlyList<CityInfo> Cities => Guard<IReadOnlyList<CityInfo>>(() => [A, B]);
    public CityInfo? FindCity(string n) => Guard(() => Cities.FirstOrDefault(c => c.Name == n));
    public IReadOnlyList<ZmanDefinition> ZmanimRegistry =>
        Guard<IReadOnlyList<ZmanDefinition>>(() => [new("alos", "עלות", null), new("sunset", "שקיעה", null), new("tzais", "צאת", null)]);
    public IReadOnlyList<string> DefaultEnabledZmanim => Guard<IReadOnlyList<string>>(() => ["sunset"]);
    public DayInfo GetDayInfo(DateOnly d, CityInfo c) =>
        Guard(() => new DayInfo { Date = d, HebrewDate = "ג' כסלו", HebrewDayOfWeek = "יום שישי" });
    public IReadOnlyList<ZmanTime> GetZmanim(DateOnly d, CityInfo c, IReadOnlyList<string> ids) =>
        Guard<IReadOnlyList<ZmanTime>>(() => ids.Select(i => new ZmanTime(i, i, i == "tzais" ? null : DateTimeOffset.Now.AddHours(1))).ToList());
    public DateOnly GetEffectiveDate(DateTimeOffset now, CityInfo c, string t) => DateOnly.FromDateTime(now.DateTime);
}

internal sealed class FakeUiShell : IShell
{
    public string? Picked;
    public bool UriThrows;
    public string? OpenedUri;
    public string? OpenedFolder;
    public void OpenFolder(string p) => OpenedFolder = p;
    public void OpenUri(string u) { if (UriThrows) throw new InvalidOperationException(); OpenedUri = u; }
    public string? PickFolder(string? i, string t) => Picked;
    public string? PickFile(string? i, string t, string f) => Picked;
}

internal sealed class FakeUiResolver : IOtzariaPathResolver
{
    public OtzariaPaths Resolve(WidgetAppSettings s) => new() { DataRoot = @"C:\auto" };
}

internal sealed class FakeUiLauncher : IOtzariaLauncher
{
    public bool Result = true;
    public string? Last;
    public string? BuildLink(ReadingEntry e) => null;
    public string BuildSearchLink(string q, string? m) => "";
    public bool Launch(string? uri, OtzariaPaths p) { Last = uri ?? "(null)"; return Result; }
}

internal static class UiTestHelpers
{
    public static SettingsServices Services(FakeSettingsStore st, FakeDataService? d = null, UiFakeCalendar? c = null, FakeUiShell? sh = null, FakeUiLauncher? l = null) => new()
    {
        Store = st, Data = d ?? new FakeDataService(), Calendar = c ?? new UiFakeCalendar(), Resolver = new FakeUiResolver(),
        Launcher = l ?? new FakeUiLauncher(), Shell = sh ?? new FakeUiShell(),
    };

    /// <summary>
    /// The settings UI captures <see cref="SynchronizationContext.Current"/> (the WPF dispatcher in production). In tests no context
    /// is wanted: xunit's own would queue callbacks behind the test that is waiting for them.
    /// </summary>
    public static T WithoutSyncContext<T>(Func<T> create)
    {
        var prev = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { return create(); }
        finally { SynchronizationContext.SetSynchronizationContext(prev); }
    }

    public static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var end = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < end)
        {
            if (condition()) return true;
            Thread.Sleep(15);
        }
        return condition();
    }
}
