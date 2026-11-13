using OtzariaWidget.App.Provider;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests.Runtime;

public class ProviderServicesTests
{
    private sealed class Store : ISettingsStore
    {
        public WidgetAppSettings Current = new();
        public WidgetAppSettings Load() => Current;
        public void Save(WidgetAppSettings s) => Current = s;
        public string SettingsFilePath => "fake";
        public event EventHandler? Changed;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Data : IOtzariaDataService
    {
        public int Invalidations;
        public bool Throw;
        public bool Disposed;
        public OtzariaSnapshot GetSnapshot() => Throw ? throw new InvalidOperationException("boom") : new OtzariaSnapshot { ReadAt = DateTimeOffset.Now, Paths = new OtzariaPaths() };
        public void Invalidate() => Invalidations++;
        public event EventHandler? Changed;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() => Disposed = true;
    }

    private sealed class Launcher : IOtzariaLauncher
    {
        public bool Throw;
        public string? BuildLink(ReadingEntry entry) => null;
        public string BuildSearchLink(string query, string? mode) => "";
        public bool Launch(string? uri, OtzariaPaths paths) => Throw ? throw new InvalidOperationException() : true;
    }

    private static ProviderServices Make(Store st, Data? data, Func<ICalendarService>? cal = null, Launcher? launcher = null) =>
        new(() => st, _ => data ?? throw new InvalidOperationException("no data"), cal ?? (() => new FakeCalendarService()), () => launcher ?? new Launcher());

    [Fact]
    public async Task Bursts_of_changes_are_coalesced_into_one_refresh()
    {
        var st = new Store(); var data = new Data();
        using var svc = Make(st, data);
        int calls = 0;
        svc.SourceChanged += _ => Interlocked.Increment(ref calls);
        svc.EnsureStarted();

        for (int i = 0; i < 10; i++) { data.Raise(); await Task.Delay(20); }
        await Task.Delay(ProviderServices.CoalesceDelay + TimeSpan.FromMilliseconds(500));
        Assert.Equal(1, calls);

        data.Raise();   // a later change still gets through
        await Task.Delay(ProviderServices.CoalesceDelay + TimeSpan.FromMilliseconds(500));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Settings_changes_are_relayed_by_the_data_service_not_double_counted()
    {
        var st = new Store(); var data = new Data();
        using var svc = Make(st, data);
        int calls = 0;
        svc.SourceChanged += _ => Interlocked.Increment(ref calls);
        svc.EnsureStarted();
        st.Raise();    // the real data service would turn this into data.Changed; we only listen to the data service
        await Task.Delay(ProviderServices.CoalesceDelay + TimeSpan.FromMilliseconds(400));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Without_a_data_service_settings_changes_still_trigger_a_refresh()
    {
        var st = new Store();
        using var svc = Make(st, null);
        int calls = 0;
        svc.SourceChanged += _ => Interlocked.Increment(ref calls);
        svc.EnsureStarted();
        st.Raise();
        await Task.Delay(ProviderServices.CoalesceDelay + TimeSpan.FromMilliseconds(500));
        Assert.Equal(1, calls);
        Assert.Null(svc.TryGetSnapshot(out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void Failing_services_degrade_instead_of_throwing()
    {
        var st = new Store(); var data = new Data { Throw = true };
        using var svc = Make(st, data, cal: () => throw new NotImplementedException(), launcher: new Launcher { Throw = true });

        Assert.Null(svc.TryGetSnapshot(out var err));
        Assert.Equal(nameof(InvalidOperationException), err);
        Assert.NotNull(svc.LoadSettings());
        svc.InvalidateData();
        Assert.Equal(1, data.Invalidations);

        var cal = svc.Calendar;                       // stand-in whose every call throws, so cards show the error state
        Assert.Throws<InvalidOperationException>(() => cal.Cities);
        Assert.False(svc.LaunchOtzaria("otzaria://open/library", new OtzariaPaths()));   // launcher exception swallowed
    }

    [Fact]
    public void Throwing_settings_store_factory_is_survivable()
    {
        using var svc = new ProviderServices(() => throw new IOException("x"), _ => new Data(), () => new FakeCalendarService(), () => new Launcher());
        Assert.NotNull(svc.LoadSettings());
        Assert.Null(svc.TryGetSnapshot(out _));
    }

    [Fact]
    public void Dispose_disposes_data_service_and_later_calls_are_safe()
    {
        var st = new Store(); var data = new Data();
        var svc = Make(st, data);
        svc.EnsureStarted();
        svc.Dispose();
        Assert.True(data.Disposed);
        Assert.Null(svc.TryGetSnapshot(out _));
        svc.InvalidateData();
        data.Raise();   // no event after dispose
    }

    [Fact]
    public void Describe_hides_exception_messages_that_contain_paths()
    {
        var withPath = new FileNotFoundException(@"Could not find file 'C:\Users\someone\AppData\Roaming\otzaria\user_state.db'.");
        var text = ProviderLog.Describe(withPath);
        Assert.DoesNotContain("someone", text);
        Assert.Contains(nameof(FileNotFoundException), text);

        var plain = ProviderLog.Describe(new InvalidOperationException("no process path"));
        Assert.Contains("no process path", plain);
    }
}
