using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OtzariaWidget.App.SettingsUi.Infrastructure;
using OtzariaWidget.App.SettingsUi.Localization;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.SettingsUi.ViewModels;

/// <summary>One override field (text + browse + reset + live validation).</summary>
public sealed class PathOverrideViewModel : ObservableObject
{
    public enum PathKind { Folder, File }

    private readonly Func<string?> _get;
    private readonly Action<string?> _set;
    private readonly Action _changed;
    private readonly IShell _shell;
    private readonly PathKind _kind;
    private readonly string _fileFilter;
    private readonly Func<string, (StateKind, string)>? _extraCheck;

    private string _text = "";
    private string _resolved = "";
    private StateKind _state = StateKind.Neutral;
    private string _validation = "";

    public PathOverrideViewModel(string title, string description, PathKind kind, IShell shell,
        Func<string?> get, Action<string?> set, Action changed,
        string? fileFilter = null,
        Func<string, (StateKind, string)>? extraCheck = null)
    {
        Title = title;
        Description = description;
        _kind = kind;
        _shell = shell;
        _get = get;
        _set = set;
        _changed = changed;
        _fileFilter = fileFilter ?? Loc.T("כל הקבצים") + "|*.*";
        _extraCheck = extraCheck;
        _text = get() ?? "";
        Validate();

        BrowseCommand = new RelayCommand(Browse);
        ResetCommand = new RelayCommand(() => Text = "", () => !string.IsNullOrEmpty(_text));
    }

    public string Title { get; }
    public string Description { get; }
    public ICommand BrowseCommand { get; }
    public ICommand ResetCommand { get; }

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (!Set(ref _text, value)) return;
            var normalized = Normalize(value);
            _set(normalized);
            Validate();
            _changed();
        }
    }

    /// <summary>What the resolver actually uses right now (override or auto-detected).</summary>
    public string Resolved { get => _resolved; set { if (Set(ref _resolved, value)) Raise(nameof(HasResolved)); } }
    public bool HasResolved => !string.IsNullOrEmpty(_resolved);

    public StateKind State { get => _state; private set => Set(ref _state, value); }
    public string Validation { get => _validation; private set => Set(ref _validation, value); }

    private static string? Normalize(string raw)
    {
        var t = raw.Trim().Trim('"').Trim();
        return t.Length == 0 ? null : t;
    }

    private void Browse()
    {
        string? initial = Normalize(_text);
        string? picked = _kind == PathKind.Folder
            ? _shell.PickFolder(initial, Title)
            : _shell.PickFile(initial, Title, _fileFilter);
        if (!string.IsNullOrEmpty(picked)) Text = picked;
    }

    private void Validate()
    {
        var path = Normalize(_text);
        if (path is null)
        {
            State = StateKind.Neutral;
            Validation = Loc.T("זיהוי אוטומטי");
            return;
        }

        bool exists;
        try { exists = _kind == PathKind.Folder ? Directory.Exists(path) : File.Exists(path); }
        catch { exists = false; }

        if (!exists)
        {
            State = StateKind.Error;
            Validation = _kind == PathKind.Folder ? Loc.T("התיקייה לא נמצאה") : Loc.T("הקובץ לא נמצא");
            return;
        }

        State = StateKind.Ok;
        Validation = _kind == PathKind.Folder ? Loc.T("התיקייה נמצאה") : Loc.T("הקובץ נמצא");
        if (_extraCheck is not null)
        {
            try
            {
                var (st, msg) = _extraCheck(path);
                if (st != StateKind.Ok) { State = st; Validation = msg; }
            }
            catch { }
        }
    }
}

/// <summary>Page 2 - "מיקום הנתונים".</summary>
public sealed class DataLocationsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private readonly SettingsServices _svc;
    private readonly List<(PathOverrideViewModel Vm, Func<OtzariaPaths, string?> Pick)> _resolvedMap = [];
    private int _gen;

    public DataLocationsViewModel(SettingsSession session, SettingsServices svc)
    {
        _session = session;
        _svc = svc;
        var s = session.Settings;
        void Changed() { _session.MarkChanged(); }

        Items =
        [
            Add(new PathOverrideViewModel(Loc.T("תיקיית הנתונים של אוצריא"),
                Loc.T("בדרך כלל %APPDATA%\\otzaria. בגרסה ניידת: otzaria_data ליד otzaria.exe."),
                PathOverrideViewModel.PathKind.Folder, svc.Shell, () => s.DataRootOverride, v => s.DataRootOverride = v, Changed,
                extraCheck: p => File.Exists(Path.Combine(p, "app_preferences.hive")) || Directory.Exists(Path.Combine(p, "databases"))
                    ? (StateKind.Ok, "")
                    : (StateKind.Warning, Loc.T("התיקייה קיימת, אך לא נמצא בה app_preferences.hive"))),
                p => p.DataRoot),
            Add(new PathOverrideViewModel(Loc.T("תיקיית מסדי הנתונים"),
                Loc.T("התיקייה שמכילה את user_state.db. ברירת המחדל נגזרת מההעדפות של אוצריא."),
                PathOverrideViewModel.PathKind.Folder, svc.Shell, () => s.DatabasesDirOverride, v => s.DatabasesDirOverride = v, Changed,
                extraCheck: p => File.Exists(Path.Combine(p, "user_state.db"))
                    ? (StateKind.Ok, "")
                    : (StateKind.Warning, Loc.T("התיקייה קיימת, אך לא נמצא בה user_state.db"))),
                p => p.UserStateDb is null ? null : Path.GetDirectoryName(p.UserStateDb)),
            Add(new PathOverrideViewModel(Loc.T("קובץ ספריית הספרים (seforim.db)"),
                Loc.T("מסד הספרייה הגדול של אוצריא. הווידג'ט קורא ממנו בלבד."),
                PathOverrideViewModel.PathKind.File, svc.Shell, () => s.LibraryDbOverride, v => s.LibraryDbOverride = v, Changed,
                fileFilter: Loc.T("מסד ספרייה (seforim.db)") + "|seforim.db|" + Loc.T("קבצי SQLite (*.db)") + "|*.db|" + Loc.T("כל הקבצים") + "|*.*"),
                p => p.LibraryDb),
            Add(new PathOverrideViewModel(Loc.T("התוכנה (otzaria.exe)"),
                Loc.T("משמש לפתיחת אוצריא מהווידג'טים. חשוב במיוחד בגרסה ניידת."),
                PathOverrideViewModel.PathKind.File, svc.Shell, () => s.OtzariaExeOverride, v => s.OtzariaExeOverride = v, Changed,
                fileFilter: "otzaria.exe|otzaria.exe|" + Loc.T("קבצי הפעלה (*.exe)") + "|*.exe|" + Loc.T("כל הקבצים") + "|*.*",
                extraCheck: p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? (StateKind.Ok, "")
                    : (StateKind.Warning, Loc.T("הקובץ קיים, אך אינו קובץ הפעלה (.exe)"))),
                p => p.OtzariaExe),
        ];

        ResetAllCommand = new RelayCommand(() => { foreach (var i in Items) i.Text = ""; });
        _session.Saved += (_, _) => RefreshResolved();
        RefreshResolved();
    }

    public ObservableCollection<PathOverrideViewModel> Items { get; }
    public ICommand ResetAllCommand { get; }
    public string Title => Loc.T("מיקום הנתונים");

    private PathOverrideViewModel Add(PathOverrideViewModel vm, Func<OtzariaPaths, string?> pick)
    {
        _resolvedMap.Add((vm, pick));
        return vm;
    }

    /// <summary>Shows what the resolver detects with the current overrides (background; resolver may touch disk/registry).</summary>
    public async void RefreshResolved()
    {
        int gen = ++_gen;
        OtzariaPaths? paths = null;
        string? error = null;
        try
        {
            var settings = _session.Settings;
            paths = await Task.Run(() => _svc.Resolver.Resolve(settings)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            error = ErrorText.From(ex);
        }
        if (gen != _gen) return;
        foreach (var (vm, pick) in _resolvedMap)
        {
            string? value = null;
            if (paths is not null)
            {
                try { value = pick(paths); } catch { }
            }
            vm.Resolved = paths is null
                ? (error ?? Loc.T("לא זמין"))
                : (string.IsNullOrWhiteSpace(value) ? Loc.T("לא נמצא") : value);
        }
    }
}
