using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.SettingsUi.ViewModels;

public sealed record StatusRow(string Label, string Detail, StateKind State, string? Note = null);

public sealed record SourceRow(string Name, string Message, StateKind State);

/// <summary>Page 1 - "מצב": what was detected, counts, quick actions, how to add the widgets.</summary>
public sealed class StatusViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private readonly SettingsServices _svc;
    private readonly SnapshotHub _hub;

    private bool _isLoading;
    private string? _error;
    private string _lastRead = "";
    private string _recentCount = "—";
    private string _bookmarkCount = "—";
    private string _progressCount = "—";
    private string _searchCount = "—";
    private StatusMessage? _action;

    public StatusViewModel(SettingsSession session, SettingsServices svc, SnapshotHub hub)
    {
        _session = session;
        _svc = svc;
        _hub = hub;
        RefreshCommand = new RelayCommand(() => _ = _hub.RefreshAsync(true), () => !IsLoading);
        OpenOtzariaCommand = new RelayCommand(OpenOtzaria);
        _hub.Updated += (_, _) => Apply();
        Apply();
    }

    public string Title => "מצב";

    public ObservableCollection<StatusRow> Rows { get; } = [];
    public ObservableCollection<SourceRow> Sources { get; } = [];
    public ObservableCollection<string> Diagnostics { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand OpenOtzariaCommand { get; }

    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }
    public string? Error
    {
        get => _error;
        private set { if (Set(ref _error, value)) Raise(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(_error);
    public string LastRead { get => _lastRead; private set => Set(ref _lastRead, value); }
    public string RecentCount { get => _recentCount; private set => Set(ref _recentCount, value); }
    public string BookmarkCount { get => _bookmarkCount; private set => Set(ref _bookmarkCount, value); }
    public string ProgressCount { get => _progressCount; private set => Set(ref _progressCount, value); }
    public string SearchCount { get => _searchCount; private set => Set(ref _searchCount, value); }

    public StatusMessage? Action { get => _action; private set => Set(ref _action, value); }

    private void Apply()
    {
        IsLoading = _hub.IsLoading;
        Error = _hub.Error;
        var snap = _hub.Current;

        Rows.Clear();
        Sources.Clear();
        Diagnostics.Clear();

        if (snap is null)
        {
            RecentCount = BookmarkCount = ProgressCount = SearchCount = "—";
            LastRead = IsLoading ? "טוען נתונים…" : "";
            return;
        }

        var p = snap.Paths;
        Rows.Add(Row("תיקיית הנתונים של אוצריא", p.DataRoot, dir: true));
        Rows.Add(Row("מסד המצב האישי (user_state.db)", p.UserStateDb));
        Rows.Add(Row("קובץ ההעדפות (app_preferences.hive)", p.PreferencesHive));
        Rows.Add(Row("מסד הספרייה (seforim.db)", p.LibraryDb));
        Rows.Add(Row("התוכנה (otzaria.exe)", p.OtzariaExe));
        Rows.Add(new StatusRow("גרסה ניידת", p.IsPortable ? "כן" : "לא", StateKind.Neutral));

        foreach (var d in p.Diagnostics) Diagnostics.Add(d);
        foreach (var s in snap.Status)
        {
            var state = s.Health switch
            {
                DataHealth.Ok => StateKind.Ok,
                DataHealth.NotFound => StateKind.Warning,
                _ => StateKind.Error,
            };
            Sources.Add(new SourceRow(s.Name, string.IsNullOrWhiteSpace(s.Message) ? HealthText(s.Health) : s.Message!, state));
        }

        RecentCount = snap.RecentBooks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        BookmarkCount = snap.Bookmarks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ProgressCount = snap.Progress.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SearchCount = snap.RecentSearches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        LastRead = "נקרא לאחרונה: " + snap.ReadAt.LocalDateTime.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string HealthText(DataHealth h) => h switch
    {
        DataHealth.Ok => "תקין",
        DataHealth.NotFound => "לא נמצא",
        DataHealth.Unreadable => "לא ניתן לקריאה",
        DataHealth.UnknownSchema => "מבנה נתונים לא מוכר",
        _ => "",
    };

    private static StatusRow Row(string label, string? path, bool dir = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return new StatusRow(label, "לא נמצא", StateKind.Error);
        bool exists;
        try { exists = dir ? Directory.Exists(path) : File.Exists(path); }
        catch { exists = false; }
        return exists
            ? new StatusRow(label, path, StateKind.Ok)
            : new StatusRow(label, path, StateKind.Error, "הנתיב אינו קיים בדיסק");
    }

    private void OpenOtzaria()
    {
        try
        {
            var paths = _hub.Current?.Paths ?? _svc.Resolver.Resolve(_session.Settings);
            bool ok = _svc.Launcher.Launch(null, paths);
            Action = ok
                ? new StatusMessage("אוצריא הופעלה.", StateKind.Ok)
                : new StatusMessage("לא נמצא otzaria.exe. אפשר לבחור אותו ידנית בדף \"מיקום הנתונים\".", StateKind.Warning);
        }
        catch (Exception ex)
        {
            SettingsLog.Error("Launch Otzaria failed", ex);
            Action = new StatusMessage("לא ניתן להפעיל את אוצריא: " + ErrorText.From(ex), StateKind.Error);
        }
    }
}
