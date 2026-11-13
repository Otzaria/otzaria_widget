using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Launch;
using OtzariaWidget.Core.Library;
using OtzariaWidget.Core.Paths;
using OtzariaWidget.Core.Preferences;
using OtzariaWidget.Core.Progress;
using OtzariaWidget.Core.UserState;

namespace OtzariaWidget.Core.Services;

/// <summary>
/// The aggregate the widget provider consumes: resolves paths, reads user_state.db / Hive preferences /
/// Shamor Vezachor progress (each source isolated, with a Hebrew <see cref="SourceStatus"/>), caches the snapshot
/// and watches Otzaria's files (debounced) to raise <see cref="Changed"/>. Thread-safe.
/// </summary>
public sealed class OtzariaDataService : IOtzariaDataService
{
    public const int MaxRecentBooks = 30;
    public const int MaxRecentSearches = 20;
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(1500);
    /// <summary>Safety net when file notifications are lazy/lost (NTFS can delay or drop change events): a cached snapshot
    /// older than this is rebuilt on the next <see cref="GetSnapshot"/> (no event is raised for that).</summary>
    public static readonly TimeSpan CacheMaxAge = TimeSpan.FromMinutes(5);
    /// <summary>A snapshot built while Otzaria's files could not be found/watched (e.g. Otzaria was never started) is
    /// re-read after this short time so that data created later is picked up without any file event.</summary>
    public static readonly TimeSpan IncompleteCacheMaxAge = TimeSpan.FromSeconds(5);

    private readonly ISettingsStore _settingsStore;
    private readonly IOtzariaPathResolver _pathResolver;
    private readonly IUserStateReader _userState;
    private readonly IPreferencesReader _preferences;
    private readonly IProgressService _progress;
    private readonly Func<string, ILibraryCatalog> _catalogFactory;
    private readonly IOtzariaLauncher _launcher;
    private readonly TimeSpan _debounce;

    private readonly object _buildLock = new();
    private readonly object _stateLock = new();
    private OtzariaSnapshot? _cached;
    private bool _cachedIncomplete;
    private long _version;
    private bool _disposed;

    private readonly List<FileSystemWatcher> _watchers = [];
    private string _watchKey = "";
    private Timer? _debounceTimer;

    private EventHandler? _changed;
    private Timer? _pollTimer;
    private int _polling;
    private OtzariaPaths? _lastPaths;
    private string? _lastToken;
    /// <summary>How often change tokens are compared while somebody listens to <see cref="Changed"/>. NTFS does NOT send change
    /// notifications for appends to a file that Otzaria keeps open (its -wal), so the FileSystemWatcher alone misses live edits.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    internal TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    private string? _catalogKey;
    private ILibraryCatalog? _catalog;

    /// <summary>Production wiring.</summary>
    public OtzariaDataService(ISettingsStore settingsStore)
        : this(settingsStore, new OtzariaPathResolver(), new UserStateReader(), new PreferencesReader(),
               new ProgressService(), path => new LibraryCatalog(path), new OtzariaLauncher()) { }

    public OtzariaDataService(
        ISettingsStore settingsStore,
        IOtzariaPathResolver pathResolver,
        IUserStateReader userStateReader,
        IPreferencesReader preferencesReader,
        IProgressService progressService,
        Func<string, ILibraryCatalog> catalogFactory,
        IOtzariaLauncher launcher)
        : this(settingsStore, pathResolver, userStateReader, preferencesReader, progressService, catalogFactory, launcher, DefaultDebounce) { }

    /// <summary>Same as the main constructor with an explicit debounce (tests).</summary>
    public OtzariaDataService(
        ISettingsStore settingsStore,
        IOtzariaPathResolver pathResolver,
        IUserStateReader userStateReader,
        IPreferencesReader preferencesReader,
        IProgressService progressService,
        Func<string, ILibraryCatalog> catalogFactory,
        IOtzariaLauncher launcher,
        TimeSpan debounce)
    {
        _settingsStore = settingsStore;
        _pathResolver = pathResolver;
        _userState = userStateReader;
        _preferences = preferencesReader;
        _progress = progressService;
        _catalogFactory = catalogFactory;
        _launcher = launcher;
        _debounce = debounce;
        _debounceTimer = new Timer(_ => OnDebounced(), null, Timeout.Infinite, Timeout.Infinite);
        try { _settingsStore.Changed += OnSettingsChanged; } catch { /* best effort */ }
    }

    public event EventHandler? Changed
    {
        add
        {
            lock (_stateLock)
            {
                _changed += value;
                if (!_disposed && _pollTimer is null && PollInterval > TimeSpan.Zero)
                    _pollTimer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
            }
        }
        remove
        {
            Timer? stop = null;
            lock (_stateLock)
            {
                _changed -= value;
                if (_changed is null) { stop = _pollTimer; _pollTimer = null; }
            }
            try { stop?.Dispose(); } catch { }
        }
    }

    // ==========================================================================================
    public OtzariaSnapshot GetSnapshot()
    {
        lock (_buildLock)
        {
            long version;
            lock (_stateLock)
            {
                if (_cached is not null && !IsStale(_cached, _cachedIncomplete)) return _cached;
                version = _version;
            }
            var snap = Build(out var incomplete);
            lock (_stateLock)
            {
                if (_version == version && !_disposed) { _cached = snap; _cachedIncomplete = incomplete; }
            }
            return snap;
        }
    }

    private static bool IsStale(OtzariaSnapshot snap, bool incomplete)
    {
        var age = DateTimeOffset.Now - snap.ReadAt;
        // a clock that went backwards also counts as stale
        return age < TimeSpan.Zero || age > (incomplete ? IncompleteCacheMaxAge : CacheMaxAge);
    }

    public void Invalidate()
    {
        lock (_stateLock)
        {
            _cached = null;
            _version++;
        }
    }

    public void Dispose()
    {
        List<FileSystemWatcher> toDispose;
        Timer? timer, poll;
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            poll = _pollTimer;
            _pollTimer = null;
            toDispose = [.. _watchers];
            _watchers.Clear();
            timer = _debounceTimer;
            _debounceTimer = null;
            _cached = null;
        }
        try { _settingsStore.Changed -= OnSettingsChanged; } catch { }
        foreach (var w in toDispose) { try { w.Dispose(); } catch { } }
        try { timer?.Dispose(); } catch { }
        try { poll?.Dispose(); } catch { }
    }

    // ==========================================================================================
    //  Change notification
    // ==========================================================================================
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // The store already debounces its own file events, so react right away.
        Invalidate();
        RaiseChanged();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            try { _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>The OS dropped events (buffer overflow) or the watched folder went away: re-read and re-arm the watchers.</summary>
    internal void OnWatcherError(object? sender, ErrorEventArgs e)
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _watchKey = ""; // forces ArmWatchers to recreate them on the next build
            try { _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
        }
    }

    internal void SimulateWatcherErrorForTests() => OnWatcherError(this, new ErrorEventArgs(new IOException("test")));

    private void OnDebounced()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
        }
        Invalidate();
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        EventHandler? h;
        lock (_stateLock)
        {
            if (_disposed) return;
            h = _changed;
        }
        if (h is null) return;
        foreach (var d in h.GetInvocationList())
        {
            // one throwing subscriber must neither break the service nor starve the other subscribers
            try { ((EventHandler)d)(this, EventArgs.Empty); } catch { }
        }
    }

    // ---- polling fallback ----------------------------------------------------------------------------
    private void Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            OtzariaPaths? paths;
            lock (_stateLock)
            {
                if (_disposed) return;
                paths = _lastPaths;
            }
            if (paths is null) return;
            var token = ComputeToken(paths);
            lock (_stateLock)
            {
                if (_disposed || token is null || token == _lastToken) return;
                _lastToken = token;
                try { _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
            }
        }
        catch { /* polling is best-effort */ }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    /// <summary>A cheap fingerprint of everything we read from Otzaria's files (null = could not be determined).</summary>
    private string? ComputeToken(OtzariaPaths paths)
    {
        try
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(paths.UserStateDb))
            {
                string? t = null;
                try { t = (_userState as IUserStateChangeProbe)?.GetChangeToken(paths.UserStateDb); } catch { }
                // fallback when the reader cannot tell: sizes of the db and its wal (FileInfo is accurate even for files held open)
                t ??= FileSig(paths.UserStateDb) + "|" + FileSig(paths.UserStateDb + "-wal");
                parts.Add(t);
            }
            if (!string.IsNullOrEmpty(paths.PreferencesHive)) parts.Add(FileSig(paths.PreferencesHive));
            return string.Join("#", parts);
        }
        catch { return null; }
    }

    private static string FileSig(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? $"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}" : "-";
        }
        catch { return "?"; }
    }

    /// <returns>true when every wanted folder is being watched.</returns>
    private bool ArmWatchers(OtzariaPaths paths)
    {
        // Only the .db and the -wal are watched: the -shm is touched by our own (and any) reader, which would loop.
        var targets = new List<(string Dir, string[] Filters)>();
        if (!string.IsNullOrEmpty(paths.UserStateDb))
        {
            var dir = Path.GetDirectoryName(paths.UserStateDb);
            var name = Path.GetFileName(paths.UserStateDb);
            if (!string.IsNullOrEmpty(dir)) targets.Add((dir, [name, name + "-wal"]));
        }
        if (!string.IsNullOrEmpty(paths.PreferencesHive))
        {
            var dir = Path.GetDirectoryName(paths.PreferencesHive);
            var name = Path.GetFileName(paths.PreferencesHive);
            if (!string.IsNullOrEmpty(dir)) targets.Add((dir, [name]));
        }
        var key = string.Join("|", targets.Select(t => t.Dir + "\\" + string.Join(",", t.Filters)));

        lock (_stateLock)
        {
            if (_disposed) return true;
            if (key == _watchKey) return _watchers.Count == targets.Count;
            foreach (var w in _watchers) { try { w.Dispose(); } catch { } }
            _watchers.Clear();
            _watchKey = key;
            foreach (var (dir, filters) in targets)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    var w = new FileSystemWatcher(dir)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                        IncludeSubdirectories = false,
                    };
                    foreach (var f in filters) w.Filters.Add(f);
                    w.Changed += OnFileEvent;
                    w.Created += OnFileEvent;
                    w.Deleted += OnFileEvent;
                    w.Renamed += OnFileEvent;
                    w.Error += OnWatcherError;
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch { /* watching is best-effort */ }
            }
            return _watchers.Count == targets.Count;
        }
    }

    // ==========================================================================================
    //  Snapshot building
    // ==========================================================================================
    private OtzariaSnapshot Build(out bool incomplete)
    {
        var status = new List<SourceStatus>();

        WidgetAppSettings settings;
        try { settings = _settingsStore.Load() ?? new WidgetAppSettings(); }
        catch { settings = new WidgetAppSettings(); }

        OtzariaPaths paths;
        try { paths = _pathResolver.Resolve(settings) ?? new OtzariaPaths(); }
        catch (Exception ex)
        {
            paths = new OtzariaPaths { Diagnostics = [$"איתור הנתיבים נכשל: {ex.GetType().Name}"] };
        }

        // the token is taken BEFORE reading: a change that lands while we read is detected by the next poll
        var token = ComputeToken(paths);
        lock (_stateLock) { _lastPaths = paths; _lastToken = token; }

        // ---- user_state.db ----
        IReadOnlyList<ReadingEntry> recent = [], bookmarks = [];
        IReadOnlyList<RecentSearch> searches = [];
        IReadOnlyList<BookmarkGroup> groups = [];
        IReadOnlyList<WorkspaceInfo> workspaces = [];
        IReadOnlyList<OpenTabInfo> tabs = [];

        if (string.IsNullOrEmpty(paths.UserStateDb))
        {
            status.Add(new SourceStatus(UserStateReader.SourceName, DataHealth.NotFound, "לא נמצא קובץ user_state.db"));
        }
        else
        {
            var path = paths.UserStateDb;
            SourceStatus? probe = null;
            try { probe = (_userState as IUserStateSchemaProbe)?.Probe(path); } catch { }

            if (probe is not null && probe.Health != DataHealth.Ok)
            {
                status.Add(probe);
            }
            else
            {
                var failures = 0;
                T Try<T>(Func<T> f, T fallback)
                {
                    try { return f(); }
                    catch { failures++; return fallback; }
                }
                recent = Try(() => _userState.ReadRecentBooks(path, MaxRecentBooks), recent);
                searches = Try(() => _userState.ReadRecentSearches(path, MaxRecentSearches), searches);
                bookmarks = Try(() => _userState.ReadBookmarks(path), bookmarks);
                groups = Try(() => _userState.ReadBookmarkGroups(path), groups);
                workspaces = Try(() => _userState.ReadWorkspaces(path), workspaces);
                tabs = Try(() => _userState.ReadOpenTabs(path), tabs);

                status.Add(failures == 0
                    ? new SourceStatus(UserStateReader.SourceName, DataHealth.Ok, null)
                    : new SourceStatus(UserStateReader.SourceName, DataHealth.Unreadable,
                        "לא ניתן היה לקרוא את כל הנתונים מ-user_state.db (ייתכן שהקובץ נעול); מוצגים הנתונים שנקראו"));
            }
        }

        if (settings.HideSearches) searches = [];

        // ---- preferences ----
        var prefs = new OtzariaPreferences();
        if (string.IsNullOrEmpty(paths.PreferencesHive))
        {
            status.Add(new SourceStatus("app_preferences.hive", DataHealth.NotFound, "לא נמצא קובץ app_preferences.hive"));
        }
        else
        {
            try
            {
                prefs = _preferences.Read(paths.PreferencesHive) ?? new OtzariaPreferences();
                status.Add(new SourceStatus("app_preferences.hive", DataHealth.Ok, null));
            }
            catch
            {
                prefs = new OtzariaPreferences();
                status.Add(new SourceStatus("app_preferences.hive", DataHealth.Unreadable, "לא ניתן לקרוא את הגדרות אוצריא (app_preferences.hive)"));
            }
        }

        // ---- library + progress ----
        ILibraryCatalog? catalog = null;
        if (string.IsNullOrEmpty(paths.LibraryDb))
        {
            status.Add(new SourceStatus("seforim.db", DataHealth.NotFound, "לא נמצא קובץ seforim.db — לא ניתן לחשב התקדמות בשמור וזכור"));
        }
        else
        {
            try
            {
                catalog = GetCatalog(paths.LibraryDb);
                status.Add(new SourceStatus("seforim.db", DataHealth.Ok, null));
            }
            catch
            {
                status.Add(new SourceStatus("seforim.db", DataHealth.Unreadable, "לא ניתן לפתוח את ספריית אוצריא (seforim.db)"));
            }
        }

        IReadOnlyList<BookProgress> progress = [];
        try
        {
            progress = _progress.Compute(prefs, catalog) ?? [];
            progress = progress.Select(FillProgressLink).ToList();
            status.Add(new SourceStatus("progress", DataHealth.Ok, null));
        }
        catch
        {
            status.Add(new SourceStatus("progress", DataHealth.Unreadable, "לא ניתן לחשב את נתוני שמור וזכור"));
        }

        // ---- otzaria.exe ----
        if (string.IsNullOrEmpty(paths.OtzariaExe))
            status.Add(new SourceStatus("otzaria.exe", DataHealth.NotFound, "לא נמצא otzaria.exe — פתיחת קישורים תעבוד רק אם הפרוטוקול otzaria:// רשום"));
        else
            status.Add(new SourceStatus("otzaria.exe", DataHealth.Ok, null));

        var watching = ArmWatchers(paths);
        incomplete = !watching || string.IsNullOrEmpty(paths.UserStateDb) || string.IsNullOrEmpty(paths.PreferencesHive);

        return new OtzariaSnapshot
        {
            ReadAt = DateTimeOffset.Now,
            Paths = paths,
            RecentBooks = recent,
            RecentSearches = searches,
            Bookmarks = bookmarks,
            BookmarkGroups = groups,
            Workspaces = workspaces,
            OpenTabs = tabs,
            Progress = progress,
            Preferences = prefs,
            Status = status,
        };
    }

    private BookProgress FillProgressLink(BookProgress p)
    {
        if (!string.IsNullOrEmpty(p.DeepLink) || p.BookId <= 0) return p;
        return p with { DeepLink = OtzariaLauncher.BuildBookOpenLink(p.BookId) };
    }

    /// <summary>One catalog per library file version (path + size + mtime), so a replaced library is re-read.</summary>
    private ILibraryCatalog GetCatalog(string dbPath)
    {
        string key = dbPath;
        try
        {
            var fi = new FileInfo(dbPath);
            if (fi.Exists) key = $"{dbPath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        }
        catch { }
        if (_catalog is not null && key == _catalogKey) return _catalog;
        _catalog = _catalogFactory(dbPath);
        _catalogKey = key;
        return _catalog;
    }
}
