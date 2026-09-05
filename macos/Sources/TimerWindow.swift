import AppKit
import Foundation

final class TimerDisplayView: NSView {
    var onMiddleClick: (() -> Void)?
    var displayText = "00:00:00" {
        didSet { needsDisplay = true }
    }
    var statusColor = NSColor(
        calibratedRed: 0.35,
        green: 0.39,
        blue: 0.43,
        alpha: 1
    ) {
        didSet { needsDisplay = true }
    }
    var completionHighlighted = false {
        didSet {
            updateShadow()
            needsDisplay = true
        }
    }
    var isCompleted = false {
        didSet { needsDisplay = true }
    }

    private var trackingAreaReference: NSTrackingArea?
    private var isHovering = false

    override var acceptsFirstResponder: Bool { true }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        wantsLayer = true
        layer?.masksToBounds = false
        layer?.shadowOffset = NSSize(width: 0, height: -2)
        updateShadow()
    }

    override func layout() {
        super.layout()
        layer?.shadowPath = CGPath(
            roundedRect: bounds.insetBy(dx: 3, dy: 3),
            cornerWidth: cornerRadius,
            cornerHeight: cornerRadius,
            transform: nil
        )
    }

    override func updateTrackingAreas() {
        if let existing = trackingAreaReference {
            removeTrackingArea(existing)
        }
        let area = NSTrackingArea(
            rect: bounds,
            options: [.activeAlways, .mouseEnteredAndExited, .inVisibleRect],
            owner: self,
            userInfo: nil
        )
        addTrackingArea(area)
        trackingAreaReference = area
        super.updateTrackingAreas()
    }

    override func mouseEntered(with event: NSEvent) {
        isHovering = true
        updateShadow()
        needsDisplay = true
    }

    override func mouseExited(with event: NSEvent) {
        isHovering = false
        updateShadow()
        needsDisplay = true
    }

    override func mouseDown(with event: NSEvent) {
        window?.performDrag(with: event)
    }

    override func otherMouseDown(with event: NSEvent) {
        if event.buttonNumber == 2 {
            onMiddleClick?()
        } else {
            super.otherMouseDown(with: event)
        }
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool {
        true
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)

        let highlighted = completionHighlighted || isHovering
        let backgroundColor: NSColor
        let borderColor: NSColor
        if completionHighlighted {
            backgroundColor = NSColor(calibratedRed: 1, green: 0.94, blue: 0.94, alpha: 1)
            borderColor = .systemRed
        } else if isHovering {
            backgroundColor = NSColor(calibratedRed: 0.94, green: 0.98, blue: 1, alpha: 1)
            borderColor = .systemBlue
        } else {
            backgroundColor = .white
            borderColor = NSColor(calibratedWhite: 0.62, alpha: 0.75)
        }

        let lineWidth: CGFloat = highlighted ? 2 : 1
        let backgroundPath = NSBezierPath(
            roundedRect: bounds.insetBy(dx: 3, dy: 3),
            xRadius: cornerRadius,
            yRadius: cornerRadius
        )
        backgroundColor.setFill()
        backgroundPath.fill()
        borderColor.setStroke()
        backgroundPath.lineWidth = lineWidth
        backgroundPath.stroke()

        let indicatorSize = min(max(fontSize * 0.28, 8), 22)
        let indicatorRect = NSRect(
            x: max(11, bounds.height * 0.17),
            y: (bounds.height - indicatorSize) / 2,
            width: indicatorSize,
            height: indicatorSize
        )
        let indicatorPath = NSBezierPath(ovalIn: indicatorRect)
        NSGraphicsContext.saveGraphicsState()
        let indicatorShadow = NSShadow()
        indicatorShadow.shadowColor = statusColor.withAlphaComponent(0.72)
        indicatorShadow.shadowBlurRadius = 7
        indicatorShadow.shadowOffset = .zero
        indicatorShadow.set()
        statusColor.setFill()
        indicatorPath.fill()
        NSGraphicsContext.restoreGraphicsState()
        NSColor.white.withAlphaComponent(0.92).setStroke()
        indicatorPath.lineWidth = 1.25
        indicatorPath.stroke()

        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = .center
        let baseFont = NSFont.monospacedDigitSystemFont(ofSize: fontSize, weight: .semibold)
        let italicFont = NSFontManager.shared.convert(baseFont, toHaveTrait: .italicFontMask)
        let textColor = isCompleted
            ? NSColor.systemRed
            : NSColor(calibratedRed: 0.45, green: 0.49, blue: 0.53, alpha: 1)
        let attributes: [NSAttributedString.Key: Any] = [
            .font: italicFont,
            .foregroundColor: textColor,
            .paragraphStyle: paragraph,
        ]
        let textRect = NSRect(
            x: indicatorRect.maxX + 5,
            y: (bounds.height - fontSize * 1.25) / 2,
            width: max(1, bounds.width - indicatorRect.maxX - 13),
            height: fontSize * 1.3
        )
        displayText.draw(in: textRect, withAttributes: attributes)
    }

    private var fontSize: CGFloat {
        min(max(min(bounds.height * 0.48, bounds.width * 0.16), 20), 96)
    }

    private var cornerRadius: CGFloat {
        min(max(bounds.height * 0.15, 7), 20)
    }

    private func updateShadow() {
        let highlighted = completionHighlighted || isHovering
        layer?.shadowColor = (completionHighlighted ? NSColor.systemRed : NSColor.black).cgColor
        layer?.shadowRadius = highlighted ? 12 : 8
        layer?.shadowOpacity = highlighted ? 0.34 : 0.22
    }
}

final class TimerWindowController: NSWindowController, NSWindowDelegate {
    private enum Settings {
        static let frame = "ProductivityTracker.WindowFrame"
        static let opacity = "ProductivityTracker.OpacityPercent"
        static let focusProtection = "ProductivityTracker.FocusProtectionEnabled"
        static let browserSetupShown = "ProductivityTracker.BrowserSetupShown"
    }

    private let engine = TimerEngine()
    private let displayView: TimerDisplayView
    private let statsRecorder: DailyStatsRecorder
    private let statsWidgetController = StatsWidgetController()
    private let defaults = UserDefaults.standard
    private var refreshTimer: Foundation.Timer?
    private var flashTimer: Foundation.Timer?
    private var flashStep = 0
    private var browserReportsDistractingSite = false
    private var focusSocketServer: FocusSocketServer?
    private var focusProtectionEnabled: Bool
    private var pendingStatsPersistenceError: Error?
    private var lastStatsPersistenceErrorSignature: String?
    private var statsWidgetVisible = false

    private let toggleMenuItem = NSMenuItem()
    private let exitTimerMenuItem = NSMenuItem()
    private let focusProtectionMenuItem = NSMenuItem()
    private let statsMenuItem = NSMenuItem()
    private var opacityMenuItems: [NSMenuItem] = []

    init() {
        focusProtectionEnabled = UserDefaults.standard.bool(forKey: Settings.focusProtection)
        displayView = TimerDisplayView(frame: NSRect(x: 0, y: 0, width: 184, height: 58))
        let statsStore = DailyStatsStore()
        do {
            statsRecorder = try DailyStatsRecorder(store: statsStore)
        } catch {
            statsRecorder = DailyStatsRecorder(
                store: statsStore,
                activeSecondsByDate: [:],
                persistenceEnabled: false
            )
            pendingStatsPersistenceError = error
        }

        let window = NSWindow(
            contentRect: displayView.frame,
            styleMask: [.titled, .resizable, .miniaturizable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        window.title = "Productivity Tracker"
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = true
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.level = .floating
        window.hidesOnDeactivate = false
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.minSize = NSSize(width: 140, height: 48)
        window.contentView = displayView
        window.standardWindowButton(.closeButton)?.isHidden = true
        window.standardWindowButton(.zoomButton)?.isHidden = true
        window.standardWindowButton(.miniaturizeButton)?.isHidden = true

        super.init(window: window)
        window.delegate = self
        restoreWindowFrame()
        configureMenu()
        applyOpacity(loadOpacity())

        displayView.onMiddleClick = { [weak self] in
            self?.toggleTracking()
        }

        focusSocketServer = FocusSocketServer { [weak self] active in
            guard let self else {
                return
            }
            self.browserReportsDistractingSite = active
            self.performEngineTransition {
                self.engine.setAutomaticPause(
                    .distractingWebsite,
                    active: self.focusProtectionEnabled && active
                )
            }
            self.refreshDisplay()
        }
        do {
            try focusSocketServer?.start()
        } catch {
            showError(title: "Focus Protection unavailable", error: error)
        }

        refreshTimer = Foundation.Timer(timeInterval: 0.1, repeats: true) { [weak self] _ in
            self?.refreshDisplay()
        }
        if let refreshTimer {
            RunLoop.main.add(refreshTimer, forMode: .common)
        }
        refreshDisplay()
        if let error = pendingStatsPersistenceError {
            DispatchQueue.main.async { [weak self] in
                self?.reportStatsPersistenceError(error)
            }
        }
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    deinit {
        refreshTimer?.invalidate()
        flashTimer?.invalidate()
        focusSocketServer?.stop()
        statsWidgetController.hide()
        statsWidgetController.close()
    }

    func showWindowAndActivate() {
        if window?.isMiniaturized == true {
            window?.deminiaturize(nil)
        }
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func setSessionLocked(_ locked: Bool) {
        performEngineTransition {
            engine.setAutomaticPause(.sessionLocked, active: locked)
        }
        refreshDisplay()
    }

    func saveState() {
        recordStats()
        guard let window else {
            return
        }
        defaults.set(NSStringFromRect(window.frame), forKey: Settings.frame)
        defaults.synchronize()
    }

    func prepareForTermination() {
        saveState()
        recordStats(forceSave: true)
        statsWidgetController.hide()
        statsWidgetController.close()
    }

    func windowDidMove(_ notification: Notification) {
        saveState()
        positionStatsWidget()
    }

    func windowDidResize(_ notification: Notification) {
        saveState()
        displayView.needsDisplay = true
        positionStatsWidget()
    }

    func windowWillMiniaturize(_ notification: Notification) {
        dismissStatsWidget()
    }

    func windowDidDeminiaturize(_ notification: Notification) {
        window?.level = .floating
        statsWidgetController.window?.level = .floating
    }

    @objc private func toggleTracking() {
        if engine.isTimerCompleted {
            stopCompletionAlert()
        }
        performEngineTransition {
            engine.toggle()
        }
        refreshDisplay()
    }

    @objc private func resetTracking() {
        stopCompletionAlert()
        performEngineTransition {
            engine.reset()
        }
        refreshDisplay()
    }

    @objc private func setTimer() {
        guard let duration = requestDuration(
            title: "Set Timer",
            message: "Enter a countdown duration.",
            includesSeconds: true
        ) else {
            return
        }
        stopCompletionAlert()
        performEngineTransition {
            engine.startTimer(duration: duration)
        }
        refreshDisplay()
    }

    @objc private func addAndStart() {
        guard let duration = requestDuration(
            title: "Add and Start",
            message: "Add time to the stopwatch and immediately continue counting.",
            includesSeconds: false
        ) else {
            return
        }
        stopCompletionAlert()
        performEngineTransition {
            engine.addAndStart(duration: duration)
        }
        refreshDisplay()
    }

    @objc private func exitTimer() {
        stopCompletionAlert()
        performEngineTransition {
            engine.exitTimer()
        }
        refreshDisplay()
    }

    @objc private func toggleFocusProtection() {
        focusProtectionEnabled.toggle()
        defaults.set(focusProtectionEnabled, forKey: Settings.focusProtection)
        focusProtectionMenuItem.state = focusProtectionEnabled ? .on : .off
        performEngineTransition {
            engine.setAutomaticPause(
                .distractingWebsite,
                active: focusProtectionEnabled && browserReportsDistractingSite
            )
        }
        refreshDisplay()

        if focusProtectionEnabled && !defaults.bool(forKey: Settings.browserSetupShown) {
            defaults.set(true, forKey: Settings.browserSetupShown)
            showBrowserExtensionSetup()
        }
    }

    @objc private func openStatsWidget() {
        statsWidgetVisible = true
        statsMenuItem.state = .on
        showStatsWidget()
    }

    func dismissStatsWidget() {
        statsWidgetController.hide()
        statsWidgetVisible = false
        statsMenuItem.state = .off
    }

    @objc private func showBrowserExtensionSetup() {
        do {
            let extensionDirectory = try FocusProtectionInstaller.install()
            let alert = NSAlert()
            alert.messageText = "Focus Protection files are ready"
            alert.informativeText =
                "The extension folder is open and its path is copied to the clipboard:\n\n" +
                "\(extensionDirectory.path)\n\n" +
                "In Chrome, open chrome://extensions. In Edge, open edge://extensions. " +
                "Enable Developer mode, choose Load unpacked, and select this folder. " +
                "Native messaging has been registered for both browsers."
            alert.alertStyle = .informational
            alert.addButton(withTitle: "OK")
            alert.runModal()
        } catch {
            showError(title: "Browser extension setup failed", error: error)
        }
    }

    @objc private func setOpacity(_ sender: NSMenuItem) {
        guard let opacity = sender.representedObject as? Int else {
            return
        }
        applyOpacity(opacity)
        defaults.set(opacity, forKey: Settings.opacity)
    }

    @objc private func minimize() {
        window?.miniaturize(nil)
    }

    @objc private func exitApplication() {
        NSApp.terminate(nil)
    }

    private func configureMenu() {
        let menu = NSMenu()

        toggleMenuItem.target = self
        toggleMenuItem.action = #selector(toggleTracking)
        menu.addItem(toggleMenuItem)

        menu.addItem(makeMenuItem("Reset", action: #selector(resetTracking)))
        menu.addItem(makeMenuItem("Add and Start…", action: #selector(addAndStart)))
        menu.addItem(makeMenuItem("Set Timer…", action: #selector(setTimer)))

        exitTimerMenuItem.title = "Exit Timer"
        exitTimerMenuItem.target = self
        exitTimerMenuItem.action = #selector(exitTimer)
        menu.addItem(exitTimerMenuItem)

        statsMenuItem.title = "My stats (mini)"
        statsMenuItem.target = self
        statsMenuItem.action = #selector(openStatsWidget)
        menu.addItem(statsMenuItem)

        let focusMenu = NSMenu()
        focusProtectionMenuItem.title = "Pause on selected websites"
        focusProtectionMenuItem.target = self
        focusProtectionMenuItem.action = #selector(toggleFocusProtection)
        focusProtectionMenuItem.state = focusProtectionEnabled ? .on : .off
        focusMenu.addItem(focusProtectionMenuItem)
        focusMenu.addItem(.separator())
        focusMenu.addItem(makeMenuItem(
            "Browser Extension Setup…",
            action: #selector(showBrowserExtensionSetup)
        ))
        let focusParent = NSMenuItem(title: "Focus Protection", action: nil, keyEquivalent: "")
        focusParent.submenu = focusMenu
        menu.addItem(focusParent)

        menu.addItem(.separator())
        let opacityMenu = NSMenu()
        for opacity in [40, 55, 70, 85, 100] {
            let item = NSMenuItem(
                title: "\(opacity)%",
                action: #selector(setOpacity(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = opacity
            opacityMenu.addItem(item)
            opacityMenuItems.append(item)
        }
        let opacityParent = NSMenuItem(title: "Opacity", action: nil, keyEquivalent: "")
        opacityParent.submenu = opacityMenu
        menu.addItem(opacityParent)

        menu.addItem(makeMenuItem("Minimize", action: #selector(minimize)))
        menu.addItem(makeMenuItem("Exit", action: #selector(exitApplication)))
        displayView.menu = menu
    }

    private func makeMenuItem(_ title: String, action: Selector) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        return item
    }

    private func refreshDisplay() {
        let sampleDate = Date()
        let sampleUptime = ProcessInfo.processInfo.systemUptime
        recordStats(at: sampleDate, uptime: sampleUptime)
        let completed = engine.update()
        recordStats(at: sampleDate, uptime: sampleUptime)
        if completed {
            startCompletionAlert()
        }

        let displayTime = engine.displayTime
        displayView.displayText = formatDuration(displayTime)
        displayView.isCompleted = engine.isTimerCompleted
        exitTimerMenuItem.isHidden = !engine.isTimerMode

        if engine.isAutomaticallyPaused {
            toggleMenuItem.title = "Remain Paused"
            displayView.statusColor = NSColor(
                calibratedRed: 1,
                green: 0.56,
                blue: 0,
                alpha: 1
            )
            displayView.toolTip = "Paused automatically"
        } else if engine.isRunning {
            toggleMenuItem.title = "Pause"
            displayView.statusColor = NSColor(
                calibratedRed: 0,
                green: 0.78,
                blue: 0.33,
                alpha: 1
            )
            displayView.toolTip = "Running"
        } else if engine.isTimerCompleted {
            toggleMenuItem.title = "Restart Timer"
            displayView.statusColor = NSColor(
                calibratedRed: 1,
                green: 0.09,
                blue: 0.27,
                alpha: 1
            )
            displayView.toolTip = "Timer complete"
        } else {
            toggleMenuItem.title = displayTime < 1 ? "Start" : "Resume"
            displayView.statusColor = NSColor(
                calibratedRed: 0.35,
                green: 0.39,
                blue: 0.43,
                alpha: 1
            )
            displayView.toolTip = "Paused"
        }
        statsWidgetController.update(rows: statsRecorder.rows(for: sampleDate))
    }

    private func startCompletionAlert() {
        NSSound.beep()
        NSApp.requestUserAttention(.criticalRequest)
        flashTimer?.invalidate()
        flashStep = 0
        displayView.completionHighlighted = true
        flashTimer = Foundation.Timer(timeInterval: 0.25, repeats: true) { [weak self] timer in
            guard let self else {
                timer.invalidate()
                return
            }
            self.flashStep += 1
            if self.flashStep >= 8 {
                self.stopCompletionAlert()
                self.refreshDisplay()
            } else {
                self.displayView.completionHighlighted = self.flashStep.isMultiple(of: 2)
            }
        }
        if let flashTimer {
            RunLoop.main.add(flashTimer, forMode: .common)
        }
    }

    private func stopCompletionAlert() {
        flashTimer?.invalidate()
        flashTimer = nil
        flashStep = 0
        displayView.completionHighlighted = false
    }

    private func requestDuration(
        title: String,
        message: String,
        includesSeconds: Bool
    ) -> TimeInterval? {
        var validationMessage = message
        while true {
            let alert = NSAlert()
            alert.messageText = title
            alert.informativeText = validationMessage
            alert.alertStyle = .informational
            alert.addButton(withTitle: includesSeconds ? "Start Timer" : "Add and Start")
            alert.addButton(withTitle: "Cancel")

            let fields = includesSeconds
                ? makeDurationFields(labels: ["Hours", "Minutes", "Seconds"])
                : makeDurationFields(labels: ["Hours", "Minutes"])
            alert.accessoryView = fields.container

            guard alert.runModal() == .alertFirstButtonReturn else {
                return nil
            }
            let values = fields.textFields.compactMap { Int($0.stringValue) }
            let expectedCount = includesSeconds ? 3 : 2
            if values.count == expectedCount,
               values[0] >= 0, values[0] <= 99,
               values[1] >= 0, values[1] <= 59,
               !includesSeconds || (values[2] >= 0 && values[2] <= 59) {
                let seconds = includesSeconds ? values[2] : 0
                let duration = TimeInterval(values[0] * 3600 + values[1] * 60 + seconds)
                if duration > 0 {
                    return duration
                }
            }
            validationMessage = includesSeconds
                ? "Enter hours from 0–99 and minutes or seconds from 0–59. Duration must be greater than zero."
                : "Enter hours from 0–99 and minutes from 0–59. Duration must be greater than zero."
            NSSound.beep()
        }
    }

    private func makeDurationFields(labels: [String]) -> (
        container: NSView,
        textFields: [NSTextField]
    ) {
        let container = NSView(frame: NSRect(x: 0, y: 0, width: 330, height: 58))
        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.distribution = .fillEqually
        stack.spacing = 12
        stack.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            stack.topAnchor.constraint(equalTo: container.topAnchor),
            stack.bottomAnchor.constraint(equalTo: container.bottomAnchor),
        ])

        var fields: [NSTextField] = []
        for label in labels {
            let field = NSTextField(string: "00")
            field.alignment = .center
            field.font = .monospacedDigitSystemFont(ofSize: 18, weight: .regular)
            let labelView = NSTextField(labelWithString: label)
            labelView.alignment = .center
            labelView.textColor = .secondaryLabelColor
            let column = NSStackView(views: [labelView, field])
            column.orientation = .vertical
            column.spacing = 5
            stack.addArrangedSubview(column)
            fields.append(field)
        }
        return (container, fields)
    }

    private func formatDuration(_ duration: TimeInterval) -> String {
        let totalSeconds = max(0, Int(duration))
        let hours = totalSeconds / 3600
        let minutes = (totalSeconds % 3600) / 60
        let seconds = totalSeconds % 60
        return String(format: "%02d:%02d:%02d", hours, minutes, seconds)
    }

    private func loadOpacity() -> Int {
        let saved = defaults.integer(forKey: Settings.opacity)
        return [40, 55, 70, 85, 100].contains(saved) ? saved : 85
    }

    private func applyOpacity(_ opacity: Int) {
        let alpha = CGFloat(opacity) / 100
        window?.alphaValue = alpha
        statsWidgetController.window?.alphaValue = alpha
        for item in opacityMenuItems {
            item.state = (item.representedObject as? Int) == opacity ? .on : .off
        }
    }

    private func performEngineTransition(_ transition: () -> Void) {
        let date = Date()
        let uptime = ProcessInfo.processInfo.systemUptime
        recordStats(at: date, uptime: uptime)
        transition()
        recordStats(at: date, uptime: uptime)
    }

    private func recordStats(
        at date: Date = Date(),
        uptime: TimeInterval = ProcessInfo.processInfo.systemUptime,
        forceSave: Bool = false
    ) {
        do {
            try statsRecorder.sample(
                at: date,
                uptime: uptime,
                isRunning: engine.isRunning,
                maximumRunningDuration: maximumStatsDuration,
                forceSave: forceSave
            )
        } catch {
            reportStatsPersistenceError(error)
        }
    }

    private func showStatsWidget() {
        guard let parentWindow = window,
              let statsWindow = statsWidgetController.window else {
            return
        }
        if statsWindow.parent !== parentWindow {
            parentWindow.addChildWindow(statsWindow, ordered: .above)
        }
        statsWindow.level = parentWindow.level
        statsWindow.alphaValue = parentWindow.alphaValue
        positionStatsWidget()
        statsWidgetController.show { [weak self] in
            self?.dismissStatsWidget()
        }
    }

    private func positionStatsWidget() {
        guard statsWidgetVisible,
              window?.isMiniaturized != true,
              let parentFrame = window?.frame,
              let statsWindow = statsWidgetController.window else {
            return
        }
        let width = min(
            max(parentFrame.width, StatsWidgetController.minimumWidth),
            320
        )
        let visibleFrame = window?.screen?.visibleFrame ?? NSScreen.main?.visibleFrame
        let desiredX = parentFrame.midX - width / 2
        let x = visibleFrame.map {
            min(max(desiredX, $0.minX), $0.maxX - width)
        } ?? desiredX
        let belowY = parentFrame.minY - StatsWidgetController.height + 3
        let y = visibleFrame.map {
            belowY >= $0.minY
                ? belowY
                : min(parentFrame.maxY - 3, $0.maxY - StatsWidgetController.height)
        } ?? belowY
        let frame = NSRect(
            x: x,
            y: y,
            width: width,
            height: StatsWidgetController.height
        )
        statsWindow.setFrame(frame, display: true)
    }

    private var maximumStatsDuration: TimeInterval? {
        engine.isRunning && engine.isTimerMode
            ? engine.displayTime
            : nil
    }

    private func reportStatsPersistenceError(_ error: Error) {
        let localizedError = error as? LocalizedError
        let signature = [
            error.localizedDescription,
            localizedError?.failureReason ?? "",
            localizedError?.recoverySuggestion ?? "",
        ].joined(separator: "\n")
        guard signature != lastStatsPersistenceErrorSignature else {
            return
        }
        lastStatsPersistenceErrorSignature = signature
        showError(title: "Statistics persistence failed", error: error)
    }

    private func restoreWindowFrame() {
        guard let window else {
            return
        }
        if let savedFrame = defaults.string(forKey: Settings.frame) {
            let frame = NSRectFromString(savedFrame)
            if frame.width >= window.minSize.width,
               frame.height >= window.minSize.height,
               NSScreen.screens.contains(where: { $0.visibleFrame.intersects(frame) }) {
                window.setFrame(frame, display: false)
                return
            }
        }
        window.center()
    }

    private func showError(title: String, error: Error) {
        let alert = NSAlert(error: error)
        alert.messageText = title
        alert.runModal()
    }
}
