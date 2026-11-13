using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OtzariaWidget.Core.Tests.UserState;

/// <summary>Creates a temp user_state.db with Otzaria's real schema and lets tests insert JSON payloads.</summary>
public sealed class UserStateFixture : IDisposable
{
    public string Dir { get; }
    public string DbPath { get; }

    public UserStateFixture(int userVersion = 1, bool createSchema = true, bool wal = false)
    {
        Dir = Path.Combine(Path.GetTempPath(), "otz-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        DbPath = Path.Combine(Dir, "user_state.db");
        using var c = Open();
        if (wal) Exec(c, "PRAGMA journal_mode=WAL");
        if (createSchema)
        {
            Exec(c, """
                CREATE TABLE lists (box TEXT NOT NULL, key TEXT NOT NULL, payload_json TEXT NOT NULL, updated_at INTEGER NOT NULL, PRIMARY KEY (box, key));
                CREATE TABLE window_sessions (slot INTEGER PRIMARY KEY, tabs_json TEXT NOT NULL, current_index INTEGER NOT NULL, active_workspace_id TEXT, bounds_json TEXT, updated_at INTEGER NOT NULL);
                CREATE TABLE pending_reports (id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, payload_json TEXT NOT NULL, created_at INTEGER NOT NULL);
                CREATE TABLE report_drafts (id TEXT PRIMARY KEY, kind TEXT NOT NULL, title TEXT NOT NULL, payload_json TEXT NOT NULL, created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL, revision INTEGER NOT NULL);
                """);
        }
        Exec(c, $"PRAGMA user_version = {userVersion}");
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void SetList(string box, string key, object payload) => SetListRaw(box, key, JsonSerializer.Serialize(payload));

    public void SetListRaw(string box, string key, string json)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO lists(box,key,payload_json,updated_at) VALUES($b,$k,$p,1)";
        cmd.Parameters.AddWithValue("$b", box);
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$p", json);
        cmd.ExecuteNonQuery();
    }

    public void SetSession(int slot, object tabs, int current, string? activeWorkspace = null) => SetSessionRaw(slot, JsonSerializer.Serialize(tabs), current, activeWorkspace);

    public void SetSessionRaw(int slot, string tabsJson, int current, string? activeWorkspace = null)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO window_sessions(slot,tabs_json,current_index,active_workspace_id,bounds_json,updated_at) VALUES($s,$t,$c,$w,NULL,1)";
        cmd.Parameters.AddWithValue("$s", slot);
        cmd.Parameters.AddWithValue("$t", tabsJson);
        cmd.Parameters.AddWithValue("$c", current);
        cmd.Parameters.AddWithValue("$w", (object?)activeWorkspace ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); } catch { }
    }

    // ---- JSON payload builders (shapes from storage-and-links.md §2.3) ------------------------------------------
    public static Dictionary<string, object?> Book(int? id, string title, string type = "TextBook", string source = "o",
        string? path = null, string? externalLibraryId = null, string? versionTitle = null, string? categoryPath = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["id"] = id, ["title"] = title, ["type"] = type, ["source"] = source,
            ["categoryPath"] = categoryPath ?? "קטגוריה, תת",
            ["versionTitle"] = versionTitle,
        };
        if (path is not null) d["path"] = path;
        if (externalLibraryId is not null) d["externalLibraryId"] = externalLibraryId;
        return d;
    }

    public static Dictionary<string, object?> Entry(Dictionary<string, object?> book, int index, string reference = "מקום",
        string targetKind = "book", string? historySlot = null, string? label = null, string? createdAt = null,
        string? workspaceName = null, string[]? commentators = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["ref"] = reference, ["book"] = book, ["index"] = index,
            ["commentatorsToShow"] = commentators ?? Array.Empty<string>(),
            ["isSearch"] = false, ["targetKind"] = targetKind, ["label"] = label, ["createdAt"] = createdAt,
            ["workspaceName"] = workspaceName,
            ["extraUnknownField"] = new { a = 1 },
        };
        if (historySlot is not null) d["historySlot"] = historySlot;
        return d;
    }

    public static Dictionary<string, object?> Search(string query, string mode = "advanced", string reference = "(ק)formatted")
        => new()
        {
            ["ref"] = reference, ["book"] = Book(null, query), ["index"] = 0, ["isSearch"] = true,
            ["searchMode"] = mode, ["targetKind"] = "book",
        };
}
