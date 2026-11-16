import Foundation
import OtzariaCalendar
import OtzariaCore

// MARK: Zmanim

struct ZmanRow: Equatable {
    var name: String
    var timeText: String
    var time: Date?
    var isNext = false
    var isPast = false
    /// Kidush levana style label for zmanim of another civil day.
    var note = ""
}

struct ZmanimModel: Equatable {
    var state: ContentState
    var cityName = ""
    var headerEnd = ""
    var tomorrow = false
    var next: ZmanRow?
    /// Medium: the three zmanim after the highlighted one.
    var after: [ZmanRow] = []
    /// Large: window around the next zman, then zmanim of other days / not occurring.
    var rows: [ZmanRow] = []
    var url = WidgetLinks.url(WidgetLinks.calendar)

    static func timeText(_ t: ZmanTime, city: CityInfo, twelveHour: Bool) -> String {
        t.time.map { WidgetContext.calendarService.plainTime($0, city: city, twelveHour: twelveHour) } ?? "--"
    }

    /// "ליל שישי ג׳ לחודש" from the kidush levana label (the label minus its time).
    static func otherDayLabel(_ t: ZmanTime) -> String {
        var text = HebrewFormat.stripBidi(t.displayText).trimmingCharacters(in: .whitespacesAndNewlines)
        if let i = text.lastIndex(of: " "), text[text.index(after: i)...].contains(where: \.isNumber) {
            text = String(text[..<i]).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return text
    }

    static func make(_ ctx: WidgetContext, _ o: ZmanimOptions) -> ZmanimModel {
        let cal = WidgetContext.calendarService
        let city = ctx.city(widgetCity: o.city)
        let ids = ctx.zmanimIds(custom: o.customIds)
        let day = cal.zmanimDay(now: ctx.now, city: city, ids: ids, dayTransition: ctx.dayTransition)
        let hebrew = cal.dayInfo(for: day.date, city: city).hebrewDate
        let headerEnd = [city.name, day.isTomorrow ? "מחר" : "", hebrew].filter { !$0.isEmpty }.joined(separator: " · ")
        var model = ZmanimModel(state: .ok, cityName: city.name, headerEnd: headerEnd, tomorrow: day.isTomorrow)
        guard !day.times.isEmpty else {
            model.state = .empty("אין זמנים להצגה")
            return model
        }
        let tw = ctx.twelveHour
        func row(_ t: ZmanTime, index: Int? = nil) -> ZmanRow {
            ZmanRow(name: t.title, timeText: timeText(t, city: city, twelveHour: tw), time: t.time,
                    isNext: index == day.nextIndex, isPast: (index ?? Int.max) < day.nextIndex)
        }
        let nextIdx = day.nextIndex
        if nextIdx < day.times.count {
            model.next = row(day.times[nextIdx], index: nextIdx)
            model.after = (nextIdx + 1..<min(day.times.count, nextIdx + 4)).map { row(day.times[$0], index: $0) }
        }
        if ctx.size == .large {
            let count = 9
            let before = min(2, nextIdx)
            var start = max(0, nextIdx - before)
            if start + count > day.times.count { start = max(0, day.times.count - count) }
            var rows = (start..<min(day.times.count, start + count)).map { row(day.times[$0], index: $0) }
            for o in day.otherDays where rows.count < count {
                var r = row(o)
                r.note = otherDayLabel(o)
                rows.append(r)
            }
            for u in day.untimed where rows.count < count {
                var r = row(u)
                r.isPast = true
                rows.append(r)
            }
            model.rows = rows
        }
        return model
    }

    /// Moments at which the display changes (a second after each zman, the day switch and midnight).
    static func refreshDates(_ ctx: WidgetContext, _ o: ZmanimOptions, count: Int = 12) -> [Date] {
        let city = ctx.city(widgetCity: o.city)
        return WidgetContext.calendarService
            .refreshMoments(after: ctx.now, count: count, city: city, ids: ctx.zmanimIds(custom: o.customIds), dayTransition: ctx.dayTransition)
            .map { $0.addingTimeInterval(1) }
    }
}

// MARK: Hebrew calendar

struct CalendarLine: Equatable {
    var label: String
    var text: String
    var emphasize = false
}

struct HebrewCalendarModel: Equatable {
    var state: ContentState = .ok
    var cityName = ""
    var day = ""
    var year = ""
    var weekday = ""
    var gregorian = ""
    var lines: [CalendarLine] = []
    var url = WidgetLinks.url(WidgetLinks.calendar)

    /// "ג׳ כסלו תשפ״ז" -> ("ג׳ כסלו", "תשפ״ז").
    static func splitHebrewDate(_ hebrew: String) -> (day: String, year: String) {
        let h = hebrew.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let i = h.lastIndex(of: " ") else { return (h, "") }
        let last = String(h[h.index(after: i)...])
        let yearLike = last.count >= 3 && (last.first == "ת" || last.first == "ה") && (last.contains("״") || last.contains("\""))
        return yearLike ? (String(h[..<i]).trimmingCharacters(in: .whitespaces), last) : (h, "")
    }

    static func weekdayName(_ di: DayInfo) -> String {
        nonBlank(di.hebrewDayOfWeek) != nil ? di.hebrewDayOfWeek : HebrewFormat.weekdays[di.date.dayOfWeek - 1]
    }

    static func make(_ ctx: WidgetContext, _ o: CalendarOptions) -> HebrewCalendarModel {
        let cal = WidgetContext.calendarService
        let city = ctx.city(widgetCity: nil)
        let date = cal.effectiveDate(now: ctx.now, city: city, dayTransition: ctx.dayTransition)
        let di = cal.dayInfo(for: date, city: city)
        let (day, year) = splitHebrewDate(di.hebrewDate)
        var model = HebrewCalendarModel(
            cityName: city.name, day: day, year: year, weekday: weekdayName(di),
            gregorian: HebrewFormat.gregorianLong(year: date.year, month: date.month, day: date.day))

        var lines: [CalendarLine] = di.holidays.compactMap(nonBlank).map { CalendarLine(label: "", text: $0, emphasize: true) }
        func time(_ d: Date) -> String { cal.plainTime(d, city: city, twelveHour: ctx.twelveHour) }
        if o.showCandle {
            if let c = di.candleLighting { lines.append(CalendarLine(label: "הדלקת נרות", text: time(c))) }
            if let h = di.havdalah { lines.append(CalendarLine(label: "הבדלה", text: time(h))) }
        }
        if let p = di.parasha.flatMap(nonBlank)?.trimmingCharacters(in: .whitespaces) {
            lines.append(p.hasPrefix("פרשת") || p.hasPrefix("פרשה") ? CalendarLine(label: "", text: p) : CalendarLine(label: "פרשה", text: p))
        }
        if o.showOmer, let omer = di.omer.flatMap(nonBlank) { lines.append(CalendarLine(label: "", text: omer)) }
        if o.showDaf, let daf = di.dafYomiBavli.flatMap(nonBlank) { lines.append(CalendarLine(label: "דף יומי", text: daf)) }
        if o.showYerushalmi ?? ctx.settings.showYerushalmiDaf, let y = di.dafYomiYerushalmi.flatMap(nonBlank) {
            lines.append(CalendarLine(label: "ירושלמי", text: y))
        }
        let cap = ctx.size == .small ? 1 : ctx.size == .medium ? 3 : 6
        model.lines = Array(lines.prefix(cap))
        return model
    }

    static func refreshDates(_ ctx: WidgetContext, count: Int = 4) -> [Date] {
        let city = ctx.city(widgetCity: nil)
        return WidgetContext.calendarService.refreshMoments(
            after: ctx.now, count: count, city: city, ids: [], dayTransition: ctx.dayTransition).map { $0.addingTimeInterval(1) }
    }
}

// MARK: Dashboard

struct DashLine: Equatable {
    var text: String
    var sub = ""
    var meta = ""
    var bar: Double?
    var emphasize = false
    var accent = false
    var url: URL
    /// Live countdown target (next zman line, large layout).
    var countdownTo: Date?
    var tomorrow = false
}

struct DashSection: Equatable {
    var section: DashboardSection
    var title: String
    var lead: String
    var lines: [DashLine]
}

struct DashboardModel: Equatable {
    var state: ContentState = .ok
    var compact = true
    var sections: [DashSection] = []
    var url = WidgetLinks.url(WidgetLinks.library)

    static let titles: [DashboardSection: String] = [
        .date: "התאריך היום", .zman: "הזמן הבא", .reading: "המשך קריאה", .progress: "שמור וזכור", .bookmarks: "סימניות",
    ]
    static let leads: [DashboardSection: String] = [
        .date: "תאריך", .zman: "הזמן הבא", .reading: "קריאה", .progress: "שמור וזכור", .bookmarks: "סימניות",
    ]

    /// Approximate height of a section with `lines` lines in the large layout.
    static func height(_ s: DashboardSection, lines: Int) -> Int { s == .progress ? 40 * lines : 40 + 24 * (lines - 1) }

    static func make(_ ctx: WidgetContext, _ o: DashboardOptions) -> DashboardModel {
        let compact = ctx.size != .large
        var model = DashboardModel(compact: compact)
        var seen = Set<DashboardSection>()
        let enabled = o.sections.filter { seen.insert($0).inserted }
        if enabled.isEmpty {
            model.state = .empty("לא נבחרו חלקים להצגה — התאם את הווידג'ט")
            return model
        }
        let maxSections = ctx.size == .small ? 2 : ctx.size == .medium ? 3 : 5
        var extra: [DashboardSection: Int] = [:]
        if !compact {
            let shown = Array(enabled.prefix(maxSections))
            var used = shown.reduce(0) { $0 + height($1, lines: 1) }
            for key in [DashboardSection.reading, .progress, .bookmarks, .zman] where shown.contains(key) {
                let add = height(key, lines: 2) - height(key, lines: 1)
                if used + add > 224 { continue }
                extra[key] = 1
                used += add
            }
        }

        var anyMissing = false
        for key in enabled {
            if model.sections.count >= maxSections { break }
            let lines = 1 + (extra[key] ?? 0)
            var built: [DashLine]?
            switch key {
            case .date: built = dateLines(ctx, compact: compact)
            case .zman: built = zmanLines(ctx, lines)
            case .reading: built = readingLines(ctx, lines, o.includePdfs, &anyMissing)
            case .progress: built = progressLines(ctx, lines, &anyMissing)
            case .bookmarks: built = bookmarkLines(ctx, lines, &anyMissing)
            }
            guard var ls = built, !ls.isEmpty else { continue }
            if compact {
                for i in ls.indices {
                    if ls[i].meta.isEmpty { ls[i].meta = HebrewFormat.clip(ls[i].sub, 16) }
                    ls[i].sub = ""
                    ls[i].bar = nil
                    ls[i].countdownTo = nil
                }
            }
            model.sections.append(DashSection(section: key, title: titles[key]!, lead: leads[key]!, lines: ls))
        }
        if model.sections.isEmpty {
            model.state = anyMissing ? .missing(DataText.noSnapshot) : .empty("אין מה להציג כרגע")
        }
        return model
    }

    private static func dateLines(_ ctx: WidgetContext, compact: Bool) -> [DashLine] {
        let cal = WidgetContext.calendarService
        let city = ctx.city(widgetCity: nil)
        let date = cal.effectiveDate(now: ctx.now, city: city, dayTransition: ctx.dayTransition)
        let di = cal.dayInfo(for: date, city: city)
        let weekday = HebrewCalendarModel.weekdayName(di)
        let extra = di.holidays.compactMap(nonBlank).first ?? di.parasha.flatMap(nonBlank)
        let greg = HebrewFormat.gregorianLong(year: date.year, month: date.month, day: date.day)
        let sub = ([weekday, greg] + (extra.map { [$0] } ?? [])).joined(separator: " · ")
        return [DashLine(text: di.hebrewDate, sub: compact ? "" : sub, meta: compact ? weekday : "", emphasize: true,
                         url: WidgetLinks.url(WidgetLinks.calendar))]
    }

    private static func zmanLines(_ ctx: WidgetContext, _ count: Int) -> [DashLine] {
        let cal = WidgetContext.calendarService
        let city = ctx.city(widgetCity: nil)
        let day = cal.zmanimDay(now: ctx.now, city: city, ids: ctx.zmanimIds(custom: []), dayTransition: ctx.dayTransition)
        var res: [DashLine] = []
        for i in day.nextIndex..<max(day.nextIndex, min(day.times.count, day.nextIndex + count)) {
            let t = day.times[i]
            var line = DashLine(text: t.title, meta: ZmanimModel.timeText(t, city: city, twelveHour: ctx.twelveHour),
                                url: WidgetLinks.url(WidgetLinks.calendar))
            if i == day.nextIndex {
                line.accent = true
                line.emphasize = true
                line.countdownTo = t.time
                line.tomorrow = day.isTomorrow
            }
            res.append(line)
        }
        return res
    }

    private static func readingLines(_ ctx: WidgetContext, _ count: Int, _ includePdfs: Bool, _ missing: inout Bool) -> [DashLine]? {
        if DataText.missingReason(ctx.snapshot, sources: [SourceNames.userState]) != nil { missing = true; return nil }
        let snap = ctx.snapshot!
        var res: [DashLine] = []
        var seen = Set<String>()
        func add(_ e: ReadingEntry, link: String?) {
            let r = ReadingRows.row(e, now: ctx.now, showLocation: true, meta: "")
            res.append(DashLine(text: r.title, sub: r.sub, url: link.map { WidgetLinks.url($0) } ?? r.url))
        }
        if let tab = ReadingRows.currentTab(snap, includePdfs: includePdfs) {
            add(tab.reading, link: tab.link.flatMap { $0.isEmpty ? nil : $0 })
            seen.insert(ReadingRows.key(tab.reading))
        }
        for e in snap.recentBooks where includePdfs || !e.book.isPdf {
            if res.count >= count { break }
            if seen.insert(ReadingRows.key(e)).inserted { add(e, link: nil) }
        }
        return res
    }

    private static func progressLines(_ ctx: WidgetContext, _ count: Int, _ missing: inout Bool) -> [DashLine]? {
        if DataText.missingReason(ctx.snapshot, sources: [SourceNames.preferences, SourceNames.progress]) != nil { missing = true; return nil }
        return ctx.snapshot!.shamorZachor.filter { ShamorZachorModel.matches($0, .inProgress) }.prefix(count).map {
            DashLine(text: HebrewFormat.clip($0.title, 50), meta: HebrewFormat.percent($0.headline), bar: $0.headline,
                     url: WidgetLinks.url($0.deepLink, fallback: WidgetLinks.shamorZachor))
        }
    }

    private static func bookmarkLines(_ ctx: WidgetContext, _ count: Int, _ missing: inout Bool) -> [DashLine]? {
        if DataText.missingReason(ctx.snapshot, sources: [SourceNames.userState]) != nil { missing = true; return nil }
        return ctx.snapshot!.bookmarks.prefix(count).map { b in
            let label = b.label.flatMap(nonBlank)
            return DashLine(text: HebrewFormat.clip(label ?? b.book.title, 50),
                            sub: HebrewFormat.clip(label != nil ? b.book.title : HebrewFormat.stripTitle(b.ref, b.book.title), 50),
                            url: WidgetLinks.url(b.deepLink))
        }
    }

    static func refreshDates(_ ctx: WidgetContext, _ o: DashboardOptions, count: Int = 8) -> [Date] {
        let city = ctx.city(widgetCity: nil)
        let ids = o.sections.contains(.zman) ? ctx.zmanimIds(custom: []) : []
        return WidgetContext.calendarService.refreshMoments(
            after: ctx.now, count: count, city: city, ids: ids, dayTransition: ctx.dayTransition).map { $0.addingTimeInterval(1) }
    }
}
