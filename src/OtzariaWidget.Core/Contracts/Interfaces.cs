// ============================================================================
//  SHARED CONTRACTS — owned by the project lead. Do NOT rename/remove members.
// ============================================================================
namespace OtzariaWidget.Core.Contracts;

/// <summary>Resolves Otzaria's file locations (storage-and-links.md §1). Never writes anything.</summary>
public interface IOtzariaPathResolver
{
    OtzariaPaths Resolve(WidgetAppSettings settings);
}

/// <summary>Reads user_state.db read-only (Mode=ReadOnly, WAL-aware, never immutable=1).</summary>
public interface IUserStateReader
{
    /// <summary>History reading entries, newest first, de-duplicated per book identity.</summary>
    IReadOnlyList<ReadingEntry> ReadRecentBooks(string userStateDbPath, int max);
    IReadOnlyList<RecentSearch> ReadRecentSearches(string userStateDbPath, int max);
    /// <summary>Bookmarks newest-first.</summary>
    IReadOnlyList<ReadingEntry> ReadBookmarks(string userStateDbPath);
    IReadOnlyList<BookmarkGroup> ReadBookmarkGroups(string userStateDbPath);
    IReadOnlyList<WorkspaceInfo> ReadWorkspaces(string userStateDbPath);
    /// <summary>Tabs of the main window (slot 1).</summary>
    IReadOnlyList<OpenTabInfo> ReadOpenTabs(string userStateDbPath);
}

/// <summary>Read-only parser for Hive box files (hive-and-progress.md §1).</summary>
public interface IHiveReader
{
    /// <summary>Returns last-frame-wins key/value map. Values: null, long, double, bool, string, byte[], List&lt;object?&gt;, Dictionary&lt;object,object?&gt;, DateTime.
    /// Only keys accepted by <paramref name="keyFilter"/> are materialized (privacy whitelist).</summary>
    IReadOnlyDictionary<string, object?> Read(string hiveFilePath, Func<string, bool> keyFilter);
}

/// <summary>Loads the whitelisted Otzaria preferences from app_preferences.hive.</summary>
public interface IPreferencesReader
{
    OtzariaPreferences Read(string hiveFilePath);
}

/// <summary>Read-only access to seforim.db (4+ GB, never copy).</summary>
public interface ILibraryCatalog
{
    /// <summary>Number of learnable items for Shamor Vezachor (TOC leaves, else totalLines [x2 for Talmud]).</summary>
    int? GetLearnableItemCount(int bookId);
    (string Title, string? CategoryPath)? GetBookInfo(int bookId);
    int? FindBookIdByTitle(string title);
}

public interface IProgressService
{
    IReadOnlyList<BookProgress> Compute(OtzariaPreferences prefs, ILibraryCatalog? catalog);
}

/// <summary>Builds otzaria:// links (storage-and-links.md §5) and launches Otzaria.</summary>
public interface IOtzariaLauncher
{
    string? BuildLink(ReadingEntry entry);
    string BuildSearchLink(string query, string? mode);
    /// <summary>Launch Otzaria with a URI (or with no args when uri is null). Returns false if Otzaria couldn't be found.</summary>
    bool Launch(string? uri, OtzariaPaths paths);
}

/// <summary>The aggregate the widget provider consumes. Thread-safe. Caches and debounces.</summary>
public interface IOtzariaDataService : IDisposable
{
    OtzariaSnapshot GetSnapshot();
    /// <summary>Force re-read on next GetSnapshot.</summary>
    void Invalidate();
    /// <summary>Raised (debounced ~1.5 s) when Otzaria's files or the widget settings change.</summary>
    event EventHandler? Changed;
}

/// <summary>Calendar + zmanim engine (OtzariaWidget.Calendar implements it).</summary>
public interface ICalendarService
{
    IReadOnlyList<CityInfo> Cities { get; }
    CityInfo? FindCity(string name);
    /// <summary>All zmanim Otzaria knows, in Otzaria's display order, with Hebrew titles.</summary>
    IReadOnlyList<ZmanDefinition> ZmanimRegistry { get; }
    /// <summary>Otzaria's default enabled zmanim ids.</summary>
    IReadOnlyList<string> DefaultEnabledZmanim { get; }
    DayInfo GetDayInfo(DateOnly date, CityInfo city);
    /// <summary>Zmanim for the date, in registry order, only those in ids. Null Time = does not occur that day.</summary>
    IReadOnlyList<ZmanTime> GetZmanim(DateOnly date, CityInfo city, IReadOnlyList<string> ids);
    /// <summary>The "current" Hebrew/Jewish day for an instant, honoring Otzaria's day-transition setting (sunset|tzais|rabbeinuTam|midnight).</summary>
    DateOnly GetEffectiveDate(DateTimeOffset now, CityInfo city, string dayTransition);
}
