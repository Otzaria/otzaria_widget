import 'dart:io';
import 'package:kosher_dart/kosher_dart.dart';
import 'package:otzaria/tools/calendar/helpers/zmanim_helpers.dart';
import 'package:otzaria/tools/calendar/models/calendar_location.dart';
import 'package:otzaria/tools/calendar/models/zman_definition.dart';
import 'package:timezone/data/latest_all.dart' as tzdata;
import 'package:timezone/timezone.dart' as tz;

String p2(int n) => n.toString().padLeft(2, '0');

// usage: gt2 <cities comma | ALL> <dates comma> [onlyDefaults]
void main(List<String> args) {
  tzdata.initializeTimeZones();
  final cities = args[0] == 'ALL' ? getCalendarCityNames() : args[0].split(',');
  final dates = args[1].split(',');
  final ids = <String>{for (final d in kZmanimRegistry) d.id};
  final out = StringBuffer();
  for (final city in cities) {
    for (final ds in dates) {
      final pr = ds.split('-').map(int.parse).toList();
      final date = DateTime(pr[0], pr[1], pr[2]);
      // Otzaria's real public entry point (display strings incl. LTR isolates + marker)
      final texts = calculateDailyTimes(date, city);
      // seconds-resolution via def.compute
      final ctx0 = buildZmanimCalendarContext(date, city)!;
      final jc = JewishCalendar.fromDateTime(date)..inIsrael = isCityInIsrael(city);
      final ctx = ZmanComputeContext(cal: ctx0.zmanimCalendar, jewishCalendar: jc, city: city, date: date);
      for (final def in kZmanimRegistry) {
        final t = texts[def.id];
        if (t == null) continue;
        String sec = '';
        if (!def.showHebrewDate) {
          try {
            final dt = def.compute(ctx);
            if (dt != null) { final l = tz.TZDateTime.from(dt, ctx0.tzLocation); sec = '${p2(l.hour)}:${p2(l.minute)}:${p2(l.second)}'; }
          } catch (_) {}
        }
        out.writeln('$city|$ds|${def.id}|$sec|$t');
      }
    }
  }
  stdout.write(out.toString());
}
