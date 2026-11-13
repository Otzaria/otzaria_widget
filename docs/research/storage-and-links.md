# Otzaria storage layout, user-state schema, and external deep links

Contract for the independent Windows 11 widget (C#/.NET) that READS Otzaria's local files and opens Otzaria
through its existing deep-link mechanism. The widget never modifies Otzaria or its data.

- Source analysed: Otzaria branch `dev`, commit `46b4af531`, app version `0.9.98+99803`, in `C:\Users\user\Documents\otzaria_project`.
  Paths below are relative to that repo root. "L123" means line 123 of that file.
- Verified against the real data on the dev machine (read-only: `sqlite3 -readonly`, directory listings, registry queries).
  No personal content is reproduced; book titles appear only as format examples.
- Language caveat: the code comments are Hebrew. All identifiers, JSON keys and URL formats below are copied verbatim.

Contents: 1 Data path discovery, 2 user_state.db, 3 personal_notes.db, 4 seforim.db, 5 Deep links and launching, 6 Open questions.

---

## 1. Data path discovery

### 1.1 Vocabulary

| Term | Meaning | Source |
|---|---|---|
| dataRoot | Folder with Hive settings, capabilities file, activation queue, per-book settings, etc. | `lib/core/app_paths.dart` L156-185 |
| databasesDir | Folder with `user_state.db`, `personal_notes.db`, `user_books.db`, `plugins_host.db`, `cache.db` | `app_paths.dart` L687-700 |
| libraryPath | Folder chosen as the library (`key-library-path`) | `app_paths.dart` L497-548 |
| libraryDbDir / seforim.db | Folder containing `seforim.db` = `libraryPath` joined with `key-library-folder-name` | `lib/data/constants/database_constants.dart` L113-135, L259-264 |

Dart's `Platform.environment['APPDATA']` is `%APPDATA%` (Roaming). Folder names are lowercase: `otzaria`.

### 1.2 dataRoot (Windows) - exact algorithm

From `AppPaths.getDataRootPath()` (`app_paths.dart` L156-185), Windows branch only:

1. Portable mode, if a FILE named `portable.marker` exists next to `otzaria.exe` (`app_paths.dart` L36, L125-136; native twin `windows/runner/main.cpp` L42-50 requires it to be a file, not a directory):
   `dataRoot = <exeDir>\otzaria_data` (`app_paths.dart` L40, L164-168; `main.cpp` L53-55).
2. Otherwise `dataRoot = %APPDATA%\otzaria` (`app_paths.dart` L171-173; `main.cpp` L56-70, falls back to `FOLDERID_RoamingAppData`).

There is NO other custom dataRoot setting. System-wide installs (exe under Program Files, or `system_install.marker` next to the exe, `app_paths.dart` L246-261) still use `%APPDATA%\otzaria` for dataRoot.
Only the library (and, for brand-new installs, the databases folder) moves to `%ProgramData%\otzaria` (`app_paths.dart` L392-418, Windows branch L406-409).

How a C# reader obtains `<exeDir>` when it does not already know it, in order of reliability:

1. `<dataRoot>\otzaria_capabilities.json` fields `exePath`, `portable` (section 1.7). Chicken and egg for portable: the file lives inside `otzaria_data`, which is what we are looking for.
2. User picks the folder manually (recommended for portable; the widget stores the choice in its own settings).
3. The registry protocol key (section 5.5). Portable copies never register it (`plugin_protocol_registration_service.dart` L31-40).

Candidate order for dataRoot discovery:

1. Widget's own saved override (manual folder chooser).
2. `%APPDATA%\otzaria` if it exists and contains `app_preferences.hive` or a `databases` folder.
3. Portable: `<exeDir>\otzaria_data` for each exe path the widget knows (user-chosen exe, `exePath` from a capabilities file, registry). Accept only if `<exeDir>\portable.marker` is a file.
4. Otherwise: ask the user.

### 1.3 databasesDir - exact algorithm

`AppPaths.getDatabasesPath()` (`app_paths.dart` L687-700), evaluated in this order:

1. Hive setting `key-databases-path` (`lib/settings/engine/settings_repository.dart` L123) if non-empty. Returned as is, no existence check.
2. `<dataRoot>\databases` if that directory exists ("legacy" location; existing installs keep it).
3. `dirname(libraryPath)\databases`, where `libraryPath` is `AppPaths.getLibraryPath()` (section 1.4). If `libraryPath` is `...\books`, this is its parent. For a default per-user install this is `%APPDATA%\otzaria\databases`; for a new system-wide install it is `%ProgramData%\otzaria\databases`; for portable `<exeDir>\otzaria_data\databases`.

Fallback that overrides the above at runtime (`app_paths.dart` L969-1001): files are opened via `resolveNotesDbPath(file)`, which uses `_writableDatabasesDirectory()`.
If the preferred directory is not writable (probe file `.otzaria_write_probe`), Otzaria copies `plugins_host.db`, `personal_notes.db`, `user_books.db` (NOT `user_state.db` and NOT `cache.db`, see `_personalDatabaseFileNames`, L947-951) into `<dataRoot>\databases` and uses that directory. From then on rule 2 selects it, because it now exists.
Consequence: `user_state.db` may be read from the preferred directory while the other DBs are in `<dataRoot>\databases` in this rare case. The robust widget rule is therefore:

> For each DB file separately, try `[key-databases-path, <dataRoot>\databases, dirname(libraryPath)\databases]` in order and use the first directory that actually contains that file.

`user_state.db` is opened with `AppPaths.resolveNotesDbPath('user_state.db')` (`lib/core/user_state/user_state_database.dart` L59-61). `personal_notes.db`: `lib/personal_notes/storage/personal_notes_database.dart` L53.
Expected files in the folder (verified on the dev machine): `cache.db`, `personal_notes.db`, `plugins_host.db`, `user_books.db`, `user_state.db`, plus `*-wal`/`*-shm`.

### 1.4 libraryPath and seforim.db

`AppPaths.getLibraryPath()` (`app_paths.dart` L497-548), Windows-relevant part:

1. Hive `key-library-path` if non-empty (`settings_repository.dart` L121). (The Linux/macOS "bundled library" override at L511-537 does not apply to Windows: `_detectBundledLibraryPath` returns null on Windows, L363-365.)
2. Otherwise the default, `getDefaultLibraryPath()` (L299-318), which then gets persisted:
   - system-wide install: `%ProgramData%\otzaria\books` (L312-315, L406-409);
   - else `<dataRoot>\books` (L317), i.e. `%APPDATA%\otzaria\books` or `<exeDir>\otzaria_data\books`.

`seforim.db` path (`database_constants.dart` L113-135, L259-264):

```
effective = Hive key-db-effective-path            (Android-only override; ignore on Windows, normally "")
if effective != "": use it
else: join(libraryPath, key-library-folder-name, "seforim.db")   # folder name is "" in practice, so libraryPath\seforim.db
```

`seforim.db` normally sits directly under `libraryPath` (folder name empty). Note the `books` suffix is part of `libraryPath` in default installs (`...\books\seforim.db`).
Dev machine: `libraryPath = C:\Users\<user>\Documents\otzaria_seforim\otzaria`, folder name empty, DB at `...\otzaria\seforim.db` (4.6 GB). A non-`books` folder name is legitimate.

Plain-text records that avoid reading Hive. Written on every Otzaria start on Windows (`lib/main.dart` L748 calls `AppPaths.recordLibraryPathForUninstaller`, `app_paths.dart` L463-494):

| File (in dataRoot) | Content | Encoding |
|---|---|---|
| `library_path.txt` | `AppPaths.getLibraryPath()` | UTF-8 WITH BOM (`\ufeff` prefix, L490), no trailing newline |
| `library_database_path.txt` | `DatabaseConstants.getDatabaseDirectoryPath()` = directory that contains `seforim.db` (libraryPath + folder name) | UTF-8 with BOM (L493) |
| `otzaria_capabilities.json` | has `libraryDbPath` = full path of seforim.db | UTF-8 JSON (section 1.7) |

The widget must strip a leading U+FEFF. Both are refreshed at each launch, so they can be stale only if the library moved since the last launch (portable folders that moved: see 1.8).

Recommended resolution order for seforim.db in the widget:
1. `otzaria_capabilities.json.libraryDbPath` (full path; trust only if file exists).
2. `library_database_path.txt` + `\seforim.db`.
3. Hive `key-library-path` + `key-library-folder-name` + `\seforim.db`.
4. `library_path.txt` + `\seforim.db`.
5. Defaults from 1.4 step 2.
6. User chooser.

Sibling files in the same folder: `lexical.db`, `otzar-HB_catalog.db`, `talmud_synopsis_pooled.db`, `תלמוד בבלי\` (PDF folder), `meivin-qa491-onnx\` (`DatabaseConstants.libraryManagedEntryNames`, `database_constants.dart` L90+).

### 1.5 app_preferences.hive

- File: `<dataRoot>\app_preferences.hive` (box name `app_preferences`, `lib/data/data_providers/hive_data_provider.dart` L65; opened with `path: dataRoot`, L67-76). It is never relocated by `key-databases-path` or the library path.
- Lock file `app_preferences.lock` is a separate file (`lib/core/info/settings_snapshot.dart` L72-76). The `.hive` file itself can be opened for reading while Otzaria runs: open with `FileShare.ReadWrite | FileShare.Delete`. Verified: the file parsed fine on the dev machine.
- Otzaria's own safe-read strategy is to copy the `.hive` to a temp folder and open the copy (`settings_snapshot.dart` L107-135); a log-structured format means a copy taken mid-write only loses the last partial frame. A C# reader can parse the file in memory instead of copying (nothing to clean up).
- Legacy per-feature Hive boxes (`history.hive`, `bookmarks.hive`, `tabs.hive`, `workspaces.hive`) were migrated into `user_state.db`; leftover files are renamed `*.hive.migrated` (present on the dev machine, can be ignored).
- Do not rely on Hive for history/bookmarks. Use `user_state.db`.

#### Hive frame format (verified by parsing the real file; 132 frames, parse ended exactly at EOF)

The file is a sequence of frames, little-endian, no file header:

```
uint32  frameLength      # total bytes of this frame INCLUDING these 4 bytes and the trailing CRC
byte    keyType          # 0 = uint32 key, 1 = UTF-8 string key (all app_preferences keys are string keys)
  if keyType == 1:  byte keyLen ; keyLen bytes UTF-8 key
  if keyType == 0:  uint32 key
value:  absent when (position == frameLength - 4)  -> this frame is a DELETE of the key
        else  byte typeId ; payload
uint32  crc32            # last 4 bytes (can be ignored)
```

Later frames override earlier ones (last write wins; deleted key = tombstone). Value type ids seen: `0` null, `1` int (stored as 8-byte IEEE double little-endian), `2` double (8 bytes), `3` bool (1 byte), `4` string (uint32 length + UTF-8 bytes). Other builtin ids from the Hive spec: `5` byte list, `6` int list, `7` double list, `8` bool list, `9` string list, `10` list, `11` map (each prefixed by uint32 count). Ids >= 32 are registered type adapters (not seen in `app_preferences`).
Keys relevant to path discovery (all string values): `key-library-path`, `key-library-folder-name`, `key-databases-path` (absent on the dev machine, i.e. default), `key-db-effective-path` (empty).
Otzaria is the only writer; a reader that hits a truncated last frame must stop silently (keep what was parsed).
A minimal verified Python probe was used to validate this layout (not committed; the layout is simple enough to re-implement in C# with `BinaryPrimitives`).

### 1.6 Other settings relevant to the widget in Hive

Stored in the same box, string-keyed. Do not guess at others; enumerate on demand.

| Key | Value | Notes |
|---|---|---|
| `sz:tracked_books` | string holding JSON, e.g. `[<int book id>, ...]` | "Shamor Zachor" tracked book ids (verified shape on dev machine). Other `sz:*` keys (`sz:progress_by_id`, `sz:book_columns`, ...) are JSON-in-string per the context doc; they were not present on the dev machine, so their format is NOT verified here. |
| `key-selected-city` | string (Hebrew city name) | Used for zmanim; value is user data |
| `key-bookmark-sort-mode` | `category` or `dateAdded` | `lib/bookmarks/models/bookmark_sort_mode.dart` L5-10, L12 |

### 1.7 otzaria_capabilities.json (best discovery hint, written by Otzaria itself)

`<dataRoot>\otzaria_capabilities.json`, written atomically (tmp file + rename) when its content (minus `writtenAt`) changes, each launch after init (`lib/core/otzaria_capabilities.dart` L17-100; call at `lib/main.dart` L1261).

```json
{
  "formatVersion": 1,
  "appVersion": "0.9.98.3",
  "buildNumber": 99803,
  "arch": "x64",
  "portable": false,
  "exePath": "<absolute path of the running otzaria.exe>",
  "readableDbSchemaVersion": 7,
  "libraryDbPath": "<absolute path of seforim.db>",
  "reportsQueueFormat": 1,
  "pluginInstallResults": 1,
  "semanticVectorFamilies": ["..."],
  "writtenAt": "2026-11-04T10:26:00.067722Z"
}
```

Useful fields: `exePath` (to launch Otzaria, including for portable copies), `portable`, `libraryDbPath`, `readableDbSchemaVersion` (the seforim.db schema this app version reads; the widget should check `schema_meta.db_schema_version` of the DB it opens, section 4).
Caveat: `exePath` is whatever executable last started (on the dev machine it is a debug build under the project's `build\windows\x64\runner\Release`). Always check the file exists. `databasesPath` is NOT included.

### 1.8 Portable specifics

- `portable.marker` file next to `otzaria.exe` (installer creates it for `/PORTABLE`, see `installer/otzaria.iss` header L8-12 and L118-126).
- `otzaria_data\portable_id` (GUID) is used by the portable launcher/shortcuts (`windows/launcher/launcher.cpp` L26-30), irrelevant to the widget.
- Absolute paths stored in settings/history (library, PDF paths) are rewritten by Otzaria on start if the portable folder moved; the last root is remembered in `otzaria_data\.otzaria_portable_root` (`lib/core/portable_paths.dart` L14-17, L38-60). If the widget reads data before Otzaria's first run after a move, PDF `path` values in history can be stale. Prefer `id` + `source` for identity.
- Portable copies do NOT register `otzaria://` (section 5.5), and use a per-data-root single-instance mutex (section 5.4).

### 1.9 Verified on the dev machine

| Check | Result |
|---|---|
| `%APPDATA%\otzaria` exists, non-portable | yes; contains `app_preferences.hive`, `app_preferences.lock`, `databases\`, `external_activations\`, `library_path.txt`, `library_database_path.txt`, `otzaria_capabilities.json` (`portable:false`), `*.hive.migrated`, `shared_preferences.json` (window bounds only, no library keys) |
| `key-databases-path` in Hive | absent -> rule 2 applies -> `%APPDATA%\otzaria\databases` (exists) |
| databases folder | `cache.db personal_notes.db plugins_host.db user_books.db user_state.db` + `-shm`/`-wal` for the three active DBs |
| `key-library-path` / `key-library-folder-name` | set / empty |
| `library_path.txt` and `library_database_path.txt` | both UTF-8 BOM + same directory as `key-library-path` |
| seforim.db | at `<libraryPath>\seforim.db`, 4.6 GB, `journal_mode=delete` |

---

## 2. user_state.db

File: `<databasesDir>\user_state.db`. Owner class `UserStateDatabase` (`lib/core/user_state/user_state_database.dart`). One shared DB for all Otzaria windows; one connection in the main isolate plus short-lived write connections in worker isolates (L9-13, L187-229).

### 2.1 Schema, versioning, journal mode

`CREATE TABLE IF NOT EXISTS` statements, `user_state_database.dart` L81-131. Verified identical in the real file (`sqlite3 -readonly .schema`):

```sql
CREATE TABLE lists (
  box TEXT NOT NULL,
  key TEXT NOT NULL,
  payload_json TEXT NOT NULL,      -- a JSON ARRAY (whole list), see 2.2
  updated_at INTEGER NOT NULL,     -- epoch MILLISECONDS (DateTime.now().millisecondsSinceEpoch)
  PRIMARY KEY (box, key)
);
CREATE TABLE window_sessions (
  slot INTEGER PRIMARY KEY,        -- 1 = main window; additional windows 2..N
  tabs_json TEXT NOT NULL,         -- JSON array of tab objects (2.5)
  current_index INTEGER NOT NULL,  -- index into tabs_json
  active_workspace_id TEXT,        -- Workspace.id, nullable
  bounds_json TEXT,                -- {"left","top","width","height","dpr","maximized"} (all optional except maximized)
  updated_at INTEGER NOT NULL      -- epoch ms
);
CREATE TABLE pending_reports (id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, payload_json TEXT NOT NULL, created_at INTEGER NOT NULL);
CREATE INDEX pending_reports_kind ON pending_reports(kind);
CREATE TABLE report_drafts (id TEXT PRIMARY KEY, kind TEXT NOT NULL, title TEXT NOT NULL, payload_json TEXT NOT NULL, created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL, revision INTEGER NOT NULL);
CREATE TABLE report_draft_attachments (draft_id TEXT PRIMARY KEY REFERENCES report_drafts(id) ON DELETE CASCADE, payload_json TEXT NOT NULL);
```

- `PRAGMA user_version = 1` (`_schemaVersion = 1`, L20, L133-138). Verified `1`. The comment at L19 says that the draft tables do not change the contract of queue readers; a bump of `user_version` is the only signal of an incompatible change, so the widget should read it and warn (not crash) if it is greater than 1.
- Journal mode: WAL (`PRAGMA journal_mode=WAL` in `lib/migration/database/sqlite3_utils.dart` L107; verified `wal`), `busy_timeout` 1000 ms for the Otzaria writer (L97, `user_state_database.dart` L71-75), `synchronous=NORMAL` (`sqlite3_utils.dart` L145-148). The file `user_state.db-wal`/`-shm` exist beside it while Otzaria is open (checkpoint on close: `closeWithCheckpoint`, `user_state_database.dart` L235-239).
- Reader guidance (read-only, never write):
  - Connection string `Data Source=<path>;Mode=ReadOnly;Pooling=False` (Microsoft.Data.Sqlite). Do NOT use `immutable=1` and do not copy only the `.db` file (it would miss the WAL).
  - Reading a WAL database read-only needs access to the `-shm` file. It works while the DB is open in Otzaria, and also after Otzaria closed (WAL checkpointed and removed) as long as the directory is writable so SQLite can create `-shm`. If the directory is read-only (e.g. a protected ProgramData folder), open fails with "unable to open database file" or returns `SQLITE_READONLY_*`; fall back to copying `user_state.db`, `user_state.db-wal` (if present) and `user_state.db-shm` (if present) to a temp folder and reading the copy (delete it afterwards).
  - Keep read transactions short; Otzaria's checkpointer waits on readers. Retry on `SQLITE_BUSY`.
  - Change detection: poll `SELECT box,key,updated_at FROM lists` plus `SELECT slot,updated_at FROM window_sessions` (cheap, ms precision), or use `FileSystemWatcher` on the databases folder (events on `user_state.db-wal`, not on `.db`) with a ~300-500 ms debounce, or `PRAGMA data_version` on a persistent connection.
  - History is written with a debounce: a position change is captured 1500 ms after the last event (`lib/history/bloc/history_bloc.dart` L372) and flushed on window close. Expect a few seconds of lag, and the live book is also captured on close via `_flushPendingSnapshots` (L191-199; the comment there explains that without this capture the book being read at close would not be recorded at all).

### 2.2 Boxes and keys in `lists`

Every list is stored as ONE JSON array in `payload_json`; there is no per-item row and no stable item id. Write path (for understanding only): `UserStateListStore.mutate` read-modify-writes the whole array in `BEGIN IMMEDIATE` (`lib/core/user_state/user_state_list_store.dart` L80-122, L179-203; upsert L149-162).
Only the following (box, key) pairs carry user-facing data. All other rows (`app_reports_queue`, `error_reports_queue`: keys such as `sent_reports_total`, `sent_reports`, `pending_reports`) are report/telemetry queues: IGNORE them (`lib/core/user_state/pending_report_store.dart`, `lib/services/sent_reports_counter.dart`).

| box | key | Content | Item type | Order | Cap | Source |
|---|---|---|---|---|---|---|
| `history` | `history` | Recently read books AND recent searches | `Bookmark` (2.3) | NEWEST FIRST (index 0 is most recent) | 200 | `lib/history/history_repository.dart` L7-8; `history_bloc.dart` L213, L240-253 (`insert(0, ...)`, trims at 200) |
| `bookmarks` | `key-bookmarks` | User bookmarks | `Bookmark` (2.3) | OLDEST FIRST (`[...current, bookmark]` append) | none | `lib/bookmarks/repository/bookmark_repository.dart` L12-23; `lib/bookmarks/bloc/bookmark_bloc.dart` L186-189 |
| `bookmarks` | `key-bookmark-groups` | "Grouped bookmarks": a named bundle of bookmarks of several books | `BookmarkGroup` (2.4) | append = oldest first | none | `bookmark_repository.dart` L12-18; `bookmark_bloc.dart` L282 |
| `workspaces` | `key-workspaces` | Saved workspaces ("desktops") | `Workspace` (2.6) | creation order (append) | none | `lib/workspaces/workspace_repository.dart` L31-32; `lib/workspaces/bloc/workspace_bloc.dart` L160 |

A missing row means "empty list" (a fresh install, or a list never written). Verified on the dev machine: rows exist for `history/history` (54 entries, 49 KB), `workspaces/key-workspaces` (1 workspace with 0 tabs); there is no `bookmarks` row because no bookmarks were ever added.
"Active workspace" is per window, not in `lists`: `window_sessions.active_workspace_id` (`lib/workspaces/workspace_repository.dart` L9-14).

There is no timestamp per history entry. `lists.updated_at` is the time of the last write of the WHOLE list. Do not invent "read at" times; show an ordered "recent books" list. The only per-item time is `createdAt` on bookmarks created by current versions (null in old ones, and always null in history entries; verified).

### 2.3 `Bookmark` JSON (used by history and bookmarks)

Serializer: `Bookmark.toJson()` (`lib/bookmarks/models/bookmark.dart` L290-321). Parser: `Bookmark.fromJson()` (L185-268). Required keys for the parser: `ref` (string), `index` (int), `book` (object). Everything else is optional/nullable. Unknown keys must be ignored (the format grows over time).

```jsonc
{
  "ref": "ריטב\"א על סוכה, ח:",           // human-readable location text (see below)
  "book": { ... },                        // 2.3.1
  "index": 140,                           // position: text = 0-based line index; PDF = 1-based page number
  "readingAnchor": {"line":140, "intraLineFraction":0.0101, "alignment":0.05},  // only when captured (text books, history only); see below
  "commentatorsToShow": ["הערות"],       // commentators that were visible; [] if none
  "isSearch": false,                      // TRUE => this is a saved search, not a book
  "targetKind": "book",                   // "book" | "commentators"
  "label": null,                          // user-editable title (bookmarks); null = show ref
  "createdAt": null,                      // ISO-8601 string; only bookmarks made by recent versions
  "workspaceName": "שולחן עבודה 1",      // workspace that was active when saved (nullable)
  "historySlot": "xxxx-1",               // only present (omitted if null) when the same book is open in several tabs; makes separate history entries per tab
  // --- search-only fields (null/absent for books) ---
  "searchOptions": {...}, "alternativeWords": {...}, "spacingValues": {...},
  "negativeSearchText": null, "negativeSearchOptions": null,
  "negativeAlternativeWords": null, "negativeSpacingValues": null,
  "searchScopeFacets": null | ["..."], "searchFilterFacets": [...],   // the latter is omitted when null
  "searchMode": "advanced" | "exact" | "fuzzy",
  "distance": 0, "proximityScope": "wordDistance" | "sameParagraph" | "sameSection",
  "searchConfiguration": {"distance":..,"proximityScope":..,"searchMode":..,"sortBy":..,"numResults":..,"resultGrouping":..,"wordMatchMode":..,"wordMatchCount":..,"anyOrder":..,"pluginSearchSelections":..,"regexEnabled":..,"caseSensitive":..,"multiline":..,"dotAll":..,"unicode":..}
}
```

Field meanings and how to interpret entries:

- Searches vs books: `isSearch` (`json['isSearch'] ?? false`, L195). Search entries have `book.id == null`, `book.type == "TextBook"`, `index == 0`, and `book.title` = the RAW query text typed by the user, while `ref` is the formatted query (options abbreviations such as `(ק)word + word`) (`history_bloc.dart` L305-336 (`isSearch: true` at L312); `buildSearchTitleSegments` L80-129). To show a search, display `book.title`; to re-run it use `searchMode`. Verified: 11 of 54 entries were searches, all with `book.id` null.
- Reading entries (`isSearch=false`): `book` identifies the book; `index` the position; `ref` is the Hebrew reference text built at capture time (`lib/bookmarks/utils/bookmark_from_tab.dart` L14-24, L72-105: e.g. `<book title>, <daf>` or a TOC path; for PDFs `"<title> <heading> — עמוד <page>"` or `"<title> עמוד <page>"`, L107-132). `ref` is already display-ready; do not rebuild it.
- `index` semantics:
  - text books (`TextBook`, and converted documents `DocxBook`/`EpubBook`/`DocumentBook` opened as text): 0-based `line.lineIndex` in `seforim.db` (verified: book 1938 has `lineIndex` 0..760, `totalLines` 761; history indices for it are within range). Equals `readingAnchor.line` when an anchor exists (`bookmark_from_tab.dart` L74-75).
  - PDF books (`PdfBook`): 1-based PDF page number (L111-131; deep link `open/pdf` enforces `>= 1`, `external_uri_router.dart` L747-751).
  - search: always 0.
- `readingAnchor` (`lib/text_book/utils/reading_position.dart` L68-99): `line` (int >= 0, same as `index`), `intraLineFraction` (0..1, how far through the line the top-of-viewport is) and `alignment` (viewport fraction where the anchor sits, default `0.05`). Present only for text books, and only if the viewer was ready when captured; absent otherwise (verified 8 of 37 text entries). Use it only for sub-line precision; `index` is always the authoritative coarse position. Parsing rule: invalid anchors (`line` not int / <0, non-finite numbers) are treated as absent.
- `commentatorsToShow` and `targetKind`: `targetKind == "commentators"` means the entry was captured from the commentaries view (`CommentatorsTab`/`PdfCommentatorsTab`), and `ref` starts with the literal prefix `מפרשים | ` (`bookmark_from_tab.dart` L36-70, L134-152). `commentatorsToShow` lists active commentator titles. Both are optional in old entries (default `book` / `[]`, L256-261). Verified: one commentators entry (PDF, page 68) exists.
- Identity / dedupe of entries (to match what Otzaria considers "the same place"): `historyKey = isSearch ? "search:<json [query, scopeFacets, filterFacets?]>" : "<targetKind>:<bookIdentity>[#<historySlot>]"` (`bookmark.dart` L99-106; `searchHistoryKey` in `lib/bookmarks/utils/search_history_key.dart` L1-22). `bookIdentity` = `id:<id>` (`id:<id>|pdf` for PDFs), else `external:<externalLibraryId>`, `file:<path>`, `filePath:<filePath>`, `category:<categoryId>|title:<title>|type:<fileType>`, `title:<title>`, plus `|src:u` / `|src:d:<slug>` suffix for non-official sources and `|version:<versionTitle>` for alternative editions (`bookmark.dart` L14-43; `book_source.dart` L139-140, L126). Bookmarks dedupe by `bookIdentity|index|targetKind` (`bookmark.dart` L114-115).
- Do not assume history entries are unique per book: they are unique per `historyKey`, so the same book can appear twice (different `historySlot`, or `targetKind` book vs commentators). A "recent books" widget should group by `bookIdentity` and keep the first (= newest) occurrence.

#### 2.3.1 `book` object (polymorphic on `book.type`)

Parser: `Book.fromJson` (`lib/models/books.dart` L112-129): `type` is one of `TextBook`, `PdfBook`, `DocxBook`, `EpubBook`, `DocumentBook`, `ExternalLibraryBook`; any other value throws in Otzaria (a defensive widget should skip such entries, not fail the whole list).

Common keys (`baseJson`, L95-110), used by `TextBook` (+ `versionTitle`, `heVersionTitle`; L315-319), `PdfBook` (L478), `EpubBook` (L667), `DocumentBook` (L726):

| Key | Type | Meaning |
|---|---|---|
| `id` | int or null | Book id in the SOURCE DB (`seforim.db` for official; `user_books.db` for user; attached DB). null for searches, personal PDFs not in a DB, and external files |
| `title` | string | Book title; titles are unique in the official `seforim.db` (8154 books, 8154 distinct titles) |
| `type` | string | see above |
| `path` | string | ONLY for file-based books (`PdfBook`, `DocxBook`, `EpubBook`, `DocumentBook`): absolute path of the file on disk |
| `author`, `heCategories`, `heEra`, `categoryPath` | string or null | display metadata. `categoryPath` is a comma-joined Hebrew category path |
| `categoryId` | int or null | id of the category in the source DB |
| `filePath`, `fileType` | string or null | `fileType`: `txt` (text), `pdf`, `docx`, `epub`, ..., `link` (external catalog) |
| `source` | string | WIRE KEY of the source: `"o"` official library (`seforim.db`), `"u"` personal book (`user_books.db`), `"d:<slug>"` attached external DB (`lib/models/book_source.dart` L4-44, L83, L99, L117) |
| `isUserBook` | bool | legacy flag; if `source` is missing/unknown the parser falls back to it (`BookSource.fromJson`, L48-57) |
| `externalLibraryId` | string or null | external catalog id (e.g. Otzar HaChochma `otzar`), or `talmud-pdf:<title>` for bundled Talmud PDFs (`database_constants.dart` `talmudBavliPdfExternalLibraryId`) |
| `versionTitle`, `heVersionTitle` | string or null | (`TextBook` only) the alternative edition; null = merged default text |

Special shape: `ExternalLibraryBook` (`books.dart` L380-398) uses `otzarId` (not `id`) and `link` (online URL), and the parser also accepts `bookName` for `title`. It has no `path`/`categoryId`. Cannot be opened by deep link; open `link` in a browser if desired.

Kinds actually seen on the dev machine: official `TextBook` (37 non-search, with `categoryId` and `versionTitle` null), official `PdfBook` (6, all bundled Talmud PDFs with `id` = the text book's id, `path` under `<library>\תלמוד בבלי\*.pdf`, `fileType:"pdf"`). No user books or attached DBs were present, but the formats are as above.

How to tell the categories:

| Entry kind | Test |
|---|---|
| search | `isSearch == true` |
| text book (official) | `!isSearch && book.type == "TextBook" && book.source == "o"` |
| PDF book | `book.type == "PdfBook"`; bundled Talmud PDF iff `externalLibraryId` starts with `talmud-pdf:` or path is under `<library>\תלמוד בבלי\` (note `id` is the id of the corresponding TEXT book, `bookIdentity` adds `\|pdf`) |
| user (personal) book | `book.source == "u"` (or legacy `isUserBook == true`) |
| attached-DB book | `book.source` starts with `d:` |
| external-library book | `book.type == "ExternalLibraryBook"` |
| converted document | `book.type` in `DocxBook`,`EpubBook`,`DocumentBook` |
| commentators view | `targetKind == "commentators"` |

### 2.4 `BookmarkGroup` (key `key-bookmark-groups`)

`lib/bookmarks/models/bookmark_group.dart` L6-69 (`fromJson` L49-59, `toJson` L61-68):

```json
{"id":"<microsecondsSinceEpoch>-<counter>", "name":"...", "items":[ <Bookmark>, ... ], "createdAt":"ISO-8601 or null"}
```

`items` are full `Bookmark` objects (2.3). `name` defaults to `''` when missing. Created from a multi-book "save all open tabs of a sugya" action.

### 2.5 `window_sessions.tabs_json` (open tabs per window)

- `slot = 1` is the main window (`lib/core/user_state/user_state_slot.dart`; constant `UserStateSlot.main = 1`, comment: "constant: saved sessions are keyed by it"). Other windows use 2..N. `loadAll` reads `slot > 0` (`window_session_store.dart` L133-139); negative slots are transient during compaction (L201-207), so filter `slot > 0`.
- A row left after Otzaria exits means it died unclean; the next launch restores it (comment `window_session_store.dart` L24-27). A row exists for a normally closed app too (it is the persisted session). Do not treat "row exists" as "Otzaria is running".
- `tabs_json` = JSON array of tab objects, written by `TabsRepository` as `jsonEncode(tabs.map((t) => t.toJson()))` (`lib/tabs/tabs_repository.dart` L264-288). `current_index` is the active tab index (clamped by Otzaria on restore). Tab decoder: `OpenedTab.fromJson` (`lib/tabs/models/tab.dart` L113-133).

Tab types (field `type`) and their keys:

| `type` | Keys (verified or from the serializer) | Source |
|---|---|---|
| `TextBookTab` | `title`, `book` (2.3.1), `initalIndex` (SIC, typo: line index), `readingAnchor?`, `commentators` (string[]), `splitedView`, `showPageShapeView`, `showLeftPane`, `isPinned`, `historySlot?`, `highlightText?`, `permanentHighlightLine?`, `initialSearchResultLines?`, `pinpointHighlight?`, `pinpointHighlightSectionIndex?`, plus in-book search keys (`searchText`, `searchMode`, `searchDistance`, ...) | `lib/tabs/models/text_tab.dart` L497-518 |
| `PdfBookTab` | `path`, `book`, `pageNumber` (1-based), `showLeftPane`, `isPinned`, `requiresStableLayout`, `historySlot?`, `savedLayoutMode?`, `activeCommentators?`, `savedZoom?`, `externalMatches?`, search keys | `lib/tabs/models/pdf_tab.dart` L382-399 (verified on dev machine) |
| `SearchingTabWindow` (also accepts `SearchingTab` when reading) | `title`, `searchText`, `negativeSearchText`, `autoRunInitialSearch`, `isPinned`, `distance`, `proximityScope` (int index), `searchMode` (int index), `numResults`, `sortBy`, `resultGrouping`, `currentFacets`, `searchScopeFacets`, `wordMatchMode`, `wordMatchCount`, `anyOrder`, option maps | `lib/tabs/models/searching_tab.dart` L564-600 (verified). NOTE: unlike history entries, here the enums are integer INDICES, not names |
| `SemanticSearchTab` | `title`, `isPinned`, + query options | `semantic_search_tab.dart` L92-96 |
| `CombinedTab` (split view) | `rightTab`, `leftTab` (nested tab objects; `rightTab` is the FIRST pane in RTL order), `splitRatio`, `isPinned` | `combined_tab.dart` L83-91 |
| `CommentatorsTab` | `title`, `isPinned`, `initialIndex`, `bookTitle`, `sourceTab` (a `TextBookTab` object), `selectedCommentators` | `commentators_tab.dart` L179-189 |
| `PdfCommentatorsTab` | `sourceTab` (a `PdfBookTab`). Not persisted in workspaces (pruned) but can be in sessions | `pdf_commentators_tab.dart` L76-80; `workspace.dart` L103-107 |
| `ToolTab` | `toolId`, `title`, `isPinned`, `instanceId`, `allowMultipleInstances`, `pluginRestoreState?` | `lib/tabs/models/tool_tab.dart` L112-122 |

The currently open book (what "continue reading" would use) is `tabs[current_index]`: for `TextBookTab` use `book.id` + `initalIndex`; for `PdfBookTab` use `book.id` + `pageNumber`; for `CombinedTab` use `rightTab`/`leftTab`; for `CommentatorsTab` use `sourceTab`. `bounds_json` = `{"left","top","width","height","dpr","maximized"}` (`lib/core/user_state/window_bounds.dart` L64-71), irrelevant to the widget.
Verified on the dev machine: slot 1 with 3 tabs (`PdfBookTab` with `book.source = "o"`, a `SearchingTabWindow`, a `ToolTab`) and `bounds_json = {"maximized":true}`. Open-tab order in the file is the display order.

### 2.6 `Workspace` (key `key-workspaces`)

`lib/workspaces/workspace.dart` L22-193:

```json
{"id":"<microsecondsSinceEpoch>-<n>", "name":"שולחן עבודה 1",
 "tabs":[ <tab object as in 2.5> ... ], "currentTab":0,
 "activePane":"right"|"left"   // only when the active tab is a CombinedTab
 "isPinned":true               // omitted when false
}
```

Tabs use exactly the shapes in 2.5, except `PdfCommentatorsTab` is never stored. Decoding skips unknown/broken tabs and keeps the rest (L109-134), a widget should do the same per tab. Verified: one workspace `{id,name,tabs:[],currentTab:0}`.
The default names are `"שולחן עבודה <N>"` (L8-20). There is no URL to switch workspace (section 5.2).

### 2.7 Reference queries (all read-only)

Recent books newest-first (adapted from the context doc: it sorted `ORDER BY CAST(j.key AS INTEGER)`, which is correct because `json_each` keys of an array are its indices; index 0 is newest):

```sql
SELECT CAST(j.key AS INTEGER) AS pos,
       json_extract(j.value,'$.book.id')            AS book_id,
       json_extract(j.value,'$.book.title')         AS title,
       json_extract(j.value,'$.book.type')          AS type,
       COALESCE(json_extract(j.value,'$.book.source'),
                CASE WHEN json_extract(j.value,'$.book.isUserBook') THEN 'u' ELSE 'o' END) AS source,
       json_extract(j.value,'$.index')              AS idx,
       json_extract(j.value,'$.readingAnchor.line') AS anchor_line,
       json_extract(j.value,'$.ref')                AS ref,
       COALESCE(json_extract(j.value,'$.targetKind'),'book') AS target_kind,
       json_extract(j.value,'$.commentatorsToShow') AS commentators
FROM lists AS l, json_each(l.payload_json) AS j
WHERE l.box = 'history' AND l.key = 'history'
  AND COALESCE(json_extract(j.value,'$.isSearch'),0) = 0
ORDER BY pos LIMIT 20;
```

Recent searches: same with `isSearch = 1`, `title = json_extract(j.value,'$.book.title')` (raw query), `mode = json_extract(j.value,'$.searchMode')`. Bookmarks: `box='bookmarks' AND key='key-bookmarks'`, order by `createdAt` descending if wanted. Groups: `key='key-bookmark-groups'`, `json_each(j.value,'$.items')`. Last-written time of a list: `lists.updated_at / 1000.0` (Unix seconds).
`json_each`/`json_extract` need SQLite built with JSON1 (always true in Microsoft.Data.Sqlite's bundled SQLite and the `sqlite3 3.54` CLI used for verification). A large history (49 KB here, up to 200 entries) is small enough to parse in C# with `System.Text.Json` instead; recommended, because it lets you tolerate bad entries individually.

---

## 3. personal_notes.db (brief)

File `<databasesDir>\personal_notes.db`, WAL, `PRAGMA user_version = 2` (verified). Class `PersonalNotesDatabase` (`lib/personal_notes/storage/personal_notes_database.dart` L11-136). Verified schema:

```sql
CREATE TABLE personal_notes (
  id TEXT PRIMARY KEY,
  book_id TEXT NOT NULL,        -- the book TITLE (text), not an integer id (verified values are titles) - for user books Otzaria uses its own key scheme via personal_notes_user_book_keys
  line_number INTEGER,          -- 1-BASED line in the book; NULL when status = 'missing'
  display_title TEXT,           -- beginning of the line, for display/relocation
  last_known_line INTEGER,
  status TEXT NOT NULL,         -- 'located' | 'missing'   (PersonalNoteStatus, lib/personal_notes/models/personal_note.dart L6-13)
  content TEXT NOT NULL,        -- text or Quill-Delta JSON depending on content_format
  created_at TEXT NOT NULL,     -- ISO-8601
  updated_at TEXT NOT NULL,     -- ISO-8601
  content_plain TEXT NOT NULL DEFAULT '',        -- plain text for search/preview
  content_format TEXT NOT NULL DEFAULT 'plain',  -- 'plain' | 'quillDelta'
  anchor_text TEXT, anchor_prefix TEXT, anchor_suffix TEXT, anchor_start INTEGER, anchor_end INTEGER,
  pdf_page INTEGER, pdf_x REAL, pdf_y REAL, pdf_document_id TEXT
);
CREATE INDEX idx_book_id ON personal_notes(book_id);
CREATE INDEX idx_book_line ON personal_notes(book_id, line_number);
CREATE TABLE personal_notes_user_book_keys (id INTEGER PRIMARY KEY CHECK (id = 1), generation INTEGER NOT NULL, completed_generation INTEGER NOT NULL);
```

Meaning: a note is anchored to (book title, line). `line_number` is 1-BASED while history `index` is 0-BASED line index: map note line N to deep-link index N-1 (inference from the doc comment at `personal_note.dart` L32 versus `lineIndex` 0-based in seforim.db; not validated against a UI). Notes' content is private user text: the widget should show at most `display_title`/`content_plain` snippets and only if the user enables it. The widget should not read `personal_notes` in v1 beyond counts.
`personal_notes_user_book_keys` is a one-row migration/bookkeeping table (generation counters).

`user_books.db` (also in databasesDir): same `seforim.db` family schema for personal books (`book`, `line`, `tocEntry`, ..., plus `pages`, `filePath`, `fileType`, `isPersonal` columns), `user_version 0`, `db_meta.line_ref_index_version = 2`. Empty on the dev machine (0 books). `book.pages` gives the page count of personal PDFs.

---

## 4. seforim.db (brief)

Location: section 1.4. Read-only everywhere: Otzaria itself treats it as read-only (`cache.db` exists so that `seforim.db` never needs writes, `app_paths.dart` L1077-1083).

- Size 4.6 GB; `journal_mode = delete` (verified), `PRAGMA user_version = 0`. Version metadata is in the `schema_meta` table: `db_version` (here `37`), `db_schema_version` (here `7`). Otzaria 0.9.98 reads schema <= 7 (`DatabaseConstants.readableDbSchemaVersion = 7`, `database_constants.dart` L19-21, also exported as `readableDbSchemaVersion` in `otzaria_capabilities.json`). A widget should check `db_schema_version <= 7` before relying on columns below, and degrade gracefully otherwise.
- Open with `Mode=ReadOnly`, never copy it; the library may be on another drive; keep queries indexed (do not scan `line`: ~millions of rows. `tocEntry` has 1,581,593 rows, `book` 8,154 rows).
- Tables relevant to the widget (all columns verified with `.schema` on the real DB):

(a) Resolve a book by id or title

```sql
-- book(id PK, categoryId, sourceId, title, heRef, heShortDesc, heDesc, orderIndex, totalLines, isBaseBook, ...,
--      dependenceType, collectiveTitleHe, collectiveTitleEn, contentHash)
-- indexes: idx_book_title(title), idx_book_category(categoryId), idx_book_heref(heRef), idx_book_source(sourceId)
SELECT id, title, categoryId, totalLines, heRef FROM book WHERE id = ?;       -- by id (history/bookmark/tab 'book.id')
SELECT id, title, categoryId, totalLines FROM book WHERE title = ?;          -- by title (unique in the verified DB; take the first and verify by id when both are known)
-- category(id PK, parentId, title, level, orderIndex, heShortDesc, heDesc);  category_closure exists for ancestry
-- source(id, name)    e.g. 1 Sefaria, 3 DictaToOtzaria, ... (not needed except for display)
-- author / book_author, book_acronym, topic / book_topic exist for richer metadata; book_version(bookId, versionTitle, heVersionTitle, hasContent, ...) lists alternative editions
```

`book.id` here is exactly the `book.id` stored in history/bookmarks/tabs when `source == "o"`, and exactly what `otzaria://open/book/<id>` expects. For `source == "u"` look in `user_books.db` instead (same shape, `book.id` is an AUTOINCREMENT local id); for `d:<slug>` the attached DB (not covered; path listed in Hive setting `key-attached-libraries`, JSON).

(b) Counts needed for progress

```sql
-- Number of lines of a text book (preferred and indexed):
SELECT totalLines FROM book WHERE id = ?;                                   -- e.g. 761 for book 1938
-- Same via line table (indexed on (bookId,lineIndex)):
SELECT MIN(lineIndex), MAX(lineIndex), COUNT(*) FROM line WHERE bookId = ?;  -- 0..760, 761 rows => lineIndex is 0-based, dense
-- TOC entries:  tocEntry(id, bookId, parentId, textId -> tocText.text, level, lineId -> line.id, isLastChild, hasChildren)
SELECT COUNT(*) FROM tocEntry WHERE bookId = ?;                              -- indexed (idx_toc_book); 108 for book 1938
SELECT t.text, e.level, l.lineIndex
  FROM tocEntry e JOIN tocText t ON t.id = e.textId LEFT JOIN line l ON l.id = e.lineId
  WHERE e.bookId = ? ORDER BY l.lineIndex;                                   -- TOC with line positions (for "chapter 12 of 54")
-- line.tocEntryId and table line_toc(lineId -> tocEntryId) map a line to its nearest TOC entry.
```

Progress for a text book = `(index + 1) / totalLines` (clamped to 0..1), or the TOC-based "section n of N" above. Caveats: `totalLines` counts lines, not characters (`line.charCount` exists if a finer measure is needed). For PDF books `totalLines` is the TEXT edition's line count (e.g. 4001 for the Talmud PDF book of Menachot, id 131) and is NOT the PDF page count. The page count of a bundled Talmud PDF is not in `seforim.db`; use the PDF file (`book.path`, e.g. read the PDF header with a PDF library) or, for personal PDFs, `user_books.db.book.pages`. Otzaria's own "Shamor Zachor" progress uses its own unit model (not part of this research).
`schema_meta` also tells whether the content is split (`line_content` table in schema 6+; `line` has no `content` column in schema 7), irrelevant unless the widget shows book text, which the context doc rules out for v1.

---

## 5. Deep links and opening Otzaria externally

### 5.1 URL scheme and parser

Single scheme `otzaria://`. Parser: `ExternalUriRouter.parseUri` (`lib/core/external_uri_router.dart` L373-416). Scheme, host and the first path segment are case-insensitive (L290). A second scheme `zayit://book/{id}[/line/{index}]` is converted to `otzaria://open/book/{id}?index={index}` (L343-371) but is NOT registered with Windows and the native launcher only forwards arguments starting with `otzaria:` (`main.cpp` L386-387, `lib/main.dart` L1453), so it cannot be used externally.

Percent-encoding: use UTF-8 percent-encoding (`Uri.EscapeDataString`). A legacy decoder also accepts Windows-1255 / ISO-8859-8 / CP862 encoded bytes (L418-456); not needed.
Unknown path => `parseUri` returns null and the link is silently ignored (the running instance just logs).
Path segments: empty segments are ignored, so a trailing `/` is harmless.

### 5.2 Complete URL list (verbatim from the router, L221-289, with parser code)

Books (the only ones that can carry a position):

| URL | Effect | Parser |
|---|---|---|
| `otzaria://open/book/<id>` | Open the text book with DB id `<id>` (official library). If already open in a tab it just switches to it (does not move the position) | L690-735 |
| `otzaria://open/book/<id>?index=<n>` | Open at line index `<n>` (0-based, `n >= 0`; invalid or negative is ignored). If the tab is already open it moves to that position (`hasExplicitPosition`, L74-77, `lib/utils/navigation/external_action_dispatcher.dart` L7-23) | L700-706 |
| `...?index=<n>&mark` | Additionally highlight the whole section at `<n>`; `mark` is a valueless flag | L713-714 |
| `...?index=<n>&m=<text>` | Highlight specific text (URL-encoded) in that section; priority `m` > `mark` > `q` | L716-725 |
| `...?q=<text>` | Highlight search text (opens an in-book search); ignored if `m` or `mark` given | L708-725 |
| `otzaria://open/book/<id>?source=user` | Personal book (`user_books.db`) | `lib/utils/book_link_builder.dart` L6-25 |
| `otzaria://open/book/<id>?source=db:<slug>` | Book from an attached external DB; slug matches `^[\p{L}\p{N}._-]{1,64}$` | same |
| `otzaria://open/pdf/<id>[?index=<page>]` | Open the PDF edition of book `<id>` at page `<page>` (1-based, `>= 1`; invalid ignored). `source` param works as above | L737-758 |

Rules that matter when generating links from stored entries:
- Text vs PDF is chosen by the URL, not auto-detected: `open/book` only matches `TextBook` or converted-document books, `open/pdf` only `PdfBook`; both also require `book.source == <source>` (`lib/utils/navigation/external_book_link_resolver.dart` L6-21). The same numeric id can be both a text book and a PDF edition (Talmud tractates, e.g. id 131 is both): choose from `book.type` of the entry (`PdfBook` => `open/pdf`).
- `source` param: omit for `"o"`, `user` for `"u"`, `db:<slug>` for `"d:<slug>"` (`book_link_builder.dart` L6-11). A missing book is reported to the user in Otzaria's UI as "book not found" (snack bar, `main_window_screen.dart` L1869-1888 and L1890+).
- Id must be an integer > 0 or the link is rejected (L691-694).
- Not expressible in a URL: sub-line anchor (`readingAnchor.intraLineFraction`), `commentatorsToShow`/`targetKind` (opening commentaries), `versionTitle` (alternative edition; the link opens the merged text), `historySlot`, workspaces, split view. There is NO open-by-title and NO open-by-path link; resolve title -> id yourself via `seforim.db` (section 4a) and link by id. Books with `id == null` (personal PDFs outside a DB, external-library books) cannot be opened through Otzaria links.

Other screens/actions (all `otzaria://open/...` unless noted; handlers in `lib/navigation/view/main_window_screen.dart` L1617-1720):

| URL | Effect |
|---|---|
| `otzaria://open/history` | History dialog (router L629-632; handler `main_window_screen.dart` L1736) |
| `otzaria://open/bookmarks` | Bookmarks dialog (router L634-637; handler L1739) |
| `otzaria://open/search` | Search screen without running a search (L576-588 / screen alias L314-317) |
| `otzaria://open/search?q=<text>[&mode=advanced\|exact\|fuzzy\|smart]` | New search tab, runs immediately; unknown/absent mode => advanced; `smart` => semantic search tab (L578-587) |
| `otzaria://open/library` | Library screen |
| `otzaria://open/tab/<index>` | Switch to open tab number `<index>` (0-based) in the handling window; ignored (returns false) if out of range (L681-688, `main_window_screen.dart` L1627-1636) |
| `otzaria://open/inspection` | Reading screen (the last open book) (L597-600) |
| `otzaria://open/daily_page` | Today's Daf Yomi in the Talmud PDF (L607-610) |
| `otzaria://open/calendar`, `gematria`, `notes`, `shamor_zachor`, `measurements`, `aramaic_dictionary`, `acronyms_dictionary`, `tikkun_korim`, `biographies` | Built-in tool tabs (aliases L302-312); `?q=<text>` only for biographies, acronyms, aramaic dictionary, gematria (`lib/tools/tool_query.dart` L13-18) |
| `otzaria://open/tool/<tool-id>`, `otzaria://open/plugin/<plugin-id>` | Tool tab / plugin by id |
| `otzaria://open/tools`, `/detection[?q=]`, `/settings[/<tab>]`, `/sdk`, `/report`, `/reports`, `/donate` | Misc dialogs/screens (L601-663) |
| `otzaria://window/new` | New empty window (L394-402) |
| `otzaria://library/reindex` | Refresh library from disk and update the index |
| `otzaria://info[/app|library|folders|plugins|errors]` | Shows an info dialog; it does NOT return data to the caller (L459-485) |
| `otzaria://plugin/install?...`, `otzaria://plugin/install-local?path=...` | Plugin installs; security sensitive: NEVER generate these |

Window targeting: a queued link is delivered to the last active visible Otzaria window, otherwise the main window (`lib/app_scope/external_activation_service.dart` L140-167). Opening a book raises the window (`externalActionRaisesWindow`, `main_window_screen.dart` L1462-1466, `_bringWindowToFront` L1484-1496).

URL examples (a book with id 1938 read at line 140, a bundled Talmud PDF id 131 at page 150, a search):

```
otzaria://open/book/1938?index=140
otzaria://open/pdf/131?index=150
otzaria://open/search?q=%D7%A9%D7%9C%D7%95%D7%9D&mode=exact
otzaria://open/history
otzaria://open/bookmarks
```

Mapping from a stored entry to a URL (recommended widget logic):

```
source = book.source ?? (book.isUserBook ? "u" : "o");  suffix = source=="o" ? "" : source=="u" ? "source=user" : "source=db:" + slug
if isSearch:                     otzaria://open/search?q=<urlencode(book.title)>&mode=<searchMode if in {advanced,exact,fuzzy}>
elif book.type == "PdfBook" && book.id != null:   otzaria://open/pdf/{id}?[suffix&]index={max(index,1)}
elif book.type in {TextBook,DocxBook,EpubBook,DocumentBook} && book.id != null:  otzaria://open/book/{id}?[suffix&]index={max(index,0)}
else:                            not deep-linkable (offer "open Otzaria" = no argument)
```

### 5.3 Command line

`otzaria.exe [<otzaria-uri> | <file.otzplugin>]...` (`windows/runner/main.cpp` L379-401; Dart side `lib/main.dart` L1446-1470). The first argument as CLI sub-command is one of `pack-plugin`, `info`, `build-release-index` (`main.cpp` L222-243); these skip the single-instance check and are headless.
Machine-readable info without the GUI: `otzaria.exe info [app|library|folders|plugins|errors] [--limit=N] [--files=N] [--compact] [--out=<file.json>]` (`lib/core/info/app_info_cli.dart` L40-60). `info app` includes `dataRootPath` and `installType` (`portable`/`allUsers`/`perUser`); `info library` includes `path`, `databasePath`, `databaseExists`, `indexPath`, `totalBooks` (`lib/core/info/app_info_service.dart` L130-205). Use `--out=` (stdout is polluted by `P ATTACH` printed by a native plugin DLL). It starts the Flutter engine and is slow, so it is a fallback for path discovery only, not for polling. It also reads `Settings` through a snapshot, so it is correct when `key-databases-path` is set, but does not report databasesPath either.
Launching is ALSO safe while Otzaria is running: a second process never starts the UI (section 5.4).

### 5.4 Single-instance forwarding and the `external_activations` queue

1. `otzaria.exe` creates the named mutex `OtzariaAppSingleInstance` (`main.cpp` L22, L260-273; portable copies append `-<FNV-1a-64 of lowercased dataRoot, 16 hex digits>`, i.e. one instance per data root; same hash in Dart `AppPaths.singleInstanceKey`, `app_paths.dart` L140-150).
2. If the mutex already exists (second instance): it calls `AllowSetForegroundWindow` for the running process, then for every argument starting with `otzaria:` (case-insensitive) appends a record to the queue file, then exits with code 0 (`main.cpp` L374-401). If no URI was queued it just brings the running window to the front.
3. Queue file: `<dataRoot>\external_activations\pending_external_activations.jsonl`, UTF-8, one JSON object per line, appended with `FILE_APPEND_DATA` (`main.cpp` L277-322; `lib/core/external_activation_queue.dart` L18-32, L113-125):

```json
{"uri":"otzaria://open/book/1938?index=140","createdAt":"2026-11-13T09:12:00Z"}
```

   `createdAt` is informational (native writes UTC with `Z`, Dart writes local time without zone); `uri` is the only field read. Malformed lines are skipped.
4. The running instance watches the `external_activations` directory (`ExternalActivationService.start`, `external_activation_service.dart` L77-111), renames the file to `<file>.processing`, reads and deletes it, and routes each URI to the active window (`external_activation_queue.dart` L43-111).
5. A first (cold) start passes the URI on the command line to Dart, which enqueues it too and processes it once the window is ready (`lib/main.dart` L401, L1446-1470). The old location `<dataRoot>\pending_external_activations.jsonl` is still drained once at start (`external_activation_queue.dart` L127-134).

Two ways for the widget to open a link:

| Method | Works when | Notes |
|---|---|---|
| (A) Start `otzaria.exe "<uri>"` (via `Process.Start` with the exe path, `UseShellExecute=false` or true) | Otzaria running or not; any install mode incl. portable | Recommended. Cold start launches the app and opens the link; warm start forwards and exits. Quote the URI as ONE argument; do not add other args |
| (B) Start the `otzaria://...` URI through the shell (`Process.Start` with `UseShellExecute=true`, or `Launcher.LaunchUriAsync`) | Registry protocol key exists (section 5.5): installer-based installs, and any non-portable copy that has been started at least once | Windows runs `"<exe>" "<uri>"` from the registry. Not available for portable copies |
| (C) Append the JSON line to the queue file | Only if Otzaria is currently running (otherwise the file stays and fires at the next start, possibly surprising) | Not recommended: reimplementation with no benefit; if used, append a single line ending in `\n`, UTF-8 without BOM, share mode `Read|Write`, do not write any other field names, and never truncate/rename the file |

### 5.5 Protocol registration on Windows and how to find the exe

Registered keys for `otzaria:`:

1. Inno Setup installer `installer/otzaria.iss` L159-165 (skipped for portable installs: `Check: not IsPortableInstall`), `Root: HKA` = HKLM for an all-users install, HKCU for a per-user install (`PrivilegesRequired=lowest`, L69):
   - `Software\Classes\otzaria` default = `URL:Otzaria Protocol`; value `URL Protocol` = empty
   - `Software\Classes\otzaria\DefaultIcon` default = `{app}\otzaria.exe`
   - `Software\Classes\otzaria\shell\open\command` default = `"{app}\otzaria.exe" "%1"`
   - plus Office "trusted protocol" marker keys `HKCU\Software\Policies\Microsoft\Office\{12,14,15,16}.0\Common\Security\Trusted Protocols\All Applications\otzaria:` (L166-173).
2. The app itself, on EVERY non-portable launch (2 s after the main window is revealed; `lib/main.dart` L1297-1309; `lib/plugins/services/plugin_protocol_registration_service.dart` L31-40, L58-76, L396-423) rewrites the same four values under `HKCU\Software\Classes\otzaria` to point to the currently running `Platform.resolvedExecutable`, and also registers the `.otzplugin` file type. This means the HKCU key always points to the most recently started non-portable executable, even for admin installs (the installer's `RemoveUserProtocolKeyIfOurs` removes it only when it points to the installation being uninstalled, L1684-1695).
3. The Windows Store/MSIX package (`pubspec.yaml` `msix_config`, identity `F8854998.otzaria`, L5-24) declares no `protocol_activation` in the configuration; whether the Store build can be launched via `otzaria:` is NOT verified.

Verified on the dev machine (registry, read-only): `HKCU\Software\Classes\otzaria` exists with `(default)=URL:Otzaria Protocol`, `URL Protocol=""`, `DefaultIcon` = the dev build `...\build\windows\x64\runner\Release\otzaria.exe`, `shell\open\command` = `"<that exe>" "%1"`. `HKLM\Software\Classes\otzaria` missing; `HKCU\Software\Classes\zayit` missing (confirms zayit:// is not registered); no `App Paths\otzaria.exe` entry; no Inno uninstall entry for Otzaria itself (only a separate `OtzariaVoice` service); no Appx package; `%ProgramFiles%\Otzaria` and `%LOCALAPPDATA%\Programs\Otzaria` do not exist (the machine only has a dev build). So registry probing was verified only for the HKCU protocol key.

Where an external app can find `otzaria.exe` (try in this order; every candidate must `File.Exists`):

1. `exePath` in `<dataRoot>\otzaria_capabilities.json` (written by Otzaria itself at each start; works for portable too).
2. `HKCU\Software\Classes\otzaria\shell\open\command` default value, falling back to `HKLM\Software\Classes\otzaria\shell\open\command` (and `HKLM\...\WOW6432Node` for 32-bit views). Parse the first quoted token (`"C:\...\otzaria.exe" "%1"`). Equivalent: read `HKCR\otzaria\shell\open\command` through the merged view.
3. Inno uninstall key: `Software\Microsoft\Windows\CurrentVersion\Uninstall\{EEC4F712-CD05-4D15-A753-509E840A51A5}_is1` (`installer/otzaria.iss` L53, L228) under HKCU then HKLM (also `WOW6432Node`); read `InstallLocation` (Inno standard value) and append `otzaria.exe`. `UninstallDisplayIcon` is a second source.
4. Default install directories (`installer/otzaria.iss` L538-545: `{autopf}\Otzaria`, so `%ProgramFiles%\Otzaria` for all-users, `%LOCALAPPDATA%\Programs\Otzaria` for per-user), `...\otzaria.exe`; an older install may be anywhere (`FindPreviousInstallDir`).
5. Portable: `<folder>\otzaria.exe` next to `portable.marker` (a sibling `otzaria_launcher.exe` starts it, `windows/launcher/launcher.cpp` L24-25); only discoverable through the user choosing the folder.
6. Else: widget setting "Choose Otzaria.exe".

Treat the exe as unavailable if none of the above exists; then deep links cannot be fulfilled and the widget should show data only.

---

## 6. Open questions and uncertainties

1. Hive parser: layout verified on one real file; typeId `5..11` and adapter ids not exercised. The widget should only extract specific string keys and ignore everything else.
2. `key-databases-path` is absent on the dev machine, so the "custom databases folder" branch is verified from code only.
3. MSIX/Store distribution: no protocol activation found in `msix_config`; exe discovery for the Store build (WindowsApps path) is unverified.
4. `bookmarks` rows (`key-bookmarks`, `key-bookmark-groups`) do not exist in the dev data; their JSON shape comes from the code (same `Bookmark` serializer as history, which was verified on real rows). `BookmarkGroup.toJson` field list is from `bookmark_group.dart` read, no live row.
5. `personal_notes.book_id` is shown as the book title in the real rows; behaviour for user/attached books (the `personal_notes_user_book_keys` generation scheme) was not analysed.
6. Note `line_number` 1-based vs history `index` 0-based is inferred from comments plus schema, not from a UI test.
7. PDF page counts of the bundled Talmud PDFs are not stored in `seforim.db` (no `pages` column in the official `book` table); progress for PDFs needs the PDF file or only a page number.
8. Whether the first (cold) start of a portable copy via `otzaria.exe "<uri>"` raises the window the same way as the registry route: from code, yes (same argument path), not tested on a running instance.
9. History "freshness": entries record the last position at capture time (1.5 s debounce, flush on close). If Otzaria crashes the last few seconds are lost; the open book may not appear in history until closed (use `window_sessions` tabs for "currently open").
