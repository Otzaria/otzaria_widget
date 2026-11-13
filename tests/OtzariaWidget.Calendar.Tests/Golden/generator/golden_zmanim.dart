import 'package:kosher_dart/kosher_dart.dart';
import 'package:otzaria/tools/calendar/helpers/zmanim_helpers.dart';
import 'package:otzaria/tools/calendar/models/calendar_location.dart';
import 'package:otzaria/tools/calendar/models/zman_definition.dart';
import 'package:timezone/data/latest_all.dart' as tzdata;
import 'package:timezone/timezone.dart' as tz;

String p2(int n) => n.toString().padLeft(2, '0');

// Mirrors _computeDailyTimes (zmanim_helpers.dart) but emits seconds too.
void main(List<String> args) {
  tzdata.initializeTimeZones();
  final cities = args[0].split(',');
  final dates = args[1].split(',');
  for (final city in cities) {
    for (final ds in dates) {
      final parts = ds.split('-').map(int.parse).toList();
      final date = DateTime(parts[0], parts[1], parts[2]);
      final ctx0 = buildZmanimCalendarContext(date, city)!;
      final jc = JewishCalendar.fromDateTime(date)..inIsrael = isCityInIsrael(city);
      final ctx = ZmanComputeContext(cal: ctx0.zmanimCalendar, jewishCalendar: jc, city: city, date: date);
      final texts = calculateDailyTimes(date, city);
      for (final def in kZmanimRegistry) {
        if (def.isRelevant != null && !def.isRelevant!(jc)) continue;
        DateTime? dt;
        try { dt = def.compute(ctx); } catch (_) { dt = null; }
        if (dt == null) continue;
        if (def.showHebrewDate) {
          final label = formatKidushLevanaNight(dt, city, pushToTzais: def.moladPushToTzais);
          if (label != null) print('$city|$ds|${def.id}|=$label');
        } else {
          final l = tz.TZDateTime.from(dt, ctx0.tzLocation);
          print('$city|$ds|${def.id}|${p2(l.hour)}:${p2(l.minute)}:${p2(l.second)}|${texts[def.id]}');
        }
      }
    }
  }
}
