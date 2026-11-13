# Otzaria Widget — Architecture & Work Plan

An independent Windows 11 Widgets Board provider that **reads** Otzaria's local data
(never writes it) and shows: recent books, bookmarks, Shamor Vezachor progress,
zmanim, Hebrew calendar, and a combined dashboard. Clicking an item opens Otzaria via
`otzaria://` deep links. Ships as its own MSIX package. No code changes in Otzaria.

Research (the contract for data formats — read the parts relevant to you):
- `docs/research/storage-and-links.md` — data paths, user_state.db JSON shapes, seforim.db, deep links, finding otzaria.exe
- `docs/research/hive-and-progress.md` — Hive binary format, app_preferences keys, Shamor Vezachor math
- `docs/research/calendar-and-zmanim.md` — zmanim list/methods, cities, Hebrew calendar
- `docs/research/widget-platform.md` — COM provider, manifest, packaging, Adaptive Cards limits (VERIFIED spike in `spikes/WidgetSpike`)

## Solution layout (`OtzariaWidget.sln`, .NET 10)

| Project | TFM | Purpose | Owner |
|---|---|---|---|
| `src/OtzariaWidget.Core` | net10.0 | Contracts (`Contracts/*` — lead only), data readers, settings store, launcher, data service | Agents A + B |
| `src/OtzariaWidget.Calendar` | net10.0 | `ICalendarService`: Hebrew date, parasha, holidays, daf yomi, omer, zmanim, cities | Agent C |
| `src/OtzariaWidget.App` | net10.0-windows10.0.19041.0, WinExe, x64, self-contained .NET, framework-dependent WinAppSDK 1.8 | One exe, two modes: COM widget provider (`-RegisterProcessAsComServer`) / WPF settings window | Agent D (`Provider/`, `Templates/`), Agent E (`SettingsUi/`) |
| `tests/OtzariaWidget.Core.Tests` | net10.0 xunit | | A + B |
| `tests/OtzariaWidget.Calendar.Tests` | net10.0 xunit | | C |
| `tests/OtzariaWidget.App.Tests` | net10.0-windows xunit | card template expansion tests (AdaptiveCards.Templating) | D |
| `packaging/` | | `Package.appxmanifest`, assets, `build.ps1`, `install-dev.ps1`, `uninstall.ps1`, `make-msix.ps1` | D |

### Folder ownership inside Core (to avoid edit conflicts)
- Agent A: `Core/Paths/`, `Core/UserState/`, `Core/Library/`, `Core/Launch/`, `Core/Settings/`, `Core/Services/` (data service + file watcher), `Core/Json/` (shared JSON helpers if needed)
- Agent B: `Core/Hive/`, `Core/Preferences/`, `Core/Progress/`
- `Core/Contracts/` — lead only. If you need a contract change, make the smallest additive change and report it.
- Tests: each agent its own subfolder in the test project (`Paths/`, `UserState/`, `Hive/` ...). Do not edit the test csproj except to add PackageReferences you need (tell the lead).

## Hard rules (all agents)
1. **Read-only toward Otzaria.** SQLite: `Mode=ReadOnly` connection string (`Data Source=...;Mode=ReadOnly;Cache=Shared` is fine; NO `immutable=1`), short-lived connections, `Pooling=False` so files aren't held. Hive: open with `FileShare.ReadWrite | FileShare.Delete`, parse in memory, never write; retry on IOException (compaction rename).
2. **Privacy:** Hive whitelist only (sz:*, key-selected-city, key-calendar-*, key-dark-mode, key-swatch-color, key-dark-swatch-color, key-library-path, key-library-folder-name, key-databases-path, key-calendar-twelve-hour-clock). Never read/log google-calendar, email, password, credentials keys. Logs contain no personal content (no book titles in logs is NOT required, but no notes text, no search queries).
3. **Defensive parsing:** unknown fields ignored; a single broken entry is skipped, never fails the whole list; unknown schema → `DataHealth.UnknownSchema` with a Hebrew message, no exception to callers.
4. **Culture:** this machine's culture is he-IL with the Hebrew calendar. Always use `CultureInfo.InvariantCulture` for parsing/formatting machine data; format Gregorian dates explicitly with `GregorianCalendar`.
5. **Hebrew UI text** everywhere user-visible. RTL.
6. Every component has xunit tests. Tests use synthetic fixtures generated in code/temp; additionally a `[Fact]` "real data smoke test" that runs against the real `%APPDATA%\otzaria` files if present (skip silently otherwise) and asserts sane output.
7. `dotnet build OtzariaWidget.sln` must have 0 errors and you must not introduce warnings in files you own. Run your tests.
8. Don't commit to git (the lead commits).

## Widget catalog (Definitions in the manifest)

| Id | Hebrew name | Sizes | Content | Per-widget customization (CustomState) |
|---|---|---|---|---|
| `RecentBooks` | אוצריא · ספרים אחרונים | S/M/L | "continue reading" (current tab), recent books with location ref; optional recent searches | item count, show searches, show location text, include PDFs |
| `Bookmarks` | אוצריא · סימניות | M/L | bookmarks (label/ref), optional groups | group filter (all / a group / ungrouped), count |
| `ShamorZachor` | שמור וזכור | S/M/L | books in progress with progress bars (per visible column), status text, completion | filter (in progress / completed / all), count, show all columns |
| `Zmanim` | זמני היום | S/M/L | city, Hebrew date header, list of zmanim with the *next* one highlighted, past ones dimmed | city (follow Otzaria/global or specific), zmanim set (follow / custom checklist) |
| `HebrewCalendar` | לוח עברי | S/M/L | Hebrew + Gregorian date, day of week, parasha, holidays, daf yomi, omer, candle lighting/havdalah on Fri/Sat | show daf yomi, show yerushalmi, show omer, show candle lighting |
| `Dashboard` | אוצריא · לוח אישי | M/L | user-selected sections: date line, next zman, continue reading, top progress, bookmarks | sections checklist + order |

Small size = one-glance info (one touch target). Medium 2–3 items, Large 4–8.
All cards: Adaptive Cards **1.6** max, `rtl: true`, theme via `$host.hostTheme`, no ProgressBar element (build bars with ColumnSet weights). A footer/empty state when data is missing (“לא נמצאו נתוני אוצריא — פתח את ההגדרות”) with a button that opens the settings app.

## Runtime flow (provider)
`ProviderHost` (COM, from spike) → `WidgetProvider` (IWidgetProvider, IWidgetProvider2) → per-widget `IWidgetHandler` (one class per kind) → `CardBuilder` produces template + data JSON.
Data: one `IOtzariaDataService` (Core) — caches snapshot, FileSystemWatcher on user_state.db(-wal), app_preferences.hive, settings.json; debounced `Changed` → provider pushes updates to **active** widgets only. Calendar widgets also schedule a timer to the next zman / day boundary while active.
Links: `LinkOpenMode.OpenUrl` → `Action.OpenUrl` with otzaria:// ; `ProviderLaunch` → `Action.Execute` verb `open` data `{url}` → `IOtzariaLauncher.Launch`. When `$host.isUserContextAuthenticated == false`, only OpenUrl works.
Settings window launched from the card: `Action.Execute` verb `settings` → provider starts its own exe without args (or OpenUrl `otzaria-widget:settings` protocol registered in the manifest — Agent D decides; verify).

## Phases
1. Research (done) → 2. Parallel build A/B/C/D/E → 3. QA agents per area (review + tests + fixes) → 4. Integration, packaging, install, E2E verification → 5. Docs (Hebrew README) and handoff.
