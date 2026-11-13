using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.UserState;
using static OtzariaWidget.Core.Tests.UserState.UserStateFixture;

namespace OtzariaWidget.Core.Tests.UserState;

public class UserStateReaderTests
{
    private readonly UserStateReader _reader = new();

    // ---- history -----------------------------------------------------------------------------------------
    [Fact]
    public void Recent_books_newest_first_skipping_searches_with_deeplinks()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", new object[]
        {
            Entry(Book(10, "ספר א"), 140, "ספר א, ח:"),
            Search("שלום", "exact"),
            Entry(Book(20, "ספר ב"), 5),
            Entry(Book(131, "מנחות", "PdfBook", path: @"C:\lib\תלמוד בבלי\מנחות.pdf", externalLibraryId: "talmud-pdf:מנחות"), 150),
        });

        var r = _reader.ReadRecentBooks(f.DbPath, 20);

        Assert.Equal(new[] { "ספר א", "ספר ב", "מנחות" }, r.Select(e => e.Book.Title));
        Assert.Equal(140, r[0].Index);
        Assert.Equal("ספר א, ח:", r[0].Ref);
        Assert.Equal("otzaria://open/book/10?index=140", r[0].DeepLink);
        Assert.Equal("otzaria://open/pdf/131?index=150", r[2].DeepLink);
        Assert.True(r[2].Book.IsPdf);
        Assert.Equal("talmud-pdf:מנחות", r[2].Book.ExternalLibraryId);
    }

    [Fact]
    public void Recent_books_dedupes_by_book_identity_keeping_newest()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", new object[]
        {
            Entry(Book(10, "ספר א"), 9, "מיקום חדש", historySlot: "x-1"),
            Entry(Book(11, "ספר ב"), 1),
            Entry(Book(10, "ספר א"), 3, "מיקום ישן"),                                   // same book, older
            Entry(Book(10, "ספר א"), 4, "מפרשים | מיקום", targetKind: "commentators"),   // same book, commentators view
            Entry(Book(10, "ספר א", "PdfBook", path: @"C:\x.pdf"), 7),                  // PDF edition = different identity
        });

        var r = _reader.ReadRecentBooks(f.DbPath, 20);

        Assert.Equal(3, r.Count);
        Assert.Equal("מיקום חדש", r[0].Ref);
        Assert.Equal(11, r[1].Book.Id);
        Assert.True(r[2].Book.IsPdf);
    }

    [Fact]
    public void Same_title_different_source_are_different_identities()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", new object[]
        {
            Entry(Book(1, "ספר", source: "o"), 0),
            Entry(Book(1, "ספר", source: "u"), 0),
            Entry(Book(1, "ספר", source: "d:my-db"), 0),
        });
        var r = _reader.ReadRecentBooks(f.DbPath, 20);
        Assert.Equal(3, r.Count);
        Assert.Equal("otzaria://open/book/1?index=0", r[0].DeepLink);
        Assert.Equal("otzaria://open/book/1?source=user&index=0", r[1].DeepLink);
        Assert.Equal("otzaria://open/book/1?source=db:my-db&index=0", r[2].DeepLink);
    }

    [Fact]
    public void Recent_books_respects_max_and_zero()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", Enumerable.Range(1, 50).Select(i => (object)Entry(Book(i, "ספר " + i), i)).ToArray());
        Assert.Equal(7, _reader.ReadRecentBooks(f.DbPath, 7).Count);
        Assert.Equal("ספר 1", _reader.ReadRecentBooks(f.DbPath, 7)[0].Book.Title);
        Assert.Empty(_reader.ReadRecentBooks(f.DbPath, 0));
    }

    [Fact]
    public void Broken_entries_are_skipped_not_fatal()
    {
        using var f = new UserStateFixture();
        f.SetListRaw("history", "history", """
            [
              "just a string",
              42,
              null,
              {"ref":"x","index":1},
              {"ref":"x","index":1,"book":"notanobject"},
              {"ref":"x","index":1,"book":{"id":5,"title":"סוג לא מוכר","type":"HologramBook"}},
              {"ref":"x","index":1,"book":{"id":5,"type":"TextBook"}},
              {"ref":"ok","index":"7","book":{"id":"6","title":"טוב","type":"TextBook"}},
              {"ref":"ext","index":0,"book":{"otzarId":"abc","bookName":"חיצוני","type":"ExternalLibraryBook","link":"https://example.org"}}
            ]
            """);
        var r = _reader.ReadRecentBooks(f.DbPath, 20);
        Assert.Equal(2, r.Count);
        Assert.Equal("טוב", r[0].Book.Title);
        Assert.Equal(7, r[0].Index);
        Assert.Equal(6, r[0].Book.Id);
        Assert.Equal("חיצוני", r[1].Book.Title);
        Assert.Null(r[1].DeepLink);                                 // external library books are not linkable
        Assert.Equal("abc", r[1].Book.ExternalLibraryId);
    }

    [Fact]
    public void Corrupt_payload_json_yields_empty()
    {
        using var f = new UserStateFixture();
        f.SetListRaw("history", "history", "{ not json [");
        Assert.Empty(_reader.ReadRecentBooks(f.DbPath, 10));
        f.SetListRaw("history", "history", "{\"not\":\"an array\"}");
        Assert.Empty(_reader.ReadRecentBooks(f.DbPath, 10));
    }

    [Fact]
    public void Legacy_isUserBook_flag_maps_to_user_source()
    {
        using var f = new UserStateFixture();
        f.SetListRaw("history", "history", """[{"ref":"r","index":3,"book":{"id":9,"title":"אישי","type":"TextBook","isUserBook":true}}]""");
        var r = _reader.ReadRecentBooks(f.DbPath, 5);
        Assert.Equal("u", r[0].Book.Source);
        Assert.Equal("otzaria://open/book/9?source=user&index=3", r[0].DeepLink);
    }

    [Fact]
    public void Pdf_page_below_one_is_clamped_and_text_index_never_negative()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", new object[]
        {
            Entry(Book(1, "pdf", "PdfBook", path: @"C:\a.pdf"), 0),
            Entry(Book(2, "txt"), -4),
        });
        var r = _reader.ReadRecentBooks(f.DbPath, 5);
        Assert.Equal("otzaria://open/pdf/1?index=1", r[0].DeepLink);
        Assert.Equal("otzaria://open/book/2?index=0", r[1].DeepLink);
    }

    // ---- searches --------------------------------------------------------------------------------------
    [Fact]
    public void Searches_query_mode_display_and_link()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", new object[]
        {
            Entry(Book(1, "ספר"), 0),
            Search("שלום עולם", "exact", "(ק)שלום עולם"),
            Search("שלום עולם", "exact"),     // duplicate
            Search("עוד", "fuzzy"),
        });
        var s = _reader.ReadRecentSearches(f.DbPath, 20);
        Assert.Equal(2, s.Count);
        Assert.Equal("שלום עולם", s[0].Query);
        Assert.Equal("exact", s[0].Mode);
        Assert.Equal("(ק)שלום עולם", s[0].DisplayText);
        Assert.Equal("otzaria://open/search?q=" + Uri.EscapeDataString("שלום עולם") + "&mode=exact", s[0].DeepLink);
        Assert.Equal("fuzzy", s[1].Mode);
        Assert.Single(_reader.ReadRecentSearches(f.DbPath, 1));
    }

    [Fact]
    public void Search_with_unknown_mode_has_no_mode_param()
    {
        using var f = new UserStateFixture();
        f.SetListRaw("history", "history", """[{"ref":"r","index":0,"isSearch":true,"book":{"id":null,"title":"x","type":"TextBook"},"searchMode":"weird"}]""");
        var s = _reader.ReadRecentSearches(f.DbPath, 5);
        Assert.Equal("otzaria://open/search?q=x", s[0].DeepLink);
    }

    // ---- bookmarks -------------------------------------------------------------------------------------
    [Fact]
    public void Bookmarks_are_returned_newest_first_with_label_and_dates()
    {
        using var f = new UserStateFixture();
        f.SetList("bookmarks", "key-bookmarks", new object[]
        {
            Entry(Book(1, "ראשון"), 1, "ref1"),
            Entry(Book(2, "שני"), 2, "ref2", label: "הכותרת שלי", createdAt: "2026-03-01T10:20:30.123456", workspaceName: "שולחן עבודה 1"),
            Entry(Book(3, "שלישי"), 3, "ref3", createdAt: "2026-04-01T10:00:00Z"),
        });
        var b = _reader.ReadBookmarks(f.DbPath);
        Assert.Equal(new[] { "שלישי", "שני", "ראשון" }, b.Select(x => x.Book.Title));
        Assert.Equal("הכותרת שלי", b[1].Label);
        Assert.Equal("שולחן עבודה 1", b[1].WorkspaceName);
        Assert.NotNull(b[1].CreatedAt);
        Assert.Equal(2026, b[1].CreatedAt!.Value.Year);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero), b[0].CreatedAt);
        Assert.Null(b[2].CreatedAt);
        Assert.Equal("otzaria://open/book/3?index=3", b[0].DeepLink);
    }

    [Fact]
    public void Bookmarks_missing_row_is_empty()
    {
        using var f = new UserStateFixture();
        Assert.Empty(_reader.ReadBookmarks(f.DbPath));
        Assert.Empty(_reader.ReadBookmarkGroups(f.DbPath));
        Assert.Empty(_reader.ReadWorkspaces(f.DbPath));
        Assert.Empty(_reader.ReadOpenTabs(f.DbPath));
        Assert.Empty(_reader.ReadRecentBooks(f.DbPath, 5));
        Assert.Empty(_reader.ReadRecentSearches(f.DbPath, 5));
    }

    [Fact]
    public void Bookmark_groups()
    {
        using var f = new UserStateFixture();
        f.SetList("bookmarks", "key-bookmark-groups", new object[]
        {
            new { id = "1-1", name = "סוגיה א", createdAt = "2026-01-01T00:00:00", items = new object[] { Entry(Book(1, "א"), 1), Entry(Book(2, "ב"), 2), "garbage" } },
            new { id = "2-1", name = "סוגיה ב", items = Array.Empty<object>() },
            new { name = "בלי מזהה" },
        });
        var g = _reader.ReadBookmarkGroups(f.DbPath);
        Assert.Equal(2, g.Count);
        Assert.Equal("סוגיה ב", g[0].Name);                  // newest first
        Assert.Equal("סוגיה א", g[1].Name);
        Assert.Equal(new[] { "א", "ב" }, g[1].Items.Select(i => i.Book.Title));
        Assert.NotNull(g[1].CreatedAt);
        Assert.Equal("otzaria://open/book/1?index=1", g[1].Items[0].DeepLink);
    }

    // ---- workspaces / tabs -----------------------------------------------------------------------------
    [Fact]
    public void Workspaces_with_active_flag_and_tab_titles()
    {
        using var f = new UserStateFixture();
        f.SetList("workspaces", "key-workspaces", new object[]
        {
            new { id = "w1", name = "שולחן עבודה 1", tabs = Array.Empty<object>(), currentTab = 0 },
            new
            {
                id = "w2", name = "לימוד", currentTab = 1, isPinned = true,
                tabs = new object[]
                {
                    new { type = "TextBookTab", title = "ספר א", book = Book(1, "ספר א"), initalIndex = 3 },
                    new { type = "PdfBookTab", book = Book(2, "ספר ב", "PdfBook", path: @"C:\b.pdf"), pageNumber = 4 },
                    new { type = "ToolTab", toolId = "calendar", title = "לוח שנה" },
                },
            },
        });
        f.SetSession(1, Array.Empty<object>(), 0, "w2");
        var w = _reader.ReadWorkspaces(f.DbPath);
        Assert.Equal(2, w.Count);
        Assert.False(w[0].IsActive);
        Assert.Equal(0, w[0].TabCount);
        Assert.True(w[1].IsActive);
        Assert.True(w[1].IsPinned);
        Assert.Equal(3, w[1].TabCount);
        Assert.Equal(new[] { "ספר א", "ספר ב", "לוח שנה" }, w[1].TabTitles);
    }

    [Fact]
    public void Workspaces_without_session_have_no_active()
    {
        using var f = new UserStateFixture();
        f.SetList("workspaces", "key-workspaces", new object[] { new { id = "w1", name = "x", tabs = Array.Empty<object>() } });
        Assert.All(_reader.ReadWorkspaces(f.DbPath), w => Assert.False(w.IsActive));
    }

    [Fact]
    public void Open_tabs_all_types_and_current_index()
    {
        using var f = new UserStateFixture();
        var text = new { type = "TextBookTab", title = "טקסט", book = Book(10, "טקסט"), initalIndex = 12, commentators = new[] { "רש\"י" } };
        var pdf = new { type = "PdfBookTab", path = @"C:\p.pdf", book = Book(131, "מנחות", "PdfBook", path: @"C:\p.pdf"), pageNumber = 68 };
        var search = new { type = "SearchingTabWindow", title = "חיפוש", searchText = "אור זרוע", searchMode = 1 };
        var tool = new { type = "ToolTab", toolId = "gematria", title = "גימטריה" };
        var combined = new { type = "CombinedTab", rightTab = new { type = "TextBookTab", title = "ימין", book = Book(30, "ימין"), initalIndex = 1 }, leftTab = new { type = "TextBookTab", title = "שמאל", book = Book(31, "שמאל"), initalIndex = 2 }, splitRatio = 0.5 };
        var commentators = new { type = "CommentatorsTab", title = "מפרשים", bookTitle = "טקסט", sourceTab = text, selectedCommentators = new[] { "ראב\"ד", "רמב\"ן" } };
        var broken = new { type = "TextBookTab", title = "שבור" };   // no book
        f.SetSession(1, new object[] { text, pdf, search, tool, combined, commentators, broken, "junk" }, 4);

        var tabs = _reader.ReadOpenTabs(f.DbPath);

        Assert.Equal(7, tabs.Count);           // "junk" skipped, broken tab kept with no reading
        Assert.Equal(new[] { "TextBookTab", "PdfBookTab", "SearchingTabWindow", "ToolTab", "CombinedTab", "CommentatorsTab", "TextBookTab" }, tabs.Select(t => t.TabType));
        Assert.Equal(4, tabs.Single(t => t.IsCurrent).TabIndex);

        Assert.Equal("otzaria://open/book/10?index=12", tabs[0].DeepLink);
        Assert.Equal(12, tabs[0].Reading!.Index);
        Assert.Equal("רש\"י", Assert.Single(tabs[0].Reading!.Commentators));

        Assert.Equal("otzaria://open/pdf/131?index=68", tabs[1].DeepLink);
        Assert.Equal(68, tabs[1].Reading!.Index);

        Assert.Null(tabs[2].Reading);
        Assert.Equal("otzaria://open/search?q=" + Uri.EscapeDataString("אור זרוע"), tabs[2].DeepLink);

        Assert.Null(tabs[3].Reading);
        Assert.Null(tabs[3].DeepLink);
        Assert.Equal("גימטריה", tabs[3].Title);

        Assert.Equal(30, tabs[4].Reading!.Book.Id);                    // rightTab = first pane
        Assert.Equal("ימין", tabs[4].Title);

        Assert.Equal("commentators", tabs[5].Reading!.TargetKind);
        Assert.Equal(2, tabs[5].Reading!.Commentators.Count);
        Assert.Equal(10, tabs[5].Reading!.Book.Id);

        Assert.Null(tabs[6].Reading);
    }

    [Fact]
    public void Open_tabs_only_slot_one()
    {
        using var f = new UserStateFixture();
        f.SetSession(2, new object[] { new { type = "ToolTab", title = "אחר" } }, 0);
        Assert.Empty(_reader.ReadOpenTabs(f.DbPath));
        f.SetSession(1, new object[] { new { type = "ToolTab", title = "ראשי" } }, 0);
        Assert.Single(_reader.ReadOpenTabs(f.DbPath));
    }

    [Fact]
    public void Open_tabs_corrupt_json_is_empty()
    {
        using var f = new UserStateFixture();
        f.SetSessionRaw(1, "not json", 0);
        Assert.Empty(_reader.ReadOpenTabs(f.DbPath));
    }

    // ---- schema / health -------------------------------------------------------------------------------
    [Fact]
    public void Probe_ok_for_valid_db()
    {
        using var f = new UserStateFixture();
        var s = _reader.Probe(f.DbPath);
        Assert.Equal(DataHealth.Ok, s.Health);
    }

    [Fact]
    public void Probe_not_found()
    {
        var s = _reader.Probe(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".db"));
        Assert.Equal(DataHealth.NotFound, s.Health);
        Assert.Contains("user_state.db", s.Message);
    }

    [Fact]
    public void Future_user_version_is_unknown_schema_and_yields_empty()
    {
        using var f = new UserStateFixture(userVersion: 5);
        f.SetList("history", "history", new object[] { Entry(Book(1, "x"), 1) });
        var s = _reader.Probe(f.DbPath);
        Assert.Equal(DataHealth.UnknownSchema, s.Health);
        Assert.False(string.IsNullOrWhiteSpace(s.Message));
        Assert.Empty(_reader.ReadRecentBooks(f.DbPath, 5));
        Assert.Empty(_reader.ReadOpenTabs(f.DbPath));
    }

    [Fact]
    public void Db_without_expected_tables_is_unknown_schema()
    {
        using var f = new UserStateFixture(createSchema: false);
        Assert.Equal(DataHealth.UnknownSchema, _reader.Probe(f.DbPath).Health);
        Assert.Empty(_reader.ReadBookmarks(f.DbPath));
    }

    [Fact]
    public void Garbage_file_is_unreadable_not_crash()
    {
        var dir = Path.Combine(Path.GetTempPath(), "otz-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = Path.Combine(dir, "user_state.db");
            File.WriteAllText(p, "this is not a sqlite database, just text padding padding padding padding");
            Assert.Equal(DataHealth.Unreadable, _reader.Probe(p).Health);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Reading_does_not_modify_the_database_file()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", new object[] { Entry(Book(1, "x"), 1) });
        var before = (new FileInfo(f.DbPath).Length, File.GetLastWriteTimeUtc(f.DbPath));
        _reader.ReadRecentBooks(f.DbPath, 5);
        _reader.ReadOpenTabs(f.DbPath);
        var after = (new FileInfo(f.DbPath).Length, File.GetLastWriteTimeUtc(f.DbPath));
        Assert.Equal(before, after);
    }

    [Fact]
    public void Wal_database_with_live_writer_is_readable()
    {
        using var f = new UserStateFixture(wal: true);
        using var writer = f.Open();                 // keeps the WAL open, like Otzaria
        using (var cmd = writer.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;";
            cmd.ExecuteNonQuery();
        }
        f.SetList("history", "history", new object[] { Entry(Book(77, "בוואל"), 5) });
        var r = _reader.ReadRecentBooks(f.DbPath, 5);
        Assert.Equal("בוואל", Assert.Single(r).Book.Title);
    }

    [Fact]
    public void Reader_does_not_hold_the_file_open()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", new object[] { Entry(Book(1, "x"), 1) });
        _reader.ReadRecentBooks(f.DbPath, 5);
        File.Delete(f.DbPath);                        // would throw if a handle were left open
        Assert.False(File.Exists(f.DbPath));
    }
}
