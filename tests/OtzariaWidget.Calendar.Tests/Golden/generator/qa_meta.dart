import 'dart:io';
import 'dart:convert';
import 'package:otzaria/tools/calendar/helpers/zmanim_helpers.dart';
import 'package:otzaria/tools/calendar/models/calendar_location.dart';
void main() {
  final out = <String, dynamic>{};
  out['zmanim'] = [for (final d in kZmanimRegistry) {'id': d.id, 'title': d.title, 'subtitle': d.subtitle, 'category': d.category, 'default': d.defaultEnabled, 'hebDate': d.showHebrewDate, 'push': d.moladPushToTzais, 'pairId': d.pairId, 'pairLabel': d.pairLabel, 'fullName': d.fullName, 'explanation': d.explanation, 'special': d.isHolidaySpecial}];
  out['cities'] = [for (final c in getCalendarCityNames()) {'name': c, 'data': getCityData(c), 'israel': isCityInIsrael(c), 'candle': getCandleLightingMinutes(c), 'country': getCityCountry(c)}];
  File('meta.json').writeAsStringSync(jsonEncode(out), encoding: utf8);
}
