import Foundation

struct DailyStatsRow {
    let date: String
    let day: String
    let hours: String
}

private struct DailyStatsArchive: Codable {
    let version: Int
    var activeSecondsByDate: [String: TimeInterval]

    init(activeSecondsByDate: [String: TimeInterval] = [:]) {
        version = 1
        self.activeSecondsByDate = activeSecondsByDate
    }
}

struct DailyStatsPersistenceError: LocalizedError {
    enum Operation: String {
        case read = "read"
        case decode = "decode"
        case createDirectory = "create the storage directory for"
        case encode = "encode"
        case write = "write"
    }

    let operation: Operation
    let path: String
    let reason: String

    var errorDescription: String? {
        "Unable to \(operation.rawValue) productivity statistics."
    }

    var failureReason: String? {
        reason
    }

    var recoverySuggestion: String? {
        "Check \(path) and its permissions."
    }
}

final class DailyStatsStore {
    private let fileManager: FileManager
    private let fileURL: URL

    init(fileManager: FileManager = .default) {
        self.fileManager = fileManager
        fileURL = fileManager.homeDirectoryForCurrentUser
            .appendingPathComponent(
                "Library/Application Support/ProductivityTracker",
                isDirectory: true
            )
            .appendingPathComponent("daily-stats.json")
    }

    func load() throws -> [String: TimeInterval] {
        guard fileManager.fileExists(atPath: fileURL.path) else {
            return [:]
        }

        let data: Data
        do {
            data = try Data(contentsOf: fileURL)
        } catch {
            throw persistenceError(.read, underlying: error)
        }

        let archive: DailyStatsArchive
        do {
            archive = try JSONDecoder().decode(DailyStatsArchive.self, from: data)
        } catch {
            throw persistenceError(.decode, underlying: error)
        }

        guard archive.version == 1,
              archive.activeSecondsByDate.values.allSatisfy({
                  $0.isFinite &&
                  $0 >= 0 &&
                  $0 <= DailyStatsRecorder.maximumDailySeconds
              }) else {
            throw DailyStatsPersistenceError(
                operation: .decode,
                path: fileURL.path,
                reason: "The saved statistics have an unsupported or invalid format."
            )
        }
        return archive.activeSecondsByDate
    }

    func save(_ activeSecondsByDate: [String: TimeInterval]) throws {
        let directory = fileURL.deletingLastPathComponent()
        do {
            try fileManager.createDirectory(
                at: directory,
                withIntermediateDirectories: true
            )
        } catch {
            throw persistenceError(.createDirectory, underlying: error)
        }

        let data: Data
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            data = try encoder.encode(
                DailyStatsArchive(activeSecondsByDate: activeSecondsByDate)
            )
        } catch {
            throw persistenceError(.encode, underlying: error)
        }

        do {
            try data.write(to: fileURL, options: .atomic)
        } catch {
            throw persistenceError(.write, underlying: error)
        }
    }

    private func persistenceError(
        _ operation: DailyStatsPersistenceError.Operation,
        underlying error: Error
    ) -> DailyStatsPersistenceError {
        DailyStatsPersistenceError(
            operation: operation,
            path: fileURL.path,
            reason: error.localizedDescription
        )
    }
}

final class DailyStatsRecorder {
    fileprivate static let maximumDailySeconds: TimeInterval = 26 * 60 * 60
    private static let saveInterval: TimeInterval = 30

    private let store: DailyStatsStore
    private let persistenceEnabled: Bool
    private var activeSecondsByDate: [String: TimeInterval]
    private var previousDate: Date?
    private var previousUptime: TimeInterval?
    private var previousWasRunning = false
    private var previousMaximumRunningDuration: TimeInterval?
    private var lastSaveUptime: TimeInterval?
    private var isDirty = false
    private lazy var dayFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.calendar = localCalendar
        formatter.locale = .autoupdatingCurrent
        formatter.timeZone = .autoupdatingCurrent
        formatter.dateFormat = "EEE"
        return formatter
    }()

    init(store: DailyStatsStore = DailyStatsStore()) throws {
        self.store = store
        activeSecondsByDate = try store.load()
        persistenceEnabled = true
    }

    init(
        store: DailyStatsStore,
        activeSecondsByDate: [String: TimeInterval],
        persistenceEnabled: Bool
    ) {
        self.store = store
        self.activeSecondsByDate = activeSecondsByDate
        self.persistenceEnabled = persistenceEnabled
    }

    func sample(
        at date: Date = Date(),
        uptime: TimeInterval = ProcessInfo.processInfo.systemUptime,
        isRunning: Bool,
        maximumRunningDuration: TimeInterval? = nil,
        forceSave: Bool = false
    ) throws {
        if let maximumRunningDuration,
           (!maximumRunningDuration.isFinite || maximumRunningDuration < 0) {
            throw DailyStatsPersistenceError(
                operation: .decode,
                path: "in-memory timer state",
                reason: "The maximum running duration is invalid."
            )
        }
        let stateChanged = previousDate != nil && previousWasRunning != isRunning

        if previousWasRunning,
           let startDate = previousDate,
           let startUptime = previousUptime {
            let fullElapsed = uptime - startUptime
            var elapsed = fullElapsed
            var effectiveEndDate = date
            if let previousMaximumRunningDuration {
                elapsed = min(elapsed, previousMaximumRunningDuration)
                let wallElapsed = date.timeIntervalSince(startDate)
                if wallElapsed > 0 && fullElapsed > 0 {
                    effectiveEndDate = startDate.addingTimeInterval(
                        wallElapsed * elapsed / fullElapsed
                    )
                }
            }
            if elapsed > 0 && elapsed.isFinite {
                distribute(
                    elapsed: elapsed,
                    from: startDate,
                    to: effectiveEndDate
                )
            }
        }

        previousDate = date
        previousUptime = uptime
        previousWasRunning = isRunning
        previousMaximumRunningDuration = isRunning
            ? maximumRunningDuration
            : nil

        guard persistenceEnabled, isDirty else {
            return
        }

        let saveIntervalElapsed = lastSaveUptime.map {
            uptime - $0 >= Self.saveInterval
        } ?? true
        if forceSave || stateChanged || saveIntervalElapsed {
            lastSaveUptime = uptime
            try store.save(activeSecondsByDate)
            isDirty = false
        }
    }

    func rows(for date: Date = Date()) -> [DailyStatsRow] {
        let calendar = localCalendar
        let today = calendar.startOfDay(for: date)

        return (0..<7).compactMap { offset in
            guard let day = calendar.date(
                byAdding: .day,
                value: -offset,
                to: today
            ) else {
                return nil
            }
            let key = dayKey(for: day, calendar: calendar)
            return DailyStatsRow(
                date: key,
                day: dayFormatter.string(from: day),
                hours: formatHours(activeSecondsByDate[key])
            )
        }
    }

    private func distribute(
        elapsed: TimeInterval,
        from startDate: Date,
        to endDate: Date
    ) {
        let wallElapsed = endDate.timeIntervalSince(startDate)
        guard wallElapsed > 0 && wallElapsed.isFinite else {
            add(elapsed, to: endDate)
            return
        }

        let calendar = localCalendar
        var cursor = startDate
        var distributed: TimeInterval = 0

        while cursor < endDate {
            let startOfDay = calendar.startOfDay(for: cursor)
            guard let nextDay = calendar.date(
                byAdding: .day,
                value: 1,
                to: startOfDay
            ), nextDay > cursor else {
                add(elapsed - distributed, to: endDate)
                return
            }

            let segmentEnd = min(nextDay, endDate)
            let segmentWallElapsed = segmentEnd.timeIntervalSince(cursor)
            let segmentElapsed: TimeInterval
            if segmentEnd == endDate {
                segmentElapsed = elapsed - distributed
            } else {
                segmentElapsed = elapsed * segmentWallElapsed / wallElapsed
            }
            add(segmentElapsed, to: cursor)
            distributed += segmentElapsed
            cursor = segmentEnd
        }
    }

    private func add(_ seconds: TimeInterval, to date: Date) {
        guard seconds > 0 && seconds.isFinite else {
            return
        }
        let key = dayKey(for: date, calendar: localCalendar)
        activeSecondsByDate[key, default: 0] += seconds
        isDirty = true
    }

    private func dayKey(for date: Date, calendar: Calendar) -> String {
        let components = calendar.dateComponents([.year, .month, .day], from: date)
        return String(
            format: "%04d-%02d-%02d",
            components.year ?? 0,
            components.month ?? 0,
            components.day ?? 0
        )
    }

    private func formatHours(_ seconds: TimeInterval?) -> String {
        guard let seconds, seconds > 0 else {
            return "NA"
        }
        let totalMinutes = Int(seconds) / 60
        return String(format: "%02d:%02d", totalMinutes / 60, totalMinutes % 60)
    }

    private var localCalendar: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .autoupdatingCurrent
        return calendar
    }
}
