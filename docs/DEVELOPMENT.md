# Otzaria Widget - developer guide

Orientation for people (and agents) who will maintain this repo. User-facing documentation is in the Hebrew [README](../README.md);
the original design/work plan is in [ARCHITECTURE.md](ARCHITECTURE.md); verified research on Otzaria's formats and the Widgets platform is in
[research/](research/). Third-party licensing constraints: [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) (read it before changing packaging or sharing builds).

## 1. What it is

A Windows 11 Widgets Board provider (out-of-proc COM server) plus a WPF settings window, shipped as one MSIX (`Otzaria.Widget`).
It reads Otzaria's local files **read-only** and renders six widgets as Adaptive Cards 1.6: `RecentBooks`, `Bookmarks`, `ShamorZachor`,
`Zmanim`, `HebrewCalendar`, `Dashboard`. Items open Otzaria through `otzaria://open/...` deep links. Otzaria itself is never modified.

## 2. Solution layout

| Project | Target | Role |
|---|---|---|
| `src/OtzariaWidget.Core` | net10.0 | Contracts, readers for Otzaria's data, path resolution, link building/launching, settings store, data service |
| `src/OtzariaWidget.Calendar` | net10.0 | `ICalendarService`: Hebrew date, parasha, holidays, daf yomi, omer, zmanim, cities. Wraps Kosher.Zmanim 3.1.1 (pinned `[3.1.1]`) |
| `src/OtzariaWidget.App` | net10.0-windows10.0.19041.0, WinExe, x64 | One exe, assembly name `OtzariaWidget`, two modes (see below). Self-contained .NET, framework-dependent Windows App SDK 1.8 |
| `tests/OtzariaWidget.{Core,Calendar,App}.Tests` | xunit | See section 6 |
| `tools/CardPreview`, `tools/HostSim`, `tools/CatalogProbe` | | Dev tools, section 6 |
| `packaging/` | | `Package.appxmanifest`, `Assets/`, `ProviderAssets/`, `build.ps1`, `install-dev.ps1`, `make-msix.ps1`, `install-msix.ps1`, `uninstall.ps1`, `run-hostsim.ps1`, `tools/make-assets.py` |
| `spikes/` | | Historical experiments (WidgetSpike, WidgetHostProbe, ZmanimSpike incl. the Dart ground-truth harness). Not part of the build |

`Program.Main` dispatches: with `-RegisterProcessAsComServer` it runs `Provider.ProviderHost` (MTA, no UI, no `[STAThread]`); otherwise it starts the
WPF settings app on an STA thread. The only place concrete implementations are wired is `src/OtzariaWidget.App/Composition.cs`.
`Core/Contracts/*` (interfaces, models, `WidgetAppSettings`) are the shared contracts: change them additively.

## 3. Data flow

```
Otzaria files (read-only)                      widget's own settings.json
  user_state.db (+wal)   app_preferences.hive   seforim.db
        |                       |                  |
  UserStateReader         PreferencesReader     LibraryCatalog
  (SQLite, ReadOnly)      (HiveReader/Parser,   (SQLite, ReadOnly,
        |                  key whitelist)        cached lookups)
        |                       |                  |
        |                 ProgressService (sz:* keys + catalog) -> BookProgress
        +-----------+-----------+------------------+
                    v
   OtzariaDataService  -> OtzariaSnapshot (paths, recents, bookmarks, tabs, searches, progress, prefs, per-source status)
   (caches; FileSystemWatcher + 5 s change-token polling because
   the -wal appends raise no events; debounced Changed, 1.5 s)
                    |
        +-----------+---------------------------+
        v                                       v
 Provider (widgets)                       Settings UI (WPF)
 WidgetProvider (WinRT adapter)           SettingsSession + SnapshotHub
  -> WidgetController (state, verbs,       view models: Status, DataLocations,
     timers; no WinRT types)               Times, Display, About
  -> CardBuilders/<Kind>Builder
  -> Templates/*.json + data JSON
  -> WidgetManager.UpdateWidget
```

Key points:

- **Paths** (`Core/Paths/OtzariaPathResolver`): resolves dataRoot, databases dir, `user_state.db`, `app_preferences.hive`, `seforim.db`, `otzaria.exe`; manual overrides in `WidgetAppSettings` win. It never throws and explains each decision in `OtzariaPaths.Diagnostics` (shown in the settings UI). Details: `research/storage-and-links.md` section 1.
- **Settings**: `WidgetAppSettings` is persisted by `JsonSettingsStore` as `settings.json` in `%LOCALAPPDATA%\OtzariaWidget` (inside the package it may be redirected to `LocalCache`; provider and settings UI are the same package, so they share the file). Corrupt file -> defaults + `settings.json.bak`.
- **Refresh**: `OtzariaDataService` watches Otzaria's files and the widget settings; `ProviderServices` coalesces change bursts (400 ms) into one refresh of **active** widgets only. Calendar widgets additionally schedule a timer for the next zman / day boundary (`CardResult.NextRefresh`).
- **Per-widget customization** is `WidgetCustomState` (JSON string persisted by the Widgets host as `CustomState`; tolerant parse, never throws). Each kind has a `BuildCustomization` that returns the `customize.json` card; `Input.*` values come back as strings and are applied by `ApplyInputs`.
- **Links**: `LinkOpenMode.OpenUrl` -> `Action.OpenUrl` with the `otzaria://` URL (host launches it; the only mode that works when `$host.isUserContextAuthenticated` is false). `ProviderLaunch` -> `Action.Execute` verb `open`; the provider runs `OtzariaLauncher.Launch` (starts `otzaria.exe "<uri>"`, falling back to the registered protocol). `OtzariaLauncher.IsSafeOtzariaUri` only allows `otzaria://open/...`.
- **Settings window from a card**: `Action.Execute` verb `settings` makes the provider start its own exe. The `otzaria-widget://settings[/page]` protocol in the manifest (`LaunchArguments`: pages `status|data|times|display|about`) is a fallback for OpenUrl-only contexts and is documented in the manifest as unreliable on a loose-registered package.
- **Provider lifetime**: stays alive while widgets exist; exits about 60 s after the last widget/host activity (`WidgetProvider.IdleExit`).

### Hard rules (carried over from ARCHITECTURE.md)

1. Read-only toward Otzaria. SQLite: `Mode=ReadOnly`, `Pooling=False`, short-lived connections, never `immutable=1`; if the directory is not writable (`-shm` cannot be created) read a private copy of db+wal+shm. Hive: open with `FileShare.ReadWrite | FileShare.Delete`, parse in memory, retry on IOException.
2. Privacy: Hive **whitelist only** (`PreferencesReader.IsWhitelisted`: `sz:*`, `key-selected-city`, `key-calendar-*`, dark mode / swatch colors, library/database path keys). Never read or log Google-calendar, email, password or credential keys. Logs carry no notes text or search queries.
3. Defensive parsing: unknown fields ignored; one broken entry is skipped, never the whole list; an unknown schema yields `DataHealth.UnknownSchema` with a Hebrew message, never an exception to callers.
4. Always `CultureInfo.InvariantCulture` for machine data (the dev machine is he-IL with the Hebrew calendar active); Gregorian dates are formatted explicitly.
5. Card text is Hebrew, RTL. The settings window is Hebrew (RTL) or English (LTR), see "Settings window language" below. Cards: Adaptive Cards 1.6 maximum, `rtl: true`, no `ProgressBar` element (bars are built from text segments / column weights).
6. Do not change packaging to single-file / trimmed / AOT / ILMerge (LGPL, see THIRD-PARTY-NOTICES.md).

### Settings window language

Hebrew is the source language and English is a catalog, mirroring Otzaria's own settings screen (`lib/settings/l10n`):

- Text stays Hebrew in the code and is the catalog key: XAML `Text="{l:T 'מצב'}"` (escape an apostrophe as `\'`), C# `Loc.T("...")` / `Loc.F("... {0}", value)`. The English values are in `SettingsUi/Localization/settings_en.json`, embedded in the exe. A key may carry a context after `|` when the same Hebrew text needs two translations. A JSON catalog was chosen over `.resx` because the keys are the Hebrew text and `build.ps1` strips satellite-assembly folders from the layout.
- `WidgetAppSettings.UiLanguage`: `system` (default; Hebrew Windows display language -> Hebrew, anything else -> English), `he` or `en`. `UiLanguage.Resolve` decides; the window's `FlowDirection` and `Language` follow the result.
- Changing the language on the Display page rebuilds the window at once (same page and bounds). `--lang he|en` forces a language for one run without saving it (with `--screenshot` for visual QA).
- Core keeps writing Hebrew (the cards show the same texts). On the Status page, `CoreText` translates the source-status messages and the path-resolver notes under "Detection details": fixed texts are catalog keys, texts with variable parts are `CoreText.Templates` (paths and counts are captured and re-inserted, paths stay LTR). A note that matches nothing stays Hebrew; an unknown status message shows the generic health text. When you add or change a note or status message in Core, add its catalog entry or template: `CoreTextTests` fails otherwise. The Times preview (card content: zman names, Hebrew date, parasha) stays Hebrew.
- `LocalizationTests` fails on a Hebrew literal outside the catalog, a missing or unused key, or a placeholder mismatch. A Hebrew literal that is card content is marked `// l10n: keep`.

## 4. Widgets and cards

- Kinds and ids: `Provider/Cards/WidgetKinds.cs`. Builders: `Provider/Cards/Builders/*Builder.cs` (one per kind). Shared helpers: `CardData` (roots, rows, pagination, bars), `CardFormat`, `Fields` (customization inputs), `CalendarContext` (city/date/refresh helpers).
- Templates: `src/OtzariaWidget.App/Templates/*.json` (Adaptive Cards Templating). `customize.json` and `error.json` are shared; the other six are one per kind. `build.ps1` copies them into the layout.
- Sizes: Small / Medium / Large. Default and maximum rows: S 1/2, M 3/4, L 7/8 (`CardData.DefaultRows/MaxRows`); builders override per kind. Bookmarks and Dashboard are offered in M/L only (manifest `Capabilities`).
- Empty/missing-data states carry the text `לא נמצאו נתוני אוצריא — פתח את ההגדרות` (`CardData.MissingText`) and a settings button.

### Adding or changing a widget kind

1. Add the id to `WidgetKinds` and a builder implementing `IWidgetCardBuilder` (register it in `CardBuilders`).
2. Add a template to `Templates/`, list it in `CardTemplates`.
3. Add fields to `WidgetCustomState` (parse + serialize + `ApplyInputs`).
4. Add a `<Definition Id="...">` to `packaging/Package.appxmanifest` (display name, description, sizes, icon, light/dark screenshots; generate assets via `packaging/tools/make-assets.py`).
5. Extend the card tests and `CardPreview`. A unit test (`Manifest_matches_code_clsid_definitions_and_executable`) fails if the manifest and `WidgetKinds` drift apart.

Manifest invariants (documented at the top of `Package.appxmanifest`): the provider CLSID `EB0AF4C1-58BE-4FA6-9F1B-88F8188C6326` must match `ProviderIds.Clsid`; `Definition` ids must equal `WidgetKinds`; `Executable` must be `OtzariaWidget.exe`.

## 5. Build, run, package

Prerequisites: .NET 10 SDK, Windows SDK 10.0.26100 (the scripts call `makepri`, `makeappx`, `signtool` from `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64`), Developer Mode for dev registration, Windows App Runtime 1.8. Python with Pillow is needed only when the PNG assets are missing (they are committed).

```powershell
dotnet build OtzariaWidget.sln
dotnet test  OtzariaWidget.sln

.\packaging\build.ps1                    # publish -> artifacts\layout (+ manifest, assets, templates, resources.pri); no registration
.\packaging\build.ps1 -Register          # the above + packaging\install-dev.ps1 (Add-AppxPackage -Register; Developer Mode, no admin)
.\packaging\uninstall.ps1                # remove the package (does not touch Otzaria)
.\packaging\make-msix.ps1                # build + sign -> artifacts\Otzaria.Widget_<ver>_x64.msix, OtzariaDev.cer, install-msix.ps1, dependencies\
.\packaging\run-hostsim.ps1              # drive the provider without the Widgets board (needs the package registered)
```

`build.ps1` details worth knowing:

- Every build gets a new version `1.0.<days since 2026-01-01>.<minute of day>` (bumped if not above the installed one) so `-Register` updates in place and pinned widgets survive. Override with `-Version`. `-Publisher` defaults to `CN=OtzariaDev` and must equal the signing certificate subject. `-SkipPublish` re-uses an existing layout.
- It stops a running `OtzariaWidget` process first (a running provider locks the exe).
- If `artifacts\layout` is locked it publishes to `artifacts\layout-<version>` and records the folder in `artifacts\layout.txt`; `install-dev.ps1` and `make-msix.ps1` read that file.
- It removes `.pdb`, `createdump.exe` and WPF localisation folders from the layout and fails if test/build junk is present.
- A loose-file registration points at the layout folder: keep it in place while installed.
- `make-msix.ps1` creates/reuses a self-signed `CN=OtzariaDev` certificate in `CurrentUser\My`, so the installing machine needs the `.cer` in `LocalMachine\TrustedPeople` (admin once, done by `install-msix.ps1`). A real certificate or the Store removes that. `-SkipBuild` re-packs the existing layout.

Logs: `provider.log` (rotates to `provider.log.old`), `settings-ui.log` and `settings.json` live in `%LOCALAPPDATA%\OtzariaWidget\`, **or** in the package's `%LOCALAPPDATA%\Packages\Otzaria.Widget_<hash>\LocalCache\Local\OtzariaWidget\` (Windows redirects only when the real folder does not already exist; an unpackaged dev run creates it and then wins). `install-dev.ps1` prints both paths.

## 6. Testing strategy

Run everything with `dotnet test OtzariaWidget.sln`. Tests use synthetic fixtures generated in code or temp folders; nothing in the test suite needs Otzaria, and nothing writes into the real Otzaria data.

| Layer | Where | What |
|---|---|---|
| Unit + hardening | `tests/OtzariaWidget.Core.Tests` | Hive parser (incl. fuzz and hardening tests with a test writer), preferences, progress math, `user_state.db` reader against fixtures built in code, library catalog, path resolver with a fake environment, launcher/URI safety, settings store, data service robustness |
| Real-data smoke | `Core.Tests/Services/RealDataSmokeTests.cs`, `App.Tests/RealDataCardTests.cs` | Run against the machine's real `%APPDATA%\otzaria` and **pass silently when it does not exist**. They assert sane shapes and print counts only, never personal content. This is the early-warning for schema drift |
| Golden / parity | `tests/OtzariaWidget.Calendar.Tests/Golden/*.txt.gz` | Outputs of **Otzaria's own Dart code** (kosher_dart 2.0.20): zmanim (`zmanim_full`, `qa_zmanim_all_cities`), Hebrew calendar (`hebrew_israel`, `hebrew_diaspora`, `qa_hebrew_*`), registry and city table (`qa_registry`, `qa_cities`), time zones (`qa_timezones`). The C# engine must match them to the second / string |
| Cards | `tests/OtzariaWidget.App.Tests` | The pure card code (`Provider/Cards`) is compiled into the test assembly and the real `Templates/*.json` are linked in. Template expansion with AdaptiveCards.Templating across a kind x size x theme matrix (`CardMatrixTests`), content, polish (rounding, countdown, source names, degradation), customization cards, `WidgetCustomState`, `WidgetController` runtime with fakes, `ProviderServices`, settings-UI view models, and the manifest consistency test |
| Card preview | `tools/CardPreview` | `dotnet run --project tools\CardPreview -c Release [-- --out <dir>] [--shots] [--no-real] [--kind <k>] [--dump] [--now 2026-11-13T10:30:00+02:00]`. Writes `artifacts\card-preview\index.html` (synthetic demo data, safe to share) and `real.html` (**real private data; never commit or share**; `--no-real` skips it). Renders every kind x size x theme with the Adaptive Cards JS renderer (downloaded on first run); `--shots` captures screenshots |
| Host simulation | `tools/HostSim` + `packaging\run-hostsim.ps1` | A fake Widgets host that CoCreates the real provider and drives `CreateWidget/Activate/OnActionInvoked/OnCustomizationRequested/Deactivate/DeleteWidget` with raw COM vtables. It must run **inside the package identity** (`Invoke-CommandInDesktopPackage`; an unpackaged process cannot QI `IWidgetProvider`, 0x80073D54), so register the package first. Scenarios: `-Scenario full` (default) / `settings` / `keep`; `-WaitIdle` also checks the 60 s idle exit; trace goes to `-Out` (default `%TEMP%\otzaria-hostsim.txt`) and the provider log lines of the run are printed. Uses widget ids `hostsim-*`, so nothing is created on the real board |
| Catalog probe | `tools/CatalogProbe` | `dotnet run --project tools\CatalogProbe` lists the registered widget providers via `AppExtensionCatalog` and shows our parsed `Definition`s (the `*` row is ours). Steps 2/3 (Hosts API, QI) are **expected** to fail from an unpackaged process. `-- --expand <template.json> [size] [theme]` expands one template offline |
| Hive dump | `tools/hive_dump.py` | Independent Python parser of `app_preferences.hive`, for comparing against `HiveReader` when investigating format changes |

What is **not** automatable here: rendering on the real Widgets board, the board's picker cache, and the real pinned-widget lifecycle. Do a manual pass for those before a release (section 9).

## 7. Refreshing the calendar goldens

The goldens are produced by Otzaria's real Dart code run outside Flutter. The full recipe is in
`tests/OtzariaWidget.Calendar.Tests/Golden/generator/HOWTO.txt`; in short:

1. Set up the harness `spikes/ZmanimSpike/dart-groundtruth` (see its `HOWTO.txt`): copy `zmanim_helpers.dart`, `zman_definition.dart`, `calendar_location.dart` and `calendar_days.dart` from an Otzaria checkout into `lib/` (replace the `calendar_date_helpers.dart` import by `package:otzaria/utils/calendar_days.dart`), then `dart pub get`. Pin `kosher_dart` to the version Otzaria uses.
2. Copy the generators (`golden_zmanim.dart`, `golden_hebrew.dart`, `qa_*.dart`) from `Golden/generator/` into the harness `bin/`.
3. Regenerate and gzip (`gzip -9 -n`):
   - zmanim: `dart run bin/golden_zmanim.dart "<cities>" "<yyyy-MM-dd,...>" > zmanim_full.txt`
   - Hebrew calendar: `dart run bin/golden_hebrew.dart 2023-09-01 2922 israel hebrew_israel.txt` (and `diaspora`)
   - QA set: `qa_zmanim_all_cities.dart`, `qa_hebrew.dart`, `qa_meta.dart` (registry + city table), and the time-zone file; see the HOWTO for arguments.
4. Machine time zone matters for kidush levana lines (generated with `Asia/Jerusalem`; the tests account for it).
5. When Otzaria changes its registry or city table, also regenerate the embedded `src/OtzariaWidget.Calendar/Data/zmanim-registry.json` (`python -I extract_registry.py out.json`) and `otzaria-cities.json` (`bin/dump.dart`, also mirrored in `docs/research/otzaria-cities.json`).
6. Run `dotnet test tests/OtzariaWidget.Calendar.Tests`. Differences point either at an Otzaria behaviour change to port or at a Kosher.Zmanim upgrade (the package is pinned exactly for this reason).

`spikes/ZmanimSpike` (`dotnet run -- dump ...` / `hebrew ...` + `compare_dumps.py`) is the quick way to diff the C# output against the Dart output for a few cities/dates.

## 8. Adapting to a change in Otzaria's storage

Symptoms: a widget shows a "schema"/"unreadable" message, the settings **מצב** page marks a source as unknown schema, or the real-data smoke tests start failing.

Where each reader lives and what it checks:

| Data | Reader | Schema guard |
|---|---|---|
| `user_state.db` (history, bookmarks, bookmark groups, workspaces, open tabs, searches) | `Core/UserState/UserStateReader.cs` | `CheckSchema`: `PRAGMA user_version` must be `<= SupportedUserVersion` (currently `1`), and the `lists` or `window_sessions` table must exist; otherwise the source status is `DataHealth.UnknownSchema` and readers return empty results. JSON payload shapes (`lists.payload_json`, `window_sessions.tabs_json`, the `book` polymorphism) are documented in `research/storage-and-links.md` sections 2.x |
| `app_preferences.hive` | `Core/Hive/HiveParser.cs`, `HiveReader.cs`, `Core/Preferences/PreferencesReader.cs` (whitelist, defaults), `Core/Progress/ProgressService.cs` (`sz:tracked_books`, `sz:progress_by_id`, `sz:book_columns`, `sz:completion_dates_by_id`) | Parser validates every frame's CRC and bounds and reports an error for a damaged file (nothing is guessed); values of unknown type are counted and skipped. Format: `research/hive-and-progress.md` |
| `seforim.db` | `Core/Library/LibraryCatalog.cs` | Failures return null and are not cached; titles degrade to "ספר N" with a note on the Shamor Vezachor card |
| Paths, portable mode, capabilities file | `Core/Paths/OtzariaPathResolver.cs` | `OtzariaPaths.Diagnostics` explains every decision |
| Deep links | `Core/Launch/OtzariaLauncher.cs` (`BuildLink`, `BuildSearchLink`, `IsSafeOtzariaUri`); fixed URLs in `Provider/Cards/CardData.cs` | Link grammar: `research/storage-and-links.md` section 5 |

Procedure:

1. Diff the Otzaria sources (`lib/core/user_state/user_state_database.dart`, history/bookmark models, `lib/main.dart` router) against the research notes; update the notes first.
2. For a `user_version` bump: inspect the new schema (`sqlite3 -readonly`), update the reader and the in-code fixture builders (`tests/OtzariaWidget.Core.Tests/UserState/UserStateFixture.cs`), then raise `SupportedUserVersion`. Until that is done the widget deliberately shows an "update the widget" message instead of guessing.
3. For Hive changes: compare `tools/hive_dump.py` and `HiveReader` output on a real file, extend `HiveTestWriter` fixtures, keep the key whitelist minimal.
4. Run `dotnet test` (including the real-data smoke tests with real Otzaria data present) and look at `tools/CardPreview` `real.html`.
5. If the deep-link grammar changed, update `OtzariaLauncher` and its tests, and the `otzaria://open/history` check in the settings **אודות** page if that route moved.

## 9. Release checklist

1. `dotnet build OtzariaWidget.sln` with 0 errors, no new warnings; `dotnet test OtzariaWidget.sln` green (with and without real Otzaria data on the machine).
2. Review `THIRD-PARTY-NOTICES.md`: Kosher.Zmanim still pinned and shipped as a separate, unmodified `Kosher.Zmanim.dll`; `PublishSingleFile`, `PublishTrimmed`, `PublishAot` still false. Any new dependency has a section.
3. **Data provenance:** the embedded city/zmanim/label data derives from Otzaria's source code, which belongs to the publisher of this widget. No redistribution restriction applies; keep `THIRD-PARTY-NOTICES.md` accurate for the third-party components.
4. Manifest sanity: `Package.appxmanifest` CLSID, `Definition` ids, `Executable`, `PackageDependency` `MinVersion` (must equal `$minRuntime` in `install-msix.ps1`), display strings. Regenerate assets if the look changed (`python packaging\tools\make-assets.py`).
5. `.\packaging\build.ps1 -Register` on a clean machine/user (Developer Mode), then in the Widgets board (`Win+W` -> add widgets -> ווידג'ט אוצריא):
   - all six widgets appear in the picker, with icons and light/dark screenshots; add each in every offered size;
   - data shows with Otzaria open **and** closed; edit something in Otzaria (open a book, add a bookmark) and the widget refreshes within a few seconds;
   - customization cards save and apply for each kind (known board bug: the widget's `...` menu can become unresponsive after the customization card is shown);
   - links open the right book/position in both **LinkMode** settings (`OpenUrl`, `ProviderLaunch`); try a portable Otzaria if available;
   - the "no data" card and its **פתח הגדרות** button, with the data folder temporarily overridden to an empty folder;
   - settings app: every page, auto-save, **בדוק קישור**, **פתח את תיקיית הלוגים**.
6. `.\packaging\run-hostsim.ps1` (and `-WaitIdle`) completes with all HRESULTs OK; check `provider.log` for exceptions.
7. `.\packaging\make-msix.ps1`; on a second machine/VM run `install-msix.ps1` as administrator, verify install, widgets, and `.\packaging\uninstall.ps1` (or Remove-AppxPackage) leaves Otzaria untouched.
8. Commit; the package version is derived from the build time unless `-Version` is passed, so for a tagged release pass an explicit `-Version` to `make-msix.ps1`.

## 10. Publishing to the Microsoft Store

The Store signs the package itself and resolves `Microsoft.WindowsAppRuntime.1.8`, so a Store build is **unsigned**, has no `.cer`/installer, and does not ship the Windows App Runtime packages. In Partner Center reserve the app name first, then open **Product management > Product identity** and copy three values:

| Partner Center field | `make-msix.ps1` parameter | Manifest |
|---|---|---|
| Package/Identity/Name | `-IdentityName` | `Identity Name` |
| Package/Identity/Publisher (`CN=<GUID>`) | `-Publisher` | `Identity Publisher` |
| Package/Properties/PublisherDisplayName | `-PublisherDisplayName` | `PublisherDisplayName` |

```powershell
.\packaging\make-msix.ps1 -Store `
  -IdentityName 'XXXXXPublisher.OtzariaWidget' `
  -Publisher 'CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX' `
  -PublisherDisplayName 'Publisher Display Name'
```

Output: `artifacts\store\Otzaria.Widget_<version>_x64.msix` (upload this file as the package in a submission). Notes:

- Version defaults to `yyyy.MMdd.HHmm.0`. The Store requires a non-zero first segment, a 4th segment of `0`, and a version higher than every previously submitted one (the script validates the first two; pass `-Version a.b.c.0` to override).
- Without all three values the script still runs but uses `REPLACE...` placeholders, prints a loud warning and names the file `*_PLACEHOLDER-IDENTITY.msix`. Partner Center rejects such a package.
- `-Store` always rebuilds (it cannot be combined with `-SkipBuild`) and overwrites `artifacts\layout`, like `build.ps1`.
- The manifest placeholders `@IDENTITYNAME@` / `@PUBLISHERDISPLAYNAME@` default to `Otzaria.Widget` / `Otzaria Dev` in `build.ps1`, so the dev flow and the GitHub release workflow are unchanged.
- Sanity check before uploading: `makeappx unpack /p <msix> /d <tmp> /nv`, then confirm `AppxManifest.xml` has the Partner Center identity and a version ending in `.0`, `resources.pri` exists, and there are no `.pdb` files.
- The submission needs justification for the restricted capability `runFullTrust`.
