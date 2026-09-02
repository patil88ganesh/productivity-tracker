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

    func send(active: Bool) {
        if socketFD < 0 && !connect() {
            return
        }

        let bytes: [UInt8] = active ? [49, 10] : [48, 10]
        var sent = 0
        while sent < bytes.count {
            let result = bytes.withUnsafeBytes { pointer in
                Darwin.write(socketFD, pointer.baseAddress!.advanced(by: sent), bytes.count - sent)
            }
            if result <= 0 {
                closeConnection()
                return
            }
            sent += result
        }
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

private func readMessage(from input: FileHandle) throws -> Bool? {
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
    return active
}

let input = FileHandle.standardInput
private let applicationConnection = ApplicationConnection()

do {
    while let active = try readMessage(from: input) {
        applicationConnection.send(active: active)
    }
    applicationConnection.send(active: false)
} catch {
    applicationConnection.send(active: false)
    FileHandle.standardError.write(Data("Native messaging error: \(error)\n".utf8))
    exit(1)
}
