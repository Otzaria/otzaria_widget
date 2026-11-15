import Foundation

/// Otzaria's city table (calendar_location.dart), in Otzaria's source order (Israel first).
/// Names are the Hebrew keys Otzaria stores in `key-selected-city`.
public enum CityTable {
    public static let defaultCityName = "ירושלים"

    private struct Row: Decodable {
        let name: String
        let country: String
        let lat: Double
        let lng: Double
        let elevation: Double
        let timezone: String
        let inIsrael: Bool
        let candleLightingMinutes: Int
    }

    private struct Loaded {
        let cities: [CityInfo]
        let byName: [String: CityInfo]
        let byNormalized: [String: CityInfo]
    }

    private static let loaded: Loaded = {
        let rows = (try? JSONDecoder().decode([Row].self, from: Data(EmbeddedData.citiesJSON.utf8))) ?? []
        let cities = rows.map {
            CityInfo(name: $0.name, country: $0.country, latitude: $0.lat, longitude: $0.lng, elevation: $0.elevation,
                     timeZoneId: $0.timezone, isInIsrael: $0.inIsrael, candleLightingMinutes: $0.candleLightingMinutes)
        }
        var byName: [String: CityInfo] = [:]
        var byNorm: [String: CityInfo] = [:]
        for c in cities {
            byName[c.name] = c
            byNorm[normalize(c.name)] = c
        }
        return Loaded(cities: cities, byName: byName, byNormalized: byNorm)
    }()

    public static var all: [CityInfo] { loaded.cities }

    public static var `default`: CityInfo { find(defaultCityName) ?? all[0] }

    /// Exact Hebrew name first, then a normalized match (trim, geresh/apostrophe variants, whitespace, maqaf).
    public static func find(_ name: String?) -> CityInfo? {
        guard let name, !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        if let exact = loaded.byName[name] { return exact }
        return loaded.byNormalized[normalize(name)]
    }

    /// Lookup normalization: trims, collapses whitespace, unifies the apostrophe family to ASCII ', quotes to ", dashes to '-',
    /// and drops RTL/LTR marks.
    public static func normalize(_ s: String) -> String {
        var out = String.UnicodeScalarView()
        var lastSpace = true
        let trimmed = s.trimmingCharacters(in: .whitespacesAndNewlines)
        for u in trimmed.unicodeScalars {
            let c: Unicode.Scalar?
            switch u.value {
            case 0x27, 0x2019, 0x2018, 0x2032, 0x05F3, 0x60, 0xB4: c = "'"
            case 0x22, 0x05F4, 0x201C, 0x201D, 0x201E: c = "\""
            case 0x2013, 0x2014, 0x2011, 0x05BE, 0x2010: c = "-"
            case 0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x2066, 0x2067, 0x2068, 0x2069, 0xFEFF: c = nil
            default: c = u
            }
            guard let c else { continue }
            if CharacterSet.whitespacesAndNewlines.contains(c) {
                if !lastSpace { out.append(" ") }
                lastSpace = true
                continue
            }
            out.append(c)
            lastSpace = false
        }
        var result = String(out)
        while result.hasSuffix(" ") { result.removeLast() }
        return result
    }
}
