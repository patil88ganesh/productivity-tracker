import AppKit

private final class StatsPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

private final class StatsWidgetView: NSView {
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        wantsLayer = true
        layer?.cornerRadius = 10
        layer?.borderWidth = 1
        layer?.borderColor = NSColor(
            calibratedWhite: 0.62,
            alpha: 0.75
        ).cgColor
        layer?.backgroundColor = NSColor.white.cgColor
        layer?.masksToBounds = true
    }
}

final class StatsWidgetController: NSWindowController {
    static let height: CGFloat = 166
    static let minimumWidth: CGFloat = 230

    private var rowLabels: [NSTextField] = []
    private var localMouseMonitor: Any?
    private var globalMouseMonitor: Any?

    init() {
        let contentView = StatsWidgetView(
            frame: NSRect(
                x: 0,
                y: 0,
                width: Self.minimumWidth,
                height: Self.height
            )
        )
        let panel = StatsPanel(
            contentRect: contentView.frame,
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.becomesKeyOnlyIfNeeded = true
        panel.isMovable = false
        panel.contentView = contentView

        super.init(window: panel)
        configureContent(in: contentView)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    deinit {
        removeMouseMonitors()
    }

    func update(rows: [DailyStatsRow]) {
        for (label, row) in zip(rowLabels, rows) {
            label.stringValue = "\(row.date) | \(row.day) | \(row.hours)"
        }
    }

    func show(onClickOutside: @escaping () -> Void) {
        installMouseMonitors(onClickOutside: onClickOutside)
        window?.orderFront(nil)
    }

    func hide() {
        removeMouseMonitors()
        window?.orderOut(nil)
    }

    private func configureContent(in contentView: NSView) {
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .centerX
        stack.distribution = .fillEqually
        stack.spacing = 0
        stack.translatesAutoresizingMaskIntoConstraints = false
        contentView.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 10),
            stack.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -10),
            stack.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 8),
            stack.bottomAnchor.constraint(equalTo: contentView.bottomAnchor, constant: -8),
        ])

        let header = makeLabel("Date       | Day | Hours", weight: .semibold)
        stack.addArrangedSubview(header)

        for _ in 0..<7 {
            let label = makeLabel("", weight: .regular)
            rowLabels.append(label)
            stack.addArrangedSubview(label)
        }
    }

    private func makeLabel(
        _ text: String,
        weight: NSFont.Weight
    ) -> NSTextField {
        let label = NSTextField(labelWithString: text)
        label.alignment = .center
        label.font = .monospacedSystemFont(ofSize: 11.5, weight: weight)
        label.textColor = NSColor(
            calibratedRed: 0.35,
            green: 0.39,
            blue: 0.43,
            alpha: 1
        )
        label.lineBreakMode = .byClipping
        return label
    }

    private func installMouseMonitors(onClickOutside: @escaping () -> Void) {
        removeMouseMonitors()
        let mouseEvents: NSEvent.EventTypeMask = [
            .leftMouseDown,
            .rightMouseDown,
            .otherMouseDown,
        ]
        localMouseMonitor = NSEvent.addLocalMonitorForEvents(
            matching: mouseEvents
        ) { [weak self] event in
            if event.window !== self?.window {
                onClickOutside()
            }
            return event
        }
        globalMouseMonitor = NSEvent.addGlobalMonitorForEvents(
            matching: mouseEvents
        ) { _ in
            DispatchQueue.main.async {
                onClickOutside()
            }
        }
    }

    private func removeMouseMonitors() {
        if let localMouseMonitor {
            NSEvent.removeMonitor(localMouseMonitor)
            self.localMouseMonitor = nil
        }
        if let globalMouseMonitor {
            NSEvent.removeMonitor(globalMouseMonitor)
            self.globalMouseMonitor = nil
        }
    }
}
