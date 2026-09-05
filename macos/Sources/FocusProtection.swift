import AppKit
import Darwin
import Foundation

enum FocusProtectionError: LocalizedError {
    case missingExtension
    case missingNativeHost
    case socketPathTooLong
    case socketFailure(String)

    var errorDescription: String? {
        switch self {
        case .missingExtension:
            return "The bundled browser extension is missing."
        case .missingNativeHost:
            return "The bundled Focus Protection native host is missing."
        case .socketPathTooLong:
            return "The local Focus Protection socket path is too long."
        case .socketFailure(let operation):
            return "Unable to \(operation) the Focus Protection socket."
        }
    }
}

enum FocusProtectionPaths {
    static let hostName = "com.patil88ganesh.productivity_tracker"
    static let extensionID = "dhnpejafolnigilfhbbdiaanpfegpggd"

    static var supportDirectory: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/ProductivityTracker", isDirectory: true)
    }

    static var socketPath: String {
        supportDirectory.appendingPathComponent("focus-protection.sock").path
    }
}

enum FocusProtectionInstaller {
    static func install() throws -> URL {
        guard let source = Bundle.main.resourceURL?
            .appendingPathComponent("browser-extension", isDirectory: true),
              FileManager.default.fileExists(atPath: source.path) else {
            throw FocusProtectionError.missingExtension
        }

        let host = Bundle.main.bundleURL
            .appendingPathComponent("Contents/MacOS/ProductivityTrackerNativeHost")
        guard FileManager.default.isExecutableFile(atPath: host.path) else {
            throw FocusProtectionError.missingNativeHost
        }

        let fileManager = FileManager.default
        try fileManager.createDirectory(
            at: FocusProtectionPaths.supportDirectory,
            withIntermediateDirectories: true
        )

        let extensionDestination = FocusProtectionPaths.supportDirectory
            .appendingPathComponent("browser-extension", isDirectory: true)
        if fileManager.fileExists(atPath: extensionDestination.path) {
            try fileManager.removeItem(at: extensionDestination)
        }
        try fileManager.copyItem(at: source, to: extensionDestination)

        let manifest: [String: Any] = [
            "name": FocusProtectionPaths.hostName,
            "description": "Productivity Tracker Focus Protection bridge",
            "path": host.path,
            "type": "stdio",
            "allowed_origins": [
                "chrome-extension://\(FocusProtectionPaths.extensionID)/"
            ],
        ]
        let manifestData = try JSONSerialization.data(
            withJSONObject: manifest,
            options: [.prettyPrinted, .sortedKeys]
        )

        let applicationSupport = fileManager.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support", isDirectory: true)
        let browserDirectories = [
            applicationSupport.appendingPathComponent(
                "Google/Chrome/NativeMessagingHosts",
                isDirectory: true
            ),
            applicationSupport.appendingPathComponent(
                "Microsoft Edge/NativeMessagingHosts",
                isDirectory: true
            ),
        ]

        for directory in browserDirectories {
            try fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
            try manifestData.write(
                to: directory.appendingPathComponent("\(FocusProtectionPaths.hostName).json"),
                options: .atomic
            )
        }

        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(extensionDestination.path, forType: .string)
        NSWorkspace.shared.activateFileViewerSelecting([extensionDestination])
        return extensionDestination
    }
}

final class FocusSocketServer {
    private let queue = DispatchQueue(label: "ProductivityTracker.FocusSocket")
    private let stateChanged: (Bool) -> Void
    private var listener: DispatchSourceRead?
    private var listenerFD: Int32 = -1
    private var clientSources: [Int32: DispatchSourceRead] = [:]
    private var clientBuffers: [Int32: [UInt8]] = [:]
    private var clientStates: [Int32: Bool] = [:]
    private var lastAggregateState = false

    init(stateChanged: @escaping (Bool) -> Void) {
        self.stateChanged = stateChanged
    }

    func start() throws {
        try FileManager.default.createDirectory(
            at: FocusProtectionPaths.supportDirectory,
            withIntermediateDirectories: true
        )

        let path = FocusProtectionPaths.socketPath
        var address = sockaddr_un()
        let pathBytes = Array(path.utf8CString).map { UInt8(bitPattern: $0) }
        guard pathBytes.count <= MemoryLayout.size(ofValue: address.sun_path) else {
            throw FocusProtectionError.socketPathTooLong
        }

        let fd = Darwin.socket(AF_UNIX, SOCK_STREAM, 0)
        guard fd >= 0 else {
            throw FocusProtectionError.socketFailure("create")
        }
        listenerFD = fd
        Darwin.unlink(path)

        address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
        address.sun_family = sa_family_t(AF_UNIX)
        withUnsafeMutableBytes(of: &address.sun_path) { destination in
            destination.copyBytes(from: pathBytes)
        }

        let bindResult = withUnsafePointer(to: &address) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                Darwin.bind(fd, $0, socklen_t(MemoryLayout<sockaddr_un>.size))
            }
        }
        guard bindResult == 0 else {
            Darwin.close(fd)
            listenerFD = -1
            throw FocusProtectionError.socketFailure("bind")
        }
        guard Darwin.listen(fd, 8) == 0 else {
            Darwin.close(fd)
            listenerFD = -1
            throw FocusProtectionError.socketFailure("listen on")
        }

        _ = Darwin.chmod(path, S_IRUSR | S_IWUSR)
        _ = Darwin.fcntl(fd, F_SETFL, O_NONBLOCK)

        let source = DispatchSource.makeReadSource(fileDescriptor: fd, queue: queue)
        source.setEventHandler { [weak self] in
            self?.acceptConnections()
        }
        source.setCancelHandler {
            Darwin.close(fd)
        }
        listener = source
        source.resume()
    }

    func stop() {
        queue.sync {
            for (fd, source) in clientSources {
                source.cancel()
                Darwin.close(fd)
            }
            clientSources.removeAll()
            clientBuffers.removeAll()
            clientStates.removeAll()
            listener?.cancel()
            listener = nil
            listenerFD = -1
            Darwin.unlink(FocusProtectionPaths.socketPath)
        }
    }

    deinit {
        stop()
    }

    private func acceptConnections() {
        while listenerFD >= 0 {
            let clientFD = Darwin.accept(listenerFD, nil, nil)
            if clientFD < 0 {
                if errno == EAGAIN || errno == EWOULDBLOCK {
                    return
                }
                return
            }

            _ = Darwin.fcntl(clientFD, F_SETFL, O_NONBLOCK)
            clientBuffers[clientFD] = []
            clientStates[clientFD] = false

            let source = DispatchSource.makeReadSource(fileDescriptor: clientFD, queue: queue)
            source.setEventHandler { [weak self] in
                self?.readClient(clientFD)
            }
            clientSources[clientFD] = source
            source.resume()
        }
    }

    private func readClient(_ fd: Int32) {
        var bytes = [UInt8](repeating: 0, count: 256)
        let count = Darwin.read(fd, &bytes, bytes.count)
        if count == 0 {
            disconnect(fd)
            return
        }
        if count < 0 {
            if errno != EAGAIN && errno != EWOULDBLOCK {
                disconnect(fd)
            }
            return
        }

        var buffer = clientBuffers[fd] ?? []
        buffer.append(contentsOf: bytes.prefix(Int(count)))
        while let newline = buffer.firstIndex(of: 10) {
            let line = buffer[..<newline]
            buffer.removeFirst(newline + 1)
            clientStates[fd] = line.first == 49
        }
        clientBuffers[fd] = buffer
        publishAggregateState()
    }

    private func disconnect(_ fd: Int32) {
        clientSources.removeValue(forKey: fd)?.cancel()
        clientBuffers.removeValue(forKey: fd)
        clientStates.removeValue(forKey: fd)
        Darwin.close(fd)
        publishAggregateState()
    }

    private func publishAggregateState() {
        let aggregate = clientStates.values.contains(true)
        guard aggregate != lastAggregateState else {
            return
        }
        lastAggregateState = aggregate
        DispatchQueue.main.async { [stateChanged] in
            stateChanged(aggregate)
        }
    }
}
