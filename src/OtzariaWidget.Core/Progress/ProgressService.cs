using System.Globalization;
using System.Text.Json;
using OtzariaWidget.Core.Contracts;

namespace OtzariaWidget.Core.Progress;

/// <summary>
/// Shamor Vezachor computation (docs/research/hive-and-progress.md section 2.4). Pure: works on the raw
/// sz:* JSON strings from <see cref="OtzariaPreferences.ShamorZachorRaw"/> plus an optional catalog for
/// titles and denominators. Malformed JSON / entries are skipped, never thrown.
/// Ordering: in-progress books first (books with real progress before merely tracked ones, most recently
/// tracked first), then other books, then completed books (most recently completed first).
/// </summary>
public sealed class ProgressService : IProgressService
{
    public const string TrackedKey = "sz:tracked_books";
    public const string ProgressKey = "sz:progress_by_id";
    public const string ColumnsKey = "sz:book_columns";
    public const string CompletionDatesKey = "sz:completion_dates_by_id";
    public const string ColumnCompletionDatesKey = "sz:column_completion_dates";

    private static readonly (string Id, string Label)[] DefaultColumns =
    [
        ("learn", "לימוד"),
        ("review1", "חזרה 1"),
        ("review2", "חזרה 2"),
        ("review3", "חזרה 3"),
    ];

    public IReadOnlyList<BookProgress> Compute(OtzariaPreferences prefs, ILibraryCatalog? catalog)
    {
        var raw = prefs?.ShamorZachorRaw;
        if (raw is null || raw.Count == 0) return [];

        var trackedList = ParseTracked(Raw(raw, TrackedKey));
        var tracked = new HashSet<int>(trackedList);
        var progress = ParseProgress(Raw(raw, ProgressKey));
        var columnsOverride = ParseColumnsOverride(Raw(raw, ColumnsKey));
        var completionDates = ParseDateMap(Raw(raw, CompletionDatesKey));
        var columnDates = ParseNestedDateMap(Raw(raw, ColumnCompletionDatesKey));

        var ids = new List<int>(trackedList);
        var seen = new HashSet<int>(trackedList);
        foreach (var id in progress.Keys.OrderBy(i => i))
            if (seen.Add(id)) ids.Add(id);

        var result = new List<(BookProgress Book, bool HasProgress, int TrackedPos)>();
        foreach (var id in ids)
        {
            try
            {
                var item = BuildBook(id, tracked, progress, columnsOverride, completionDates, columnDates, catalog);
                if (item is null) continue;
                int pos = trackedList.LastIndexOf(id);
                bool hasProgress = progress.TryGetValue(id, out var p) && p.Values.Any(cols => cols.Count > 0);
                result.Add((item, hasProgress, pos));
            }
            catch (Exception)
            {
                // a single broken book never fails the list
            }
        }

        return result
            .OrderBy(x => x.Book.IsCompleted ? 2 : x.Book.IsInProgress ? 0 : 1)
            .ThenByDescending(x => x.Book.IsInProgress && x.HasProgress)
            .ThenByDescending(x => x.TrackedPos)
            .ThenByDescending(x => x.Book.CompletedAt ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Book.BookId)
            .Select(x => x.Book)
            .ToList();
    }

    private static BookProgress? BuildBook(
        int id,
        HashSet<int> tracked,
        Dictionary<int, Dictionary<string, HashSet<string>>> progress,
        Dictionary<int, List<(string Id, string Label)>> columnsOverride,
        Dictionary<int, DateTimeOffset> completionDates,
        Dictionary<int, Dictionary<string, DateTimeOffset>> columnDates,
        ILibraryCatalog? catalog)
    {
        string title = $"ספר {id}";
        string? categoryPath = null;
        int n = 0;
        if (catalog is not null)
        {
            var info = catalog.GetBookInfo(id);
            if (info is null) return null; // unknown id (renumbered / different library): drop
            title = string.IsNullOrWhiteSpace(info.Value.Title) ? title : info.Value.Title;
            categoryPath = info.Value.CategoryPath;
            n = Math.Max(0, catalog.GetLearnableItemCount(id) ?? 0);
        }

        var cols = columnsOverride.TryGetValue(id, out var co) && co.Count > 0
            ? co
            : DefaultColumns.ToList();
        progress.TryGetValue(id, out var items);
        columnDates.TryGetValue(id, out var colDates);

        // Valid items only (key == canonical "0".."N-1"): what the book card rings iterate in Otzaria.
        var validItems = new List<HashSet<string>>();
        if (items is not null && n > 0)
        {
            foreach (var (key, set) in items)
                if (IsValidIndexKey(key, n)) validItems.Add(set);
        }

        var columns = new List<ProgressColumn>(cols.Count);
        bool allRawComplete = n > 0 && cols.Count > 0;
        foreach (var (colId, label) in cols)
        {
            int done = 0;
            foreach (var set in validItems)
                if (set.Contains(colId)) done++;
            // ProgressService.getColumnCompletedCount in Otzaria counts EVERY entry (stale indexes too);
            // isBookCompletedById (the "completed" chip / tab) is based on that.
            int rawDone = 0;
            if (items is not null)
                foreach (var set in items.Values)
                    if (set.Contains(colId)) rawDone++;
            if (rawDone < n) allRawComplete = false;

            double ratio = n == 0 ? 0 : Math.Clamp((double)done / n, 0, 1);
            DateTimeOffset? date = colDates is not null && colDates.TryGetValue(colId, out var d) ? d : null;
            columns.Add(new ProgressColumn(colId, label, done, ratio, date));
        }

        var visible = new List<ProgressColumn>();
        var visibleIdx = new List<int>();
        for (int i = 0; i < columns.Count; i++)
        {
            if (i == 0 || columns[i].Ratio > 0 || columns[i - 1].Ratio >= 1.0)
            {
                visible.Add(columns[i]);
                visibleIdx.Add(i);
            }
        }

        double headline = Math.Clamp(columns[0].Ratio, 0, 1);
        bool completed = allRawComplete;
        bool hasAnyItem = items is not null && items.Values.Any(s => s.Count > 0);
        bool inProgress = !completed && (tracked.Contains(id) || hasAnyItem);
        DateTimeOffset? completedAt = completionDates.TryGetValue(id, out var cd) ? cd : null;

        return new BookProgress
        {
            BookId = id,
            Title = title,
            CategoryPath = categoryPath,
            TotalItems = n,
            Columns = columns,
            VisibleColumns = visible,
            Headline = headline,
            IsCompleted = completed,
            IsInProgress = inProgress,
            IsTracked = tracked.Contains(id),
            CompletedAt = completedAt,
            StatusText = StatusText(n, columns, visibleIdx, completed),
            CurrentCycleText = CurrentCycleText(n, columns, visibleIdx),
            DeepLink = $"otzaria://open/book/{id}",
        };
    }

    // ------------------------------------------------------------------ status text

    private static string StatusText(int n, List<ProgressColumn> columns, List<int> visibleIdx, bool completed)
    {
        if (n <= 0) return "לימוד פעיל";
        if (completed) return CycleDone(columns.Count);

        // current cycle = last visible column that is not yet complete (as in Otzaria's book card)
        int current = -1;
        foreach (var i in visibleIdx)
            if (columns[i].Ratio < 1.0) current = i;
        if (current < 0) return CycleDone(columns.Count);

        double p = columns[current].Ratio;
        if (p <= 0)
            return current > 0 ? CycleDone(current) : "עדיין לא התחלת!";
        if (p < 0.30) return "התחלה מצוינת!";
        if (p < 0.50) return "שליש הדרך כבר הושלם!";
        if (p < 0.60) return "חצי הדרך מאחוריך!";
        if (p < 0.75) return "רוב הדרך כבר מאחוריך!";
        return "הסוף כבר באופק!";
    }

    /// <summary>The line Otzaria's book card shows under the rings: "{round(ring*N)} מתוך {N}" for the current cycle.</summary>
    private static string? CurrentCycleText(int n, List<ProgressColumn> columns, List<int> visibleIdx)
    {
        if (n <= 0) return null;
        int current = -1;
        foreach (var i in visibleIdx)
            if (columns[i].Ratio < 1.0) current = i;
        if (current < 0) return null;
        // Dart's double.round() rounds half away from zero.
        long shown = (long)Math.Round(columns[current].Ratio * n, MidpointRounding.AwayFromZero);
        return $"{shown.ToString(CultureInfo.InvariantCulture)} מתוך {n.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>True when <paramref name="key"/> is exactly the decimal string of an index in [0, n).</summary>
    internal static bool IsValidIndexKey(string key, int n)
    {
        if (key.Length == 0 || key.Length > 10) return false;
        if (key.Length > 1 && key[0] == '0') return false;
        long v = 0;
        foreach (char c in key)
        {
            if (c < '0' || c > '9') return false;
            v = v * 10 + (c - '0');
        }
        return v < n;
    }

    private static string CycleDone(int cycle) => cycle switch
    {
        1 => "סיימת מחזור ראשון בהצלחה!",
        2 => "סיימת מחזור שני בהצלחה!",
        3 => "סיימת מחזור שלישי בהצלחה!",
        4 => "סיימת מחזור רביעי בהצלחה!",
        _ => "הושלם בהצלחה!",
    };

    // ------------------------------------------------------------------ parsing

    private static string? Raw(IReadOnlyDictionary<string, string> raw, string key)
        => raw.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static JsonDocument? TryParse(string? json)
    {
        if (json is null) return null;
        try { return JsonDocument.Parse(json); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static bool TryId(JsonElement e, out int id)
    {
        id = 0;
        if (e.ValueKind == JsonValueKind.Number)
        {
            if (e.TryGetInt32(out id)) return true;
            if (e.TryGetDouble(out var d) && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
            {
                id = (int)d;
                return true;
            }
            return false;
        }
        return e.ValueKind == JsonValueKind.String &&
               int.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    private static List<int> ParseTracked(string? json)
    {
        var list = new List<int>();
        using var doc = TryParse(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        var seen = new HashSet<int>();
        foreach (var e in doc.RootElement.EnumerateArray())
            if (TryId(e, out var id) && seen.Add(id)) list.Add(id);
        return list;
    }

    private static Dictionary<int, Dictionary<string, HashSet<string>>> ParseProgress(string? json)
    {
        var result = new Dictionary<int, Dictionary<string, HashSet<string>>>();
        using var doc = TryParse(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var book in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(book.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            if (book.Value.ValueKind != JsonValueKind.Object) continue;
            // Item keys stay strings exactly as Otzaria stores them (Dart looks items up by absoluteIndex.toString()).
            var items = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var item in book.Value.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.Object) continue;
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var col in item.Value.EnumerateObject())
                    if (col.Value.ValueKind == JsonValueKind.True) set.Add(col.Name);
                items[item.Name] = set; // duplicate JSON keys: last wins, like Dart's jsonDecode
            }
            result[id] = items;
        }
        return result;
    }

    private static Dictionary<int, List<(string Id, string Label)>> ParseColumnsOverride(string? json)
    {
        var result = new Dictionary<int, List<(string, string)>>();
        using var doc = TryParse(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var book in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(book.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            if (book.Value.ValueKind != JsonValueKind.Array) continue;
            var cols = new List<(string, string)>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in book.Value.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                if (!c.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
                var colId = idEl.GetString();
                if (string.IsNullOrEmpty(colId) || !ids.Add(colId)) continue;
                string? label = c.TryGetProperty("label", out var lEl) && lEl.ValueKind == JsonValueKind.String
                    ? lEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(label)) label = $"מחזור {cols.Count + 1}";
                cols.Add((colId, label));
            }
            if (cols.Count > 0) result[id] = cols;
        }
        return result;
    }

    private static DateTimeOffset? ParseDate(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.String) return null;
        return DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d)
            ? d : null;
    }

    private static Dictionary<int, DateTimeOffset> ParseDateMap(string? json)
    {
        var result = new Dictionary<int, DateTimeOffset>();
        using var doc = TryParse(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var p in doc.RootElement.EnumerateObject())
            if (int.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && ParseDate(p.Value) is { } d)
                result[id] = d;
        return result;
    }

    private static Dictionary<int, Dictionary<string, DateTimeOffset>> ParseNestedDateMap(string? json)
    {
        var result = new Dictionary<int, Dictionary<string, DateTimeOffset>>();
        using var doc = TryParse(json);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var book in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(book.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            if (book.Value.ValueKind != JsonValueKind.Object) continue;
            var map = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            foreach (var c in book.Value.EnumerateObject())
                if (ParseDate(c.Value) is { } d) map[c.Name] = d;
            result[id] = map;
        }
        return result;
    }
}
