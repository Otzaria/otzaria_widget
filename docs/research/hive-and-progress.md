# Otzaria `app_preferences.hive` and "שמור וזכור" - implementation spec for a read-only C# reader

Sources: Otzaria branch `dev` at `C:\Users\user\Documents\otzaria_project` (HEAD 46b4af531) and hive_ce 2.22.0 from
`C:\Users\user\AppData\Local\Pub\Cache\hosted\pub.dev\hive_ce-2.22.0` (cited below as `HIVE/...`, meaning `HIVE/lib/src/...`).
All claims were checked against the real file `%APPDATA%\otzaria\app_preferences.hive` using `tools/hive_dump.py`
(6608 bytes, 132 frames, 131 live keys, every CRC passed, clean tail). No sensitive values are reproduced here.

---------------------------------------------------------------------------------------------------

## 1. Hive binary format

### 1.1 Package and box

| Fact | Evidence |
|---|---|
| Package is `hive_ce` 2.22.0 (pubspec says `^2.22.0`, lock resolves to 2.22.0). Not the original `hive`. | `pubspec.yaml:46`, `pubspec.lock:868-875` |
| The box is opened as `Hive.openBox<dynamic>('app_preferences', path: <dataRoot>)`: a plain **non-lazy, untyped, unencrypted** box (no `encryptionCipher`, no `HiveAesCipher`, no adapters registered for it). File = `<dataRoot>/app_preferences.hive`, lock = `<dataRoot>/app_preferences.lock`. | `lib/data/data_providers/hive_data_provider.dart:63-76`; grep for `encryptionCipher|HiveAesCipher` in `lib/` is empty |
| `<dataRoot>`: Windows default `%APPDATA%\otzaria`. **Portable mode**: if a `portable.marker` file sits next to the exe, it is `<exeDir>\otzaria_data`. (macOS `~/Library/Application Support/otzaria`, Linux `~/.local/share/otzaria`.) | `lib/core/app_paths.dart:156-185`, `:35-41` |
| `Hive.init(dataRoot)` is also called globally (other boxes, all of which are now `*.hive.migrated` on this machine; history/bookmarks/tabs moved to SQLite `user_state.db`). | `lib/main.dart:1641` |

### 1.2 File layout

No file header, no footer, no magic number. The file is a bare concatenation of **frames**, appended in write order.

```
frame   := u32le frameLen            // total bytes of the frame INCLUDING these 4 bytes and the trailing crc (>= 8)
           key
           [ value ]                 // omitted entirely => tombstone (key deleted)
           u32le crc
key     := 0x00 u32le                // int key   (0 .. 0xFFFFFFFF)
         | 0x01 u8 n  n*utf8 bytes   // string key, n <= 255 BYTES
value   := typeId payload
typeId  := u8                        // if u8 == 21: the real id follows as u16le (used for ids >= 256, custom adapters)
```

* Little-endian everywhere. Key tags: `FrameKeyType.uintT=0`, `utf8StringT=1` (`HIVE/binary/frame.dart:108-116`; writer `binary_writer_impl.dart:217-228`, reader `binary_reader_impl.dart:224-234`).
* `app_preferences` only uses string keys (confirmed on the real file).
* Frame read algorithm: `binary_reader_impl.dart:263-336` (`readFrame`); frame write: `binary_writer_impl.dart:260-294`.
* A frame is a tombstone iff, after the key, there are no bytes left inside `frameLen - 4 (crc)` (`binary_reader_impl.dart:315-316`).

### 1.3 Checksum

* CRC32, **standard IEEE 802.3 / zlib CRC-32** (poly 0xEDB88320 reflected, init 0xFFFFFFFF, xorout 0xFFFFFFFF), table in `HIVE/crypto/crc32.dart:4-69`.
* Covers bytes `[frameStart, frameStart + frameLen - 4)`, i.e. the length prefix + key + value, **excluding** the 4 crc bytes. Stored little-endian in the last 4 bytes of the frame.
* Initial `crc` seed is 0 for unencrypted boxes (`crc: keyCrc ?? cipher?.calculateKeyCrc() ?? 0`, `binary_reader_impl.dart:283-288`). So Python `zlib.crc32(frame[:-4])` and .NET `System.IO.Hashing.Crc32.HashToUInt32` (or `Force.Crc32`) both match. Verified on all 132 real frames.
* (There is a fallback that recomputes the crc without the key crc only when a `keyCrc` is provided - irrelevant here.)

### 1.4 Value type ids (`HIVE/binary/frame.dart:121-190`)

All numbers little-endian. **"int" is stored as an IEEE-754 float64** (not int64!): writer `binary_writer_impl.dart:98-104` (`writeDouble(value.toDouble())`), reader `binary_reader_impl.dart:116-119` (`readDouble().toInt()`, truncation toward zero). Exact up to 2^53.

| id | Dart type | payload |
|---|---|---|
| 0 | null | (none) |
| 1 | int | f64 -> truncated to long |
| 2 | double | f64 |
| 3 | bool | u8 (`> 0` = true) |
| 4 | String | u32 byteLen + UTF-8 bytes |
| 5 | Uint8List | u32 len + raw bytes |
| 6 | List<int> | u32 count + count x f64 (truncate each) |
| 7 | List<double> | u32 count + count x f64 |
| 8 | List<bool> | u32 count + count x u8 |
| 9 | List<String> | u32 count + count x (u32 byteLen + UTF-8) |
| 10 | List<dynamic> | u32 count + count x full value (each with own typeId) |
| 11 | Map<dynamic,dynamic> | u32 count + count x (key value, value value), both with typeIds |
| 12 | HiveList | u32 count + u8 boxNameLen + boxName bytes + count x key (key encoding of 1.2) |
| 13 / 14 / 15 | Set<int> / Set<double> / Set<String> | same payload as 6 / 7 / 9 |
| 16 | DateTime (`DateTimeAdapter`) | **f64-encoded int**: ms since epoch (UTC instant) - `HIVE/adapters/date_time_adapter.dart:6-17` |
| 17 | BigInt | u8 strLen + ASCII decimal string (no u32 prefix) - `adapters/big_int_adapter.dart` |
| 18 | DateTime with TZ | f64 (ms + micros/1000) + u8 isUtc - `date_time_adapter.dart:31-50` |
| 19 | Set<dynamic> | like 10 |
| 20 | Duration | f64 ms (+ fractional micros) |
| 21 | **type-id extension** | followed by u16le = real type id |
| >= 22 | custom `TypeAdapter` / `HiveObject` | adapter-defined; the reader needs the adapter. **Not used** in `app_preferences` (every value in the real file is string/bool/int/double). A parser should throw on unknown ids and skip the frame's value (the frame length lets you skip it safely). |

Values written by Otzaria's settings layer are only: `String` (including large JSON blobs), `bool`, `int`, `double` (setObject, `hive_data_provider.dart:118-121`).

### 1.5 Semantics: last frame wins, tombstones, compaction, recovery

* The in-memory view is built by replaying frames in file order: `keystore.insert(frame)` -> a later frame for the same key replaces the earlier one; a tombstone removes the key (`HIVE/box/keystore.dart:149-180`, `binary/frame_helper.dart:10-36`). Order of keys in the map = insertion order, but Hive sorts keys with `defaultKeyComparator` (ints before strings, then ordinal) when listing - irrelevant to a reader.
* Every `put` appends one full new frame (the whole value, e.g. the whole `sz:progress_by_id` JSON string) - `put` never rewrites in place. File access: `readRaf = file.open()`, `writeRaf = file.open(mode: writeOnlyAppend)` (`HIVE/backend/vm/storage_backend_vm.dart:92-93`).
* **Compaction** (`storage_backend_vm.dart:216-285`): when `deletedEntries > 60 && deletedEntries/entries > 0.15` (`HIVE/box/default_compaction_strategy.dart`; Otzaria passes no custom strategy) Hive writes live frames, sorted by offset, to `app_preferences.hivec`, closes the box files, and **renames `.hivec` over `.hive`**. A reader may therefore briefly see the file missing/replaced: retry on `FileNotFoundException`/`IOException`.
* **Crash recovery** (`storage_backend_vm.dart:123-160`, `crashRecovery = true` default): on open, Hive parses; at the first truncated/bad-CRC frame it **truncates the file to that offset** (if offset 0 and file non-empty, throws "Wrong checksum"). A *read-only* reader must NOT truncate; just stop at the first bad frame and use what was parsed. Otzaria's own CLI does exactly this on a temp copy: `lib/core/info/settings_snapshot.dart:62-141` ("worst value is one setting that changed in the last fraction of a second").
* Locking: Hive takes an exclusive `lockRaf.lock()` on `app_preferences.lock` only (`storage_backend_vm.dart:118-121`); the `.hive` file itself can be read/copied concurrently (Python `shutil.copy` and Otzaria's own `source.copy` succeed). In C# open with `FileShare.ReadWrite | FileShare.Delete`, read all bytes, parse in memory. Copy-to-temp is optional but is what Otzaria does; reading directly into a `byte[]` is equivalent and safer for a tick-based widget.
* Change detection for a live widget: watch size + `LastWriteTime` of `app_preferences.hive` (FileSystemWatcher + short debounce, or poll). Because the file is append-only, one can also remember the last parsed offset and parse only new frames, but a compaction (file gets *smaller* or changes) must trigger a full re-parse.

### 1.6 How settings are written (it is not typed storage)

* Otzaria's `Settings` is its own copy of `flutter_settings_screens` (`lib/settings/engine/app_settings_store.dart`, MIT header lines 1-2). `Settings.setValue<T>(key, v)` -> `HiveCache.setObject` -> `box.put(key, v)` (`app_settings_store.dart:74-81`, `hive_data_provider.dart:118-121`); `null` removes the key (a tombstone frame). `getValue<T>` returns the default when the stored type differs from `T` (`hive_data_provider.dart:152-158`, `app_settings_store.dart:65-71`).
* `SettingsRepository` (`lib/settings/engine/settings_repository.dart`, key constants lines 14-241, `allKeys` 248-362, defaults 378-675 and 1222-1310) wraps it; defaults are written once at first run (`settings_initialized` = true).
* **Fallback store**: if `HiveCache` init fails, `main.dart:690-702` switches to `SharePreferenceCache` -> `%APPDATA%\otzaria\shared_preferences.json` with `flutter.`-prefixed keys. On this machine that file exists but is a stale 268-byte relic (Apr 2026, only window bounds + 2 flags). Treat Hive as the only source; fall back to this JSON only if the `.hive` file is missing/unparseable.
* Other consumers read the same data the same way (backup: `lib/settings/services/backup_service.dart:353-372`).

### 1.7 Reference parser (C# pseudo-code)

```csharp
record Entry(string Type, object? Value);
Dictionary<string, Entry> Parse(byte[] b) {
  var map = new Dictionary<string, Entry>(); int pos = 0;
  while (b.Length - pos >= 4) {
    uint len = BitConverter.ToUInt32(b, pos);
    if (len < 8 || pos + len > b.Length) break;                       // torn tail: stop, don't truncate
    uint crc = BitConverter.ToUInt32(b, pos + (int)len - 4);
    if (Crc32.HashToUInt32(b.AsSpan(pos, (int)len - 4)) != crc) break; // bad frame: stop (Hive would truncate here)
    var r = new Rd(b, pos + 4, pos + (int)len - 4);                    // limit excludes crc
    object key = r.Key();                                              // 0x00 u32 | 0x01 u8 n + utf8
    if (r.Pos == r.Limit) map.Remove(key.ToString()!);                 // tombstone
    else map[key.ToString()!] = r.Value();                             // last wins
    pos += (int)len;
  }
  return map;
}
// Rd.TypeId(): t = u8; return t == 21 ? u16 : t.   Rd.Int(): (long)BitConverter.ToDouble(...)  // truncation
// DateTime(16): DateTimeOffset.FromUnixTimeMilliseconds(Int()) ; DateTimeTZ(18): double ms + bool isUtc.
```

`tools/hive_dump.py` is a working reference (also round-trip tested on a synthetic file covering every type, an extension type id, an overriding frame, a tombstone and a torn tail).

---------------------------------------------------------------------------------------------------

## 2. "שמור וזכור" (Shamor Vezachor) - ProgressService

Code: `lib/tools/shamor_zachor/` - `services/progress_service.dart`, `providers/shamor_zachor_progress_provider.dart`, `providers/shamor_zachor_data_provider.dart`, `services/shamor_zachor_bootstrap_worker.dart`, `models/{progress_model,book_model}.dart`, UI in `screens/` and `widgets/`.

### 2.1 Keys (all values are Hive **String** containing JSON, except the bool markers)

| Key | Hive type | JSON shape | Written by |
|---|---|---|---|
| `sz:tracked_books` | string | `[6834, 120, ...]` JSON array of ints (book ids the user added to tracking via the "+" button). Union semantics on import. | `shamor_zachor_data_provider.dart:46,725-767` |
| `sz:progress_by_id` | string | `{ "<bookId>": { "<itemIndex>": { "<columnId>": true, ... }, ... }, ... }` - only checked columns are stored (value always `true`); an item with no checks is removed; a book with no items is removed. | `progress_service.dart:16,70-143`, `progress_model.dart:48-75` |
| `sz:book_columns` | string | `{ "<bookId>": [ {"id":"learn","label":"לימוד"}, ... ] }` - present only for books whose columns were customised (renamed/added/removed). Absent => default columns. | `progress_service.dart:22-68` |
| `sz:completion_dates_by_id` | string | `{ "<bookId>": "<ISO-8601 local datetime>" }`, e.g. `2026-11-09T11:03:09.635953` (no zone suffix; the typedef comment saying "Hebrew" is wrong, the code writes `DateTime.now().toIso8601String()`). Set when a book first becomes fully complete (all columns x all items). | `progress_service.dart:17-18,145-209`, `progress_provider.dart:309-334` |
| `sz:column_completion_dates` | string | `{ "<bookId>": { "<columnId>": "<ISO-8601>" } }` - when a whole column went from partial to complete; removed when it stops being complete. | `progress_service.dart:19-20,211-250`, `progress_provider.dart:222-240` |
| `sz:last_accessed` | string | ISO-8601 timestamp of the last progress save (write-only, not read anywhere). | `progress_service.dart:21,314-324` |
| `sz:sidebar_visible` | bool | UI state only. | `screens/shamor_zachor_main_screen.dart:60,72,169` |
| `sz:migration_completed` | bool | Legacy marker; no reader/writer left in `lib/` (present in the real file). Ignore. | - |

Notes:
* The key prefix `sz:` is excluded from "settings" backup and handled as its own section (`backup_service.dart:414`, merge rules `backup_import_merge.dart:98-140`).
* **State of the real file**: only `sz:tracked_books` (one id) and `sz:migration_completed` exist; no `sz:progress_by_id` yet (the user has not ticked anything). JSON shapes above are therefore derived from code, with no real sample. Treat a missing key as "empty".
* **That one tracked id (6834) does not exist in the current `seforim.db`** (ids there are sparse, 1..8454, 8154 rows). IDs are database-specific; a rebuilt/updated library can renumber books. Otzaria silently shows nothing for an unknown id. The widget must skip ids it cannot find in `book`.

### 2.2 What an "id" is, and what an "item" is

* `bookId` = `book.id` in **seforim.db** (the official library DB; `_findOfficialBook` explicitly says ids from `user_books.db` would be wrong - `shamor_zachor_data_provider.dart:648-651`). Keys in the JSON objects are the decimal string of that integer.
* `itemIndex` ("absoluteIndex") = zero-based index of a **learnable item** in the book's item list (see 2.4), as a decimal string. It is *positional*, not a TOC id.
* `columnId`: default columns `learn`, `review1`, `review2`, `review3` (Hebrew labels `לימוד`, `חזרה 1`, `חזרה 2`, `חזרה 3`) - `models/progress_model.dart:37-42`. A user can rename (id kept), add (id `col_<DateTime.now().microsecondsSinceEpoch>`, max **8** columns total, `maxColumns`, `progress_provider.dart:52`), or remove columns (at least 1 must remain, `:88-91`). Column order = list order; the "cycle number" of a column is its 1-based position (`:336-349`).
* Effective columns of a book = `sz:book_columns[bookId]` if present else the 4 defaults (`progress_provider.dart:55-56`).

### 2.3 The denominator: it comes from seforim.db (no bundled asset)

There is **no asset list** - `ShamorZachorConfig.assetsBasePath` is vestigial. The set of books and per-book item counts are computed at runtime from `seforim.db` (tables `book`, `category`, `tocEntry`, `tocText`, `line`). The production path is `ShamorZachorBootstrapWorker._loadCategoryTreeInWorker` (`services/shamor_zachor_bootstrap_worker.dart:25-310`), which opens the DB `OpenMode.readOnly` + `PRAGMA query_only=ON`; the data provider has an equivalent in-isolate fallback (`shamor_zachor_data_provider.dart:163-387, 427-497`).

Item list per book (`BookDetails.learnableItems`, `models/book_model.dart:155-203, 227-265`):

1. Load TOC rows for the book (worker `:196-220`):
   ```sql
   SELECT t.*, tt.text, COALESCE(l.lineIndex, t.lineId) AS lineIndex
   FROM tocEntry t JOIN tocText tt ON t.textId = tt.id LEFT JOIN line l ON t.lineId = l.id
   WHERE t.bookId = :id
   ORDER BY COALESCE(l.lineIndex, t.lineId) ASC, CASE WHEN t.id < 0 THEN -t.id ELSE t.id END ASC
   ```
2. If there is at least one TOC row ("sections" present): build the tree by `parentId` (roots = `parentId IS NULL`; children and roots sorted by `lineIndex`), then **DFS in order; every LEAF (entry with no children) is one learnable item**; `absoluteIndex` = running counter 0,1,2... over leaves (`_buildLearnableItemsFromSections`). Non-leaf entries are not items.
   Verified on the real DB: Bereshit (book id 1) = root + 50 `פרק` rows -> **50 items**; Berachot (id 103) = root + 125 `דף X.`/`דף X:` rows -> **125 items** (one per amud); Shabbat (id 104) -> 312 items.
3. If the book has **no TOC rows**: fallback to the "ראשי" part `start=1, end=max(totalLines,1)`: one item per line index 1..N (`totalLines` from `book.totalLines`); if the book's inherited contentType is `דף` (category title containing `בבלי` or `ירושלמי`) there are **2 items per line** (amud `a` and `b`). Rare; mirror it for fidelity.
4. `totalLearnableItems = learnableItems.length` = denominator for everything.

Which books Otzaria lists: books with `book.isBaseBook = 1` (386 in the real DB) plus ids in `sz:tracked_books` (`worker :40-44`). Progress for other ids can exist in `sz:progress_by_id` (data kept) but those books are not in the tree (the grid filter works on the tree) - for the widget, decide explicitly: show every id in progress/tracked that resolves in `book`.

Category/title shown: `book.title`; "category" shown on the card = first element of the category path = the **top-level** `category.title` (e.g. תלמוד בבלי, תנ"ך) - `worker :192`, `data_provider.dart:370-374`. Category tree = `category(id,parentId,title,level,orderIndex)`.

Tie-ordering caveat: leaf order depends on a Dart `List.sort` of siblings by `lineIndex` (not guaranteed stable) over rows that SQL already ordered by `(lineIndex, abs(id))`. If two sibling TOC rows share a `lineIndex`, positional ids could in principle differ from a stable sort. The widget only needs **counts**, which are order-independent (see 2.5), so this only matters if you ever map an index back to a section title.

### 2.4 How the numbers are computed (and shown)

Definitions, with `N = totalLearnableItems`, `P = sz:progress_by_id[bookId]` (map itemIndex -> {col:true}), `cols = columns(bookId)`:

* `count(c) = number of entries in P whose value has c == true` (`ProgressService.getColumnCompletedCount`, `progress_service.dart:305-312` - counts **all** map entries, even indexes >= N).
* Column percentage `pct(c) = count(c) / N` (0 if N == 0 or no progress) - `progress_provider.dart:582-596`.
* **Book card rings** (what the user sees): `book_card_widget.dart:100-120` recomputes per column by iterating the actual items `0..N-1` (so stale indexes are ignored): `ring(c) = checkedItems(c)/N`. Rings shown: column i is visible if `i == 0 || ring[i] > 0 || ring[i-1] >= 1.0` (`:296-308`); each ring is labelled with the column's label; below it the current (last visible < 100%) cycle is shown as `"<round(ring*N)> מתוך <N>"` (`:315-322`). Headline "learn progress" = `pct(first column)` clamped to [0,1], shown as `"<round(100*x)>%"` (`:128-133, 350-370`).
* `bookCompleted` = `N > 0` and for **every** column `count(c) >= N` (`progress_provider.dart:606-620`).
* `inProgress` (card status / filter) = P non-empty, has a non-empty item, and not completed (`:638-645`).
* The default list tab shows "בתהליך" books = `(tracked || inProgress) && !completed`; tab "הושלם" = completed; tab "הכל" = everything (`widgets/category_books_grid.dart:300-340`; segment labels `screens/shamor_zachor_main_screen.dart:231-258`, default filter `in_progress` at `:57`). Screen titles: `שמור וזכור`, `שמור וזכור - <category>`, `שמור וזכור - <book>` (`:185-189`). Empty-state strings: `אין ספרים בתהליך`, `אין ספרים שהושלמו`.
* Status text by completion ratio (`progress_model.dart:169-206`): 0 `עדיין לא התחלת!`; <0.30 `התחלה מצוינת!`; <0.50 `שליש הדרך כבר הושלם!`; <0.60 `חצי הדרך מאחוריך!`; <0.75 `רוב הדרך כבר מאחוריך!`; <1 `הסוף כבר באופק!`; 1.0: `סיימת מחזור ראשון/שני/שלישי/רביעי בהצלחה!` by cycle number.
* "Active review" (`progress_service.dart:277-290`): `cols.Count > 1`, at least one column fully done (`count >= N`) and at least one other column partially done (`0 < count < N`).

Recommended widget pseudo-code (mirrors the UI, robust to stale indexes):

```text
load tracked  = parse_json_list(hive["sz:tracked_books"])            // [] if missing
load progress = parse_json_obj (hive["sz:progress_by_id"])           // {} if missing
load columnsOverride = parse_json_obj(hive["sz:book_columns"])       // {} if missing
ids = tracked ∪ keys(progress)  (ints)  ∩ {book.id in seforim.db}

for id in ids:
  N      = leaf_count(id)                         // 2.3; cache per (db mtime, id)
  cols   = columnsOverride[id] ?? [learn,review1,review2,review3 with Hebrew labels]
  P      = progress[id] ?? {}
  for c in cols:  done[c] = count of idx in P where 0 <= int(idx) < N and P[idx][c.id] == true
                  ring[c] = N == 0 ? 0 : done[c] / N
  headline = clamp(ring[cols[0]], 0, 1)
  completed  = N > 0 and all(done[c] >= N for c in cols)
  inProgress = !completed and (id in tracked or P has any non-empty item)
  show visible rings: i==0 or ring[i]>0 or ring[i-1]>=1
  completionDate = completion_dates_by_id[id]; colDate[c] = column_completion_dates[id][c.id]
```

`leaf_count(id)` pseudo-SQL/code (validated against the real DB):

```text
rows = SELECT t.id, t.parentId FROM tocEntry t WHERE t.bookId = :id
if rows is empty:
    totalLines = SELECT totalLines FROM book WHERE id = :id
    n = max(totalLines, 1);  if categoryAncestors(id) title contains 'בבלי' or 'ירושלמי': n *= 2
    return n
parents = { parentId of rows where parentId not null };  return count(rows where id not in parents)   // leaves
```
(Equivalent to the DFS over the tree built from roots; rows whose `parentId` points outside the book would be unreachable in Otzaria - none exist in the checked books, but guard with a reachability walk if exactness matters.)

Cost/perf note: `seforim.db` is 4.6 GB; query only needed ids with indexed `bookId` lookups, open `mode=ro` (and `immutable=0`, WAL aware) and never copy it.

### 2.5 Caveats for fidelity

* Otzaria's headline `pct(first column)` counts *all* entries (including stale indexes >= N, clamped at 1.0) while rings iterate valid indexes only. Prefer the valid-index count.
* Progress is positional: if the DB is rebuilt and a book's TOC changes, Otzaria itself shows shifted marks. Nothing to do.
* Cross-process writes: while Otzaria runs, a settings write appends a whole new `sz:progress_by_id` frame, so a reader can see the previous version for a moment - fine.

---------------------------------------------------------------------------------------------------

## 3. Locating seforim.db

* `library_database_path.txt` and `library_path.txt` in `<dataRoot>` (written for the Inno uninstaller, UTF-8 **with BOM**; `app_paths.dart:480-493`, names `:43,:47`). Strip the `\ufeff`.
* Authoritative (what Otzaria uses): `dbPath = join(hive["key-library-path"], hive["key-library-folder-name"] or "", "seforim.db")` (`lib/data/constants/database_constants.dart:259-264`, `getDatabasePath` `:~90-104`). If `hive["key-db-effective-path"]` is a non-empty string it wins (written on Android, but `getDatabasePath` honours it everywhere).
* In the real file `key-library-path` is set (a folder holding `seforim.db`, `lexical.db`, ...), `key-library-folder-name` and `key-db-effective-path` are empty strings. The two `.txt` files hold the same location; use them as fallback.
* Other path keys: `key-databases-path` (personal DBs, `app_paths.getDatabasesPath()` `:~685-700`), `key-index-path`, `key-backup-path`, `key-custom-folders` (JSON), `key-attached-libraries` / `-folders` (JSON). Absent from the real file unless noted below.

---------------------------------------------------------------------------------------------------

## 4. Other useful keys in `app_preferences` (code defaults vs. real file)

All 131 keys present in the real file (names + Hive types), grouped. "-" = not present in the real file.

### 4.1 Theme, font, language

| Key | Type | Default (code) | Notes |
|---|---|---|---|
| `key-dark-mode` | bool | false | `settings_repository.dart:15,380` |
| `key-follow-system-theme` | bool | false | - in real file => default false |
| `key-swatch-color` | **int ARGB32 in current code**; real file holds a **string `"#aarrggbb"`** (written by an older Otzaria) | light seed `0xFF2C1B02` (darkBrown, `theme/app_seed_colors.dart:10,29`) | `getValue<int>` on a String returns the default in Otzaria, so the *effective* colour in the app is the default. A widget should accept both int and `#aarrggbb`. |
| `key-dark-swatch-color` | same as above | dark seed `0xFF9C27B0` (purple, `app_seed_colors.dart:11,22`) | real file: string `#ffce93d8` style |
| `key-font-family` | string | `FrankRuhlCLM` (`theme/app_fonts.dart:26`) | real file: present |
| `key-font-size` | double | 25 | present (25.0) |
| `key-commentators-font-family` / `-size` | string / double | `NotoRashiHebrew` / 22 | present |
| `key-line-height`, `key-font-bold`, `key-commentators-font-bold` | double / bool / bool | 1.5 / false / false | - |
| `key-settings-language` | string | `kDefaultSettingsLanguageCode` = system | - ; affects only the settings screen language (`settings/l10n/settings_language.dart:57`) |
| `key-text-max-width` (real file) / `key-text-column-width` (code) | double | 0 | legacy name present |

### 4.2 Calendar, zmanim, location (all in `lib/tools/calendar/`)

| Key | Type | Default | Meaning |
|---|---|---|---|
| `key-selected-city` | string | `ירושלים` (`settings_repository.dart:502-505,1262`) | **Only the Hebrew city name is stored** (no lat/long/elevation/tz in prefs). It is the key into the bundled table `cityCoordinates` (`tools/calendar/models/calendar_location.dart:2-...`, 129 cities): `{country: {city: {lat, lng, elevation, timezone}}}`, e.g. `'ירושלים'` -> lat/lng/elevation + IANA `Asia/Jerusalem`. Real file: present. |
| (derived) Israel vs diaspora | - | - | `isCityInIsrael(city)` = city is under the top-level country key `'ארץ ישראל'` of that table (`calendar_location.dart:~940`). There is no setting; it is derived from the city (`calendar_cubit.dart:168,1583`). Affects YomTov/second-day logic and Daf/parsha for Israel. |
| (derived) candle lighting minutes | - | 30 | `candleLightingMinutesByCity[city] ?? candleLightingMinutesByCountry[country] ?? 30` (`calendar_location.dart:~870-930`); Jerusalem 40, Tel Aviv 18, etc. |
| `key-calendar-type` | string | `combined` | `hebrew` \| `gregorian` \| `combined` (`calendar_cubit.dart:1597-1607`) - real file `combined` |
| `key-calendar-day-transition` | string | `sunset` (code reads default `sunset`; first-run writer sets `sunset`) | `sunset` \| `tzais` \| `rabbeinuTam` \| `midnight` (`calendar_cubit.dart:1658-1685`) - real file `midnight` |
| `key-calendar-twelve-hour-clock` | bool | false | - in real file |
| `key-calendar-enabled-zmanim` | string (JSON array of zman ids) | `''` = use registry defaults `kDefaultEnabledZmanim` (`helpers/zmanim_helpers.dart:1359-1363`, parse `calendar_cubit.dart:427-436`) | - in real file => defaults. Default set (31 ids): `alos72Degrees, alos90Degrees, misheyakir10point2, sunrise, sofZmanShmaMGA90Degrees, sofZmanShmaGRA, sofZmanTfilaMGA90Degrees, sofZmanTfilaGRA, chatzos, chatzosLayla, minchaGedolaGreater30, minchaKetanaGRA, plagGRA, sunset, seaLevelSunset, tzeitTikochinsky, tzeitItimLeVina, rt72Shavos, candleLighting, motzeiShabbos, motzeiShabbosChazonIsh, motzeiYomTov, motzeiYomTovChazonIsh, achilasChametzMGADegrees, achilasChametzGRA, biurChametzMGADegrees, biurChametzGRA, fastEndTikLenient, fastEndItimLenient, fastEndTikStringent, fastEndItimStringent`. Full registry (~100 ids with Hebrew titles and kosher_dart formulas) is `kZmanimRegistry` in `zmanim_helpers.dart`. |
| `key-calendar-events` | string (JSON array) | `[]` | user events; real: `[]` |
| `key-calendar-zman-alerts` | string (JSON object) | `{}` | per-zman alert prefs |
| `key-calendar-notifications-enabled / -time / -sound` | bool / int (minutes) / bool | true / (see `calendar_alerts_store.dart`) / - | real: time = 60 |
| `key-google-calendar-*`, `key-calendar-ics-subscriptions` | mixed | - | **contain credentials/secrets (`client-secret`, `credentials-json`) - never read or log them** |
| Daf Yomi | - | - | **No setting.** Computed from the date: `YomiCalculator.getDafYomiBavli(JewishCalendar)` from `kosher_dart ^2.0.20` (`calendar_date_helpers.dart:59-61`). The widget must reimplement/port (e.g. KosherZmanim .NET / Hebcal). Likewise no zmanim "opinion" selector exists beyond the enabled-id list above (each zman id already encodes its opinion). |
| Zmanim computation inputs | - | - | `GeoLocation(lat,lng,elevation>0?elevation:0, IANA tz)` + `ComplexZmanimCalendar` (`zmanim_helpers.dart:36-65`). Elevation is passed to the calendar; "seaLevelSunset" is separate. |

### 4.3 Library, paths, misc

`key-library-path` (string; `app_paths.dart:496-621`), `key-library-folder-name` (string, "" = DB directly under library path), `key-db-effective-path` (string, ""), `key-library-view-mode` (`grid`), `key-library-show-preview` (bool), `key-show-otzar-hachochma/-hebrew-books/-external-books` (bool), `key-talmud-bavli-open-format` (string), `key-auto-index-update`, `key-auto-sync`, `key-software-and-book-updates-enabled`, `key-last-library-update-check` (ISO string), `key-last-software-update-check`, window state `window_bounds_{left,top,width,height,dpr}` (double), `window_is_maximized` (bool), `page_shape_*` (layout; many keys carry a book-title suffix), `tour_status`, `live_tips_*`, `update_dialog_shown_<ver>` (bool), `key-app-*` (version/timeline strings).

### 4.4 Complete key inventory of the real file (names grouped, all "string" unless a type is shown)

`ad_popup_dont_show_again`(bool), `ad_popup_last_campaign_show`, `key-align-tabs-to-right`(bool), `key-app-installed-at`, `key-app-installed-at-source`, `key-app-previous-version`, `key-app-tracked-version`, `key-app-updated-at`, `key-auto-index-update`(bool), `key-auto-sync`(bool), `key-auto-sync-catalogs`(bool), `key-bundled-plugins-upgrade-checked-version`, `key-calendar-day-transition`, `key-calendar-event-notification-ids`, `key-calendar-events`, `key-calendar-notification-sound`(bool), `key-calendar-notification-time`(int), `key-calendar-notifications-enabled`(bool), `key-calendar-type`, `key-calendar-zman-alerts`, `key-commentary-pane-tooltip-first-shown`, `key-commentary-pane-width`(double), `key-commentators-font-family`, `key-commentators-font-size`(double), `key-compact-menu-mode`(bool), `key-copy-header-format`, `key-copy-with-headers`, `key-custom-folders-refresh-signature`, `key-dark-mode`(bool), `key-dark-swatch-color`, `key-db-effective-path`, `key-default-nikud`(bool), `key-default-sidebar-open`(bool), `key-dev-channel`(bool), `key-enable-per-book-settings`(bool), `key-error-report-sender-email`(personal), `key-external-results-first`(bool), `key-facet-filtering-width`(double), `key-find-ref-examples-offset`(int), `key-find-ref-recent-queries`, `key-font-family`, `key-font-size`(double), `key-google-calendar-{client-id, client-secret, credentials-json, selected-ids}` (secrets), `key-google-calendar-enabled`(bool), `-last-sync`(int), `-sync-future-days`(int), `-sync-past-days`(int), `key-is-fullscreen`(bool), `key-last-auto-backup`, `key-last-library-update-check`, `key-last-search-mode`, `key-last-search-typing`, `key-last-search-typo-tolerance`(bool), `key-last-software-update-check`, `key-library-folder-name`, `key-library-path`, `key-library-show-preview`(bool), `key-library-view-mode`, `key-line-height`(double), `key-pdf-book-view-by-default`(bool), `key-personal-notes-collapsed`(bool), `key-pin-sidebar`(bool), `key-plugin-search-selections`, `key-protected-mode-enabled`(bool), `key-queue-error-reports-when-offline`(bool), `key-remove-nikud-tanach`(bool), `key-replace-holy-names`(bool), `key-search-all-categories-enabled`(bool), `key-search-dimension-facets`, `key-search-feedback-consent`, `key-search-feedback-consent-version`(int), `key-search-manual-category-facets`, `key-seeded-bundled-plugins`, `key-selected-city`, `key-semantic-data-enabled`(bool), `key-semantic-download-paused`(bool), `key-show-external-books`(bool), `key-show-hebrew-books`(bool), `key-show-otzar-hachochma`(bool), `key-show-teamim`(bool), `key-sidebar-tab-index-combined`(int), `key-sidebar-width`(double), `key-software-and-book-updates-enabled`(bool), `key-splited-view`(bool), `key-swatch-color`, `key-talmud-bavli-open-format`, `key-text-max-width`(double), `last_seen_app_version`, `live_tips_launch_count`(int), `live_tips_resolved`, `page_shape_book_<title>` (string, several), `page_shape_bottom_font`, `page_shape_bottom_height`(double), `page_shape_bottom_left_width`(double), `page_shape_global_highlight`(bool), `page_shape_global_visibility_{bottom,bottomRight,left,right}`(bool), `page_shape_left_sidebar_width`/`left_width`/`right_width`(double), `page_shape_use_book_settings_<title>`/`page_shape_view_mode_<title>`/`page_shape_visibility_*_<title>`(bool), `settings_initialized`(bool), `sz:migration_completed`(bool), `sz:tracked_books`, `tour_status`, `update_dialog_shown_<version>`(bool, several), `window_bounds_{dpr,height,left,top,width}`(double), `window_is_maximized`(bool).

Keys declared in code but absent from the real file (so the code default applies): `key-follow-system-theme`, `key-calendar-twelve-hour-clock`, `key-calendar-enabled-zmanim`, `key-settings-language`, `key-databases-path`, `key-index-path`, `key-custom-folders`, `key-attached-libraries`, and all `sz:progress_by_id / book_columns / completion_dates_by_id / column_completion_dates`. Always `getOrDefault`.

---------------------------------------------------------------------------------------------------

## 5. Risks and recommendations for the widget

1. Parse defensively: stop at first bad frame, never truncate/write, retry on file replace (compaction), open with `FileShare.ReadWrite|Delete`.
2. Do not trust types: a key may hold a different type than the code expects (e.g. colours as `#aarrggbb` strings, ints stored as doubles in files written by other versions). Coerce.
3. Resolve ids against the live `seforim.db`; drop unknown ids. Cache per-book leaf counts keyed by (db path, db mtime or `contentHash`).
4. Security/privacy: the box holds secrets (Google client secret/credentials, error-report e-mail, protected-mode password hash). Whitelist the keys the widget reads and never log the rest; `hive_dump.py` redacts keys matching secret/token/password/credential/email/hash/path in its default preview.
5. Version drift: the format is stable across hive/hive_ce (ids 0-15 identical; 16/17 same; hive_ce adds 18-21). The `sz:*` JSON and some value types evolve (code comments show `PageProgress` kept "backward compatible" legacy columns learn/review1-3 and is now a dynamic map; the real file shows colours as strings while current code writes ints), so keep the reader tolerant to unknown fields and types.
