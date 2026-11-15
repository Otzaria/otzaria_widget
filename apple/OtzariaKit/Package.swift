// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "OtzariaKit",
    defaultLocalization: "he",
    platforms: [.macOS(.v14), .iOS(.v17)],
    products: [
        .library(name: "OtzariaCore", targets: ["OtzariaCore"]),
        // Dynamic so the LGPL KosherSwift code ships as a separately replaceable framework.
        .library(name: "OtzariaCalendar", type: .dynamic, targets: ["OtzariaCalendar"]),
        .library(name: "OtzariaWidgetsUI", targets: ["OtzariaWidgetsUI"]),
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
        .testTarget(
            name: "OtzariaCalendarTests",
            dependencies: ["OtzariaCalendar"],
            resources: [.copy("Golden")]
        ),
    ]
)
