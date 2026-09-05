import Foundation

enum AutomaticPauseReason: Hashable {
    case sessionLocked
    case distractingWebsite
}

final class TimerEngine {
    private enum ContinueCountingHandoffState {
        case none
        case awaitingFocusLoss
        case awaitingRefocus
        case confirmed
    }

    private static let continueCountingHandoffWindow: TimeInterval = 30

    private var accumulated: TimeInterval = 0
    private var startedAt: TimeInterval = 0
    private var automaticPauseReasons = Set<AutomaticPauseReason>()
    private var ignoredAutomaticPauseReasons = Set<AutomaticPauseReason>()
    private var continueCountingAvailableUntil: TimeInterval?
    private var continueCountingOfferVisitToken: String?
    private var continueCountingVisitToken: String?
    private var continueCountingHandoffState = ContinueCountingHandoffState.none
    private var distractingWebsiteIsActive = false
    private var distractingWebsiteVisitToken: String?
    private var resumeAfterAutomaticPause = false

    private(set) var isRunning = false
    private(set) var timerDuration: TimeInterval?
    private(set) var isTimerCompleted = false

    var isTimerMode: Bool {
        timerDuration != nil
    }

    var isAutomaticallyPaused: Bool {
        resumeAfterAutomaticPause && !automaticPauseReasons.isEmpty
    }

    var canContinueCountingOnDistractingWebsite: Bool {
        !ignoredAutomaticPauseReasons.contains(.distractingWebsite) &&
            !automaticPauseReasons.contains(.sessionLocked) &&
            ((resumeAfterAutomaticPause &&
                automaticPauseReasons.contains(.distractingWebsite) &&
                distractingWebsiteVisitToken != nil) ||
                ((continueCountingAvailableUntil.map { now <= $0 } ?? false) &&
                    continueCountingOfferVisitToken != nil))
    }

    var isContinuingCountingOnDistractingWebsite: Bool {
        ignoredAutomaticPauseReasons.contains(.distractingWebsite) &&
            (continueCountingHandoffState == .awaitingFocusLoss ||
                continueCountingHandoffState == .confirmed)
    }

    var hasContinueCountingOverride: Bool {
        ignoredAutomaticPauseReasons.contains(.distractingWebsite)
    }

    var displayTime: TimeInterval {
        guard let duration = timerDuration else {
            return elapsed
        }
        return max(0, duration - elapsed)
    }

    func toggle() {
        if isTimerCompleted {
            resetStopwatch()
            start()
            isTimerCompleted = false
        } else if isRunning || resumeAfterAutomaticPause {
            stop()
        } else {
            start()
        }
    }

    func reset() {
        accumulated = 0
        if isRunning {
            startedAt = now
        }
        isTimerCompleted = false
    }

    func startTimer(duration: TimeInterval) {
        guard duration > 0 else {
            return
        }
        stop()
        resetStopwatch()
        timerDuration = duration
        isTimerCompleted = false
        start()
    }

    func addAndStart(duration: TimeInterval) {
        guard duration > 0 else {
            return
        }
        if isTimerMode {
            exitTimer()
        }
        add(duration)
        start()
    }

    func exitTimer() {
        stop()
        resetStopwatch()
        timerDuration = nil
        isTimerCompleted = false
    }

    @discardableResult
    func update() -> Bool {
        guard let duration = timerDuration,
              isRunning,
              elapsed >= duration else {
            return false
        }
        stop()
        isTimerCompleted = true
        return true
    }

    func setAutomaticPause(
        _ reason: AutomaticPauseReason,
        active: Bool,
        visitToken: String? = nil
    ) {
        if reason == .distractingWebsite {
            let normalizedToken = visitToken?.isEmpty == false ? visitToken : nil
            distractingWebsiteIsActive = active
            distractingWebsiteVisitToken = normalizedToken
            if !hasContinueCountingOverride,
               let offerVisitToken = continueCountingOfferVisitToken,
               offerVisitToken != normalizedToken {
                continueCountingAvailableUntil = nil
                continueCountingOfferVisitToken = nil
            }
            if hasContinueCountingOverride &&
                continueCountingVisitToken != normalizedToken {
                clearContinueCountingOverride()
            }
        }

        if active {
            if ignoredAutomaticPauseReasons.contains(reason) {
                guard reason == .distractingWebsite else {
                    return
                }

                if continueCountingHandoffState == .awaitingFocusLoss ||
                    continueCountingHandoffState == .confirmed {
                    return
                }
                if continueCountingHandoffState == .awaitingRefocus,
                   continueCountingAvailableUntil.map({ now <= $0 }) ?? false {
                    continueCountingHandoffState = .confirmed
                    continueCountingAvailableUntil = nil
                    return
                }

                ignoredAutomaticPauseReasons.remove(reason)
                continueCountingHandoffState = .none
                continueCountingVisitToken = nil
            }
            guard automaticPauseReasons.insert(reason).inserted else {
                return
            }
            if isRunning {
                accumulated = elapsed
                isRunning = false
                resumeAfterAutomaticPause = true
            }
            return
        }

        if reason == .distractingWebsite,
           automaticPauseReasons.contains(reason),
           automaticPauseReasons.count == 1,
           resumeAfterAutomaticPause,
           distractingWebsiteVisitToken != nil {
            continueCountingAvailableUntil =
                now + Self.continueCountingHandoffWindow
            continueCountingOfferVisitToken = distractingWebsiteVisitToken
        }

        if ignoredAutomaticPauseReasons.contains(reason),
           reason == .distractingWebsite {
            if continueCountingHandoffState == .awaitingFocusLoss ||
                continueCountingHandoffState == .confirmed {
                continueCountingHandoffState = .awaitingRefocus
                continueCountingAvailableUntil =
                    now + Self.continueCountingHandoffWindow
            } else if continueCountingHandoffState == .awaitingRefocus &&
                        (continueCountingAvailableUntil.map { now > $0 } ?? false) {
                ignoredAutomaticPauseReasons.remove(reason)
                continueCountingHandoffState = .none
                continueCountingAvailableUntil = nil
                continueCountingVisitToken = nil
            }
        }
        guard automaticPauseReasons.remove(reason) != nil,
              automaticPauseReasons.isEmpty,
              resumeAfterAutomaticPause else {
            return
        }
        startedAt = now
        isRunning = true
        resumeAfterAutomaticPause = false
    }

    func continueCountingOnDistractingWebsite() {
        continueCounting(through: .distractingWebsite)
    }

    func cancelContinueCountingOnDistractingWebsite() {
        clearContinueCountingOverride()
        if distractingWebsiteIsActive {
            setAutomaticPause(
                .distractingWebsite,
                active: true,
                visitToken: distractingWebsiteVisitToken
            )
        }
    }

    private var now: TimeInterval {
        ProcessInfo.processInfo.systemUptime
    }

    private var elapsed: TimeInterval {
        isRunning ? accumulated + (now - startedAt) : accumulated
    }

    private func start() {
        guard !isRunning, !resumeAfterAutomaticPause else {
            return
        }
        if !automaticPauseReasons.isEmpty {
            resumeAfterAutomaticPause = true
            return
        }
        startedAt = now
        isRunning = true
    }

    private func stop() {
        if isRunning {
            accumulated = elapsed
        }
        isRunning = false
        resumeAfterAutomaticPause = false
        if !ignoredAutomaticPauseReasons.contains(.distractingWebsite) {
            continueCountingAvailableUntil = nil
        }
    }

    private func continueCounting(through reason: AutomaticPauseReason) {
        let automaticPauseWasActive = automaticPauseReasons.contains(reason)
        let visitToken = automaticPauseWasActive
            ? distractingWebsiteVisitToken
            : continueCountingOfferVisitToken
        guard !automaticPauseReasons.contains(.sessionLocked) else {
            return
        }
        guard reason != .distractingWebsite || visitToken != nil else {
            return
        }
        guard (resumeAfterAutomaticPause && automaticPauseWasActive) ||
                (continueCountingAvailableUntil.map { now <= $0 } ?? false) else {
            return
        }

        automaticPauseReasons.remove(reason)
        ignoredAutomaticPauseReasons.insert(reason)
        continueCountingVisitToken = visitToken
        continueCountingHandoffState = automaticPauseWasActive
            ? .awaitingFocusLoss
            : .awaitingRefocus
        continueCountingAvailableUntil =
            now + Self.continueCountingHandoffWindow
        guard automaticPauseReasons.isEmpty else {
            return
        }

        if resumeAfterAutomaticPause {
            startedAt = now
            isRunning = true
            resumeAfterAutomaticPause = false
        }
    }

    private func clearContinueCountingOverride() {
        ignoredAutomaticPauseReasons.remove(.distractingWebsite)
        continueCountingHandoffState = .none
        continueCountingAvailableUntil = nil
        continueCountingOfferVisitToken = nil
        continueCountingVisitToken = nil
    }

    private func add(_ duration: TimeInterval) {
        if isRunning {
            accumulated = elapsed + duration
            startedAt = now
        } else {
            accumulated += duration
        }
    }

    private func resetStopwatch() {
        accumulated = 0
        if isRunning {
            startedAt = now
        }
    }
}
