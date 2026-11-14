using OtzariaWidget.App.SettingsUi.Localization;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.SettingsUi.Infrastructure;

/// <summary>
/// Holds the one live <see cref="WidgetAppSettings"/> instance that all pages edit, and saves it
/// (debounced) through <see cref="ISettingsStore"/>. Also drives the subtle "saved" indicator.
/// Construct on the UI thread: timer callbacks are marshalled back to the captured SynchronizationContext.
/// </summary>
public sealed class SettingsSession : ObservableObject, IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan IndicatorLifetime = TimeSpan.FromSeconds(2.5);

    private readonly ISettingsStore _store;
    private readonly SynchronizationContext? _ctx = SynchronizationContext.Current;
    private readonly object _gate = new();
    private Timer? _saveTimer;
    private Timer? _hideTimer;
    private bool _dirty;
    private bool _disposed;

    private string _saveText = "";
    private bool _saveVisible;
    private bool _saveFailed;

    public SettingsSession(ISettingsStore store)
    {
        _store = store;
        try
        {
            Settings = store.Load() ?? new WidgetAppSettings();
        }
        catch (Exception ex)
        {
            SettingsLog.Error("Loading settings failed", ex);
            Settings = new WidgetAppSettings();
            LoadError = Loc.F("לא ניתן לטעון את ההגדרות הקיימות ({0}). מוצגות הגדרות ברירת מחדל.", ErrorText.From(ex));
        }
    }

    public WidgetAppSettings Settings { get; }
    public string? LoadError { get; }

    public string SaveText { get => _saveText; private set => Set(ref _saveText, value); }
    public bool SaveVisible { get => _saveVisible; private set => Set(ref _saveVisible, value); }
    public bool SaveFailed { get => _saveFailed; private set => Set(ref _saveFailed, value); }

    /// <summary>Raised on the UI context after every successful save.</summary>
    public event EventHandler? Saved;

    /// <summary>Call after mutating <see cref="Settings"/>; saves after a short quiet period.</summary>
    public void MarkChanged()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _dirty = true;
            _saveTimer ??= new Timer(_ => Post(SaveNow), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Saves immediately if something is pending (called on window close).</summary>
    public void Flush() => SaveNow();

    private void Post(Action action)
    {
        if (_ctx is null) action();
        else _ctx.Post(_ => action(), null);
    }

    private void SaveNow()
    {
        lock (_gate)
        {
            if (!_dirty || _disposed) return;
            _dirty = false;
        }

        try
        {
            _store.Save(Settings);
            SaveFailed = false;
            SaveText = Loc.T("נשמר");
            SaveVisible = true;
            ScheduleHide();
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SettingsLog.Error("Saving settings failed", ex);
            SaveFailed = true;
            SaveText = Loc.T("השמירה נכשלה");
            SaveVisible = true;
        }
    }

    private void ScheduleHide()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _hideTimer ??= new Timer(_ => Post(() => { if (!SaveFailed) SaveVisible = false; }), null, Timeout.Infinite, Timeout.Infinite);
            _hideTimer.Change(IndicatorLifetime, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _saveTimer?.Dispose();
            _hideTimer?.Dispose();
        }
    }
}
