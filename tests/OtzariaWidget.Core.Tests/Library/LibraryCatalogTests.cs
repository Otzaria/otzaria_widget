using Microsoft.Data.Sqlite;
using OtzariaWidget.Core.Library;
using Xunit.Abstractions;

namespace OtzariaWidget.Core.Tests.Library;

public sealed class SeforimFixture : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "otz-seforim-" + Guid.NewGuid().ToString("N"));
    public string DbPath { get; }

    public SeforimFixture()
    {
        Directory.CreateDirectory(Dir);
        DbPath = Path.Combine(Dir, "seforim.db");
        Exec("""
            CREATE TABLE category (id INTEGER PRIMARY KEY NOT NULL, parentId INTEGER, title TEXT NOT NULL, level INTEGER NOT NULL DEFAULT 0, orderIndex INTEGER NOT NULL DEFAULT 999);
            CREATE TABLE category_closure (ancestorId INTEGER NOT NULL, descendantId INTEGER NOT NULL, PRIMARY KEY (ancestorId, descendantId));
            CREATE TABLE book (id INTEGER PRIMARY KEY NOT NULL, categoryId INTEGER NOT NULL, sourceId INTEGER NOT NULL DEFAULT 1, title TEXT NOT NULL, totalLines INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX idx_book_title ON book(title);
            CREATE TABLE tocEntry (id INTEGER PRIMARY KEY NOT NULL, bookId INTEGER NOT NULL, parentId INTEGER, textId INTEGER NOT NULL DEFAULT 0, level INTEGER NOT NULL DEFAULT 0, lineId INTEGER, isLastChild INTEGER NOT NULL DEFAULT 0, hasChildren INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX idx_toc_book ON tocEntry(bookId);
            -- categories: 1 תנ"ך (root) > 2 תורה ; 10 תלמוד בבלי (root) > 11 סדר מועד ; 20 תלמוד ירושלמי > 21 זרעים ; 30 הלכה
            INSERT INTO category(id,parentId,title,level) VALUES (1,NULL,'תנ"ך',0),(2,1,'תורה',1),(10,NULL,'תלמוד בבלי',0),(11,10,'סדר מועד',1),(20,NULL,'תלמוד ירושלמי',0),(21,20,'זרעים',1),(30,NULL,'הלכה',0);
            INSERT INTO category_closure(ancestorId,descendantId) VALUES (1,1),(2,2),(1,2),(10,10),(11,11),(10,11),(20,20),(21,21),(20,21),(30,30);
            -- book 1: TOC tree: root(1) with 3 chapters (2,3,4); chapter 3 has two sub-leaves (5,6)  => leaves: 2,5,6,4 = 4
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (1,2,'בראשית',1500);
            INSERT INTO tocEntry(id,bookId,parentId) VALUES (1,1,NULL),(2,1,1),(3,1,1),(4,1,1),(5,1,3),(6,1,3);
            -- book 2: no TOC, Bavli, 100 lines => 200
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (2,11,'ברכות',100);
            -- book 3: no TOC, Yerushalmi, 0 lines => max(0,1)*2 = 2
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (3,21,'פאה',0);
            -- book 4: no TOC, halacha, 761 lines => 761
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (4,30,'שלחן ערוך',761);
            -- book 5: no TOC, no lines => 1
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (5,30,'ריק',0);
            -- book 6: TOC with a single root => 1 leaf
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (6,30,'קצר',10);
            INSERT INTO tocEntry(id,bookId,parentId) VALUES (100,6,NULL);
            -- book 7: TOC entry whose parent is outside the book (unreachable) + one proper leaf
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (7,30,'יתום',10);
            INSERT INTO tocEntry(id,bookId,parentId) VALUES (200,7,NULL),(201,7,9999);
            """);
    }

    private void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch { }
    }
}

public class LibraryCatalogTests
{
    [Theory]
    [InlineData(1, 4)]       // TOC leaves
    [InlineData(2, 200)]     // no TOC, Bavli doubles
    [InlineData(3, 2)]       // no TOC, Yerushalmi, max(0,1) doubled
    [InlineData(4, 761)]     // no TOC, plain
    [InlineData(5, 1)]       // no TOC, no lines
    [InlineData(6, 1)]       // single root
    [InlineData(7, 1)]       // unreachable orphan ignored (Otzaria's DFS from roots)
    public void Learnable_item_counts(int bookId, int expected)
    {
        using var f = new SeforimFixture();
        var c = new LibraryCatalog(f.DbPath);
        Assert.Equal(expected, c.GetLearnableItemCount(bookId));
    }

    [Fact]
    public void Unknown_book_returns_null()
    {
        using var f = new SeforimFixture();
        var c = new LibraryCatalog(f.DbPath);
        Assert.Null(c.GetLearnableItemCount(424242));
        Assert.Null(c.GetBookInfo(424242));
        Assert.Null(c.FindBookIdByTitle("אין כזה"));
    }

    [Fact]
    public void Book_info_has_category_path()
    {
        using var f = new SeforimFixture();
        var c = new LibraryCatalog(f.DbPath);
        var info = c.GetBookInfo(2);
        Assert.NotNull(info);
        Assert.Equal("ברכות", info!.Value.Title);
        Assert.Equal("תלמוד בבלי, סדר מועד", info.Value.CategoryPath);
        Assert.Equal("הלכה", c.GetBookInfo(4)!.Value.CategoryPath);
    }

    [Fact]
    public void Find_book_by_title()
    {
        using var f = new SeforimFixture();
        var c = new LibraryCatalog(f.DbPath);
        Assert.Equal(1, c.FindBookIdByTitle("בראשית"));
        Assert.Equal(2, c.FindBookIdByTitle("ברכות"));
        Assert.Null(c.FindBookIdByTitle(""));
    }

    [Fact]
    public void Missing_or_invalid_database_never_throws()
    {
        var c = new LibraryCatalog(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N"), "seforim.db"));
        Assert.Null(c.GetLearnableItemCount(1));
        Assert.Null(c.GetBookInfo(1));
        Assert.Null(c.FindBookIdByTitle("x"));

        var dir = Path.Combine(Path.GetTempPath(), "otz-seforim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var bad = Path.Combine(dir, "seforim.db");
            File.WriteAllText(bad, "garbage garbage garbage garbage garbage garbage garbage garbage");
            var c2 = new LibraryCatalog(bad);
            Assert.Null(c2.GetLearnableItemCount(1));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Results_are_cached_and_db_is_not_held_open()
    {
        var f = new SeforimFixture();
        var c = new LibraryCatalog(f.DbPath);
        Assert.Equal(4, c.GetLearnableItemCount(1));
        Assert.Equal("בראשית", c.GetBookInfo(1)!.Value.Title);
        Assert.Equal(1, c.FindBookIdByTitle("בראשית"));
        File.Delete(f.DbPath);                         // no handle may be left open
        Assert.Equal(4, c.GetLearnableItemCount(1));   // served from cache
        Assert.Equal("בראשית", c.GetBookInfo(1)!.Value.Title);
        Assert.Equal(1, c.FindBookIdByTitle("בראשית"));
        Assert.Null(c.GetLearnableItemCount(2));       // not cached => db gone => null, no throw
        f.Dispose();
    }

    [Fact]
    public void Does_not_modify_the_database()
    {
        using var f = new SeforimFixture();
        var before = (new FileInfo(f.DbPath).Length, File.GetLastWriteTimeUtc(f.DbPath));
        var c = new LibraryCatalog(f.DbPath);
        c.GetLearnableItemCount(1); c.GetLearnableItemCount(2); c.GetBookInfo(1); c.FindBookIdByTitle("ברכות");
        Assert.Equal(before, (new FileInfo(f.DbPath).Length, File.GetLastWriteTimeUtc(f.DbPath)));
    }
}

/// <summary>Smoke tests against the real %APPDATA%\otzaria library (skipped silently if absent).</summary>
public class LibraryCatalogRealDataTests(ITestOutputHelper output)
{
    private static string? RealDb()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appData)) return null;
        var txt = Path.Combine(appData, "otzaria", "library_database_path.txt");
        if (!File.Exists(txt)) return null;
        var dir = File.ReadAllText(txt).TrimStart('﻿').Trim();
        var db = Path.Combine(dir, "seforim.db");
        return File.Exists(db) ? db : null;
    }

    [Fact]
    public void Real_library_known_counts()
    {
        var db = RealDb();
        if (db is null) return;
        var c = new LibraryCatalog(db);
        // values validated in docs/research/hive-and-progress.md §2.3
        Assert.Equal(50, c.GetLearnableItemCount(1));      // Bereshit
        Assert.Equal(125, c.GetLearnableItemCount(103));   // Berachot
        Assert.Equal(312, c.GetLearnableItemCount(104));   // Shabbat
        var info = c.GetBookInfo(103);
        Assert.NotNull(info);
        Assert.False(string.IsNullOrEmpty(info!.Value.CategoryPath));
        Assert.Equal(103, c.FindBookIdByTitle(info.Value.Title));
        output.WriteLine($"real library ok: counts 50/125/312, book 103 category depth {info.Value.CategoryPath!.Split(',').Length}");
    }
}
