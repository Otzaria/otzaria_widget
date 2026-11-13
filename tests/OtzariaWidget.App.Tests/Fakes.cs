using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.App.Tests;

/// <summary>Deterministic calendar with realistic Hebrew strings (Israel, UTC+2, Friday 13 Nov 2026).</summary>
public sealed class FakeCalendarService : ICalendarService
{
    public static readonly CityInfo Jerusalem = new()
    {
        Name = "ירושלים", Country = "ארץ ישראל", Latitude = 31.7683, Longitude = 35.2137, Elevation = 754,
        TimeZoneId = "Asia/Jerusalem", IsInIsrael = true, CandleLightingMinutes = 40,
    };
    public static readonly CityInfo BneiBrak = Jerusalem with { Name = "בני ברק", Elevation = 20, CandleLightingMinutes = 22 };
    public static readonly CityInfo NewYork = new()
    {
        Name = "ניו יורק", Country = "ארצות הברית", Latitude = 40.71, Longitude = -74.0, Elevation = 10,
        TimeZoneId = "America/New_York", IsInIsrael = false, CandleLightingMinutes = 18,
    };

    /// <summary>Set to make the service throw (simulates the Calendar project being a stub).</summary>
    public bool Throw { get; set; }
    /// <summary>Set to return no zmanim at all.</summary>
    public bool NoZmanim { get; set; }

    /// <summary>Extra zmanim returned for any id list that contains their id (e.g. a null-time zman, kidush levana on another date).</summary>
    public List<ZmanTime> Extra { get; } = [];

    public IReadOnlyList<CityInfo> Cities { get; } = [Jerusalem, BneiBrak, NewYork];
    public CityInfo? FindCity(string name) { Check(); return Cities.FirstOrDefault(c => c.Name == name); }

    public IReadOnlyList<ZmanDefinition> ZmanimRegistry { get { Check(); return Registry; } }
    public IReadOnlyList<string> DefaultEnabledZmanim { get { Check(); return Registry.Take(9).Select(z => z.Id).ToList(); } }

    private static readonly (string Id, string Title, int Minutes)[] Table =
    [
        ("alos", "עלות השחר", 4 * 60 + 52),
        ("misheyakir", "משיכיר", 5 * 60 + 33),
        ("sunrise", "הנץ החמה", 6 * 60 + 17),
        ("sofZmanShma", "סוף זמן קריאת שמע (מג״א)", 8 * 60 + 47),
        ("sofZmanShmaGra", "סוף זמן קריאת שמע (גר״א)", 9 * 60 + 31),
        ("sofZmanTfila", "סוף זמן תפילה", 10 * 60 + 25),
        ("chatzos", "חצות היום", 11 * 60 + 37),
        ("minchaGedola", "מנחה גדולה", 12 * 60 + 8),
        ("minchaKetana", "מנחה קטנה", 14 * 60 + 52),
        ("plag", "פלג המנחה", 15 * 60 + 53),
        ("sunset", "שקיעת החמה", 16 * 60 + 37),
        ("tzeis", "צאת הכוכבים", 17 * 60 + 15),
        ("tzeisRt", "צאת הכוכבים (ר״ת)", 17 * 60 + 49),
    ];

    private static readonly IReadOnlyList<ZmanDefinition> Registry =
        Table.Select(t => new ZmanDefinition(t.Id, t.Title, null)).ToList();

    private void Check() { if (Throw) throw new NotImplementedException(); }

    public IReadOnlyList<ZmanTime> GetZmanim(DateOnly date, CityInfo city, IReadOnlyList<string> ids)
    {
        Check();
        if (NoZmanim) return [];
        var off = TimeSpan.FromHours(city.TimeZoneId == "America/New_York" ? -5 : 2);
        return Table.Where(t => ids.Contains(t.Id))
            .Select(t => new ZmanTime(t.Id, t.Title,
                new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue).AddMinutes(t.Minutes), off)))
            .Concat(Extra.Where(e => ids.Contains(e.Id)))
            .ToList();
    }

    public DayInfo GetDayInfo(DateOnly date, CityInfo city)
    {
        Check();
        int dayNum = date.DayNumber - new DateOnly(2026, 11, 13).DayNumber;
        var dow = (int)date.DayOfWeek;
        string[] days = ["יום ראשון", "יום שני", "יום שלישי", "יום רביעי", "יום חמישי", "יום שישי", "שבת קודש"];
        return new DayInfo
        {
            Date = date,
            HebrewDate = $"{3 + dayNum}׳ בכסלו תשפ״ז",
            HebrewDayOfWeek = days[dow],
            GregorianDate = "13/11/2026",
            Parasha = "ויצא",
            Holidays = dayNum == 0 ? ["ערב שבת"] : [],
            DafYomiBavli = "בבא קמא ד",
            DafYomiYerushalmi = "שבת ה",
            Omer = null,
            CandleLighting = dow == 5 ? new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 57)), TimeSpan.FromHours(2)) : null,
            Havdalah = dow == 6 ? new DateTimeOffset(date.ToDateTime(new TimeOnly(17, 33)), TimeSpan.FromHours(2)) : null,
            IsShabbat = dow == 6,
        };
    }

    /// <summary>After sunset (16:37 Israel time) the Jewish day is the next Gregorian date.</summary>
    public DateOnly GetEffectiveDate(DateTimeOffset now, CityInfo city, string dayTransition)
    {
        Check();
        var local = now.ToOffset(TimeSpan.FromHours(2));
        var d = DateOnly.FromDateTime(local.DateTime);
        return local.TimeOfDay >= TimeSpan.FromMinutes(16 * 60 + 37) ? d.AddDays(1) : d;
    }
}

public sealed class FakeDataService : IOtzariaDataService
{
    public OtzariaSnapshot Snapshot { get; set; }
    public event EventHandler? Changed;
    public int Invalidations { get; private set; }

    public FakeDataService(OtzariaSnapshot snapshot) { Snapshot = snapshot; }
    public OtzariaSnapshot GetSnapshot() => Snapshot;
    public void Invalidate() => Invalidations++;
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() { }
}

public sealed class FakeSettingsStore : ISettingsStore
{
    public WidgetAppSettings Current { get; set; } = new();
    public string SettingsFilePath => "fake";
    public event EventHandler? Changed;
    public WidgetAppSettings Load() => Current;
    public void Save(WidgetAppSettings settings) { Current = settings; Changed?.Invoke(this, EventArgs.Empty); }
}

/// <summary>Realistic Otzaria data (Hebrew titles, refs, deep links) for the three data situations.</summary>
public static class TestData
{
    /// <summary>Friday 13 Nov 2026, 10:30 Israel time.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 30, 0, TimeSpan.FromHours(2));

    private static BookRef Book(int id, string title, string type = "TextBook") =>
        new() { Id = id, Title = title, Type = type };

    private static ReadingEntry Entry(int id, string title, string reference, int index, string? label = null,
        string type = "TextBook", int minutesAgo = 5)
    {
        var url = type == "PdfBook" ? $"otzaria://open/pdf/{id}?index={index}" : $"otzaria://open/book/{id}?index={index}";
        return new ReadingEntry
        {
            Book = Book(id, title, type), Ref = reference, Index = index, Label = label,
            CreatedAt = Now.AddMinutes(-minutesAgo), DeepLink = url,
        };
    }

    public static readonly OtzariaPaths GoodPaths = new()
    {
        DataRoot = @"C:\Users\test\AppData\Roaming\otzaria",
        UserStateDb = @"C:\Users\test\AppData\Roaming\otzaria\user_state.db",
        PreferencesHive = @"C:\Users\test\AppData\Roaming\otzaria\app_preferences.hive",
        LibraryDb = @"C:\Users\test\Otzaria\seforim.db",
        OtzariaExe = @"C:\Program Files\Otzaria\otzaria.exe",
    };

    public static OtzariaSnapshot Full()
    {
        var recents = new List<ReadingEntry>
        {
            Entry(131, "ברכות", "דף ב עמוד א", 3, type: "PdfBook", minutesAgo: 20),
            Entry(1938, "שולחן ערוך אורח חיים", "סימן א סעיף ב", 140, minutesAgo: 60 * 5),
            Entry(2210, "משנה ברורה", "סימן רמב ס״ק ג", 88, minutesAgo: 60 * 30),
            Entry(417, "רמב״ם הלכות שבת", "פרק ה הלכה ג", 52, minutesAgo: 60 * 24 * 3),
            Entry(901, "ספר החינוך מצווה לב", "", 12, minutesAgo: 60 * 24 * 9),
            Entry(77, "תניא ליקוטי אמרים", "פרק ג", 30, minutesAgo: 60 * 24 * 12),
            Entry(78, "מסילת ישרים", "פרק א", 4, minutesAgo: 60 * 24 * 13),
            Entry(79, "חובות הלבבות שער הבטחון", "פרק ב", 9, minutesAgo: 60 * 24 * 14),
            Entry(80, "כוזרי מאמר ראשון", "אות א", 1, minutesAgo: 60 * 24 * 20),
            Entry(81, "עין יעקב ברכות", "דף ה", 17, minutesAgo: 60 * 24 * 21),
        };
        var bookmarks = new List<ReadingEntry>
        {
            Entry(1938, "שולחן ערוך אורח חיים", "סימן קכב", 300, label: "הלכות תפילה"),
            Entry(131, "ברכות", "דף י עמוד ב", 20, type: "PdfBook"),
            Entry(417, "רמב״ם הלכות שבת", "פרק א הלכה א", 1, label: "יסודות"),
            Entry(2210, "משנה ברורה", "סימן שכ", 60),
            Entry(77, "תניא ליקוטי אמרים", "פרק לב", 80, label: "אהבת ישראל"),
        };
        var groups = new List<BookmarkGroup>
        {
            new() { Id = "g1", Name = "תפילה", Items = [bookmarks[0], bookmarks[3]] },
            new() { Id = "g2", Name = "שבת", Items = [bookmarks[2]] },
        };
        var tabs = new List<OpenTabInfo>
        {
            new() { Title = "ברכות", TabType = "PdfBookTab", IsCurrent = true, Reading = recents[0], DeepLink = recents[0].DeepLink },
        };
        return new OtzariaSnapshot
        {
            ReadAt = Now, Paths = GoodPaths,
            RecentBooks = recents,
            RecentSearches =
            [
                new RecentSearch { Query = "שמירת הלשון", Mode = "exact", DeepLink = "otzaria://open/search?q=%D7%A9%D7%9E%D7%99%D7%A8%D7%AA&mode=exact" },
                new RecentSearch { Query = "הלכות ברכות", Mode = null },
                new RecentSearch { Query = "אהבת חינם", Mode = "fuzzy" },
            ],
            Bookmarks = bookmarks, BookmarkGroups = groups, OpenTabs = tabs,
            Progress =
            [
                Progress(131, "ברכות", 0.45, tracked: true),
                Progress(1938, "שולחן ערוך אורח חיים", 0.12),
                Progress(417, "רמב״ם הלכות שבת", 0.80),
                Progress(77, "תניא", 1.0, completed: true),
                Progress(78, "מסילת ישרים", 0.0, tracked: true),
                Progress(79, "חובות הלבבות", 0.33),
            ],
            Preferences = new OtzariaPreferences { SelectedCity = "ירושלים", CalendarType = "combined" },
            Status =
            [
                new SourceStatus(SourceNames.UserState, DataHealth.Ok, null),
                new SourceStatus(SourceNames.Preferences, DataHealth.Ok, null),
                new SourceStatus(SourceNames.Library, DataHealth.Ok, null),
            ],
        };
    }

    public static BookProgress Progress(int id, string title, double headline, bool tracked = false, bool completed = false)
    {
        var cols = new List<ProgressColumn>
        {
            new("learn", "לימוד", (int)(headline * 100), headline, null),
            new("review1", "חזרה ראשונה", completed ? 100 : (int)(headline * 30), completed ? 1.0 : headline * 0.3, null),
            new("review2", "חזרה שנייה", completed ? 100 : 0, completed ? 1.0 : 0.0, null),
        };
        return new BookProgress
        {
            BookId = id, Title = title, TotalItems = 100, Columns = cols,
            VisibleColumns = headline >= 1 ? cols : cols.Take(headline > 0.3 ? 2 : 1).ToList(),
            Headline = headline, IsCompleted = completed, IsInProgress = !completed && headline > 0, IsTracked = tracked,
            CompletedAt = completed ? Now.AddDays(-3) : null,
            StatusText = completed ? "סיימת מחזור ראשון בהצלחה!" : headline switch
            {
                0 => "עדיין לא התחלת!", < 0.3 => "התחלה מצוינת!", < 0.5 => "שליש הדרך כבר הושלם!",
                < 0.6 => "חצי הדרך מאחוריך!", < 0.75 => "רוב הדרך כבר מאחוריך!", _ => "הסוף כבר באופק!",
            },
            DeepLink = $"otzaria://open/book/{id}",
        };
    }

    /// <summary>All files present but nothing in them.</summary>
    public static OtzariaSnapshot Empty() => new()
    {
        ReadAt = Now, Paths = GoodPaths,
        Preferences = new OtzariaPreferences(),
        Status = [new SourceStatus(SourceNames.UserState, DataHealth.Ok, null), new SourceStatus(SourceNames.Preferences, DataHealth.Ok, null)],
    };

    /// <summary>Otzaria not found / unreadable.</summary>
    public static OtzariaSnapshot Missing() => new()
    {
        ReadAt = Now, Paths = new OtzariaPaths(),
        Preferences = new OtzariaPreferences(),
        Status =
        [
            new SourceStatus(SourceNames.UserState, DataHealth.NotFound, "לא נמצא קובץ הנתונים של אוצריא"),
            new SourceStatus(SourceNames.Preferences, DataHealth.NotFound, "לא נמצאו הגדרות אוצריא"),
        ],
    };
}
