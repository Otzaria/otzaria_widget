using System.Diagnostics;
using Microsoft.Data.Sqlite;
using OtzariaWidget.Core.Library;
using Xunit.Abstractions;

namespace OtzariaWidget.Core.Tests.Library;

/// <summary>QA regression tests: fidelity with Otzaria's Shamor Zachor item counting (shamor_zachor_data_provider.dart).</summary>
public class LibraryCatalogQaTests(ITestOutputHelper output)
{
    private static void Exec(string db, string sql)
    {
        using var c = new SqliteConnection($"Data Source={db};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Toc_without_any_root_falls_back_to_total_lines_like_otzaria()
    {
        // _buildSectionsFromToc keeps only parentId==null entries; with none, `sections` is empty and Otzaria counts totalLines.
        using var f = new SeforimFixture();
        Exec(f.DbPath, """
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (20,30,'ללא שורש',40), (21,11,'ללא שורש בבלי',40);
            INSERT INTO tocEntry(id,bookId,parentId) VALUES (300,20,9001),(301,20,300),(302,21,9002);
            """);
        var c = new LibraryCatalog(f.DbPath);
        Assert.Equal(40, c.GetLearnableItemCount(20));
        Assert.Equal(80, c.GetLearnableItemCount(21));   // Bavli doubling applies to the totalLines fallback only
    }

    [Fact]
    public void Tanach_category_below_a_talmud_category_resets_the_daf_doubling()
    {
        // _buildRecursiveCategory: contentType flips to "daf" on בבלי/ירושלמי and back to "text" on תנ"ך, inherited otherwise.
        using var f = new SeforimFixture();
        Exec(f.DbPath, """
            INSERT INTO category(id,parentId,title,level) VALUES (40,10,'תנ"ך מוטבע',1),(41,40,'עמוק',2),(42,11,'סתם',2);
            INSERT INTO category_closure(ancestorId,descendantId) VALUES (40,40),(10,40),(41,41),(40,41),(10,41),(42,42),(11,42),(10,42);
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (30,41,'בתוך תנך',10),(31,42,'עמוק בבבלי',10);
            """);
        var c = new LibraryCatalog(f.DbPath);
        Assert.Equal(10, c.GetLearnableItemCount(30));
        Assert.Equal(20, c.GetLearnableItemCount(31));
    }

    [Fact]
    public void Toc_with_several_roots_counts_all_leaves()
    {
        using var f = new SeforimFixture();
        Exec(f.DbPath, """
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (50,30,'כמה שורשים',5);
            INSERT INTO tocEntry(id,bookId,parentId) VALUES (500,50,NULL),(501,50,NULL),(502,50,501),(503,50,501),(504,50,NULL);
            """);
        Assert.Equal(4, new LibraryCatalog(f.DbPath).GetLearnableItemCount(50)); // 500, 502, 503, 504
    }

    [Fact]
    public void Self_referencing_toc_does_not_loop()
    {
        using var f = new SeforimFixture();
        Exec(f.DbPath, """
            INSERT INTO book(id,categoryId,title,totalLines) VALUES (60,30,'מעגל',7);
            INSERT INTO tocEntry(id,bookId,parentId) VALUES (600,60,600),(601,60,602),(602,60,601),(603,60,NULL);
            """);
        Assert.Equal(1, new LibraryCatalog(f.DbPath).GetLearnableItemCount(60));
    }

    // ------------------------------------------------------------------------------------------------
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
    public void Real_library_leaf_counts_match_an_independent_recursive_sql_count()
    {
        var db = RealDb();
        if (db is null) return;
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();

        var ids = new List<int> { 1, 2, 100, 103, 104, 131, 500, 1000, 1938, 2000, 5000, 6868 };
        using (var cmd = conn.CreateCommand())
        {
            // a deterministic spread of books incl. big TOCs
            cmd.CommandText = "SELECT id FROM book WHERE id % 397 = 0 ORDER BY id LIMIT 20";
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetInt32(0));
        }

        var catalog = new LibraryCatalog(db);
        var checkedBooks = 0;
        foreach (var id in ids.Distinct())
        {
            int? expected;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT (SELECT count(*) FROM tocEntry e WHERE e.bookId = $id AND e.parentId IS NULL),
                           (SELECT totalLines FROM book WHERE id = $id),
                           (WITH RECURSIVE reach(id) AS (
                                SELECT id FROM tocEntry WHERE bookId = $id AND parentId IS NULL
                                UNION ALL
                                SELECT e.id FROM tocEntry e JOIN reach r ON e.parentId = r.id)
                            SELECT count(*) FROM reach r WHERE NOT EXISTS (SELECT 1 FROM tocEntry c WHERE c.parentId = r.id)),
                           (SELECT count(*) FROM book WHERE id = $id)
                    """;
                cmd.Parameters.AddWithValue("$id", id);
                using var r = cmd.ExecuteReader();
                r.Read();
                if (r.GetInt64(3) == 0) expected = null;
                else if (r.GetInt64(0) > 0) expected = (int)r.GetInt64(2);
                else
                {
                    expected = Math.Max((int)r.GetInt64(1), 1);
                    if (IsDaf(conn, id)) expected *= 2;
                }
            }
            Assert.Equal(expected, catalog.GetLearnableItemCount(id));
            checkedBooks++;
        }
        output.WriteLine($"verified {checkedBooks} real books against recursive SQL");
        Assert.Equal(48, catalog.GetLearnableItemCount(6868)); // the only TOC-less book: a Talmud tractate, 24 lines x 2
    }

    private static bool IsDaf(SqliteConnection conn, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT c.title FROM category_closure cc JOIN category c ON c.id = cc.ancestorId WHERE cc.descendantId = (SELECT categoryId FROM book WHERE id = $id) ORDER BY c.level";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        var daf = false;
        while (r.Read())
        {
            var t = r.GetString(0);
            if (t.Contains("בבלי") || t.Contains("ירושלמי")) daf = true;
            else if (t.Contains("תנ\"ך")) daf = false;
        }
        return daf;
    }

    [Fact]
    public void Real_library_queries_are_fast()
    {
        var db = RealDb();
        if (db is null) return;
        var c = new LibraryCatalog(db);
        c.GetLearnableItemCount(1); // warm the OS cache / SQLite
        var sw = Stopwatch.StartNew();
        foreach (var id in new[] { 103, 104, 131, 1938, 2000, 5000 }) c.GetLearnableItemCount(id);
        c.GetBookInfo(1938);
        c.FindBookIdByTitle("בראשית");
        sw.Stop();
        output.WriteLine($"6 counts + info + title lookup: {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < 1500, sw.ElapsedMilliseconds + " ms");
    }
}
