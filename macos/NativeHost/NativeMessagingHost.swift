import Darwin
import Foundation

private let maximumMessageLength = 1_048_576
private let socketPath = FileManager.default.homeDirectoryForCurrentUser
    .appendingPathComponent("Library/Application Support/ProductivityTracker/focus-protection.sock")
    .path

private final class ApplicationConnection {
    private var socketFD: Int32 = -1

    init() {
        Darwin.signal(SIGPIPE, SIG_IGN)
    }

    deinit {
        closeConnection()
    }

    func send(site: BrowserSite) -> Bool {
        if socketFD < 0 && !connect() {
            return false
        }

        let bytes: [UInt8]
        switch site {
        case .youtube:
            bytes = [121, 10]
        case .other:
            bytes = [49, 10]
        case .unknown:
            bytes = [117, 10]
        case .none:
            bytes = [48, 10]
        }
        var sent = 0
        while sent < bytes.count {
            let result = bytes.withUnsafeBytes { pointer in
                Darwin.write(socketFD, pointer.baseAddress!.advanced(by: sent), bytes.count - sent)
            }
            if result <= 0 {
                closeConnection()
                return false
            }
            sent += result
        }
        return true
    }

    private func connect() -> Bool {
        var address = sockaddr_un()
        let pathBytes = Array(socketPath.utf8CString).map { UInt8(bitPattern: $0) }
        guard pathBytes.count <= MemoryLayout.size(ofValue: address.sun_path) else {
            return false
        }

        let fd = Darwin.socket(AF_UNIX, SOCK_STREAM, 0)
        guard fd >= 0 else {
            return false
        }
        var noSignal: Int32 = 1
        Darwin.setsockopt(
            fd,
            SOL_SOCKET,
            SO_NOSIGPIPE,
            &noSignal,
            socklen_t(MemoryLayout<Int32>.size)
        )

        address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
        address.sun_family = sa_family_t(AF_UNIX)
        withUnsafeMutableBytes(of: &address.sun_path) { destination in
            destination.copyBytes(from: pathBytes)
        }

        let result = withUnsafePointer(to: &address) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                Darwin.connect(fd, $0, socklen_t(MemoryLayout<sockaddr_un>.size))
            }
        }
        guard result == 0 else {
            Darwin.close(fd)
            return false
        }
        socketFD = fd
        return true
    }

    private func closeConnection() {
        if socketFD >= 0 {
            Darwin.close(socketFD)
            socketFD = -1
        }
    }
}

private func readExactly(_ count: Int, from input: FileHandle) throws -> Data? {
    var result = Data()
    while result.count < count {
        guard let chunk = try input.read(upToCount: count - result.count),
              !chunk.isEmpty else {
            if result.isEmpty {
                return nil
            }
            throw CocoaError(.fileReadCorruptFile)
        }
        result.append(chunk)
    }
    return result
}

private struct BrowserState {
    let active: Bool
    let site: BrowserSite
    let visitToken: String?
}

private enum BrowserSite: String {
    case none
    case youtube
    case other
    case unknown
}

private func readMessage(from input: FileHandle) throws -> BrowserState? {
    guard let lengthData = try readExactly(4, from: input) else {
        return nil
    }
    let lengthBytes = [UInt8](lengthData)
    let length = Int(lengthBytes[0])
        | (Int(lengthBytes[1]) << 8)
        | (Int(lengthBytes[2]) << 16)
        | (Int(lengthBytes[3]) << 24)
    guard length > 0, length <= maximumMessageLength,
          let payload = try readExactly(length, from: input),
          let object = try JSONSerialization.jsonObject(with: payload) as? [String: Any],
          let active = object["active"] as? Bool else {
        throw CocoaError(.fileReadCorruptFile)
    }
    let candidate = object["visitToken"] as? String
    let visitToken = candidate.flatMap { $0.utf8.count <= 64 ? $0 : nil }
    let reportedSite = object["site"] as? String
    let site: BrowserSite
    if !active {
        site = .none
    } else if reportedSite == BrowserSite.youtube.rawValue {
        site = .youtube
    } else if reportedSite == BrowserSite.other.rawValue {
        site = .other
    } else {
        site = .unknown
    }
    return BrowserState(active: active, site: site, visitToken: visitToken)
}

private func writeMessage(
    site: BrowserSite,
    visitToken: String?,
    appConnected: Bool,
    to output: FileHandle
) throws {
    var response: [String: Any] = [
        "ok": true,
        "active": site != .none,
        "site": site.rawValue,
        "appConnected": appConnected,
    ]
    if let visitToken {
        response["visitToken"] = visitToken
    }
    let payload = try JSONSerialization.data(withJSONObject: response)
    guard payload.count <= maximumMessageLength else {
        throw CocoaError(.fileWriteOutOfSpace)
    }
    var length = UInt32(payload.count).littleEndian
    output.write(Data(bytes: &length, count: MemoryLayout<UInt32>.size))
    output.write(payload)
}

let input = FileHandle.standardInput
let output = FileHandle.standardOutput
private let applicationConnection = ApplicationConnection()

do {
    while let state = try readMessage(from: input) {
        let connected = applicationConnection.send(site: state.site)
        try writeMessage(
            site: state.site,
            visitToken: state.visitToken,
            appConnected: connected,
            to: output
        )
    }
    _ = applicationConnection.send(site: .none)
} catch {
    _ = applicationConnection.send(site: .none)
    FileHandle.standardError.write(Data("Native messaging error: \(error)\n".utf8))
    exit(1)
}
