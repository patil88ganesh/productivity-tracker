import Foundation

enum AutomaticPauseReason: Hashable {
    case sessionLocked
    case distractingWebsite
}

final class TimerEngine {
    private var accumulated: TimeInterval = 0
    private var startedAt: TimeInterval = 0
    private var automaticPauseReasons = Set<AutomaticPauseReason>()
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

    func setAutomaticPause(_ reason: AutomaticPauseReason, active: Bool) {
        if active {
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

        guard automaticPauseReasons.remove(reason) != nil,
              automaticPauseReasons.isEmpty,
              resumeAfterAutomaticPause else {
            return
        }
        startedAt = now
        isRunning = true
        resumeAfterAutomaticPause = false
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
