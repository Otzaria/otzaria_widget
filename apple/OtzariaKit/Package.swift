// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "OtzariaKit",
    defaultLocalization: "he",
    platforms: [.macOS(.v14), .iOS(.v17)],
    products: [
        .library(name: "OtzariaCore", targets: ["OtzariaCore"]),
        // The apps link OtzariaCalendar and the dynamic OtzariaWidgetsUI, so Xcode builds OtzariaCalendar and the
        // LGPL KosherSwift as two separate embedded frameworks. A same-named dynamic product would prevent that.
        .library(name: "OtzariaCalendar", targets: ["OtzariaCalendar"]),
        .library(name: "OtzariaWidgetsUI", type: .dynamic, targets: ["OtzariaWidgetsUI"]),
    ],
    dependencies: [
        .package(url: "https://github.com/Elyahu41/KosherSwift.git", .upToNextMinor(from: "3.1.1")),
    ],
    targets: [
        .target(name: "OtzariaCore"),
        .target(
            name: "OtzariaCalendar",
            dependencies: [.product(name: "KosherSwift", package: "KosherSwift")]
        ),
        .target(name: "OtzariaWidgetsUI", dependencies: ["OtzariaCore", "OtzariaCalendar"]),
        .testTarget(name: "OtzariaCoreTests", dependencies: ["OtzariaCore"]),
        .testTarget(name: "OtzariaWidgetsUITests", dependencies: ["OtzariaWidgetsUI", "OtzariaCore", "OtzariaCalendar"]),
        .testTarget(
            name: "OtzariaCalendarTests",
            dependencies: ["OtzariaCalendar"],
            resources: [.copy("Golden")]
        ),
    ]
)
