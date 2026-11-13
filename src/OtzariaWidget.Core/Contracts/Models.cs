// ============================================================================
//  SHARED CONTRACTS — owned by the project lead. Do NOT rename/remove members.
//  You MAY add optional members (with defaults) if you need them; note it in
//  your final report so the lead can propagate.
//  Spec sources: docs/research/*.md, docs/ARCHITECTURE.md
// ============================================================================
namespace OtzariaWidget.Core.Contracts;

/// <summary>Book reference as stored in Otzaria's Bookmark/history JSON (storage-and-links.md §2.3.1).</summary>
public sealed record BookRef
{
    public int? Id { get; init; }
    public required string Title { get; init; }
    /// <summary>TextBook | PdfBook | DocxBook | EpubBook | DocumentBook | ExternalLibraryBook</summary>
    public required string Type { get; init; }
    /// <summary>Wire source key: "o" official, "u" personal, "d:&lt;slug&gt;" attached DB.</summary>
    public string Source { get; init; } = "o";
    public string? Author { get; init; }
    public string? CategoryPath { get; init; }
    public string? FileType { get; init; }
    public string? Path { get; init; }
    public string? ExternalLibraryId { get; init; }
    public string? VersionTitle { get; init; }
    public bool IsPdf => Type == "PdfBook";
}

/// <summary>A reading location (history entry or bookmark), storage-and-links.md §2.3.</summary>
public sealed record ReadingEntry
{
    public required BookRef Book { get; init; }
    /// <summary>Display-ready Hebrew location text ("ref"); may be empty.</summary>
    public string Ref { get; init; } = "";
    /// <summary>Text: 0-based line index. PDF: 1-based page.</summary>
    public int Index { get; init; }
    /// <summary>"book" | "commentators"</summary>
    public string TargetKind { get; init; } = "book";
    public IReadOnlyList<string> Commentators { get; init; } = [];
    /// <summary>Bookmarks: user label (null = show Ref).</summary>
    public string? Label { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? WorkspaceName { get; init; }
    /// <summary>Deep link that opens this entry in Otzaria, or null if not linkable.</summary>
    public string? DeepLink { get; init; }
}

public sealed record RecentSearch
{
    public required string Query { get; init; }
    /// <summary>advanced | exact | fuzzy | smart | null</summary>
    public string? Mode { get; init; }
    public string? DisplayText { get; init; }
    public string? DeepLink { get; init; }
}

public sealed record BookmarkGroup
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public IReadOnlyList<ReadingEntry> Items { get; init; } = [];
    public DateTimeOffset? CreatedAt { get; init; }
}

public sealed record WorkspaceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int TabCount { get; init; }
    public IReadOnlyList<string> TabTitles { get; init; } = [];
    public bool IsPinned { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>An open tab in a saved window session (window_sessions).</summary>
public sealed record OpenTabInfo
{
    public required string Title { get; init; }
    /// <summary>TextBookTab | PdfBookTab | SearchingTabWindow | CombinedTab | CommentatorsTab | ToolTab | ...</summary>
    public required string TabType { get; init; }
    public ReadingEntry? Reading { get; init; }
    public bool IsCurrent { get; init; }
    public int TabIndex { get; init; }
    public string? DeepLink { get; init; }
}

// ----------------------------------------------------------------------------
//  Shamor Vezachor (hive-and-progress.md §2)
// ----------------------------------------------------------------------------
public sealed record ProgressColumn(string Id, string Label, int Done, double Ratio, DateTimeOffset? CompletedAt);

public sealed record BookProgress
{
    public required int BookId { get; init; }
    public required string Title { get; init; }
    public string? CategoryPath { get; init; }
    /// <summary>Number of learnable items (TOC leaves or lines), the denominator.</summary>
    public int TotalItems { get; init; }
    public IReadOnlyList<ProgressColumn> Columns { get; init; } = [];
    /// <summary>Columns Otzaria would show (rule: i==0 || ring[i]&gt;0 || ring[i-1]&gt;=1).</summary>
    public IReadOnlyList<ProgressColumn> VisibleColumns { get; init; } = [];
    /// <summary>First column ratio clamped [0,1].</summary>
    public double Headline { get; init; }
    public bool IsCompleted { get; init; }
    public bool IsInProgress { get; init; }
    public bool IsTracked { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    /// <summary>Hebrew status text (e.g. "חצי הדרך מאחוריך!").</summary>
    public string StatusText { get; init; } = "";
    /// <summary>What Otzaria's book card prints under the rings for the current (last visible, unfinished) cycle:
    /// "{done} מתוך {total}". Null when total is unknown or every visible cycle is complete. Added by QA (optional member).</summary>
    public string? CurrentCycleText { get; init; }
    public string? DeepLink { get; init; }
}

// ----------------------------------------------------------------------------
//  Otzaria preferences (whitelisted subset of app_preferences.hive)
// ----------------------------------------------------------------------------
public sealed record OtzariaPreferences
{
    public string SelectedCity { get; init; } = "ירושלים";
    /// <summary>JSON-decoded key-calendar-enabled-zmanim; empty => use defaults.</summary>
    public IReadOnlyList<string> EnabledZmanim { get; init; } = [];
    /// <summary>hebrew | gregorian | combined</summary>
    public string CalendarType { get; init; } = "combined";
    /// <summary>sunset | tzais | rabbeinuTam | midnight</summary>
    public string DayTransition { get; init; } = "sunset";
    public bool TwelveHourClock { get; init; }
    public bool DarkMode { get; init; }
    public uint? LightSeedColorArgb { get; init; }
    public uint? DarkSeedColorArgb { get; init; }
    public string? LibraryPath { get; init; }
    public string? LibraryFolderName { get; init; }
    public string? DatabasesPath { get; init; }
    /// <summary>Raw sz:* values (JSON strings) — consumed by the progress service.</summary>
    public IReadOnlyDictionary<string, string> ShamorZachorRaw { get; init; } = new Dictionary<string, string>();
}

// ----------------------------------------------------------------------------
//  Resolved locations
// ----------------------------------------------------------------------------
public sealed record OtzariaPaths
{
    public string? DataRoot { get; init; }
    public string? UserStateDb { get; init; }
    public string? PersonalNotesDb { get; init; }
    public string? PreferencesHive { get; init; }
    public string? LibraryDb { get; init; }
    public string? OtzariaExe { get; init; }
    public bool IsPortable { get; init; }
    /// <summary>Human-readable notes on how each path was found (for the diagnostics page).</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

public enum DataHealth { Ok, NotFound, Unreadable, UnknownSchema }

public sealed record SourceStatus(string Name, DataHealth Health, string? Message);

/// <summary>Everything the widgets need from Otzaria's files, read in one go.</summary>
public sealed record OtzariaSnapshot
{
    public required DateTimeOffset ReadAt { get; init; }
    public required OtzariaPaths Paths { get; init; }
    public IReadOnlyList<ReadingEntry> RecentBooks { get; init; } = [];
    public IReadOnlyList<RecentSearch> RecentSearches { get; init; } = [];
    public IReadOnlyList<ReadingEntry> Bookmarks { get; init; } = [];
    public IReadOnlyList<BookmarkGroup> BookmarkGroups { get; init; } = [];
    public IReadOnlyList<WorkspaceInfo> Workspaces { get; init; } = [];
    public IReadOnlyList<OpenTabInfo> OpenTabs { get; init; } = [];
    public IReadOnlyList<BookProgress> Progress { get; init; } = [];
    public OtzariaPreferences Preferences { get; init; } = new();
    public IReadOnlyList<SourceStatus> Status { get; init; } = [];
}

// ----------------------------------------------------------------------------
//  Calendar / Zmanim (docs/research/calendar-and-zmanim.md)
// ----------------------------------------------------------------------------
public sealed record CityInfo
{
    public required string Name { get; init; }      // Hebrew, key used by Otzaria
    public required string Country { get; init; }   // Hebrew country group ("ארץ ישראל" ...)
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double Elevation { get; init; }
    public required string TimeZoneId { get; init; } // IANA
    public bool IsInIsrael { get; init; }
    public int CandleLightingMinutes { get; init; } = 30;
}

public sealed record ZmanDefinition(string Id, string Title, string? Description);

public sealed record ZmanTime(string Id, string Title, DateTimeOffset? Time)
{
    /// <summary>Otzaria's display text for the value (24h "HH:mm." with seconds marker, wrapped in LTR isolates; kidush levana: the full
    /// "ליל ... לחודש HH:mm." label). Added by Agent C (optional member).</summary>
    public string? DisplayText { get; init; }
}

public sealed record DayInfo
{
    public required DateOnly Date { get; init; }
    /// <summary>e.g. "כ"ב בחשון תשפ"ז"</summary>
    public required string HebrewDate { get; init; }
    public string HebrewDayOfWeek { get; init; } = "";  // "יום שישי"
    public string GregorianDate { get; init; } = "";    // "13/11/2026"
    public string? Parasha { get; init; }               // weekly parasha (of the coming Shabbat)
    public IReadOnlyList<string> Holidays { get; init; } = []; // yom tov / fast / chanukah / rosh chodesh etc.
    public string? DafYomiBavli { get; init; }          // "ברכות ב"
    public string? DafYomiYerushalmi { get; init; }
    public string? Omer { get; init; }                  // "היום שלושה ימים לעומר" or null
    public DateTimeOffset? CandleLighting { get; init; }
    public DateTimeOffset? Havdalah { get; init; }
    public bool IsShabbat { get; init; }
    public bool IsYomTov { get; init; }
}
