using System.Globalization;
using System.Text.Json;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.Preferences;
using OtzariaWidget.Core.Progress;
using Xunit.Abstractions;

namespace OtzariaWidget.Core.Tests.Progress;

public class ProgressServiceTests(ITestOutputHelper output)
{
    private sealed class FakeCatalog : ILibraryCatalog
    {
        public Dictionary<int, (string Title, string? Cat, int? N)> Books { get; } = new();
        public int? GetLearnableItemCount(int bookId) => Books.TryGetValue(bookId, out var b) ? b.N : null;
        public (string Title, string? CategoryPath)? GetBookInfo(int bookId)
            => Books.TryGetValue(bookId, out var b) ? (b.Title, b.Cat) : null;
        public int? FindBookIdByTitle(string title) => null;
    }

    private static FakeCatalog Catalog(params (int Id, string Title, string? Cat, int? N)[] books)
    {
        var c = new FakeCatalog();
        foreach (var b in books) c.Books[b.Id] = (b.Title, b.Cat, b.N);
        return c;
    }

    // ---- JSON builders ----

    private static string Json(object o) => JsonSerializer.Serialize(o);

    /// <summary>sz:progress_by_id JSON for one or more books: bookId -> (itemIndex, columnId) marks.</summary>
    private static string Progress(params (int Book, (int Idx, string Col)[] Marks)[] books)
    {
        var root = new Dictionary<string, Dictionary<string, Dictionary<string, bool>>>();
        foreach (var (book, marks) in books)
        {
            var items = new Dictionary<string, Dictionary<string, bool>>();
            foreach (var (idx, col) in marks)
            {
                if (!items.TryGetValue(idx.ToString(CultureInfo.InvariantCulture), out var d))
                    items[idx.ToString(CultureInfo.InvariantCulture)] = d = new();
                d[col] = true;
            }
            root[book.ToString(CultureInfo.InvariantCulture)] = items;
        }
        return Json(root);
    }

    private static (int Idx, string Col)[] Range(string col, int from, int toExclusive)
        => Enumerable.Range(from, toExclusive - from).Select(i => (i, col)).ToArray();

    private static (int Idx, string Col)[] Concat(params (int, string)[][] parts) => parts.SelectMany(p => p).ToArray();

    private static OtzariaPreferences Prefs(string? tracked = null, string? progress = null, string? columns = null,
        string? completion = null, string? columnCompletion = null)
    {
        var d = new Dictionary<string, string>();
        if (tracked is not null) d["sz:tracked_books"] = tracked;
        if (progress is not null) d["sz:progress_by_id"] = progress;
        if (columns is not null) d["sz:book_columns"] = columns;
        if (completion is not null) d["sz:completion_dates_by_id"] = completion;
        if (columnCompletion is not null) d["sz:column_completion_dates"] = columnCompletion;
        return new OtzariaPreferences { ShamorZachorRaw = d };
    }

    private static readonly string[] DefaultIds = ["learn", "review1", "review2", "review3"];

    private static BookProgress Single(OtzariaPreferences prefs, ILibraryCatalog? catalog)
        => Assert.Single(new ProgressService().Compute(prefs, catalog));

    // ---- tests ----

    [Fact]
    public void No_sz_data_gives_empty_list()
    {
        Assert.Empty(new ProgressService().Compute(new OtzariaPreferences(), Catalog()));
        Assert.Empty(new ProgressService().Compute(new OtzariaPreferences(), null));
    }

    [Fact]
    public void Default_columns_half_done_first_column()
    {
        var cat = Catalog((1, "בראשית", "תנ\"ך", 10));
        var b = Single(Prefs(tracked: "[1]", progress: Progress((1, Range("learn", 0, 5)))), cat);

        Assert.Equal(1, b.BookId);
        Assert.Equal("בראשית", b.Title);
        Assert.Equal("תנ\"ך", b.CategoryPath);
        Assert.Equal(10, b.TotalItems);
        Assert.Equal(DefaultIds, b.Columns.Select(c => c.Id));
        Assert.Equal(["לימוד", "חזרה 1", "חזרה 2", "חזרה 3"], b.Columns.Select(c => c.Label));
        Assert.Equal([5, 0, 0, 0], b.Columns.Select(c => c.Done));
        Assert.Equal(0.5, b.Columns[0].Ratio);
        Assert.Equal(0.5, b.Headline);
        Assert.Single(b.VisibleColumns);
        Assert.Equal("learn", b.VisibleColumns[0].Id);
        Assert.True(b.IsInProgress);
        Assert.True(b.IsTracked);
        Assert.False(b.IsCompleted);
        Assert.Null(b.CompletedAt);
        Assert.Equal("חצי הדרך מאחוריך!", b.StatusText);
        Assert.Equal("otzaria://open/book/1", b.DeepLink);
    }

    [Fact]
    public void Stale_and_invalid_indexes_are_ignored()
    {
        var cat = Catalog((1, "x", null, 10));
        var marks = Concat(Range("learn", 8, 12), [(99, "learn"), (-1, "learn")]);
        var b = Single(Prefs(progress: Progress((1, marks))), cat);
        Assert.Equal(2, b.Columns[0].Done);       // only 8 and 9
        Assert.Equal(0.2, b.Columns[0].Ratio, 10);
        Assert.False(b.IsCompleted);
        Assert.True(b.IsInProgress);              // has progress entries
    }

    [Fact]
    public void Completed_flag_counts_every_entry_like_otzarias_isBookCompletedById_but_rings_use_valid_items_only()
    {
        // Otzaria: completed chip <=> every column has >= N entries (getColumnCompletedCount counts stale indexes too),
        // while the rings iterate items 0..N-1 only. The widget mirrors both.
        var cat = Catalog((1, "x", null, 3));
        var marks = Concat(Range("learn", 0, 3), Range("review1", 0, 3), Range("review2", 0, 3), Range("review3", 10, 13));
        var b = Single(Prefs(progress: Progress((1, marks))), cat);
        Assert.True(b.IsCompleted);
        Assert.Equal(0, b.Columns[3].Done);
        Assert.Equal(0.0, b.Columns[3].Ratio);
        Assert.False(b.IsInProgress);

        // two entries only => not complete
        var few = Concat(Range("learn", 0, 3), Range("review1", 0, 3), Range("review2", 0, 3), Range("review3", 10, 12));
        Assert.False(Single(Prefs(progress: Progress((1, few))), cat).IsCompleted);
    }

    [Fact]
    public void Item_keys_must_be_the_canonical_decimal_string_like_dart_lookup()
    {
        // Dart looks items up with absoluteIndex.toString(): "01", " 1", "+1", "1.0" never match, "1" does.
        var cat = Catalog((1, "x", null, 5));
        var json = "{\"1\":{\"1\":{\"learn\":true},\"01\":{\"learn\":true},\" 2\":{\"learn\":true},\"+3\":{\"learn\":true},\"4.0\":{\"learn\":true},\"x\":{\"learn\":true},\"0\":{\"learn\":true}}}";
        var b = Single(Prefs(progress: json), cat);
        Assert.Equal(2, b.Columns[0].Done); // "0" and "1"
    }

    [Theory]
    [InlineData("0", 1, true)]
    [InlineData("0", 0, false)]
    [InlineData("9", 10, true)]
    [InlineData("10", 10, false)]
    [InlineData("-1", 10, false)]
    [InlineData("007", 10, false)]
    [InlineData("", 10, false)]
    [InlineData("99999999999", 2147483647, false)]
    [InlineData("2147483646", 2147483647, true)]
    public void IsValidIndexKey_cases(string key, int n, bool expected)
        => Assert.Equal(expected, ProgressService.IsValidIndexKey(key, n));

    [Fact]
    public void Current_cycle_text_matches_the_book_card_line_under_the_rings()
    {
        var cat = Catalog((1, "x", null, 10));
        // learn complete, review1 3/10 => card shows rings [learn done, review1 30%] and "3 מתוך 10"
        var marks = Concat(Range("learn", 0, 10), Range("review1", 0, 3));
        var b = Single(Prefs(progress: Progress((1, marks))), cat);
        Assert.Equal("3 מתוך 10", b.CurrentCycleText);

        // only learn, 5/10
        var b2 = Single(Prefs(progress: Progress((1, Range("learn", 0, 5)))), cat);
        Assert.Equal("5 מתוך 10", b2.CurrentCycleText);

        // learn complete, review1 not started: review1 is visible (previous complete) and current => "0 מתוך 10"
        var b3 = Single(Prefs(progress: Progress((1, Range("learn", 0, 10)))), cat);
        Assert.Equal("0 מתוך 10", b3.CurrentCycleText);

        // everything complete => no current cycle; unknown total => null
        var all = DefaultIds.SelectMany(c => Range(c, 0, 10)).ToArray();
        Assert.Null(Single(Prefs(progress: Progress((1, all))), cat).CurrentCycleText);
        Assert.Null(Single(Prefs(tracked: "[1]"), null).CurrentCycleText);
    }

    [Fact]
    public void Item_key_collection_from_duplicate_json_keys_last_wins()
    {
        var cat = Catalog((1, "x", null, 5));
        var b = Single(Prefs(progress: "{\"1\":{\"0\":{\"learn\":true},\"0\":{\"review1\":true}}}"), cat);
        Assert.Equal(0, b.Columns[0].Done);
        Assert.Equal(1, b.Columns[1].Done);
    }

    [Fact]
    public void Visible_ring_rule_matches_dart_for_every_pattern_of_four_columns()
    {
        // exhaustive: ratios in {0, 0.5, 1} for 4 columns; reference = literal port of book_card_widget.dart:304-312
        var cat = Catalog((1, "x", null, 2));
        double[] levels = [0, 0.5, 1];
        int checkedCases = 0;
        foreach (var a in levels) foreach (var b in levels) foreach (var c in levels) foreach (var d in levels)
        {
            var r = new[] { a, b, c, d };
            var marks = new List<(int, string)>();
            for (int i = 0; i < 4; i++)
                for (int item = 0; item < (int)(r[i] * 2); item++) marks.Add((item, DefaultIds[i]));
            var bp = Single(Prefs(tracked: "[1]", progress: marks.Count == 0 ? null : Progress((1, marks.ToArray()))), cat);

            var expectedVisible = new List<int>();
            for (int i = 0; i < 4; i++)
                if (i == 0 || r[i] > 0.0 || r[i - 1] >= 1.0) expectedVisible.Add(i);
            Assert.Equal(expectedVisible.Select(i => DefaultIds[i]), bp.VisibleColumns.Select(x => x.Id));

            int? current = null;
            foreach (var i in expectedVisible) if (r[i] < 1.0) current = i;
            var expectedText = current is null ? null : $"{(int)Math.Round(r[current.Value] * 2, MidpointRounding.AwayFromZero)} מתוך 2";
            Assert.Equal(expectedText, bp.CurrentCycleText);

            bool completed = r.All(x => x >= 1.0);
            Assert.Equal(completed, bp.IsCompleted);
            checkedCases++;
        }
        Assert.Equal(81, checkedCases);
    }

    [Fact]
    public void First_cycle_complete_shows_second_ring_and_cycle_text()
    {
        var cat = Catalog((1, "x", null, 4));
        var b = Single(Prefs(progress: Progress((1, Range("learn", 0, 4)))), cat);
        Assert.Equal(1.0, b.Headline);
        Assert.Equal(["learn", "review1"], b.VisibleColumns.Select(c => c.Id));
        Assert.False(b.IsCompleted);
        Assert.True(b.IsInProgress);
        Assert.Equal("סיימת מחזור ראשון בהצלחה!", b.StatusText);
    }

    [Fact]
    public void Visible_columns_rule_includes_started_columns_even_after_a_gap()
    {
        var cat = Catalog((1, "x", null, 10));
        var marks = Concat(Range("learn", 0, 5), Range("review2", 0, 1));
        var b = Single(Prefs(progress: Progress((1, marks))), cat);
        Assert.Equal(["learn", "review2"], b.VisibleColumns.Select(c => c.Id));
    }

    [Fact]
    public void Second_column_visible_when_first_complete_and_third_when_second_partial()
    {
        var cat = Catalog((1, "x", null, 10));
        var marks = Concat(Range("learn", 0, 10), Range("review1", 0, 3));
        var b = Single(Prefs(progress: Progress((1, marks))), cat);
        Assert.Equal(["learn", "review1"], b.VisibleColumns.Select(c => c.Id));
        Assert.Equal("שליש הדרך כבר הושלם!", b.StatusText); // current cycle = review1 at 30%
    }

    [Fact]
    public void Fully_complete_book_with_completion_dates()
    {
        var cat = Catalog((1, "x", null, 3));
        var marks = DefaultIds.SelectMany(c => Range(c, 0, 3)).ToArray();
        var prefs = Prefs(tracked: "[1]", progress: Progress((1, marks)),
            completion: "{\"1\":\"2026-11-09T11:03:09.635953\"}",
            columnCompletion: "{\"1\":{\"learn\":\"2026-10-01T08:00:00\",\"review3\":\"2026-11-09T11:03:09.635953\"}}");
        var b = Single(prefs, cat);
        Assert.True(b.IsCompleted);
        Assert.False(b.IsInProgress);
        Assert.Equal(4, b.VisibleColumns.Count);
        Assert.Equal("סיימת מחזור רביעי בהצלחה!", b.StatusText);
        Assert.Equal(1.0, b.Headline);

        Assert.NotNull(b.CompletedAt);
        Assert.Equal(new DateTime(2026, 11, 9, 11, 3, 9).AddTicks(6359530), b.CompletedAt!.Value.DateTime);
        Assert.Equal(new DateTime(2026, 10, 1, 8, 0, 0), b.Columns[0].CompletedAt!.Value.DateTime);
        Assert.Null(b.Columns[1].CompletedAt);
        Assert.NotNull(b.Columns[3].CompletedAt);
    }

    [Fact]
    public void Dates_parse_independently_of_current_culture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("he-IL");
            var cat = Catalog((1, "x", null, 1));
            var b = Single(Prefs(tracked: "[1]", completion: "{\"1\":\"2026-11-09T11:03:09.635953\"}"), cat);
            Assert.Equal(2026, b.CompletedAt!.Value.Year);
            Assert.Equal(11, b.CompletedAt.Value.Month);
            Assert.Equal(9, b.CompletedAt.Value.Day);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Bad_dates_are_ignored()
    {
        var cat = Catalog((1, "x", null, 1));
        var b = Single(Prefs(tracked: "[1]", completion: "{\"1\":\"not a date\"}", columnCompletion: "{\"1\":{\"learn\":5}}"), cat);
        Assert.Null(b.CompletedAt);
        Assert.Null(b.Columns[0].CompletedAt);
    }

    [Fact]
    public void Custom_columns_override_defaults_and_complete_with_cycle_two()
    {
        var cat = Catalog((5, "ספר", null, 2));
        var cols = "{\"5\":[{\"id\":\"learn\",\"label\":\"קריאה\"},{\"id\":\"col_123\",\"label\":\"חזרה א\"}]}";
        var marks = Concat(Range("learn", 0, 2), Range("col_123", 0, 2), Range("review1", 0, 2));
        var b = Single(Prefs(progress: Progress((5, marks)), columns: cols), cat);
        Assert.Equal(["learn", "col_123"], b.Columns.Select(c => c.Id));
        Assert.Equal(["קריאה", "חזרה א"], b.Columns.Select(c => c.Label));
        Assert.True(b.IsCompleted);
        Assert.Equal("סיימת מחזור שני בהצלחה!", b.StatusText);
    }

    [Fact]
    public void Single_custom_column_complete_is_first_cycle()
    {
        var cat = Catalog((5, "ספר", null, 2));
        var b = Single(Prefs(progress: Progress((5, Range("only", 0, 2))), columns: "{\"5\":[{\"id\":\"only\",\"label\":\"L\"}]}"), cat);
        Assert.True(b.IsCompleted);
        Assert.Equal("סיימת מחזור ראשון בהצלחה!", b.StatusText);
    }

    [Fact]
    public void Custom_column_missing_label_gets_fallback_and_bad_entries_are_skipped()
    {
        var cat = Catalog((5, "ספר", null, 2));
        var cols = "{\"5\":[{\"id\":\"a\"},5,{\"label\":\"x\"},{\"id\":\"a\",\"label\":\"dup\"},{\"id\":\"b\",\"label\":\"B\"}]}";
        var b = Single(Prefs(tracked: "[5]", columns: cols), cat);
        Assert.Equal(["a", "b"], b.Columns.Select(c => c.Id));
        Assert.Equal("מחזור 1", b.Columns[0].Label);
        Assert.Equal("B", b.Columns[1].Label);
    }

    [Fact]
    public void Empty_custom_columns_array_falls_back_to_defaults()
    {
        var cat = Catalog((5, "ספר", null, 2));
        var b = Single(Prefs(tracked: "[5]", columns: "{\"5\":[]}"), cat);
        Assert.Equal(DefaultIds, b.Columns.Select(c => c.Id));
    }

    [Fact]
    public void Unknown_ids_are_dropped_when_catalog_does_not_know_them()
    {
        var cat = Catalog((1, "known", null, 5));
        var res = new ProgressService().Compute(
            Prefs(tracked: "[1, 6834]", progress: Progress((7, Range("learn", 0, 1)))), cat);
        Assert.Equal([1], res.Select(b => b.BookId));
    }

    [Fact]
    public void Null_catalog_still_returns_entries_with_unknown_denominator()
    {
        var res = new ProgressService().Compute(
            Prefs(tracked: "[6834]", progress: Progress((7, Range("learn", 0, 3)))), null);
        Assert.Equal(2, res.Count);
        Assert.All(res, b =>
        {
            Assert.Equal(0, b.TotalItems);
            Assert.Equal($"ספר {b.BookId}", b.Title);
            Assert.Null(b.CategoryPath);
            Assert.False(b.IsCompleted);
            Assert.Equal(0.0, b.Headline);
            Assert.All(b.Columns, c => Assert.Equal(0, c.Done));
            Assert.Equal("לימוד פעיל", b.StatusText);
            Assert.Equal($"otzaria://open/book/{b.BookId}", b.DeepLink);
        });
    }

    [Fact]
    public void Catalog_without_item_count_gives_zero_total()
    {
        var cat = Catalog((1, "x", null, null));
        var b = Single(Prefs(tracked: "[1]", progress: Progress((1, Range("learn", 0, 3)))), cat);
        Assert.Equal(0, b.TotalItems);
        Assert.False(b.IsCompleted);
        Assert.Equal("לימוד פעיל", b.StatusText);
    }

    [Fact]
    public void Tracked_only_book_is_in_progress_with_not_started_text()
    {
        var cat = Catalog((1, "x", null, 10));
        var b = Single(Prefs(tracked: "[1]"), cat);
        Assert.True(b.IsInProgress);
        Assert.True(b.IsTracked);
        Assert.Equal(0.0, b.Headline);
        Assert.Equal("עדיין לא התחלת!", b.StatusText);
    }

    [Fact]
    public void Progress_only_book_is_in_progress_but_not_tracked()
    {
        var cat = Catalog((2, "y", null, 10));
        var b = Single(Prefs(progress: Progress((2, Range("learn", 0, 1)))), cat);
        Assert.True(b.IsInProgress);
        Assert.False(b.IsTracked);
    }

    [Fact]
    public void Progress_entry_with_only_false_values_is_not_progress()
    {
        var cat = Catalog((2, "y", null, 10));
        var b = Single(Prefs(progress: "{\"2\":{\"0\":{\"learn\":false}}}"), cat);
        Assert.False(b.IsInProgress);
        Assert.Equal(0, b.Columns[0].Done);
    }

    [Theory]
    [InlineData(0, "עדיין לא התחלת!")]
    [InlineData(1, "התחלה מצוינת!")]
    [InlineData(29, "התחלה מצוינת!")]
    [InlineData(30, "שליש הדרך כבר הושלם!")]
    [InlineData(49, "שליש הדרך כבר הושלם!")]
    [InlineData(50, "חצי הדרך מאחוריך!")]
    [InlineData(59, "חצי הדרך מאחוריך!")]
    [InlineData(60, "רוב הדרך כבר מאחוריך!")]
    [InlineData(74, "רוב הדרך כבר מאחוריך!")]
    [InlineData(75, "הסוף כבר באופק!")]
    [InlineData(99, "הסוף כבר באופק!")]
    public void Status_text_thresholds(int done, string expected)
    {
        var cat = Catalog((1, "x", null, 100));
        var b = Single(Prefs(tracked: "[1]", progress: done == 0 ? null : Progress((1, Range("learn", 0, done)))), cat);
        Assert.Equal(expected, b.StatusText);
    }

    [Theory]
    [InlineData(1, "סיימת מחזור ראשון בהצלחה!")]
    [InlineData(2, "סיימת מחזור שני בהצלחה!")]
    [InlineData(3, "סיימת מחזור שלישי בהצלחה!")]
    [InlineData(4, "סיימת מחזור רביעי בהצלחה!")]
    [InlineData(5, "הושלם בהצלחה!")]
    public void Cycle_completion_texts_by_column_count(int columnCount, string expected)
    {
        var colList = Enumerable.Range(1, columnCount).Select(i => new { id = $"c{i}", label = $"L{i}" }).ToArray();
        var marks = Enumerable.Range(1, columnCount).SelectMany(i => Range($"c{i}", 0, 2)).ToArray();
        var b = Single(Prefs(progress: Progress((1, marks)), columns: Json(new Dictionary<string, object> { ["1"] = colList })),
            Catalog((1, "x", null, 2)));
        Assert.True(b.IsCompleted);
        Assert.Equal(expected, b.StatusText);
    }

    [Fact]
    public void Malformed_json_never_throws()
    {
        var cat = Catalog((1, "x", null, 5));
        var garbage = new[] { "", "   ", "not json", "[", "{", "null", "42", "\"s\"", "[[]]", "{\"1\":5}", "{\"x\":{}}", "[\"a\",null,1.5]" };
        foreach (var g in garbage)
        {
            var res = new ProgressService().Compute(Prefs(g, g, g, g, g), cat);
            Assert.NotNull(res);
        }
    }

    [Fact]
    public void Broken_part_does_not_prevent_other_parts()
    {
        var cat = Catalog((1, "x", null, 4));
        var prefs = Prefs(tracked: "[1]", progress: "{\"1\":{\"0\":{\"learn\":true},\"abc\":{\"learn\":true},\"1\":5,\"2\":{\"learn\":true}}, \"bad\":{}}",
            columns: "garbage", completion: "[");
        var b = Single(prefs, cat);
        Assert.Equal(2, b.Columns[0].Done);
        Assert.Equal(DefaultIds, b.Columns.Select(c => c.Id));
    }

    [Fact]
    public void Tracked_accepts_numeric_strings_and_ignores_junk_and_duplicates()
    {
        var cat = Catalog((1, "a", null, 1), (2, "b", null, 1));
        var res = new ProgressService().Compute(Prefs(tracked: "[1,\"2\",\"x\",null,1,1.5,true]"), cat);
        Assert.Equal([1, 2], res.Select(b => b.BookId).OrderBy(i => i));
    }

    [Fact]
    public void Duplicate_equivalent_item_keys_are_not_double_counted()
    {
        var cat = Catalog((1, "x", null, 5));
        var b = Single(Prefs(progress: "{\"1\":{\"1\":{\"learn\":true},\"01\":{\"learn\":true}}}"), cat);
        Assert.Equal(1, b.Columns[0].Done);
    }

    [Fact]
    public void Ordering_in_progress_with_progress_first_then_tracked_only_then_completed()
    {
        var cat = Catalog((1, "done", null, 1), (2, "tracked-only", null, 5), (3, "with-progress", null, 5), (4, "tracked-later", null, 5));
        var doneMarks = DefaultIds.SelectMany(c => Range(c, 0, 1)).ToArray();
        var prefs = Prefs(
            tracked: "[1,2,4]",
            progress: Progress((1, doneMarks), (3, Range("learn", 0, 1))),
            completion: "{\"1\":\"2026-01-01T00:00:00\"}");
        var res = new ProgressService().Compute(prefs, cat);
        Assert.Equal([3, 4, 2, 1], res.Select(b => b.BookId));
    }

    [Fact]
    public void Completed_books_are_sorted_most_recent_first()
    {
        var cat = Catalog((1, "a", null, 1), (2, "b", null, 1));
        var marks = DefaultIds.SelectMany(c => Range(c, 0, 1)).ToArray();
        var prefs = Prefs(progress: Progress((1, marks), (2, marks)),
            completion: "{\"1\":\"2026-01-01T00:00:00\",\"2\":\"2026-06-01T00:00:00\"}");
        var res = new ProgressService().Compute(prefs, cat);
        Assert.Equal([2, 1], res.Select(b => b.BookId));
    }

    [Fact]
    public void A_throwing_catalog_entry_is_skipped_not_fatal()
    {
        var res = new ProgressService().Compute(Prefs(tracked: "[1,2]"), new ThrowingFor1());
        Assert.Equal([2], res.Select(b => b.BookId));
    }

    private sealed class ThrowingFor1 : ILibraryCatalog
    {
        public int? GetLearnableItemCount(int bookId) => 3;
        public (string Title, string? CategoryPath)? GetBookInfo(int bookId)
            => bookId == 1 ? throw new InvalidOperationException() : ("t", null);
        public int? FindBookIdByTitle(string title) => null;
    }

    [Fact]
    public void Real_data_smoke_test_counts_only()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appData)) return;
        var path = Path.Combine(appData, "otzaria", "app_preferences.hive");
        if (!File.Exists(path)) return;

        var prefs = new PreferencesReader().Read(path);
        var res = new ProgressService().Compute(prefs, null);
        output.WriteLine($"szKeys={prefs.ShamorZachorRaw.Count} books={res.Count} " +
                         $"inProgress={res.Count(b => b.IsInProgress)} completed={res.Count(b => b.IsCompleted)}");
        Assert.All(res, b =>
        {
            Assert.StartsWith("otzaria://open/book/", b.DeepLink);
            Assert.InRange(b.Headline, 0, 1);
        });
    }
}
