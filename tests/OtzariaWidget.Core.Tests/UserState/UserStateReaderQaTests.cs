using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OtzariaWidget.Core.Contracts;
using OtzariaWidget.Core.UserState;
using static OtzariaWidget.Core.Tests.UserState.UserStateFixture;

namespace OtzariaWidget.Core.Tests.UserState;

/// <summary>QA regression tests for UserStateReader (fidelity with Otzaria's Dart parsing, robustness, read-only safety).</summary>
public class UserStateReaderQaTests
{
    private readonly UserStateReader _reader = new();

    // ---- BookSource parsing (lib/models/book_source.dart) ----------------------------------------------
    [Theory]
    [InlineData("x", false, "o")]            // unknown wire key => official (legacy flag absent)
    [InlineData("x", true, "u")]             // unknown wire key => legacy isUserBook flag
    [InlineData("d:bad slug", false, "o")]   // invalid slug => not attached
    [InlineData("d:", false, "o")]
    [InlineData("d:ok.slug-1", false, "d:ok.slug-1")]
    [InlineData("u", false, "u")]
    [InlineData("o", true, "o")]             // a valid wire key wins over the legacy flag
    public void Source_is_normalised_like_BookSource_fromJson(string source, bool legacyUser, string expected)
    {
        using var f = new UserStateFixture();
        var book = Book(5, "ספר");
        book["source"] = source;
        book["isUserBook"] = legacyUser;
        f.SetList("history", "history", new object[] { Entry(book, 3) });
        var e = Assert.Single(_reader.ReadRecentBooks(f.DbPath, 5));
        Assert.Equal(expected, e.Book.Source);
    }

    [Fact]
    public void Invalid_source_still_gets_a_link_for_the_fallback_source()
    {
        using var f = new UserStateFixture();
        var book = Book(5, "ספר");
        book["source"] = "d:../evil";
        f.SetList("history", "history", new object[] { Entry(book, 3) });
        Assert.Equal("otzaria://open/book/5?index=3", Assert.Single(_reader.ReadRecentBooks(f.DbPath, 5)).DeepLink);
    }

    // ---- bookIdentity (bookmark.dart) ------------------------------------------------------------------
    [Fact]
    public void Identity_ignores_empty_path_and_empty_external_id_like_dart()
    {
        // two different id-less PDFs with empty `path`/`externalLibraryId` but different titles must not collapse into "file:"/"external:"
        using var f = new UserStateFixture();
        var a = Book(null, "א", "PdfBook"); a["path"] = ""; a["externalLibraryId"] = "";
        var b = Book(null, "ב", "PdfBook"); b["path"] = ""; b["externalLibraryId"] = "";
        f.SetList("history", "history", new object[] { Entry(a, 1), Entry(b, 1) });
        Assert.Equal(2, _reader.ReadRecentBooks(f.DbPath, 5).Count);
    }

    [Fact]
    public void Path_counts_for_identity_only_for_file_books()
    {
        // TextBook never serialises `path`; a stray path must not make two different titles "the same file"
        using var f = new UserStateFixture();
        var a = Book(null, "א"); a["path"] = @"C:\same.txt";
        var b = Book(null, "ב"); b["path"] = @"C:\same.txt";
        f.SetList("history", "history", new object[] { Entry(a, 1), Entry(b, 1) });
        Assert.Equal(2, _reader.ReadRecentBooks(f.DbPath, 5).Count);
    }

    [Fact]
    public void Same_pdf_path_is_one_identity()
    {
        using var f = new UserStateFixture();
        var a = Book(null, "א", "PdfBook", path: @"C:\same.pdf");
        var b = Book(null, "א (שם אחר)", "PdfBook", path: @"C:\same.pdf");
        f.SetList("history", "history", new object[] { Entry(a, 1), Entry(b, 7) });
        Assert.Single(_reader.ReadRecentBooks(f.DbPath, 5));
    }

    [Fact]
    public void Version_title_only_matters_for_text_books()
    {
        using var f = new UserStateFixture();
        var a = Book(7, "ספר", versionTitle: "מהדורה א");
        var b = Book(7, "ספר", versionTitle: "מהדורה ב");
        var p1 = Book(8, "פדף", "PdfBook", path: "p", versionTitle: "x");
        var p2 = Book(8, "פדף", "PdfBook", path: "p", versionTitle: "y");
        f.SetList("history", "history", new object[] { Entry(a, 1), Entry(b, 1), Entry(p1, 1), Entry(p2, 2) });
        Assert.Equal(3, _reader.ReadRecentBooks(f.DbPath, 10).Count); // two editions + one pdf
    }

    [Fact]
    public void External_library_book_uses_otzarId_for_identity_but_is_never_linkable_or_given_an_id()
    {
        using var f = new UserStateFixture();
        var ext = new Dictionary<string, object?> { ["type"] = "ExternalLibraryBook", ["bookName"] = "ספר חיצוני", ["otzarId"] = 99, ["link"] = "https://example.org/x", ["source"] = "o" };
        var ext2 = new Dictionary<string, object?> { ["type"] = "ExternalLibraryBook", ["title"] = "ספר חיצוני שני", ["otzarId"] = 99, ["source"] = "o" };
        var ext3 = new Dictionary<string, object?> { ["type"] = "ExternalLibraryBook", ["title"] = "אחר", ["otzarId"] = 100, ["source"] = "o" };
        f.SetList("history", "history", new object[] { Entry(ext, 0), Entry(ext2, 0), Entry(ext3, 0) });
        var r = _reader.ReadRecentBooks(f.DbPath, 10);
        Assert.Equal(2, r.Count);                       // 99 deduped
        Assert.All(r, e => { Assert.Null(e.Book.Id); Assert.Null(e.DeepLink); Assert.Equal("ExternalLibraryBook", e.Book.Type); });
        Assert.Equal("99", r[0].Book.ExternalLibraryId);
    }

    [Fact]
    public void Unknown_book_type_is_skipped_but_unknown_type_inside_a_search_is_not()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", new object[]
        {
            Entry(Book(1, "x", "AlienBook"), 1),
            Entry(Book(2, "ok"), 1),
        });
        Assert.Equal("ok", Assert.Single(_reader.ReadRecentBooks(f.DbPath, 10)).Book.Title);
    }

    [Fact]
    public void Index_accepts_numeric_string_and_whole_double_and_rejects_garbage()
    {
        using var f = new UserStateFixture();
        var items = new List<Dictionary<string, object?>>();
        foreach (var (i, idx) in new (int, object?)[] { (1, "12"), (2, 3.0), (3, "abc"), (4, 4294967296L), (5, -7), (6, null) })
        {
            var e = Entry(Book(i, "ספר " + i), 0);
            e["index"] = idx;
            items.Add(e);
        }
        f.SetList("history", "history", items);
        var r = _reader.ReadRecentBooks(f.DbPath, 10).ToDictionary(x => x.Book.Id!.Value);
        Assert.Equal(12, r[1].Index);
        Assert.Equal(3, r[2].Index);
        Assert.Equal(0, r[3].Index);
        Assert.Equal(0, r[4].Index);                     // out of int range => 0, never an overflow
        Assert.Equal("otzaria://open/book/5?index=0", r[5].DeepLink); // negative clamped in the link
        Assert.Equal("otzaria://open/book/6?index=0", r[6].DeepLink);
    }

    [Fact]
    public void Reading_anchor_line_is_used_when_index_is_missing()
    {
        using var f = new UserStateFixture();
        var e = Entry(Book(1, "ספר"), 0);
        e.Remove("index");
        e["readingAnchor"] = new { line = 33, intraLineFraction = 0.5, alignment = 0.05 };
        f.SetList("history", "history", new object[] { e });
        Assert.Equal("otzaria://open/book/1?index=33", Assert.Single(_reader.ReadRecentBooks(f.DbPath, 5)).DeepLink);
    }

    // ---- searches --------------------------------------------------------------------------------------
    [Fact]
    public void Searches_are_deduped_per_query_and_mode_newest_first_and_never_become_books()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", new object[]
        {
            Search("שלום", "exact"), Search("שלום", "exact", "(ק)other ref"), Search("שלום", "fuzzy"), Search("  ", "exact"),
            Entry(Book(1, "ספר"), 1),
        });
        var s = _reader.ReadRecentSearches(f.DbPath, 10);
        Assert.Equal(new[] { "exact", "fuzzy" }, s.Select(x => x.Mode));
        Assert.Single(_reader.ReadRecentBooks(f.DbPath, 10));
    }

    [Fact]
    public void Search_query_with_special_characters_is_percent_encoded_in_the_link()
    {
        using var f = new UserStateFixture();
        f.SetList("history", "history", new object[] { Search("\"אור\" & זרוע?=#+ %41\n", "advanced") });
        var s = Assert.Single(_reader.ReadRecentSearches(f.DbPath, 5));
        Assert.DoesNotContain(" ", s.DeepLink);
        Assert.DoesNotContain("\"", s.DeepLink);
        Assert.DoesNotContain("\n", s.DeepLink);
        var q = s.DeepLink!["otzaria://open/search?q=".Length..].Split('&')[0];
        Assert.Equal("\"אור\" & זרוע?=#+ %41\n", Uri.UnescapeDataString(q));
    }

    // ---- tabs ------------------------------------------------------------------------------------------
    [Fact]
    public void Text_tab_prefers_the_valid_reading_anchor_over_the_stored_index()
    {
        // bookmark_from_tab.dart: index = anchor?.line ?? tab.index
        using var f = new UserStateFixture();
        var withAnchor = new { type = "TextBookTab", title = "א", book = Book(1, "א"), initalIndex = 10, readingAnchor = new { line = 42, intraLineFraction = 0.1, alignment = 0.05 } };
        var badAnchor = new { type = "TextBookTab", title = "ב", book = Book(2, "ב"), initalIndex = 11, readingAnchor = new { line = -5 } };
        f.SetSession(1, new object[] { withAnchor, badAnchor }, 0);
        var t = _reader.ReadOpenTabs(f.DbPath);
        Assert.Equal("otzaria://open/book/1?index=42", t[0].DeepLink);
        Assert.Equal("otzaria://open/book/2?index=11", t[1].DeepLink);
    }

    [Fact]
    public void Commentators_tab_uses_its_own_position_not_the_source_book_tab()
    {
        using var f = new UserStateFixture();
        var src = new { type = "TextBookTab", title = "טקסט", book = Book(10, "טקסט"), initalIndex = 12 };
        var tab = new { type = "CommentatorsTab", title = "מפרשים", initialIndex = 77, bookTitle = "טקסט", sourceTab = src, selectedCommentators = new[] { "רש\"י" } };
        var noPos = new { type = "CommentatorsTab", title = "מפרשים", bookTitle = "טקסט", sourceTab = src };
        f.SetSession(1, new object[] { tab, noPos }, 0);
        var t = _reader.ReadOpenTabs(f.DbPath);
        Assert.Equal(77, t[0].Reading!.Index);
        Assert.Equal("otzaria://open/book/10?index=77", t[0].DeepLink);
        Assert.Equal("commentators", t[0].Reading!.TargetKind);
        Assert.Equal(12, t[1].Reading!.Index);
    }

    [Fact]
    public void Pdf_commentators_tab_resolves_to_the_pdf_page()
    {
        using var f = new UserStateFixture();
        var src = new { type = "PdfBookTab", book = Book(131, "מנחות", "PdfBook", path: @"C:\p.pdf"), pageNumber = 9 };
        f.SetSession(1, new object[] { new { type = "PdfCommentatorsTab", sourceTab = src } }, 0);
        var t = Assert.Single(_reader.ReadOpenTabs(f.DbPath));
        Assert.Equal("otzaria://open/pdf/131?index=9", t.DeepLink);
        Assert.Equal("commentators", t.Reading!.TargetKind);
    }

    [Fact]
    public void Deeply_nested_combined_tabs_do_not_overflow_the_stack()
    {
        using var f = new UserStateFixture();
        JsonNode node = new JsonObject { ["type"] = "TextBookTab", ["book"] = JsonSerializer.SerializeToNode(Book(1, "x")), ["initalIndex"] = 1 };
        for (var i = 0; i < 40; i++) node = new JsonObject { ["type"] = "CombinedTab", ["rightTab"] = node };
        f.SetSessionRaw(1, new JsonArray(node).ToJsonString(), 0);
        var t = Assert.Single(_reader.ReadOpenTabs(f.DbPath));
        Assert.Null(t.Reading); // deeper than the resolver follows => no link, no crash
    }

    [Theory]
    [InlineData(5, 2)]     // beyond the end => clamped to the last restorable tab
    [InlineData(-3, 0)]    // negative => first
    [InlineData(1, 0)]     // points at junk => nearest previous restorable tab
    public void Current_tab_index_is_resolved_like_TabsRepository(int saved, int expectedIndex)
    {
        using var f = new UserStateFixture();
        var t = new { type = "ToolTab", title = "t" };
        f.SetSession(1, new object[] { t, "junk", t }, saved);
        var current = _reader.ReadOpenTabs(f.DbPath).Single(x => x.IsCurrent);
        Assert.Equal(expectedIndex, current.TabIndex);
    }

    // ---- workspaces ------------------------------------------------------------------------------------
    [Fact]
    public void Workspace_tab_count_skips_broken_unknown_and_pdf_commentators_tabs()
    {
        using var f = new UserStateFixture();
        f.SetList("workspaces", "key-workspaces", new object[]
        {
            new
            {
                id = "w", name = "ש",
                tabs = new object?[]
                {
                    new { type = "ToolTab", title = "א" }, "junk", null, 17, new { type = "FutureTab" }, new { title = "no type" },
                    new { type = "PdfCommentatorsTab", sourceTab = new { type = "PdfBookTab" } },
                    new { type = "TextBookTab", title = "ב", book = Book(1, "ב") },
                },
            },
        });
        var w = Assert.Single(_reader.ReadWorkspaces(f.DbPath));
        Assert.Equal(2, w.TabCount);
        Assert.Equal(new[] { "א", "ב" }, w.TabTitles);
    }

    [Fact]
    public void Workspace_without_id_is_kept_and_cannot_be_falsely_active()
    {
        using var f = new UserStateFixture();
        f.SetList("workspaces", "key-workspaces", new object[] { new { name = "בלי מזהה", tabs = Array.Empty<object>() } });
        f.SetSession(1, Array.Empty<object>(), 0, "something-else");
        var w = Assert.Single(_reader.ReadWorkspaces(f.DbPath));
        Assert.Equal("בלי מזהה", w.Name);
        Assert.False(w.IsActive);
    }

    [Fact]
    public void Active_workspace_uses_slot_one_only()
    {
        using var f = new UserStateFixture();
        f.SetList("workspaces", "key-workspaces", new object[] { new { id = "a", name = "א", tabs = Array.Empty<object>() }, new { id = "b", name = "ב", tabs = Array.Empty<object>() } });
        f.SetSession(1, Array.Empty<object>(), 0, "a");
        f.SetSession(2, Array.Empty<object>(), 0, "b");
        var w = _reader.ReadWorkspaces(f.DbPath);
        Assert.True(w.Single(x => x.Id == "a").IsActive);
        Assert.False(w.Single(x => x.Id == "b").IsActive);
    }

    // ---- bookmarks -------------------------------------------------------------------------------------
    [Fact]
    public void Bookmarks_keep_stored_order_reversed_even_with_mixed_created_dates_and_skip_searches()
    {
        using var f = new UserStateFixture();
        f.SetList("bookmarks", "key-bookmarks", new object[]
        {
            Entry(Book(1, "ראשון"), 1, createdAt: "2026-01-01T10:00:00.000"),
            Entry(Book(2, "שני"), 2),
            Search("חיפוש"),
            Entry(Book(3, "שלישי"), 3, createdAt: "garbage-date"),
        });
        var b = _reader.ReadBookmarks(f.DbPath);
        Assert.Equal(new[] { "שלישי", "שני", "ראשון" }, b.Select(x => x.Book.Title));
        Assert.Null(b[0].CreatedAt);
        Assert.NotNull(b[2].CreatedAt);
    }

    // ---- robustness: wrong JSON types at every level --------------------------------------------------
    [Fact]
    public void Wrong_json_types_in_any_field_never_throw_and_never_lose_the_other_entries()
    {
        var baseEntry = JsonSerializer.SerializeToNode(Entry(Book(7, "ספר", "TextBook", "d:slug", categoryPath: "א, ב"), 5, "מקום", commentators: ["רש\"י"], label: "תווית", createdAt: "2026-01-01T00:00:00", workspaceName: "ש"))!;
        baseEntry["readingAnchor"] = JsonSerializer.SerializeToNode(new { line = 5, intraLineFraction = 0.2, alignment = 0.05 });
        var bad = new JsonNode?[] { null, JsonValue.Create(1), JsonValue.Create("x"), JsonValue.Create(true), new JsonArray(), new JsonObject(), JsonValue.Create(1.5), JsonValue.Create(1e300) };
        var paths = new List<string[]>();
        Collect(baseEntry, [], paths);
        var okEntry = JsonSerializer.SerializeToNode(Entry(Book(99, "תקין"), 1))!;

        var n = 0;
        foreach (var path in paths)
        {
            foreach (var replacement in bad)
            {
                var clone = JsonNode.Parse(baseEntry.ToJsonString())!;
                Set(clone, path, replacement?.DeepClone());
                using var f = new UserStateFixture();
                f.SetListRaw("history", "history", new JsonArray(clone, okEntry.DeepClone()).ToJsonString());
                f.SetListRaw("bookmarks", "key-bookmarks", new JsonArray(clone.DeepClone(), okEntry.DeepClone()).ToJsonString());
                f.SetListRaw("bookmarks", "key-bookmark-groups", new JsonArray(new JsonObject { ["id"] = "g", ["name"] = "n", ["items"] = new JsonArray(clone.DeepClone(), okEntry.DeepClone()) }).ToJsonString());
                f.SetSessionRaw(1, new JsonArray(new JsonObject { ["type"] = "TextBookTab", ["book"] = clone["book"]?.DeepClone(), ["initalIndex"] = clone["index"]?.DeepClone(), ["readingAnchor"] = clone["readingAnchor"]?.DeepClone(), ["commentators"] = clone["commentatorsToShow"]?.DeepClone() }, okEntry.DeepClone()).ToJsonString(), 0);

                var books = _reader.ReadRecentBooks(f.DbPath, 10);
                Assert.Contains(books, e => e.Book.Id == 99);         // the healthy neighbour always survives
                _reader.ReadRecentSearches(f.DbPath, 10);
                Assert.Contains(_reader.ReadBookmarks(f.DbPath), e => e.Book.Id == 99);
                _reader.ReadBookmarkGroups(f.DbPath);
                _reader.ReadOpenTabs(f.DbPath);
                n++;
            }
        }
        Assert.True(n > 100);
    }

    private static void Collect(JsonNode node, string[] prefix, List<string[]> acc)
    {
        if (node is JsonObject o)
            foreach (var (k, v) in o)
            {
                var p = prefix.Append(k).ToArray();
                acc.Add(p);
                if (v is not null) Collect(v, p, acc);
            }
    }

    private static void Set(JsonNode root, string[] path, JsonNode? value)
    {
        var cur = (JsonObject)root;
        for (var i = 0; i < path.Length - 1; i++) cur = (JsonObject)cur[path[i]]!;
        cur[path[^1]] = value;
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("\"str\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("[null,1,\"x\",[],{}]")]
    [InlineData("[{\"book\":null},{\"book\":[]},{\"book\":{\"title\":5,\"type\":\"TextBook\"}},{\"book\":{\"title\":\"x\",\"type\":7}}]")]
    [InlineData("[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]")]
    public void Non_array_or_garbage_list_payloads_yield_empty_lists(string payload)
    {
        using var f = new UserStateFixture();
        f.SetListRaw("history", "history", payload);
        f.SetListRaw("bookmarks", "key-bookmarks", payload);
        f.SetListRaw("bookmarks", "key-bookmark-groups", payload);
        f.SetListRaw("workspaces", "key-workspaces", payload);
        f.SetSessionRaw(1, payload, 0);
        Assert.Empty(_reader.ReadRecentBooks(f.DbPath, 10));
        Assert.Empty(_reader.ReadRecentSearches(f.DbPath, 10));
        Assert.Empty(_reader.ReadBookmarks(f.DbPath));
        Assert.Empty(_reader.ReadBookmarkGroups(f.DbPath));
        Assert.Empty(_reader.ReadWorkspaces(f.DbPath));
        Assert.Empty(_reader.ReadOpenTabs(f.DbPath));
    }

    [Fact]
    public void Non_text_payload_and_session_columns_do_not_throw()
    {
        using var f = new UserStateFixture();
        using (var c = f.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO lists VALUES('history','history',x'DEADBEEF',1); INSERT INTO window_sessions VALUES(1,x'00',x'01',NULL,NULL,1);";
            cmd.ExecuteNonQuery();
        }
        Assert.Empty(_reader.ReadRecentBooks(f.DbPath, 10));
        Assert.Empty(_reader.ReadOpenTabs(f.DbPath));
    }

    [Fact]
    public void Huge_history_is_read_quickly_and_respects_max()
    {
        using var f = new UserStateFixture();
        var items = Enumerable.Range(1, 5000).Select(i => (object)Entry(Book(i % 800 + 1, "ספר " + (i % 800 + 1)), i, "מקום ארוך מאוד " + new string('א', 200))).ToList();
        f.SetList("history", "history", items);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = _reader.ReadRecentBooks(f.DbPath, 30);
        sw.Stop();
        Assert.Equal(30, r.Count);
        Assert.True(sw.ElapsedMilliseconds < 2000, sw.ElapsedMilliseconds + " ms");
    }

    // ---- read-only guarantees -----------------------------------------------------------------------------
    private static string Hash(string path)
    {
        if (!File.Exists(path)) return "";
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    [Fact]
    public void Reading_a_wal_database_leaves_db_and_wal_bytes_untouched()
    {
        using var f = new UserStateFixture(wal: true);
        using var writer = f.Open();
        using (var cmd = writer.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;";
            cmd.ExecuteNonQuery();
        }
        f.SetList("history", "history", new object[] { Entry(Book(1, "x"), 1) });
        f.SetSession(1, new object[] { new { type = "ToolTab", title = "t" } }, 0);
        var before = (Hash(f.DbPath), Hash(f.DbPath + "-wal"));
        Assert.NotEqual("", before.Item2);
        _reader.ReadRecentBooks(f.DbPath, 5);
        _reader.ReadRecentSearches(f.DbPath, 5);
        _reader.ReadBookmarks(f.DbPath);
        _reader.ReadBookmarkGroups(f.DbPath);
        _reader.ReadWorkspaces(f.DbPath);
        _reader.ReadOpenTabs(f.DbPath);
        _reader.Probe(f.DbPath);
        Assert.Equal(before, (Hash(f.DbPath), Hash(f.DbPath + "-wal")));
    }

    [Fact]
    public void Writes_through_the_readers_connection_string_are_rejected()
    {
        // Same connection settings the reader uses must refuse to write (guards against someone loosening Mode=ReadOnly).
        using var f = new UserStateFixture();
        var cs = new SqliteConnectionStringBuilder { DataSource = f.DbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using var c = new SqliteConnection(cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO lists VALUES('x','y','[]',1)";
        Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
    }

    private static int StaleCopyDirs() =>
        Directory.EnumerateDirectories(Path.GetTempPath(), "OtzariaWidget-uscopy-*").Count();

    [Fact]
    public void Temp_copy_fallback_reads_wal_content_and_removes_the_copy()
    {
        using var f = new UserStateFixture(wal: true);
        using var writer = f.Open();                     // live writer: the data sits only in the -wal
        using (var cmd = writer.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;";
            cmd.ExecuteNonQuery();
        }
        f.SetList("history", "history", new object[] { Entry(Book(1, "רק בוואל"), 1) });
        Assert.True(new FileInfo(f.DbPath + "-wal").Length > 0);

        var dirsBefore = StaleCopyDirs();
        var title = UserStateReader.RunOnCopy(f.DbPath, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT payload_json FROM lists WHERE box='history'";
            return (string?)cmd.ExecuteScalar();
        });
        Assert.Contains("רק בוואל", System.Text.RegularExpressions.Regex.Unescape(title!));
        Assert.Equal(dirsBefore, StaleCopyDirs());       // the copy (personal data) is gone
    }

    [Fact]
    public void Temp_copy_fallback_triggers_when_the_folder_does_not_allow_creating_files()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f = new UserStateFixture(wal: true);
        f.SetList("history", "history", new object[] { Entry(Book(1, "ספר"), 1) });
        Assert.False(File.Exists(f.DbPath + "-shm"));    // checkpointed: a reader would have to create -wal/-shm

        var dir = new DirectoryInfo(f.Dir);
        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.CreateFiles | FileSystemRights.AppendData, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        var acl = dir.GetAccessControl();
        acl.AddAccessRule(deny);
        dir.SetAccessControl(acl);
        try
        {
            var dirsBefore = StaleCopyDirs();
            var books = _reader.ReadRecentBooks(f.DbPath, 5);
            Assert.Equal("ספר", Assert.Single(books).Book.Title);
            Assert.False(File.Exists(f.DbPath + "-shm"));        // nothing was created next to Otzaria's database
            Assert.False(File.Exists(f.DbPath + "-wal"));
            Assert.Equal(dirsBefore, StaleCopyDirs());
        }
        finally
        {
            var acl2 = dir.GetAccessControl();
            acl2.RemoveAccessRule(deny);
            dir.SetAccessControl(acl2);
        }
    }

    [Fact]
    public void Stale_temp_copies_left_by_a_crash_are_cleaned_up()
    {
        var stale = Path.Combine(Path.GetTempPath(), "OtzariaWidget-uscopy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "user_state.db"), "x");
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-1));
        using var f = new UserStateFixture();
        UserStateReader.RunOnCopy(f.DbPath, _ => 0);
        Assert.False(Directory.Exists(stale));
    }
}
