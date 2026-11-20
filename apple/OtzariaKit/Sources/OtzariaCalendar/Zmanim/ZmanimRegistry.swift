import Foundation

/// The 98 Otzaria zmanim in Otzaria's display order (kZmanimRegistry).
public enum ZmanimRegistry {
    private struct Row: Decodable {
        let id: String
        let title: String
        let subtitle: String?
        let category: String
        let description: String?
        let defaultEnabled: Bool
        let showHebrewDate: Bool
        let pairId: String?
        let pairLabel: String?
    }

    private struct Loaded {
        let entries: [ZmanRegistryEntry]
        let definitions: [ZmanDefinition]
        let defaultEnabled: [String]
        let indexById: [String: Int]
    }

    private static let loaded: Loaded = {
        let rows = (try? JSONDecoder().decode([Row].self, from: Data(EmbeddedData.registryJSON.utf8))) ?? []
        let entries = rows.map {
            ZmanRegistryEntry(id: $0.id, title: $0.title, subtitle: $0.subtitle ?? "", category: $0.category,
                              description: $0.description ?? "", defaultEnabled: $0.defaultEnabled,
                              showHebrewDate: $0.showHebrewDate, pairId: $0.pairId, pairLabel: $0.pairLabel ?? "")
        }
        var index: [String: Int] = [:]
        for (i, e) in entries.enumerated() { index[e.id] = i }
        return Loaded(
            entries: entries,
            definitions: entries.map { ZmanDefinition(id: $0.id, title: $0.fullName, description: $0.description) },
            defaultEnabled: entries.filter(\.defaultEnabled).map(\.id),
            indexById: index)
    }()

    public static var all: [ZmanRegistryEntry] { loaded.entries }
    public static var definitions: [ZmanDefinition] { loaded.definitions }
    public static var defaultEnabled: [String] { loaded.defaultEnabled }

    public static func find(_ id: String) -> ZmanRegistryEntry? { loaded.indexById[id].map { loaded.entries[$0] } }
    public static func indexOf(_ id: String) -> Int { loaded.indexById[id] ?? -1 }
}
