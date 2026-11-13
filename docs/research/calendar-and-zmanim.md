# Calendar and zmanim: how Otzaria does it, and how the .NET widget should match it

Research date: 2026-11-13. Otzaria source: branch `dev`, commit `46b4af531`, `C:\Users\user\Documents\otzaria_project`.
All Otzaria paths below are relative to `lib/` unless stated. Line numbers are from that commit.
Verified artifacts in this repo: `spikes/ZmanimSpike/` (runnable spike), `docs/research/otzaria-cities.json` (the exact city table exported from Otzaria),
`spikes/ZmanimSpike/sample-output-2026-11-13.txt` (spike output).

## 0. Decision in one screen

* **Library: NuGet `Kosher.Zmanim` 3.1.1** (net8.0 + net10.0, LGPL-2.1-only, a complete port of KosherJava 3.1.0). It has everything Otzaria uses (see section 9). With two settings flipped it reproduces Otzaria's numbers to within +-1 second in all but 2 of 67,844 (city x date x zman) cells I compared, and to the displayed minute in 99.5% of them (details in 9.3).
* **Otzaria uses `kosher_dart` 2.0.20** (a Dart port of the older KosherJava 2.x). The Otzaria-specific behaviour (zman list, labels, candle-lighting minutes per city, day-transition, text formatting) lives in Otzaria's own Dart code, not in the library, so it must be re-implemented in C# (it is small).
* **City list is Dart source, not an asset**: `tools/calendar/models/calendar_location.dart`. 129 cities. Exported verbatim to `docs/research/otzaria-cities.json` (fields: name, country, lat, lng, elevation, timezone, inIsrael, candleLightingMinutes). Default city `ירושלים`.
* **The user's configured city on this machine is `מודיעין עילית`** (read from `%APPDATA%\otzaria\app_preferences.hive`, key `key-selected-city`), day transition `midnight`, calendar type `combined`; no zman-selection override (so registry defaults apply).
* .NET `System.Globalization.HebrewCalendar` is not sufficient (section 10).
* Known gaps and traps are in section 11. The three that matter most: (1) string differences between kosher_dart and KosherJava 3 (`חשוון` vs `חשון`, `ניצבים` vs `נצבים`, `שושן פורים`, `אסרו חג`); (2) KosherJava 3 defaults (apparent solar radius, astronomical chatzos) must be switched off; (3) on a he-IL Windows the *current culture formats dates in the Hebrew calendar*, so always format machine dates with `InvariantCulture`.

---

## 1. Where the feature lives in Otzaria

| Concern | File | Lines |
|---|---|---|
| Calendar tool (screen, cubit, state) | `tools/calendar/calendar_screen.dart`, `tools/calendar/bloc/calendar_cubit.dart`, `calendar_state.dart` | - |
| Zman registry + calculation + formatting | `tools/calendar/helpers/zmanim_helpers.dart` | registry 404-1356, calc 114-168, format 173-223 |
| Zman definition model | `tools/calendar/models/zman_definition.dart` | 32-95 |
| City table, candle-lighting minutes, Israel flag | `tools/calendar/models/calendar_location.dart` | cities 2-860, candle 864-924, helpers 927-951 |
| Hebrew formatting helpers (own gematria, month names) | `tools/calendar/helpers/calendar_date_helpers.dart` | 14-27, 59-70, 79-145 |
| Per-day holiday labels (grid cell) | `tools/calendar/widgets/calendar_day_cell.dart` | 316-380 |
| Times panel (cards, omer, daf button, city dropdown) | `tools/calendar/widgets/calendar_times_panel.dart` | 150-198, 276-373, 542-574, 628-657 |
| Header date line ("אור ל...") | `tools/calendar/widgets/calendar_top_bar.dart` | 200-210; logic `calendar_cubit.dart:1687-1711` |
| Day-transition logic ("which civil day is today") | `tools/calendar/bloc/calendar_cubit.dart` | 1621-1654, 1768-1797 |
| Molad card + true conjunction | `tools/calendar/helpers/molad_helpers.dart` | 108-465 |
| Library top-bar date + daf chip | `library/view/library_daf_yomi.dart` | 33-82 |
| Settings keys/defaults | `settings/engine/settings_repository.dart` | keys 64-69, 207-211; defaults 489-508, 618-628 |
| Settings file location / reading from another process | `core/app_paths.dart:156-185`, `core/info/settings_snapshot.dart:72-110` | - |
| Upcoming-parsha helpers (other tools/plugins) | `tools/tikkun_korim/bloc/tikkun_korim_bloc.dart:25-35`, `plugins/bridge/plugin_bridge_adapter.dart:7311-7360` | - |

Package versions: `kosher_dart: ^2.0.20` (`pubspec.yaml:92`), locked 2.0.20 (`pubspec.lock:1009-1016`); `timezone: ^0.11.2` (`pubspec.yaml:143`, for IANA zones). There is no zmanim code in Rust/native; everything is Dart.

## 2. How a day's zmanim are computed in Otzaria

`buildZmanimCalendarContext(date, city)` (`zmanim_helpers.dart:17-65`):

1. Look up the city (`getCityData`).
2. `GeoLocation`: name, latitude, longitude, **`elevation > 0 ? elevation : 0`** (negative elevation such as Tiberias -200 m becomes 0; line 45).
3. IANA tz = `timezone` field (default `Asia/Jerusalem`), the date's Y-M-D is set as a `TZDateTime` in that zone.
4. `ComplexZmanimCalendar.intGeoLocation(location)`. Defaults of kosher_dart are kept: NOAA calculator, 16 arcminute solar radius, `useElevation=false` (GRA hours use sea-level sunrise/sunset), `getChatzos()` = half-way between sea-level sunrise and sunset.

`calculateDailyTimes` (`zmanim_helpers.dart:71-168`) loops `kZmanimRegistry` (98 definitions), skips a definition if `isRelevant(jewishCalendar)` is false, calls `compute`, formats the result. `JewishCalendar.fromDateTime(date)` with `inIsrael = isCityInIsrael(city)` (line 124-125).
Omer counting time `omerCounting` = `zmanimCalendar.getTzais()` (8.5 degrees) when `getDayOfOmer() != -1` (lines 158-165).

### 2.1 Time display format (must be copied)

* `formatZmanTime` (`zmanim_helpers.dart:185-190`): convert to the city's zone, **truncate** to minutes (no rounding), print `HH:mm` + a seconds marker: `.` if seconds are 0-29, `:` if 30-59 (`secondsMarker`, line 173; the "Luach Itim LaBina" convention). The whole token is wrapped in U+2066 ... U+2069 (LTR isolate) so the marker stays on the correct side in RTL text.
* 12-hour clock option (`key-calendar-twelve-hour-clock`): `zmanTimeForDisplay` (lines 202-211) turns `HH:mm.` into `h:mm.` + LRM + NBSP + `AM`/`PM`.
* Sorting of cards (`calendar_times_panel.dart:183-196`): `chatzosLayla` always last; clock times sorted by the `HH:MM` string; non-clock (kidush levana) after clocks, in registry order.
* Pairs: two enabled definitions with the same `pairId` are shown as one wide card (title once, each value labelled with `pairLabel`): `alosDeg, alosShavos, alosZmanis, shmaMgaGra, tfilaMgaGra, motzeiSh, motzeiYT, achilasChametz, biurChametz` (`calendar_times_panel.dart:164-181`, 80-114).
* Header line printed above the cards: `אין לסמוך על הזמנים כלל!` ("do not rely on these times at all") (`calendar_times_panel.dart:446`).

## 3. The zman list (all 98 registry entries)

Order below is registry order = display category order (categories: עלות השחר, ציצית ותפילין, זריחה, קריאת שמע, תפילה, חצות, מנחה, פלג המנחה, שקיעה, בין השמשות, צאת הכוכבים, רבנו תם, שבת וחג, ערב פסח, תעניות, קידוש לבנה).
"def" = shown by default (31 of 98; `kDefaultEnabledZmanim`, `zmanim_helpers.dart:1360-1363`). "only when" = `isRelevant` predicate (section 3.2).
Hebrew label shown on a card = `title` (line 1) + `subtitle` (line 2); in paired cards the title is shared and the `pairLabel` precedes each value. Each registry entry also has a long Hebrew `explanation` (the info dialog); copy those texts straight from `zmanim_helpers.dart` if the widget needs them.

| # | id | Hebrew label (title / subtitle) | Otzaria (kosher_dart) call | .NET Kosher.Zmanim 3.1.1 call | def | only when |
|---|----|----|----|----|----|----|
| 1 | `alos72Degrees` | עלות השחר / 72 דק׳ (מעלות) | `getAlos16Point1Degrees()` | `GetAlos16Point1Degrees()` | Y |  |
| 2 | `alos90Degrees` | עלות השחר / 90 דק׳ (מעלות) | `getAlos19Point8Degrees()` | `GetAlos19Point8Degrees()` | Y |  |
| 3 | `alos120Degrees` | עלות השחר / 120 דק׳ (מעלות) | `getAlos26Degrees()` | `GetAlos26Degrees()` |  |  |
| 4 | `alos72Shavos` | עלות השחר / 72 דק׳ (שוות) | `getAlos72()` | `GetAlos72Minutes()` |  |  |
| 5 | `alos90Shavos` | עלות השחר / 90 דק׳ (שוות) | `getAlos90()` | `GetAlos90Minutes()` |  |  |
| 6 | `alos120Shavos` | עלות השחר / 120 דק׳ (שוות) | `getAlos120()` | `GetAlos120Minutes()` |  |  |
| 7 | `alos72Zmanis` | עלות השחר / 72 דק׳ (זמניות) | `getAlos72Zmanis()` | `GetAlos72Zmanis()` |  |  |
| 8 | `alos90Zmanis` | עלות השחר / 90 דק׳ (זמניות) | `getAlos90Zmanis()` | `GetAlos90Zmanis()` |  |  |
| 9 | `alos120Zmanis` | עלות השחר / 120 דק׳ (זמניות) | `getAlos120Zmanis()` | `GetAlos120Zmanis()` |  |  |
| 10 | `misheyakir10point2` | זמן ציצית ותפילין | `getMisheyakir10Point2Degrees()` | `GetMisheyakir10Point2Degrees()` | Y |  |
| 11 | `misheyakir11` | זמן ציצית ותפילין / 11° | `getMisheyakir11Degrees()` | `GetMisheyakir11Degrees()` |  |  |
| 12 | `misheyakir11point5` | זמן ציצית ותפילין / 11.5° | `getMisheyakir11Point5Degrees()` | `GetMisheyakir11Point5Degrees()` |  |  |
| 13 | `sunriseAstronomical` | נץ אסטרונומי / בתיקון גובה | `getSunrise()` | `GetSunrise()` |  |  |
| 14 | `sunrise` | הנץ הנראה | `getSeaLevelSunrise()` | `GetSeaLevelSunrise()` | Y |  |
| 15 | `sofZmanShmaMGA90Degrees` | סוף זמן ק"ש / מג"א 90 דק׳ (מעלות) | `getSofZmanShmaMGA19Point8Degrees()` | `GetSofZmanShmaMGA19Point8Degrees()` | Y |  |
| 16 | `sofZmanShmaGRA` | סוף זמן ק"ש / גר"א | `getSofZmanShmaGRA()` | `GetSofZmanShmaGRA()` | Y |  |
| 17 | `sofZmanShmaMGA72Degrees` | סוף זמן ק"ש / מג"א 72 דק׳ (מעלות) | `getSofZmanShmaMGA16Point1Degrees()` | `GetSofZmanShmaMGA16Point1Degrees()` |  |  |
| 18 | `sofZmanShmaMGA72Shavos` | סוף זמן ק"ש / מג"א 72 דק׳ (שוות) | `getSofZmanShmaMGA72Minutes()` | `GetSofZmanShmaMGA72Minutes()` |  |  |
| 19 | `sofZmanShmaMGA72Zmanis` | סוף זמן ק"ש / מג"א 72 דק׳ (זמניות) | `getSofZmanShmaMGA72MinutesZmanis()` | `GetSofZmanShmaMGA72MinutesZmanis()` |  |  |
| 20 | `sofZmanShmaMGA90Shavos` | סוף זמן ק"ש / מג"א 90 דק׳ (שוות) | `getSofZmanShmaMGA90Minutes()` | `GetSofZmanShmaMGA90Minutes()` |  |  |
| 21 | `sofZmanShmaMGA90Zmanis` | סוף זמן ק"ש / מג"א 90 דק׳ (זמניות) | `getSofZmanShmaMGA90MinutesZmanis()` | `GetSofZmanShmaMGA90MinutesZmanis()` |  |  |
| 22 | `sofZmanShmaMGA120Shavos` | סוף זמן ק"ש / מג"א 120 דק׳ (שוות) | `getSofZmanShmaMGA120Minutes()` | `GetSofZmanShmaMGA120Minutes()` |  |  |
| 23 | `sofZmanTfilaMGA90Degrees` | סוף זמן תפילה / מג"א 90 דק׳ (מעלות) | `getSofZmanTfilaMGA19Point8Degrees()` | `GetSofZmanTfilaMGA19Point8Degrees()` | Y |  |
| 24 | `sofZmanTfilaGRA` | סוף זמן תפילה / גר"א | `getSofZmanTfilaGRA()` | `GetSofZmanTfilaGRA()` | Y |  |
| 25 | `sofZmanTfilaMGA72Degrees` | סוף זמן תפילה / מג"א 72 דק׳ (מעלות) | `getSofZmanTfilaMGA16Point1Degrees()` | `GetSofZmanTfilaMGA16Point1Degrees()` |  |  |
| 26 | `sofZmanTfilaMGA72Shavos` | סוף זמן תפילה / מג"א 72 דק׳ (שוות) | `getSofZmanTfilaMGA72Minutes()` | `GetSofZmanTfilaMGA72Minutes()` |  |  |
| 27 | `sofZmanTfilaMGA72Zmanis` | סוף זמן תפילה / מג"א 72 דק׳ (זמניות) | `getSofZmanTfilaMGA72MinutesZmanis()` | `GetSofZmanTfilaMGA72MinutesZmanis()` |  |  |
| 28 | `sofZmanTfilaMGA90Shavos` | סוף זמן תפילה / מג"א 90 דק׳ (שוות) | `getSofZmanTfilaMGA90Minutes()` | `GetSofZmanTfilaMGA90Minutes()` |  |  |
| 29 | `sofZmanTfilaMGA90Zmanis` | סוף זמן תפילה / מג"א 90 דק׳ (זמניות) | `getSofZmanTfilaMGA90MinutesZmanis()` | `GetSofZmanTfilaMGA90MinutesZmanis()` |  |  |
| 30 | `sofZmanTfilaMGA120Shavos` | סוף זמן תפילה / מג"א 120 דק׳ (שוות) | `getSofZmanTfilaMGA120Minutes()` | `GetSofZmanTfilaMGA120Minutes()` |  |  |
| 31 | `chatzos` | חצות היום | `getChatzos()` (= half-day midpoint of SEA-LEVEL sunrise/sunset) | `GetChatzosHayom()` with `UseAstronomicalChatzos=false` | Y |  |
| 32 | `chatzosFixedLocal` | חצות מקומי קבוע | `getFixedLocalChatzos()` | `GetFixedLocalChatzosHayom()` |  |  |
| 33 | `chatzosLayla` | חצות לילה | `calculateSolarMidnight` = midpoint(today getSunset(), tomorrow getSunrise()); fallback getChatzos()+12h | custom, same formula (needs 2nd calendar for date+1) | Y |  |
| 34 | `minchaGedolaGreater30` | מנחה גדולה / המאוחר | `getMinchaGedolaGreaterThan30()` | `GetMinchaGedolaGRAGreaterThan30()` | Y |  |
| 35 | `minchaGedolaGRA` | מנחה גדולה / גר"א | `getMinchaGedola()` | `GetMinchaGedolaGRA()` |  |  |
| 36 | `minchaGedola30` | מנחה גדולה / 30 דק׳ שוות | `getMinchaGedola30Minutes()` | `GetMinchaGedola30Minutes()` |  |  |
| 37 | `minchaGedola16point1` | מנחה גדולה / 16.1° | `getMinchaGedola16Point1Degrees()` | `GetMinchaGedola16Point1Degrees()` |  |  |
| 38 | `minchaKetanaGRA` | מנחה קטנה / גר"א | `getMinchaKetana()` | `GetMinchaKetanaGRA()` | Y |  |
| 39 | `minchaKetana16point1` | מנחה קטנה / 72 דק׳ (מעלות) | `getMinchaKetana16Point1Degrees()` | `GetMinchaKetana16Point1Degrees()` |  |  |
| 40 | `minchaKetana90Degrees` | מנחה קטנה / 90 דק׳ (מעלות) | `getMinchaKetana(getAlos19Point8Degrees(), getTzais19Point8Degrees())` (null if either is null) | `GetMinchaKetana(GetAlos19Point8Degrees(), GetTzais19Point8Degrees())` |  |  |
| 41 | `minchaKetana72` | מנחה קטנה / 72 דק׳ (שוות) | `getMinchaKetana72Minutes()` | `GetMinchaKetana72Minutes()` |  |  |
| 42 | `plagGRA` | פלג המנחה / גר"א | `getPlagHamincha()` | `GetPlagHaminchaGRA()` | Y |  |
| 43 | `plag72Shavos` | פלג המנחה / 72 דק׳ (שוות) | `getPlagHamincha72Minutes()` | `GetPlagHamincha72Minutes()` |  |  |
| 44 | `plag90Shavos` | פלג המנחה / 90 דק׳ (שוות) | `getPlagHamincha90Minutes()` | `GetPlagHamincha90Minutes()` |  |  |
| 45 | `plag120Shavos` | פלג המנחה / 120 דק׳ (שוות) | `getPlagHamincha120Minutes()` | `GetPlagHamincha120Minutes()` |  |  |
| 46 | `plag72Zmanis` | פלג המנחה / 72 דק׳ (זמניות) | `getPlagHamincha72MinutesZmanis()` | `GetPlagHamincha72MinutesZmanis()` |  |  |
| 47 | `plag90Zmanis` | פלג המנחה / 90 דק׳ (זמניות) | `getPlagHamincha90MinutesZmanis()` | `GetPlagHamincha90MinutesZmanis()` |  |  |
| 48 | `plag120Zmanis` | פלג המנחה / 120 דק׳ (זמניות) | `getPlagHamincha120MinutesZmanis()` | `GetPlagHamincha120MinutesZmanis()` |  |  |
| 49 | `plag72Degrees` | פלג המנחה / 72 דק׳ (מעלות) | `getPlagHamincha16Point1Degrees()` | `GetPlagHamincha16Point1Degrees()` |  |  |
| 50 | `plag90Degrees` | פלג המנחה / 90 דק׳ (מעלות) | `getPlagHamincha19Point8Degrees()` | `GetPlagHamincha19Point8Degrees()` |  |  |
| 51 | `plag120Degrees` | פלג המנחה / 120 דק׳ (מעלות) | `getPlagHamincha26Degrees()` | `GetPlagHamincha26Degrees()` |  |  |
| 52 | `plagAlosToSunset` | פלג המנחה / מעלוה"ש עד שקיעה | `getPlagAlosToSunset()` | `GetPlagAlosToSunset()` |  |  |
| 53 | `plagAlos16ToGeonim` | פלג המנחה / מ-16.1° עד גאונים 7.083° | `getPlagAlos16Point1ToTzaisGeonim7Point083Degrees()` | `GetPlagAlos16Point1DegreesToTzaisGeonim7Point083Degrees()` |  |  |
| 54 | `sunset` | שקיעה | `getSunset()` | `sunset` | Y |  |
| 55 | `seaLevelSunset` | שקיעה מישורית | `getSeaLevelSunset()` | `GetSeaLevelSunset()` | Y |  |
| 56 | `binRT13point24` | בין השמשות / ר"ת 13.24° | `getBainHasmashosRT13Point24Degrees()` | `GetBainHashmashosRT13Point24Degrees()` |  |  |
| 57 | `binRT58point5` | בין השמשות / ר"ת 58.5 דק׳ | `getBainHasmashosRT58Point5Minutes()` | `GetBainHashmashosRT58Point5Minutes()` |  |  |
| 58 | `binRT13point5` | בין השמשות / ר"ת 13.5 דק׳ | `getBainHasmashosRT13Point5MinutesBefore7Point083Degrees()` | `GetBainHashmashosRT13Point5MinutesBefore7Point083Degrees()` |  |  |
| 59 | `binYereim18` | בין השמשות / יראים 18 דק׳ | `getBainHasmashosYereim18Minutes()` | `GetBainHashmashosYereim18Minutes()` |  |  |
| 60 | `binYereim16point875` | בין השמשות / יראים 16.875 דק׳ | `getBainHasmashosYereim16Point875Minutes()` | `GetBainHashmashosYereim16Point875Minutes()` |  |  |
| 61 | `binYereim13point5` | בין השמשות / יראים 13.5 דק׳ | `getBainHasmashosYereim13Point5Minutes()` | `GetBainHashmashosYereim13Point5Minutes()` |  |  |
| 62 | `binYereim3point5` | בין השמשות / יראים 3.5° | `getBainHasmashosYereim3Point5Degrees()` | `GetBainHashmashosYereim3Point05Degrees()` |  |  |
| 63 | `binYereim2point8` | בין השמשות / יראים 2.8° | `getBainHasmashosYereim2Point8Degrees()` | `GetBainHashmashosYereim2Point8Degrees()` |  |  |
| 64 | `binYereim2point1` | בין השמשות / יראים 2.1° | `getBainHasmashosYereim2Point1Degrees()` | `GetBainHashmashosYereim2Point1Degrees()` |  |  |
| 65 | `tzeitTikochinsky` | צאת הכוכבים / טיקוצ׳ינסקי | `getSunset()?.add(const Duration(minutes: 13, seconds: 30))` | `sunset + 13m30s` | Y |  |
| 66 | `tzeitItimLeVina` | צאת הכוכבים / עיתים לבינה | `getSunset()?.add(const Duration(minutes: 18))` | `sunset + 18)` | Y |  |
| 67 | `tzeitGeonim3point65` | צאת הכוכבים / גאונים 3.65° | `getTzaisGeonim3Point65Degrees()` | `GetSunsetOffsetByDegrees(90 + 3.65)` |  |  |
| 68 | `tzeitGeonim7point083` | צאת הכוכבים / גאונים 7.083° | `getTzaisGeonim7Point083Degrees()` | `GetTzaisGeonim7Point083Degrees()` |  |  |
| 69 | `tzeitGeonim8point5` | צאת הכוכבים / גאונים 8.5° | `getTzaisGeonim8Point5Degrees()` | `GetTzaisGeonim8Point5Degrees()` |  |  |
| 70 | `tzeitChazonIsh` | צאת הכוכבים / חזו"א (9.3°) | `getTzaisGeonim9Point3Degrees()` | `GetTzaisGeonim9Point3Degrees()` |  |  |
| 71 | `rt72Shavos` | רבנו תם / 72 דק׳ (שוות) | `getSunset()?.add(const Duration(minutes: 72))` | `sunset + 72)` | Y |  |
| 72 | `rt72Zmanis` | רבנו תם / 72 דק׳ (זמניות) | `getTzais72Zmanis()` | `GetTzais72Zmanis()` |  |  |
| 73 | `rt72Degrees` | רבנו תם / 72 דק׳ (מעלות) | `getTzais16Point1Degrees()` | `GetTzais16Point1Degrees()` |  |  |
| 74 | `rt90Shavos` | רבנו תם / 90 דק׳ (שוות) | `getTzais90()` | `GetTzais90Minutes()` |  |  |
| 75 | `rt90Zmanis` | רבנו תם / 90 דק׳ (זמניות) | `getTzais90Zmanis()` | `GetTzais90Zmanis()` |  |  |
| 76 | `rt90Degrees` | רבנו תם / 90 דק׳ (מעלות) | `getTzais19Point8Degrees()` | `GetTzais19Point8Degrees()` |  |  |
| 77 | `rt120Shavos` | רבנו תם / 120 דק׳ (שוות) | `getTzais120()` | `GetTzais120Minutes()` |  |  |
| 78 | `rt120Zmanis` | רבנו תם / 120 דק׳ (זמניות) | `getTzais120Zmanis()` | `GetTzais120Zmanis()` |  |  |
| 79 | `rt120Degrees` | רבנו תם / 120 דק׳ (מעלות) | `getTzais26Degrees()` | `GetTzais26Degrees()` |  |  |
| 80 | `candleLighting` | הדלקת נרות | `getSunset() - getCandleLightingMinutes(city) min` | `sunset - city.CandleLightingMinutes min` | Y | isErevShabbosOrYomTov |
| 81 | `motzeiShabbos` | מוצאי שבת / רגיל | `getSunset()?.add(const Duration(minutes: 34))` | `sunset + 34)` | Y | isMotzeiShabbos |
| 82 | `motzeiShabbosChazonIsh` | מוצאי שבת / חזו"א | `getSunset()?.add(const Duration(minutes: 38))` | `sunset + 38)` | Y | isMotzeiShabbos |
| 83 | `motzeiYomTov` | מוצאי יום טוב / רגיל | `getSunset()?.add(const Duration(minutes: 34))` | `sunset + 34)` | Y | isMotzeiYomTov |
| 84 | `motzeiYomTovChazonIsh` | מוצאי יום טוב / חזו"א | `getSunset()?.add(const Duration(minutes: 38))` | `sunset + 38)` | Y | isMotzeiYomTov |
| 85 | `achilasChametzMGADegrees` | סוף זמן אכילת חמץ / מג"א (מעלות) | `getSofZmanAchilasChametzMGA16Point1Degrees()` | `GetSofZmanAchilasChametzMGA16Point1Degrees()` | Y | isErevPesach |
| 86 | `achilasChametzGRA` | סוף זמן אכילת חמץ / גר"א | `getSofZmanAchilasChametzGRA()` | `GetSofZmanAchilasChametzGRA()` | Y | isErevPesach |
| 87 | `achilasChametzMGAShavos` | סוף זמן אכילת חמץ / מג"א (שוות) | `getSofZmanAchilasChametzMGA72Minutes()` | `GetSofZmanAchilasChametzMGA72Minutes()` |  | isErevPesach |
| 88 | `biurChametzMGADegrees` | סוף זמן ביעור חמץ / מג"א (מעלות) | `getSofZmanBiurChametzMGA16Point1Degrees()` | `GetSofZmanBiurChametzMGA16Point1Degrees()` | Y | isErevPesach |
| 89 | `biurChametzGRA` | סוף זמן ביעור חמץ / גר"א | `getSofZmanBiurChametzGRA()` | `GetSofZmanBiurChametzGRA()` | Y | isErevPesach |
| 90 | `biurChametzMGAShavos` | סוף זמן ביעור חמץ / מג"א (שוות) | `getSofZmanBiurChametzMGA72Minutes()` | `GetSofZmanBiurChametzMGA72Minutes()` |  | isErevPesach |
| 91 | `fastEndTikLenient` | יציאת הצום / לקולא — טיקוצ׳ינסקי | `getSunset()?.add(const Duration(minutes: 13, seconds: 30))` | `sunset + 13m30s` | Y | isTaanisNotYomKippur |
| 92 | `fastEndItimLenient` | יציאת הצום / לקולא — עיתים לבינה | `getSunset()?.add(const Duration(minutes: 18))` | `sunset + 18)` | Y | isTaanisNotYomKippur |
| 93 | `fastEndTikStringent` | יציאת הצום / לחומרא — טיקוצ׳ינסקי | `getSunset()?.add(const Duration(minutes: 18))` | `sunset + 18)` | Y | isTaanisNotYomKippur |
| 94 | `fastEndItimStringent` | יציאת הצום / לחומרא — עיתים לבינה | `getSunset()?.add(const Duration(minutes: 22))` | `sunset + 22)` | Y | isTaanisNotYomKippur |
| 95 | `tchilasKidushLevana3` | תחילת קידוש לבנה / 3 ימים | `jc.getTchilasZmanKidushLevana3Days()` | see section 7.6 (raw molad + offset; NOT KJ3 GetTchilasZmanKidushLevana*) |  |  |
| 96 | `tchilasKidushLevana7` | תחילת קידוש לבנה / 7 ימים | `jc.getTchilasZmanKidushLevana7Days()` | see section 7.6 (raw molad + offset; NOT KJ3 GetTchilasZmanKidushLevana*) |  |  |
| 97 | `sofKidushLevanaMoldos` | סוף קידוש לבנה / אמצע מולדות | `jc.getSofZmanKidushLevanaBetweenMoldos()` | see section 7.6 (raw molad + offset; NOT KJ3 GetTchilasZmanKidushLevana*) |  |  |
| 98 | `sofKidushLevana15` | סוף קידוש לבנה / 15 ימים | `jc.getSofZmanKidushLevana15Days()` | see section 7.6 (raw molad + offset; NOT KJ3 GetTchilasZmanKidushLevana*) |  |  |

### 3.1 Notes on the table

* `getSunset()` / `getSunrise()` are **elevation-adjusted** (Jerusalem 800 m: sunset 16:45:32 vs sea-level 16:40:59 on 2026-11-13). `sunrise` ("הנץ הנראה") deliberately uses `getSeaLevelSunrise()` while `sunriseAstronomical` ("נץ אסטרונומי / בתיקון גובה") uses `getSunrise()`. All "fixed minute" zmanim (tzeit 13.5/18 min, Rabbeinu Tam 72, motzei 34/38, fast ends 13.5/18/22) are added to the **elevation-adjusted** `getSunset()` by Otzaria code, not by library methods.
* `tzeitTikochinsky` = sunset + 13m30s; `tzeitItimLeVina` = sunset + 18 min; `rt72Shavos` = sunset + 72 min; `fastEndTikLenient` = +13m30s, `fastEndItimLenient` = +18, `fastEndTikStringent` = +18, `fastEndItimStringent` = +22. `motzeiShabbos`/`motzeiYomTov` = +34 ("רגיל"), `...ChazonIsh` = +38.
* **Candle lighting is NOT KosherJava's `getCandleLighting()`**: it is `getSunset()` minus `getCandleLightingMinutes(city)` (`zmanim_helpers.dart:362-370`). Minutes per city: table at `calendar_location.dart:864-914` (Jerusalem 40, Modi'in Illit 30, Bnei Brak 30, Haifa 30, Beit Shemesh 30, Tel Aviv 18, Ashdod 18, NYC 18, ...), fallback `candleLightingMinutesByCountry` (USA 18, line 916-918), final fallback 30 (lines 921-924). Already merged into `otzaria-cities.json` as `candleLightingMinutes`.
* `chatzosLayla` (`calculateSolarMidnight`, `zmanim_helpers.dart:330-359`): midpoint of *today's* `getSunset()` and *tomorrow's* `getSunrise()` (second calendar built for date+1, elevation-adjusted); falls back to `getChatzos() + 12h`; null if no sunset. (Comment in code: kosher_dart's own `getSolarMidnight` is buggy with TZDateTime.)
* `chatzos` is the **half-day midpoint of sea-level sunrise/sunset** (kosher_dart `getChatzos() => getSunTransit()`, `zmanim_calendar.dart:207`, `astronomical_calendar.dart:417-424`) - up to ~30 s different from true astronomical noon. KosherJava 3 defaults to true astronomical noon, so for parity set `UseAstronomicalChatzos = false` (section 9.2).
* Defaults grouped as users see them: dawn (72 and 90 degrees-based), misheyakir 10.2, hanetz (visible, sea level), Shma (MGA 90 degrees / GRA), Tfila (MGA 90 degrees / GRA), chatzos, chatzos layla, mincha gedola (later of GRA / 30 min), mincha ketana GRA, plag GRA, sunset, sea-level sunset, tzeit (Tikochinsky, Itim LaBina), Rabbeinu Tam 72, candle lighting, motzei Shabbat/YT (regular and Chazon Ish), chametz times, fast ends.
* "Degree" zmanim: alos 16.1 = "72 degrees", 19.8 = "90 degrees", 26 = "120 degrees"; MGA 90-degrees = `...19Point8Degrees`; the 90-minute mincha ketana (row 40) is built manually from the 19.8 degree day.
* Not shown anywhere in the UI: Baal HaTanya zmanim, Ateret Torah, Ahavat Shalom, 18-degree variants (present in KosherJava, ignored by Otzaria).

### 3.2 "Only when" predicates (`zmanim_helpers.dart:376-395`)

* `isErevShabbosOrYomTov`: `dayOfWeek == 6 || (isErevYomTov() && dayOfWeek != 7)` (day of week 1 = Sunday ... 7 = Shabbat). Used for `candleLighting`. (It evaluates the *displayed* day only; it does not add candle lighting for the second Yom Tov night in the diaspora.)
* `isMotzeiShabbos`: `isAssurBemelacha() && dayOfWeek == 7 && !isTomorrowShabbosOrYomTov()`.
* `isMotzeiYomTov`: `isAssurBemelacha() && isYomTov() && dayOfWeek != 7 && !isTomorrowShabbosOrYomTov()`.
* `isErevPesach`: `getYomTovIndex() == EREV_PESACH`. `isTaanisNotYomKippur`: `isTaanis() && getYomTovIndex() != YOM_KIPPUR`.
* `JewishCalendar.inIsrael` must be set per city before evaluating these (Israel vs diaspora Yom Tov days and parsha).
* All of these exist 1:1 in `Kosher.Zmanim.JewishCalendar` (`IsErevYomTov`, `IsAssurBemelacha`, `IsTomorrowShabbosOrYomTov`, `GetYomTovIndex`, `IsTaanis`, `InIsrael`, `EREV_PESACH`/`ErevPesach` ...).

### 3.3 Which zmanim are user-selectable

All of them: the "זמנים נוספים" button (`calendar_times_panel.dart:398-407`, dialog `tools/calendar/dialogs/zmanim_settings_dialog.dart`) toggles each registry id. The selection is stored as a JSON array of ids in `key-calendar-enabled-zmanim` (empty/missing = defaults; parsing at `calendar_cubit.dart:427-436`). Per-zman alerts live in `key-calendar-zman-alerts` (JSON map id -> {minutesBefore, displayName}); alerts are out of scope for the widget.

## 4. Location

* Selection UI: a searchable dropdown labelled `עיר` with all 129 names sorted alphabetically (`calendar_times_panel.dart:628-657`; list = `getCalendarCityNames()`, `calendar_location.dart:935-936`). Changing it calls `CalendarCubit.changeCity`, which persists `key-selected-city`.
* Data: `const Map<String country, Map<String city, {lat, lng, elevation, timezone}>> cityCoordinates` (`calendar_location.dart:2-860`). Grouped by country (Hebrew names): `ארץ ישראל` (55 cities, all `Asia/Jerusalem`), `ארצות הברית` (19), then Canada, UK, France, Germany, Italy, Spain, Netherlands, Switzerland, Austria, Hungary, Czechia, Poland, Russia, Turkey, Portugal, Ireland, Sweden, Denmark, Finland, Norway, Iceland, Argentina, Brazil, Chile, Venezuela, Peru, Mexico, Morocco, Egypt, South Africa, UAE, Kuwait, India, Thailand, Singapore, HK, China, Japan, S. Korea, Australia (42 countries incl. Israel; 52 distinct IANA zones).
* **Israel flag** = city is under the country key `ארץ ישראל` (`isCityInIsrael`, lines 939-941). There is no separate flag in the data. Used for `JewishCalendar.inIsrael` (Yom Tov days, parsha, Chol HaMoed numbering, Shmini Atzeret/Simchat Torah labels).
* Defaults: city `ירושלים` (31.7683, 35.2137, 800 m) (`settings_repository.dart:501-504`; reset at 1262). Candle-lighting minutes default 30.
* A city not in the table yields no times (`buildZmanimCalendarContext` returns null). Arbitrary coordinates exist only in the plugin API (`calculateDailyTimesForCoordinates`, `zmanim_helpers.dart:87-112`; `plugin_bridge_adapter.dart:6405-6445`): kidush levana omitted, 30-min candle lighting, chatzos layla approximated.
* JSON export: `docs/research/otzaria-cities.json` (UTF-8):

```json
{ "name": "מודיעין עילית", "country": "ארץ ישראל", "lat": 31.9254, "lng": 35.0364,
  "elevation": 400.0, "timezone": "Asia/Jerusalem", "inIsrael": true, "candleLightingMinutes": 30 }
```

(Regenerate with `spikes/ZmanimSpike/dart-groundtruth/bin/dump.dart`; if Otzaria adds cities the widget should re-export.)

## 5. Settings (Hive box `app_preferences`) - keys and defaults from code

Declared in `settings/engine/settings_repository.dart` (keys 64-69, 207-211, 222-239; defaults 489-508, 605-628). Box name `app_preferences` (`data/data_providers/hive_data_provider.dart:65`).

| Key | Type | Default | Meaning |
|---|---|---|---|
| `key-selected-city` | string | `ירושלים` | city name (key into the city table) |
| `key-calendar-type` | string | `combined` | `hebrew` / `gregorian` / `combined` (UI only) |
| `key-calendar-day-transition` | string | `sunset` | `sunset` / `tzais` / `rabbeinuTam` / `midnight` - when the Hebrew day changes (section 6) |
| `key-calendar-twelve-hour-clock` | bool | `false` | 12h display of zmanim |
| `key-calendar-enabled-zmanim` | string (JSON array of ids) | `''` -> use registry defaults | selected zmanim |
| `key-calendar-zman-alerts` | string (JSON map) | `{}` | per-zman notifications |
| `key-calendar-notifications-enabled` / `-time` / `-sound` | bool / int / bool | `true` / `60` / `true` | event notifications |
| `key-calendar-events` | string (JSON) | `[]` | user calendar events |
| Google/ICS keys | | | not relevant |

**Location of the file**: Windows `%APPDATA%\otzaria\app_preferences.hive` (`core/app_paths.dart:171-173`); portable mode (a `portable.marker` file next to the exe) uses `<exe dir>\otzaria_data\` instead (`app_paths.dart:36-40,164-168`). Two older copies exist on this machine (`%APPDATA%\beit_hillel\`, `%APPDATA%\אוצריא\אוצריא\`); the live one is `%APPDATA%\otzaria\` (modified 2026-11-12).
**Reading it while Otzaria runs**: Hive locks the box exclusively (`app_preferences.lock`); Otzaria's own CLI path copies the file to a temp dir and opens the copy (`core/info/settings_snapshot.dart:72-110`). The format is an append-only frame log, so a copy taken during a write loses at most the last frame. The spike has a ~40-line reader (`OtzariaSettings` in `Program.cs`): frame = `uint32 LE length | 0x01, uint8 keyLen, utf8 key | typeId + payload | uint32 CRC`; typeId 4 = string (uint32 LE length + utf8), 3 = bool (1 byte); later frames override earlier ones. Reading the copy gave: `key-selected-city=מודיעין עילית`, `key-calendar-day-transition=midnight`, `key-calendar-type=combined`; `key-calendar-enabled-zmanim` and `key-calendar-twelve-hour-clock` absent (= defaults).
The widget should treat Otzaria's settings as optional input (fall back to Jerusalem / sunset / defaults) and offer its own override.

## 6. Which civil day is "today" (day transition) and the "אור ל" prefix

`resolveCalendarDayForTransition` (`calendar_cubit.dart:1621-1654`):

1. `nowInCity` = now in the city's zone; `civilToday` = its Y-M-D.
2. `midnight` -> `civilToday`.
3. Otherwise compute the transition instant of `civilToday` (`calendar_cubit.dart:1768-1787`): `sunset` -> `getSunset()` (elevation-adjusted); `tzais` -> `getTzais()` (8.5 degrees); `rabbeinuTam` -> `getSunset() + 72 min`. If `now >= transition` -> `civilToday + 1 day`, else `civilToday`. If the transition cannot be computed -> `civilToday`.

The resulting date drives *everything* shown as "today": Hebrew date (`JewishDate.fromDateTime(day)`), holidays, omer, parsha lookup, daf yomi (the library chip uses `todayGregorianDate`, `library_daf_yomi.dart:44-52`), and the zmanim computed for that civil date. Refresh timer fires at the next alos-90/transition boundary (`nextCalendarTodayRefreshTime`, 1713-1766).

"אור ל" prefix (`shouldShowOhrPrefixForCalendarHeader`, 1687-1711, shown in `calendar_top_bar.dart:204-209`): only when the selected day is today and `nowInCity < (getSunrise() - 90 min)` for the civil day (`_calculateAlos90`, 1789-1793; note: *elevation-adjusted* sunrise minus 90 minutes, not the 19.8 degree alos). The header then reads `אור ל` + the Hebrew date (the night leading into that Hebrew day) - independent of the transition setting.

The user's configured transition here is `midnight`, so `today = civil date`.

## 7. Hebrew date, holidays, parsha, omer, daf, molad

### 7.1 Hebrew date text - there are two spellings in Otzaria

* **Library top bar chip** (`library_daf_yomi.dart:53`, `getHebrewDateFormattedAsString`, `calendar_date_helpers.dart:65-70`): kosher_dart `HebrewDateFormatter()..hebrewFormat = true` -> `format(jc)`, e.g. `ג׳ כסלו תשפ״ז` (day with geresh/gershayim, **no** `ה׳` thousands), months as kosher_dart spells them: `ניסן אייר סיוון תמוז אב אלול תשרי חשוון כסלו טבת שבט אדר אדר א׳ אדר ב׳` (note **`סיוון`, `חשוון`**).
* **Calendar screen header** (`calendar_top_bar.dart:200-210`): own helpers. Day = `formatHebrewDay` (**no geresh/gershayim**: 3 -> `ג`, 15 -> `טו`, 16 -> `טז`, 25 -> `כה`, 30 -> `ל`; `calendar_date_helpers.dart:91-110`). Month = `kHebrewMonths` index by Nissan-first month number (`ניסן, אייר, סיון, תמוז, אב, אלול, תשרי, חשון, כסלו, טבת, שבט, אדר`; lines 14-27) with leap-year override `אדר א׳` (month 12) / `אדר ב׳` (month 13) (`getHebrewMonthNameFor`, 112-122; **`סיון`, `חשון`** here). Year = `formatHebrewYear` (125-145): `ה׳` + remainder in gematria with gershayim before the last letter, e.g. 5787 -> `ה׳תשפ״ז`. Whole line: `ג כסלו ה׳תשפ״ז  •  13 נובמבר 2026` (Hebrew + Gregorian with `kGregorianMonths` = `ינואר ... דצמבר`, no `ב` prefix).
* Characters: geresh U+05F3 `׳`, gershayim U+05F4 `״` (kosher_dart and KosherJava 3 both emit these; Otzaria's own code uses them in `formatHebrewYear` and `אדר א׳`; ASCII `'` and `"` are used in a few labels - Hoshana Rabba `ו' דחוה"מ`, `ר"ח`, `ק"ש`).
* Days of week: `kHebrewDays` = `ראשון שני שלישי רביעי חמישי שישי שבת` (lines 29-37; `kGregorianMonths` 39-52). Day-of-week numbering from `JewishCalendar` is 1 (Sunday) ... 7 (Shabbat).
* Recommendation: implement your own date formatter in C# (gematria is 20 lines) and choose explicitly between `חשוון/סיוון` (home-screen chip) and `חשון/סיון` (calendar header). KosherJava 3's `HebrewDateFormatter` produces `חשון`/`סיון` and otherwise the same gematria (`ג׳ כסלו תשפ״ז` verified identical for 2600 days).

### 7.2 Holiday / special-day labels (calendar grid cell, `calendar_day_cell.dart:316-380`; print view `calendar_print_helpers.dart:24-39`; plugin API `plugin_bridge_adapter.dart:7356-7425`)

Algorithm (port literally; `hdf` = kosher_dart `HebrewDateFormatter` with `hebrewFormat=true`, `jc.inIsrael` set):

1. `yomTov = hdf.formatYomTov(jc)`; split on `,`, trim, add each.
2. If `jc.dayOfWeek == 7` add `hdf.formatParsha(jc)` if non-empty.
3. If `jc.isRoshChodesh()` and the list lacks `ראש חודש` add `ר"ח` (formatYomTov never returns Rosh Chodesh, so `ר"ח` is always added).
4. Chol HaMoed (index `CHOL_HAMOED_SUCCOS`/`CHOL_HAMOED_PESACH`): remove entries containing `חול המועד`; add `<letter> דחוה"מ <פסח|סוכות>` where n = `jewishDayOfMonth - (inIsrael ? 15 : 16)` and letter = `א..ח` (no punctuation).
5. Index `PESACH` and day-of-month 21 -> replace `פסח` with `שביעי של פסח`.
6. Index `CHANUKAH`: remove entries containing `חנוכה`; add `נר <letter> דחנוכה` (letter from `getDayOfChanukah()`, `א..ח`).
7. Index `HOSHANA_RABBA`: add `ו' דחוה"מ` (Israel) / `ה' דחוה"מ` (diaspora).
8. Month 7: day 22 -> add `שמיני עצרת` (if missing) and, in Israel, `שמחת תורה`; day 23 and diaspora -> `שמחת תורה`.
9. De-duplicate preserving order.

kosher_dart `formatYomTov` vocabulary (observed over 2600 days): `א׳..ח׳ חנוכה`, `הושענא רבה`, `חול המועד סוכות`, `חול המועד פסח`, `ט״ו באב`, `ט״ו בשבט`, `יום כיפור`, `ל״ג בעומר`, `סוכות`, `ערב יום כיפור`, `ערב סוכות`, `ערב פסח`, `ערב ראש השנה`, `ערב שבועות`, `עשרה בטבת`, `פורים`, `פורים קטן`, `פורים שושן`, `פורים שושן קטן`, `פסח`, `פסח שני`, `צום גדליה`, `ראש השנה`, `שבועות`, `שבעה עשר בתמוז`, `שמיני עצרת`, `תענית אסתר`, `תשעה באב`. Modern holidays (Yom HaAtzmaut etc.) are **not** shown (`useModernHolidays` stays false).
KosherJava 3.1.1's formatter differs in exactly: `אסרו חג` (Isru Chag - kosher_dart returns empty: **filter it out**), `שושן פורים` / `שושן פורים קטן` (kosher_dart: `פורים שושן` / `פורים שושן קטן` - **remap**), and the 17 Tammuz label (kosher_dart wrongly prints it on both the 17th when it is Shabbat and the postponed 18th; KosherJava 3 is correct). Everything else matched across the whole 2600-day Israel and diaspora comparison. Alternatively build the label table yourself from `GetYomTovIndex()` constants.

Background colour: Shabbat, Yom Tov, taanis, Rosh Chodesh get a tinted cell (`calendar_day_cell.dart:10-26`).

### 7.3 Parsha

Only displayed on Shabbat cells and in plugin/tikkun payloads. `hdf.formatParsha(jc)` returns the parsha of that Shabbat (empty on a Shabbat that is Yom Tov / Chol HaMoed without a regular parsha), combined parshiyot joined with a space (`ויקהל פקודי`, `מטות מסעי`, `ניצבים וילך` ...). "Upcoming parsha" (tikkun/plugin): take the Saturday on or after the date (`tikkun_korim_bloc.dart:25-35`, `plugin_bridge_adapter.dart:7345-7360`), `inIsrael` set per user. 2026-11-13 (Friday) -> upcoming Shabbat 2026-11-14 -> **`תולדות`**.
.NET: `HebrewDateFormatter.FormatParshah(JewishCalendar)` (note spelling). Only difference: kosher_dart `ניצבים` / `ניצבים וילך` vs KosherJava 3 `נצבים` / `נצבים וילך` (remap). All other parsha strings are identical (checked 2024-09..2031-08, both Israel and diaspora). Special Shabbatot (שקלים, זכור...) are not displayed by Otzaria (`FormatSpecialParshah` exists in .NET if wanted).

### 7.4 Omer

Text (`calendar_times_panel.dart:276-373`), shown as a button when `getDayOfOmer() != -1` (1..49) for the selected day: `היום <N days> בעומר` for day < 7; else `היום <N days> שהם <weeks> בעומר` or `... שהם <weeks> ו<extra days> בעומר`. Day words: 1 `יום אחד`, 2 `שני ימים`, 3 `שלשה ימים`, 4 `ארבעה ימים`, 5 `חמשה ימים`, 6 `ששה ימים`, 7 `שבעה ימים`, 8 `שמונה ימים`, 9 `תשעה ימים`, 10 `עשרה ימים`, 11-19 `<unit-masc> עשר יום` (`אחד עשר יום`, `שנים עשר יום`, `שלשה עשר יום`, `ארבעה...`, `חמשה...`, `ששה...`, `שבעה...`, `שמונה...`, `תשעה עשר יום`), 20/30/40 `עשרים יום`/`שלשים יום`/`ארבעים יום`, 21-49 `<unit> ו<tens> יום` (e.g. 33 `שלשה ושלשים יום`). Weeks: 1 `שבוע אחד`, 2 `שני שבועות`, 3-7 `<שלשה|ארבעה|חמשה|ששה|שבעה> שבועות`. Example: day 33 -> `היום שלשה ושלשים יום שהם ארבעה שבועות וחמשה ימים בעומר`. (Note: Otzaria spells the unit forms `שלשה`/`חמשה`/`ששה` without the extra vav; copy the arrays verbatim.)
Omer alert time = `getTzais()` 8.5 degrees (`zmanim_helpers.dart:158-165`). The count belongs to the *evening that starts the halachic day*: because of the day transition (section 6) with sunset-based transition the count already advances at sunset; with `midnight` it advances at 00:00.
.NET: `GetDayOfOmer()` (-1 outside the omer) - identical for all 2600 days tested. Otzaria does not use `FormatOmer`.

### 7.5 Daf Yomi

* **Bavli only** (`YomiCalculator.getDafYomiBavli(jc)`, `calendar_date_helpers.dart:59-62`; `calendar_times_panel.dart:542-574`; `library_daf_yomi.dart:50-52`). **Yerushalmi is not shown anywhere in Otzaria.**
* Label: masechta (`daf.getMasechta()`, Hebrew name) + page in gematria **without** geresh/gershayim (`formatAmud`: `נ״ו` -> `נו`, `calendar_date_helpers.dart:79-83`). Examples 2026-11-13: `בכורות נו` (Library chip `בכורות נו`; calendar button `דף היומי: בכורות נו`). If the date is before the first cycle (11 Sep 1923) the calc throws; Otzaria shows `דף היומי בבלי` and an error toast (`calendar_times_panel.dart:526-532,566-567`).
* Clicking opens the book at that daf (`daf_yomi_navigation.dart`) - not applicable to the widget.
* .NET: `YomiCalculator.GetDafYomiBavli(jc)` -> `Daf.GetMasechta()` and `Daf.DafNumber`; identical for 2600 consecutive days (includes the Shekalim change day). `YerushalmiYomiCalculator.GetDafYomiYerushalmi(jc)` also works in .NET (`null` on Yom Kippur / Tisha B'Av) and is correct, whereas kosher_dart 2.0.20's Yerushalmi output is wrong (mismatches 66/800 days) - irrelevant unless the widget adds Yerushalmi.

### 7.6 Molad and kidush levana (optional for the widget)

* The "מולד" card (`calendar_times_panel.dart:423-435`, `molad_helpers.dart`) appears on Shabbat Mevorchim (shows next month's molad), on Rosh Chodesh (the month's molad) or on the Gregorian day of the *visible* molad (Meeus true conjunction, accurate to ~3 min, `molad_helpers.dart:301-465`). Announcement text built by `formatMoladAnnouncement` (`molad_helpers.dart:248-299`): `<יום> בשעה <h12> <בבוקר|בצהריים|אחר הצהריים|בערב|בלילה> ו-<m> דקות ו-<k> חלקים`. Raw fields are `getMolad()` hours/minutes/chalakim (Jerusalem local mean time, day of week 1..7). Verified: KosherJava 3 `GetMolad()` returns identical day/hour/minute/chalakim for 30 consecutive months.
* Kidush levana zmanim (4 optional registry entries, default off) use `JewishCalendar.getTchilasZmanKidushLevana3Days()/7Days()/getSofZmanKidushLevanaBetweenMoldos()/15Days()`, then `formatKidushLevanaNight` (`zmanim_helpers.dart:251-321`): treats the raw instant as Jerusalem *standard* time (UTC+2), converts to the city's zone, if it falls between alos (16.1) and tzais (8.5) pushes it to tzais (start-of-window items) or back to alos (end-of-window items), then prints `ליל <weekday> <day-of-month in gematria> לחודש HH:mm.` using `JewishDate` advanced by one day if after sunset. **Important**: kosher_dart's `getMoladAsDateTime()` forgets to subtract the 20m56.496s local-mean-time offset (the result of `cal.add` is discarded), so Otzaria's kidush levana instants are the raw molad wall-clock fields + 3/7/14d18h22m1.666s/15 days. KosherJava 3 `GetTchilasZmanKidushLevana3Days()` etc. return the correct LMT-corrected instants (~21 min earlier), so to match Otzaria compute them from `GetMolad()` fields as shown in `Program.cs` (`MoladDump`, `raw=`). `IsShabbosMevorchim()` exists in .NET.

## 8. Hebrew text conventions worth knowing

* Gematria: `formatHebrewNumber` (kosher_dart/KosherJava): 15 -> `ט״ו`, 16 -> `ט״ז`; single letters get geresh (`ד׳`), multi letters get gershayim before the last letter. 5787 -> `תשפ״ז` (kosher_dart/KosherJava default drops the thousands).
* Otzaria never uses nikud in zmanim labels.
* Right-to-left: times are wrapped in U+2066...U+2069 (section 2.1). Widget should do the same for mixed Hebrew/number strings.

## 9. The .NET side

### 9.1 Library evaluation

| Candidate | Verdict |
|---|---|
| **`Kosher.Zmanim` 3.1.1** (author Elyahu41; 3.1.0 and 3.1.1 published 2026-11-05; LGPL-2.1-only; targets `net8.0` and `net10.0`; no dependencies; native `DateTimeOffset/DateOnly/TimeSpan/TimeZoneInfo`; port of KosherJava 3.1.0; 680 documented members; ~146 downloads - brand new) | **Recommended.** Contains `ComprehensiveZmanimCalendar` (all zmanim in the registry except the few renamed/removed below), `JewishCalendar` (parsha, yom tov index, taanis, omer, Chanukah, isAssurBemelacha, molad, shabbos mevorchim), `HebrewDateFormatter` (Hebrew gematria output), `YomiCalculator` (Bavli), `YerushalmiYomiCalculator`, `TefilaRules`, NOAA/SPA/Meeus calculators. Supports IANA and Windows tz ids (ICU). |
| `Zmanim` 1.5.0 (`yitzchok`, published 2021-04-01, netstandard1.0-2.0, LGPL) | Rejected: older KosherJava, lacks 13 of the 86 methods Otzaria uses (all Yereim variants, `getTzaisGeonim9Point3Degrees`, `getSofZmanAchilasChametzGRA`, all kidush levana, `getDayOfOmer`), no Yerushalmi, no parsha *formatter*. |
| `Zmanim.NodaTime`, `Zmanim.Olson` (same author) | Not evaluated (extra deps; no benefit). |
| Write our own NOAA code | Unnecessary. The calculators are public and subclassable if exact kosher_dart parity is ever needed. |

Risks: single new maintainer and a very young package. Mitigations: pin the exact version, keep the spike's parity test (it is a regression test against Otzaria's real kosher_dart output), or vendor the source (LGPL allows modification, keep the licence and publish changes to the library itself). LGPL practical note: ship as a separate `Kosher.Zmanim.dll` (do not IL-merge/ILRepack or NativeAOT-link it into a monolith) and include the licence text.

### 9.2 Required configuration to behave like kosher_dart 2.0.20

```csharp
var tz  = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZone);       // "Asia/Jerusalem" works on Win10 1903+ (ICU); fall back to "Israel Standard Time" / TimeZoneConverter
var geo = new GeoLocation(city.Name, city.Lat, city.Lng, city.Elevation > 0 ? city.Elevation : 0, tz);   // Otzaria clamps negative elevation to 0
var c   = new ComprehensiveZmanimCalendar(geo) { LocalDate = date };       // DateOnly = the *calendar day* from section 6
c.AstronomicalCalculator.UseApparentSolarRadius = false;   // KJ3 default true shifts sunrise/sunset by 1-2 s vs kosher_dart's fixed 16'
c.UseAstronomicalChatzos = false;                          // kosher_dart chatzos = half-day midpoint, KJ3 default = true solar noon (differs up to 32 s)
c.UseAstronomicalChatzosForOtherZmanim = false;            // mincha gedola 30 min etc. follow the same rule
// c.UseElevation stays false (default) in both libraries; getSunrise()/getSunset() are elevation-adjusted, getSeaLevel*() are not.
var jc = new JewishCalendar(date) { InIsrael = city.InIsrael };
```

Method-name differences KosherJava 2 (kosher_dart) -> 3 (.NET): `Hasmashos` -> `Hashmashos`; `getAlos72/90/120()` -> `GetAlos72Minutes/90Minutes/120Minutes()`; `getTzais90/120()` -> `GetTzais90Minutes/120Minutes()`; `getChatzos()` -> `GetChatzosHayom()`; `getFixedLocalChatzos()` -> `GetFixedLocalChatzosHayom()`; `getBainHasmashosYereim3Point5Degrees()` -> `GetBainHashmashosYereim3Point05Degrees()` (same 3.05 degrees, kosher_dart's name is misleading); `getMinchaGedolaGreaterThan30()` -> `GetMinchaGedolaGRAGreaterThan30()`; `getPlagAlos16Point1ToTzaisGeonim7Point083Degrees()` -> `GetPlagAlos16Point1DegreesToTzaisGeonim7Point083Degrees()`; `getTzais()` (8.5 degrees) -> `GetTzaisGeonim8Point5Degrees()`; `getTzaisGeonim3Point65Degrees()` (not in KJ3) -> `GetSunsetOffsetByDegrees(90 + 3.65)`; `getTchilasZmanKidushLevana*` moved from `JewishCalendar`-raw semantics to LMT-corrected (see 7.6). Several methods are `[Obsolete]` "lechumra only" in KJ3 (alos 26/120, plag 72/90/120 variants, tzais 120/26): works, just suppress `CS0618`. KJ3 returns `null` (kosher_dart returns a number) when the sun never reaches the degree (London 2026-06-21 alos 16.1) and the Chametz methods return `null` unless the date is Erev Pesach.
Every zman returns `DateTimeOffset?` already in the city's local offset; apply Otzaria's display rule (`HH:mm` truncated + `.`/`:` by seconds) - never `Round`.

### 9.3 Parity results (what I ran)

Spike: `spikes/ZmanimSpike` (`dotnet run`; modes `report`, `dump`, `hebrew`, `molad`, `netcal`, `tz`). Ground truth: I copied Otzaria's real `zmanim_helpers.dart`, `zman_definition.dart`, `calendar_location.dart`, `calendar_days.dart` unchanged (one import line rewritten) into a small Dart project with `kosher_dart 2.0.20` and `timezone`, and dumped every registry zman to seconds. (The harness is saved in `spikes/ZmanimSpike/dart-groundtruth/` with a `HOWTO.txt` and `compare_dumps.py`.)

* **2026-11-13 (today), Jerusalem and Modi'in Illit, the 19 default zmanim relevant that day**, `HH:mm:ss`:

| id | label | Jerusalem Otzaria (kosher_dart) | Jerusalem .NET | Modi'in Illit Otzaria | Modi'in Illit .NET |
|---|---|---|---|---|---|
| `alos72Degrees` | עלות השחר / 72 דק׳ (מעלות) | 04:50:34 | 04:50:34 | 04:51:26 | 04:51:26 |
| `alos90Degrees` | עלות השחר / 90 דק׳ (מעלות) | 04:32:52 | 04:32:52 | 04:33:42 | 04:33:42 |
| `misheyakir10point2` | זמן ציצית ותפילין | 05:19:06 | 05:19:06 | 05:20:00 | 05:20:00 |
| `sunrise` | הנץ הנראה | 06:05:25 | 06:05:25 | 06:06:24 | 06:06:24 |
| `sofZmanShmaMGA90Degrees` | סוף זמן ק"ש / מג"א 90 דק׳ (מעלות) | 07:58:01 | 07:58:02 | 07:58:48 | 07:58:48 |
| `sofZmanShmaGRA` | סוף זמן ק"ש / גר"א | 08:44:18 | 08:44:18 | 08:45:09 | 08:45:09 |
| `sofZmanTfilaMGA90Degrees` | סוף זמן תפילה / מג"א 90 דק׳ (מעלות) | 09:06:25 | 09:06:25 | 09:07:10 | 09:07:10 |
| `sofZmanTfilaGRA` | סוף זמן תפילה / גר"א | 09:37:16 | 09:37:16 | 09:38:04 | 09:38:04 |
| `chatzos` | חצות היום | 11:23:12 | 11:23:12 | 11:23:54 | 11:23:54 |
| `chatzosLayla` | חצות לילה | 23:23:37 | 23:23:38 | 23:24:20 | 23:24:20 |
| `minchaGedolaGreater30` | מנחה גדולה / המאוחר | 11:53:12 | 11:53:12 | 11:53:54 | 11:53:54 |
| `minchaKetanaGRA` | מנחה קטנה / גר"א | 14:28:34 | 14:28:34 | 14:29:06 | 14:29:07 |
| `plagGRA` | פלג המנחה / גר"א | 15:34:46 | 15:34:46 | 15:35:15 | 15:35:15 |
| `sunset` | שקיעה | 16:45:32 | 16:45:32 | 16:44:38 | 16:44:38 |
| `seaLevelSunset` | שקיעה מישורית | 16:40:59 | 16:40:59 | 16:41:24 | 16:41:24 |
| `tzeitTikochinsky` | צאת הכוכבים / טיקוצ׳ינסקי | 16:59:02 | 16:59:02 | 16:58:08 | 16:58:08 |
| `tzeitItimLeVina` | צאת הכוכבים / עיתים לבינה | 17:03:32 | 17:03:32 | 17:02:38 | 17:02:38 |
| `rt72Shavos` | רבנו תם / 72 דק׳ (שוות) | 17:57:32 | 17:57:32 | 17:56:38 | 17:56:38 |
| `candleLighting` | הדלקת נרות | 16:05:32 | 16:05:32 | 16:14:38 | 16:14:38 |

  The two columns agree to the second in 35 of 38 cells and are 1 second apart in the other 3 (`sofZmanShmaMGA90Degrees` Jerusalem, `chatzosLayla` Jerusalem, `minchaKetanaGRA` Modi'in Illit) - none changes the displayed minute. Hebrew date `ג׳ כסלו תשפ״ז` (Friday, month 9, leap year), parasha `תולדות`, no yom tov, no omer, Bavli `בכורות נו`, candle lighting (Otzaria rule) 16:05.(32s) Jerusalem / 16:14.(38s) Modi'in Illit.
* **Wide run**: all 129 cities x 6 dates (2026-01-05, 03-20, 06-21, 09-10, 11-13, 12-21), 98 zmanim each (excluding chametz and polar nulls): 67,844 compared cells -> difference distribution (seconds, .NET minus Dart): -1: 1,528; 0: 51,549; +1: 14,765; +2: 1; -32: 1. Displayed-minute mismatches: 323 = **0.48%** (every one is a +-1 s boundary case; remaining 2 outliers: Tafrach rt72Zmanis +2 s, Reykjavik 2026-03-20 alos 26 degrees -32 s). Without the two settings in 9.2 a smaller run (7 cities x 10 dates, 6,209 cells) had 93 displayed-minute mismatches (1.5%; chatzos and mincha gedola off by up to 32 s, sunrise/sunset-based +-2 s); with them it had 23 (0.4%).
* The +-1 s residue is because kosher_dart floors every instant to whole seconds before deriving others, while KJ3 keeps milliseconds. It cannot be fully removed by configuration; if the widget needs bit-exact parity, floor `GetSunrise/GetSeaLevelSunrise/GetSunset/GetSeaLevelSunset` to whole seconds and compute the derived zmanim from those with the `(start, end)` overloads (e.g. `GetSofZmanShma(start, end)`), which KJ3 exposes - not tried.
* **Time-zone database differences**: Edmonton, Calgary, Vancouver (Nov-Dec 2026) and Casablanca (Jan 2026) differ by exactly 1 hour because Otzaria's bundled `timezone 0.11.2` IANA data says Edmonton -6 / Vancouver -7 / Casablanca 0 on 2026-11-13, while this Windows' tz data says -7 / -8 / 0. (`tz` mode: `America/Edmonton 2026-11-13 offset=-07:00`.) Israeli cities are unaffected. If diaspora fidelity matters bundle IANA data (e.g. `TimeZoneConverter`/NodaTime tzdb) rather than relying on the OS.
* Remaining differences: high-latitude `null`s (above); Hebrew strings (7.1-7.3); kidush levana (7.6).

### 9.4 Capability checklist for `Kosher.Zmanim` 3.1.1

| Need | API | Status |
|---|---|---|
| Hebrew date, leap year, month numbers (Nissan=1) | `JewishCalendar/JewishDate`: `JewishYear/JewishMonth/JewishDayOfMonth/IsJewishLeapYear()/DayOfWeek` | OK |
| Hebrew text | `HebrewDateFormatter{IsHebrewFormat=true}`: `Format`, `FormatMonth`, `FormatHebrewNumber`, `FormatYomTov`, `FormatParshah`, `FormatSpecialParshah`, `FormatOmer`, `FormatRoshChodesh`, `FormatDafYomiBavli/Yerushalmi` | OK, with the 5 string remaps |
| Parsha (Israel/diaspora) | `HebrewDateFormatter.FormatParshah(jc)` / `JewishCalendar.GetParshah()/GetUpcomingParshah()` | OK |
| Yom tov / taanis / chol hamoed / chanukah day / erev... | `GetYomTovIndex()`, `IsYomTov()`, `IsTaanis()`, `IsCholHamoed()`, `GetDayOfChanukah()`, `IsErevYomTov()`, `IsAssurBemelacha()`, `IsRoshChodesh()`, `IsErevRoshChodesh()` | OK |
| Omer | `GetDayOfOmer()` | OK |
| Daf Yomi Bavli | `YomiCalculator.GetDafYomiBavli(jc)` -> `Daf` | OK (identical to kosher_dart) |
| Daf Yomi Yerushalmi | `YerushalmiYomiCalculator.GetDafYomiYerushalmi(jc)` | OK (not in Otzaria) |
| Molad | `jc.GetMolad()` (`MoladHours/MoladMinutes/MoladChalakim/DayOfWeek`), `GetMoladAsDateTimeOffset()` | OK (visible molad via Meeus must be ported if wanted) |
| All registry zmanim | `ComprehensiveZmanimCalendar` + trivial arithmetic | 94 of 98 computable (direct call, rename per 9.2, or sunset +/- minutes); the 4 kidush levana entries need the raw-molad construction (7.6) |
| Candle lighting minutes per city | own table | custom (already in `otzaria-cities.json`) |
| Chatzos layla | own formula | custom |

## 10. .NET `System.Globalization.HebrewCalendar` - what it cannot do (probe: `dotnet run -- netcal`)

* Range: 1583-01-01 .. 2239-09-29 only (`MinSupportedDateTime/MaxSupportedDateTime`), Hebrew years 5343-5999; year 5342/6000 throw `ArgumentOutOfRangeException`. Otzaria/KosherJava have no such limit.
* Month numbering is Tishrei=1 (2026-11-13 -> year 5787 month 3 day 3), leap-year Adar I/II are months 6/7; KosherJava numbers Nissan=1 (Tishrei=7, Adar I=12, Adar II=13). Mixing them is a classic bug.
* It is a pure date arithmetic calendar. It has **no** parsha, yom tov/holiday logic, Israel-vs-diaspora, omer, daf yomi, molad, Rosh Chodesh/Shabbos Mevorchim, and no zmanim/sunrise at all.
* Formatting: via `he-IL` culture with `HebrewCalendar` it prints `ג' כסלו תשפ"ז` (ASCII `'` and `"`, not geresh/gershayim; `dddd` -> `יום שישי`), month names per CLDR (`חשוון`, ...), no `ה'` thousands control, no "אור ל" nuance, and the output depends on ICU/OS data.
* **Gotcha found on this machine**: the `he-IL` culture's *default* calendar is `HebrewCalendar`, so `DateOnly.ToString("yyyy-MM-dd")` and `DateOnly.Parse("2026-11-13")` under the current culture produced `תשפ"ז-ג'-ג'` / threw. Set `CultureInfo.CurrentCulture/DefaultThreadCurrentCulture = InvariantCulture` for machine formatting and parsing (the spike does).
* Good use in the widget: only as a cheap cross-check of year/month/day; not as the source of truth.

## 11. Gaps, traps and TODO list for the implementer

1. Re-implement in C#: registry (id, labels, pairing, defaults, relevance), candle lighting minutes, chatzos layla, fixed-minute zmanim, minchaKetana90Degrees, time formatter (truncate + `.`/`:` marker, LTR isolates, optional 12h), day-transition logic, "אור ל", omer text, holiday-label algorithm (7.2), own Hebrew date formatter.
2. String remaps when using KosherJava 3 formatter: `נצבים`->`ניצבים` (parsha, 2 forms), `שושן פורים`->`פורים שושן`, `שושן פורים קטן`->`פורים שושן קטן`, drop `אסרו חג`, month `חשון`->`חשוון` / `סיון`->`סיוון` **only** if you want the library-chip spelling.
3. Do not use `JewishCalendar` defaults blindly: always set `InIsrael` from the city and `UseModernHolidays=false`.
4. Kidush levana raw-molad quirk (7.6) if those zmanim are offered.
5. Visible (true) molad uses a custom Meeus algorithm in Otzaria (`molad_helpers.dart:285-465`); port it only if the molad card is wanted.
6. Tz data for Edmonton/Calgary/Vancouver/Casablanca disagrees between Otzaria and Windows (9.3); Israel fine.
7. `Kosher.Zmanim` is days old: pin 3.1.1, keep the parity test (`spikes/ZmanimSpike` + a stored Dart dump), watch for 3.1.x fixes.
8. The settings file may be locked; read a copy (section 5). Support `portable.marker` data dirs.
9. KosherJava 3 returns `null` where kosher_dart returns numbers at high latitudes; the UI must show `--`.
10. Otzaria's own "today" never flips at civil midnight unless the user chose `midnight` (this user did); do not default the widget to `DateTime.Today`.

## 12. Files produced

* `docs/research/calendar-and-zmanim.md` (this file)
* `docs/research/otzaria-cities.json` (129 cities, UTF-8)
* `spikes/ZmanimSpike/ZmanimSpike.csproj`, `Program.cs` (net8.0, `Kosher.Zmanim` 3.1.1), `sample-output-2026-11-13.txt`
  * `dotnet run` -> Hebrew date, parasha, daf (Bavli/Yerushalmi), omer, molad and all zmanim for Jerusalem and the Otzaria-configured city for 2026-11-13.
  * `dotnet run -- dump "ירושלים,בני ברק" "2026-11-13,2026-12-01"` -> machine lines `city|date|id|HH:mm:ss` (diff against the Dart dump).
  * `dotnet run -- hebrew 2026-09-01 800 israel|diaspora`, `-- molad`, `-- netcal`, `-- tz`.
* `spikes/ZmanimSpike/dart-groundtruth/` - the Dart harness that runs Otzaria's real zmanim code (see `HOWTO.txt`) plus `compare_dumps.py`.
