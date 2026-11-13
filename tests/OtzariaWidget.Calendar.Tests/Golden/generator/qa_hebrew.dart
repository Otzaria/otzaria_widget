import 'dart:io';
import 'dart:convert';
import 'package:kosher_dart/kosher_dart.dart';
import 'package:otzaria/utils/calendar_days.dart';

const List<String> kHebrewMonths = [
  'ניסן',
  'אייר',
  'סיון',
  'תמוז',
  'אב',
  'אלול',
  'תשרי',
  'חשון',
  'כסלו',
  'טבת',
  'שבט',
  'אדר',
];

const List<String> kHebrewDays = [
  'ראשון',
  'שני',
  'שלישי',
  'רביעי',
  'חמישי',
  'שישי',
  'שבת',
];

const List<String> kGregorianMonths = [
  'ינואר',
  'פברואר',
  'מרץ',
  'אפריל',
  'מאי',
  'יוני',
  'יולי',
  'אוגוסט',
  'ספטמבר',
  'אוקטובר',
  'נובמבר',
  'דצמבר',
];

// ═══════════════════════════════════════════════════════════════════════════
//  חישובי תאריך עברי בסיסיים
// ═══════════════════════════════════════════════════════════════════════════

/// מחזיר את הדף היומי (בבלי) לתאריך נתון
Daf getDafYomi(DateTime date) {
  JewishCalendar jewishCalendar = JewishCalendar.fromDateTime(date);
  return YomiCalculator.getDafYomiBavli(jewishCalendar);
}

/// מחזיר תאריך עברי מפורמט כמחרוזת
String getHebrewDateFormattedAsString(DateTime dateTime) {
  final hebrewCalendar = JewishCalendar.fromDateTime(dateTime);
  HebrewDateFormatter hebrewDateFormatter = HebrewDateFormatter()
    ..hebrewFormat = true;
  return hebrewDateFormatter.format(hebrewCalendar);
}

/// מחזיר חותמת זמן עברית (תאריך + שעה)
String getHebrewTimeStamp() {
  final now = DateTime.now();
  return '${getHebrewDateFormattedAsString(now)} ${now.hour}:${now.minute}:${now.second}';
}

/// מעצב מספר עמוד לאותיות עבריות ללא גרשיים
String formatAmud(int amud) {
  return HebrewDateFormatter()
      .formatHebrewNumber(amud)
      .replaceAll('״', '')
      .replaceAll('׳', '');
}

// ═══════════════════════════════════════════════════════════════════════════
//  עיצוב תאריכים לתצוגה
// ═══════════════════════════════════════════════════════════════════════════

/// ממיר מספר יום עברי לאותיות (ללא גרשיים)
String formatHebrewDay(int number) {
  if (number <= 0) return '';
  String result = '';
  int num = number;
  const ones = ['', 'א', 'ב', 'ג', 'ד', 'ה', 'ו', 'ז', 'ח', 'ט'];
  const tens = ['', 'י', 'כ', 'ל', 'מ', 'נ', 'ס', 'ע', 'פ', 'צ'];
  if (num == 15) {
    result += 'טו';
  } else if (num == 16) {
    result += 'טז';
  } else {
    if (num >= 10) {
      result += tens[num ~/ 10];
      num %= 10;
    }
    if (num > 0) result += ones[num];
  }
  return result;
}

/// מחזיר שם החודש העברי (כולל אדר א/ב בשנה מעוברת)
String getHebrewMonthNameFor(JewishDate jewishDate) {
  final int m = jewishDate.getJewishMonth();
  final bool leap = jewishDate.isJewishLeapYear();
  if (leap && m == 12) return 'אדר א׳';
  if (leap && m == 13) return 'אדר ב׳';
  final int idx = (m - 1).clamp(0, kHebrewMonths.length - 1);
  return kHebrewMonths[idx];
}

/// מחזיר שם חודש לועזי
String getGregorianMonthName(int month) => kGregorianMonths[month - 1];

/// מעצב שנה עברית בפורמט ה׳תשפ״ה
String formatHebrewYear(int year) {
  final hdf = HebrewDateFormatter()..hebrewFormat = true;
  final thousands = year ~/ 1000;
  final remainder = year % 1000;
  String remainderStr = hdf
      .formatHebrewNumber(remainder)
      .replaceAll('"', '')
      .replaceAll("'", '')
      .replaceAll('׳', '')
      .replaceAll('״', '');

  String formattedRemainder;
  if (remainderStr.length > 1) {
    formattedRemainder =
        '${remainderStr.substring(0, remainderStr.length - 1)}״${remainderStr.substring(remainderStr.length - 1)}';
  } else if (remainderStr.length == 1) {
    formattedRemainder = '$remainderStr׳';
  } else {
    formattedRemainder = remainderStr;
  }
  return thousands == 5 ? 'ה׳$formattedRemainder' : formattedRemainder;
}


  String numberToHebrewLetter(int n) {
    const letters = ['א', 'ב', 'ג', 'ד', 'ה', 'ו', 'ז', 'ח'];
    if (n > 0 && n <= letters.length) return letters[n - 1];
    return '';
  }

  // משותף לכל התאים: formatYomTov/formatParsha מקבלים את היומן כארגומנט
  // ואינם שומרים מצב. אין לשנות עליו שדות — זה ישפיע על כל הלוח.
  final HebrewDateFormatter hdfCell = HebrewDateFormatter()
    ..hebrewFormat = true;

  List<String> calcJewishEvents(JewishCalendar jc) {
    final List<String> l = [];

    final yomTov = hdfCell.formatYomTov(jc);
    if (yomTov.isNotEmpty) {
      l.addAll(yomTov.split(',').map((e) => e.trim()));
    }

    if (jc.getDayOfWeek() == 7) {
      final parsha = hdfCell.formatParsha(jc);
      if (parsha.isNotEmpty) l.add(parsha);
    }

    if (jc.isRoshChodesh() && !l.contains('ראש חודש')) l.add('ר"ח');

    final yomTovIndex = jc.getYomTovIndex();

    if (yomTovIndex == JewishCalendar.CHOL_HAMOED_SUCCOS ||
        yomTovIndex == JewishCalendar.CHOL_HAMOED_PESACH) {
      l.removeWhere((e) => e.contains('חול המועד'));
      final dayOfCholHamoed =
          jc.getJewishDayOfMonth() - (jc.inIsrael ? 15 : 16);
      final cholHamoedName = yomTovIndex == JewishCalendar.CHOL_HAMOED_PESACH
          ? 'פסח'
          : 'סוכות';
      l.add('${numberToHebrewLetter(dayOfCholHamoed)} דחוה"מ $cholHamoedName');
    }

    if (yomTovIndex == JewishCalendar.PESACH) {
      final dayOfMonth = jc.getJewishDayOfMonth();
      if (dayOfMonth == 21) {
        l.removeWhere((e) => e == 'פסח');
        l.add('שביעי של פסח');
      }
    }

    if (yomTovIndex == JewishCalendar.CHANUKAH) {
      l.removeWhere((e) => e.contains('חנוכה'));
      final dayOfChanukah = jc.getDayOfChanukah();
      if (dayOfChanukah != -1) {
        l.add('נר ${numberToHebrewLetter(dayOfChanukah)} דחנוכה');
      }
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

  String buildOmerCountingText(int day) {
    final totalDaysText = buildOmerDayCountText(day);
    final weeks = day ~/ 7;
    final extraDays = day % 7;

    if (weeks == 0) {
      return 'היום $totalDaysText בעומר';
    }

    final weeksText = buildOmerWeekCountText(weeks);
    if (extraDays == 0) {
      return 'היום $totalDaysText שהם $weeksText בעומר';
    }

    final extraDaysText = buildOmerDayCountText(extraDays);
    return 'היום $totalDaysText שהם $weeksText ו$extraDaysText בעומר';
  }

  String buildOmerDayCountText(int day) {
    const ones = [
      '',
      'יום אחד',
      'שני ימים',
      'שלשה ימים',
      'ארבעה ימים',
      'חמשה ימים',
      'ששה ימים',
      'שבעה ימים',
      'שמונה ימים',
      'תשעה ימים',
      'עשרה ימים',
      'אחד עשר יום',
      'שנים עשר יום',
      'שלשה עשר יום',
      'ארבעה עשר יום',
      'חמשה עשר יום',
      'ששה עשר יום',
      'שבעה עשר יום',
      'שמונה עשר יום',
      'תשעה עשר יום',
    ];
    const tens = ['', '', 'עשרים', 'שלשים', 'ארבעים'];
    const onesSimple = [
      '',
      'אחד',
      'שנים',
      'שלשה',
      'ארבעה',
      'חמשה',
      'ששה',
      'שבעה',
      'שמונה',
      'תשעה',
    ];

    if (day <= 0 || day > 49) {
      return 'יום $day';
    }

    if (day < 20) {
      return ones[day];
    }

    final tensText = tens[day ~/ 10];
    final onesValue = day % 10;
    if (onesValue == 0) {
      return '$tensText יום';
    }

    return '${onesSimple[onesValue]} ו$tensText יום';
  }

  String buildOmerWeekCountText(int weeks) {
    const oneToNine = [
      '',
      'אחד',
      'שני',
      'שלשה',
      'ארבעה',
      'חמשה',
      'ששה',
      'שבעה',
      'שמונה',
      'תשעה',
    ];

    if (weeks <= 0) {
      return '';
    }
    if (weeks == 1) {
      return 'שבוע אחד';
    }
    if (weeks == 2) {
      return 'שני שבועות';
    }

    return '${oneToNine[weeks]} שבועות';
  }


String defaultUpcomingParashaName(
  DateTime date, {
  required bool inIsrael,
}) {
  final daysUntilShabbat = date.weekday == DateTime.saturday
      ? 0
      : (DateTime.saturday - date.weekday) % 7;
  final shabbat = JewishCalendar.fromDateTime(
    addCalendarDays(date, daysUntilShabbat),
  )..inIsrael = inIsrael;
  return (HebrewDateFormatter()..hebrewFormat = true).formatParsha(shabbat);
}


String p2(int n) => n.toString().padLeft(2, '0');
void main(List<String> a) {
  final p = a[0].split('-').map(int.parse).toList();
  final days = int.parse(a[1]);
  final inIsrael = a[2] == 'israel';
  final sink = File(a[3]).openWrite(encoding: utf8);
  for (var i = 0; i < days; i++) {
    final d = DateTime(p[0], p[1], p[2] + i);
    final jc = JewishCalendar.fromDateTime(d)..inIsrael = inIsrael;
    final jd = JewishDate.fromDateTime(d);
    final top = '${formatHebrewDay(jd.getJewishDayOfMonth())} ${getHebrewMonthNameFor(jd)} ${formatHebrewYear(jd.getJewishYear())}';
    String daf = '';
    try { final f = YomiCalculator.getDafYomiBavli(jc); daf = '${f.getMasechta()} ${formatAmud(f.getDaf())}'; } catch (_) {}
    final omer = jc.getDayOfOmer() == -1 ? '' : buildOmerCountingText(jc.getDayOfOmer());
    final ev = calcJewishEvents(jc);
    final par = defaultUpcomingParashaName(d, inIsrael: inIsrael);
    sink.writeln('${d.year}-${p2(d.month)}-${p2(d.day)}|$top|${kHebrewDays[d.weekday % 7]}|${ev.join(';')}|$par|$daf|$omer|${jc.isYomTovAssurBemelacha() ? 1 : 0}');
  }
  sink.close();
}
