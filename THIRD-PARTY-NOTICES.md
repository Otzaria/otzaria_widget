# Third-party notices

Otzaria Widget uses the following third-party components. Each section lists the component, its license, how it is
used here, and where the license text can be found. Add new entries as new sections (keep this file at the repo root
and ship it next to the application / inside the MSIX).

---

## Kosher.Zmanim (.NET port of KosherJava) 3.1.1

* Used by: `src/OtzariaWidget.Calendar` (Hebrew calendar, zmanim, daf yomi, molad).
* Package: <https://www.nuget.org/packages/Kosher.Zmanim/3.1.1>
* Project: <https://github.com/Elyahu41/Kosher.Zmanim.NET> (port of KosherJava, <https://github.com/KosherJava/zmanim>)
* Copyright (C) 2004-2026 Eliyahu Hershfeld (KosherJava). .NET port copyright (C) 2026 Elyahu41.
* License: **GNU Lesser General Public License, version 2.1 only (LGPL-2.1-only)**
  * Text: <https://licenses.nuget.org/LGPL-2.1-only> / <https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html>
  * The full text also ships inside the NuGet package (`LICENSE`).
* How it is used (LGPL compliance):
  * Referenced as an **unmodified binary NuGet package** (`<PackageReference Include="Kosher.Zmanim" Version="[3.1.1]" />`).
  * It is shipped as the **separate assembly `Kosher.Zmanim.dll`** next to our own assemblies - it is **dynamically
    linked**, never merged, embedded, obfuscated, trimmed or bundled into a single file (`PublishSingleFile=false`,
    `PublishTrimmed=false` in `OtzariaWidget.App.csproj`). A user may replace it with a modified build of the library
    (same assembly identity/version) and the application will use it.
  * Our own code extends it only by subclassing in `OtzariaWidget.Calendar` (no changes to the library itself).
  * The corresponding source of the library is available at the project URL above (tag/commit referenced by the package:
    `aeb3a4fc413492f48e4fe3f6745538f6826f41b6`).
* **Do not** change the packaging to single-file / ILMerge / NativeAOT without revisiting this entry (those would
  make the library part of a combined work).

---

## KosherSwift (Swift port of KosherJava) 3.1.x - macOS / iOS builds

* Used by: `apple/OtzariaKit/Sources/OtzariaCalendar` (Hebrew calendar, zmanim, daf yomi, molad) in the macOS and iOS apps
  and widget extensions.
* Project: <https://github.com/Elyahu41/KosherSwift> (port of KosherJava, <https://github.com/KosherJava/zmanim>), pinned in
  `apple/OtzariaKit/Package.resolved` (tag `3.1.2`, commit `6b12b4089f2a8b198971b793e6d29e171c3f1b37`).
* Copyright (C) 2004-2026 Eliyahu Hershfeld (KosherJava). Swift port copyright (C) Elyahu41.
* License: **GNU Lesser General Public License, version 2.1 (LGPL-2.1)**
  * Text: the `LICENSE` file in the project repository / <https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html>
* How it is used (LGPL compliance):
  * Referenced as an **unmodified Swift Package Manager dependency** (`exact: "3.1.2"`); no file of the
    library is copied into this repository or changed.
  * In the macOS and iOS apps it ships as its **own dynamic framework** (`KosherSwift_…_PackageProduct.framework`,
    built by Xcode from the unmodified package), embedded once in the app bundle and loaded by the widget extension from
    there. Our calendar code is the separate `OtzariaCalendar.framework`; no app, extension or other framework binary
    contains KosherSwift code (checked with `nm` in CI). A user can rebuild KosherSwift from modified source and replace
    that framework.
  * Our code only calls its public API; a Daf Yomi Bavli calculation is reimplemented in `DafYomiText.swift` because the
    library derives day numbers from the device time zone.
* **Do not** change the product types in `apple/OtzariaKit/Package.swift` or the package products linked by the Xcode
  targets without revisiting this entry (see `apple/README.md`).

---

## Data and text derived from Otzaria

* Used by: `src/OtzariaWidget.Calendar/Data/otzaria-cities.json` (city table: names, coordinates, elevations, time zones,
  candle-lighting minutes), `src/OtzariaWidget.Calendar/Data/zmanim-registry.json` (zman ids, Hebrew titles, short
  descriptions, display order, defaults) and the Hebrew label/omer spellings in `src/OtzariaWidget.Calendar/Hebrew`.
  They are copied/ported from Otzaria's source (`lib/tools/calendar/**`) so that the widget matches the app.
* Source: the Otzaria project. The owner of Otzaria is also the publisher of this widget, so these data files may be
  distributed together with the widget; there is no redistribution restriction on them.

---

## Time zone data

* The widget resolves IANA zone ids through the operating system (Windows ICU / registry) via `TimeZoneInfo`, and on
  macOS / iOS via Foundation `TimeZone`. No third-party time zone package is bundled. (`OtzariaWidget.Calendar.Cities.TimeZones`
  and its Swift counterpart `CityZone` contain a small correction for Alberta / British Columbia, whose permanent-time change
  in tzdata 2026 may not yet be present in every OS build.)

---

## .NET runtime and libraries (self-contained)

* The package ships the .NET 10 runtime and base class libraries (self-contained): **MIT License**, (c) .NET Foundation and Contributors.
  <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

---

## Microsoft.Data.Sqlite 11.0.0, SQLitePCLRaw 2.1.12 and SQLite

* Used by: `src/OtzariaWidget.Core` (strictly read-only access to Otzaria's `user_state.db` / `seforim.db`).
* `Microsoft.Data.Sqlite` / `Microsoft.Data.Sqlite.Core`: **MIT License** (c) .NET Foundation and Contributors.
  <https://licenses.nuget.org/MIT>
* `SQLitePCLRaw.core`, `SQLitePCLRaw.provider.e_sqlite3`, `SQLitePCLRaw.lib.e_sqlite3`, `SQLitePCLRaw.bundle_e_sqlite3`: **Apache License 2.0**,
  (c) Eric Sink. <https://licenses.nuget.org/Apache-2.0>
* `e_sqlite3.dll` is a build of **SQLite**, which is in the **public domain**. <https://www.sqlite.org/copyright.html>

---

## Windows App SDK 1.8 (Widgets projection)

* Used by: `src/OtzariaWidget.App` (`Microsoft.WindowsAppSDK.Widgets` / `.Base` / `.Runtime` 1.8.x). The projection assembly
  `Microsoft.Windows.Widgets.Projection.dll` is shipped in the package; the Windows App Runtime itself is a framework package
  (`Microsoft.WindowsAppRuntime.1.8`) that the user's system provides.
* License: Microsoft Software License Terms (the `license.txt` inside each NuGet package; the open source parts are MIT:
  <https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE>).

---

## SQLite on macOS / iOS

* `apple/OtzariaKit/Sources/OtzariaCore` reads Otzaria's databases strictly read-only through the **system SQLite library**
  (`SQLite3` module of the OS); nothing is bundled. SQLite is in the **public domain**. <https://www.sqlite.org/copyright.html>
