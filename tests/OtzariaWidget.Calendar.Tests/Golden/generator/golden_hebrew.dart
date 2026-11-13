import 'dart:io';
import 'dart:convert';
import 'package:kosher_dart/kosher_dart.dart';
import 'package:otzaria/tools/calendar/helpers/zmanim_helpers.dart';

String p2(int n) => n.toString().padLeft(2, '0');

String formatAmud(int amud) => HebrewDateFormatter().formatHebrewNumber(amud).replaceAll('״', '').replaceAll('׳', '');

String numberToHebrewLetter(int n) {
  const letters = ['א', 'ב', 'ג', 'ד', 'ה', 'ו', 'ז', 'ח'];
  if (n > 0 && n <= letters.length) return letters[n - 1];
  return '';
}

final HebrewDateFormatter hdf = HebrewDateFormatter()..hebrewFormat = true;

// Verbatim port of CalendarDayCell._calcJewishEvents (calendar_day_cell.dart:316-380)
List<String> calcJewishEvents(JewishCalendar jc) {
  final List<String> l = [];
  final yomTov = hdf.formatYomTov(jc);
  if (yomTov.isNotEmpty) l.addAll(yomTov.split(',').map((e) => e.trim()));
  if (jc.getDayOfWeek() == 7) {
    final parsha = hdf.formatParsha(jc);
    if (parsha.isNotEmpty) l.add(parsha);
  }
  if (jc.isRoshChodesh() && !l.contains('ראש חודש')) l.add('ר"ח');
  final yomTovIndex = jc.getYomTovIndex();
  if (yomTovIndex == JewishCalendar.CHOL_HAMOED_SUCCOS || yomTovIndex == JewishCalendar.CHOL_HAMOED_PESACH) {
    l.removeWhere((e) => e.contains('חול המועד'));
    final dayOfCholHamoed = jc.getJewishDayOfMonth() - (jc.inIsrael ? 15 : 16);
    final cholHamoedName = yomTovIndex == JewishCalendar.CHOL_HAMOED_PESACH ? 'פסח' : 'סוכות';
    l.add('${numberToHebrewLetter(dayOfCholHamoed)} דחוה"מ $cholHamoedName');
  }
  if (yomTovIndex == JewishCalendar.PESACH) {
    if (jc.getJewishDayOfMonth() == 21) {
      l.removeWhere((e) => e == 'פסח');
      l.add('שביעי של פסח');
    }
  }
  if (yomTovIndex == JewishCalendar.CHANUKAH) {
    l.removeWhere((e) => e.contains('חנוכה'));
    final d = jc.getDayOfChanukah();
    if (d != -1) l.add('נר ${numberToHebrewLetter(d)} דחנוכה');
  }
  if (yomTovIndex == JewishCalendar.HOSHANA_RABBA) {
    l.add("${jc.inIsrael ? 'ו' : 'ה'}' דחוה\"מ");
  }
  if (jc.getJewishMonth() == 7) {
    if (jc.getJewishDayOfMonth() == 22) {
      if (!l.contains('שמיני עצרת')) l.add('שמיני עצרת');
      if (jc.inIsrael && !l.contains('שמחת תורה')) l.add('שמחת תורה');
    }
    if (jc.getJewishDayOfMonth() == 23 && !jc.inIsrael) {
      if (!l.contains('שמחת תורה')) l.add('שמחת תורה');
    }
  }
  return l.toSet().toList();
}

String omerText(int day) {
  final totalDaysText = omerDay(day);
  final weeks = day ~/ 7;
  final extraDays = day % 7;
  if (weeks == 0) return 'היום $totalDaysText בעומר';
  final weeksText = omerWeek(weeks);
  if (extraDays == 0) return 'היום $totalDaysText שהם $weeksText בעומר';
  return 'היום $totalDaysText שהם $weeksText ו${omerDay(extraDays)} בעומר';
}

String omerDay(int day) {
  const ones = ['', 'יום אחד', 'שני ימים', 'שלשה ימים', 'ארבעה ימים', 'חמשה ימים', 'ששה ימים', 'שבעה ימים', 'שמונה ימים', 'תשעה ימים', 'עשרה ימים', 'אחד עשר יום', 'שנים עשר יום', 'שלשה עשר יום', 'ארבעה עשר יום', 'חמשה עשר יום', 'ששה עשר יום', 'שבעה עשר יום', 'שמונה עשר יום', 'תשעה עשר יום'];
  const tens = ['', '', 'עשרים', 'שלשים', 'ארבעים'];
  const onesSimple = ['', 'אחד', 'שנים', 'שלשה', 'ארבעה', 'חמשה', 'ששה', 'שבעה', 'שמונה', 'תשעה'];
  if (day <= 0 || day > 49) return 'יום $day';
  if (day < 20) return ones[day];
  final tensText = tens[day ~/ 10];
  final o = day % 10;
  if (o == 0) return '$tensText יום';
  return '${onesSimple[o]} ו$tensText יום';
}

String omerWeek(int weeks) {
  const oneToNine = ['', 'אחד', 'שני', 'שלשה', 'ארבעה', 'חמשה', 'ששה', 'שבעה', 'שמונה', 'תשעה'];
  if (weeks <= 0) return '';
  if (weeks == 1) return 'שבוע אחד';
  if (weeks == 2) return 'שני שבועות';
  return '${oneToNine[weeks]} שבועות';
}

String upcomingParasha(DateTime date, bool inIsrael) {
  final daysUntilShabbat = date.weekday == DateTime.saturday ? 0 : (DateTime.saturday - date.weekday) % 7;
  final shabbat = JewishCalendar.fromDateTime(DateTime(date.year, date.month, date.day + daysUntilShabbat))..inIsrael = inIsrael;
  return hdf.formatParsha(shabbat);
}

// args: startDate days israel|diaspora outFile
void main(List<String> args) {
  final p = args[0].split('-').map(int.parse).toList();
  final days = int.parse(args[1]);
  final inIsrael = args[2] == 'israel';
  final out = StringBuffer();
  for (var i = 0; i < days; i++) {
    final d = DateTime(p[0], p[1], p[2] + i);
    final jc = JewishCalendar.fromDateTime(d)..inIsrael = inIsrael;
    String daf;
    try { final f = YomiCalculator.getDafYomiBavli(jc); daf = '${f.getMasechta()} ${formatAmud(f.getDaf())}'; } catch (_) { daf = ''; }
    final omer = jc.getDayOfOmer() == -1 ? '' : omerText(jc.getDayOfOmer());
    final events = calcJewishEvents(jc);
    // fields: date|hebrewChip|events(;)|upcomingParasha|daf|omer|assurBemelacha(YT)|dow|y-m-d
    out.writeln('${d.year}-${p2(d.month)}-${p2(d.day)}|${hdf.format(jc)}|${events.join(';')}|${upcomingParasha(d, inIsrael)}|$daf|$omer|${jc.isYomTovAssurBemelacha() ? 1 : 0}|${jc.getDayOfWeek()}|${jc.getJewishYear()}-${jc.getJewishMonth()}-${jc.getJewishDayOfMonth()}|${kZmanimRegistry.where((x) => x.isRelevant != null && x.isRelevant!(jc)).map((x) => x.id).join(',')}');
  }
  File(args[3]).writeAsStringSync(out.toString(), encoding: utf8);
}
