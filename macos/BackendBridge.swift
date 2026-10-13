import Foundation

final class BackendBridge {
    private var process: Process?
    private var input: FileHandle?
    private var buffer = Data()
    private let lock = NSLock()
    private var nextID = 0
    private var pending: [Int: (Result<[String: Any], Error>) -> Void] = [:]
    let resources: URL
    let configuration: URL

    init(resources: URL, configuration: URL) { self.resources = resources; self.configuration = configuration }

    private func start() throws {
        if process?.isRunning == true { return }
        let child = Process()
        child.executableURL = resources.appendingPathComponent("askbetter-core")
        child.arguments = ["--prompts", resources.appendingPathComponent("prompts").path, "--config", configuration.path]
        let incoming = Pipe(), outgoing = Pipe()
        child.standardInput = incoming
        child.standardOutput = outgoing
        child.standardError = FileHandle.nullDevice
        outgoing.fileHandleForReading.readabilityHandler = { [weak self] file in
            let bytes = file.availableData
            guard !bytes.isEmpty else { file.readabilityHandler = nil; return }
            self?.receive(bytes)
        }
        child.terminationHandler = { [weak self] _ in
            self?.failAll(ABError.message("Codex 后台连接已退出，请检查 CLI 安装和登录"))
        }
        try child.run()
        process = child; input = incoming.fileHandleForWriting
    }

    func request(_ operation: String, draft: String? = nil, settings: [String: Any], completion: @escaping (Result<[String: Any], Error>) -> Void) {
        do {
            try start()
            lock.lock(); nextID += 1; let identifier = nextID; pending[identifier] = completion; lock.unlock()
            var request: [String: Any] = ["id": identifier, "op": operation, "settings": settings]
            if let draft = draft { request["draft"] = draft }
            var bytes = try JSONSerialization.data(withJSONObject: request)
            bytes.append(10)
            try input?.write(contentsOf: bytes)
        } catch { completion(.failure(error)) }
    }

    private func receive(_ data: Data) {
        lock.lock()
        buffer.append(data)
        var deliveries: [([String: Any], (Result<[String: Any], Error>) -> Void)] = []
        while let newline = buffer.firstIndex(of: 10) {
            let line = buffer[..<newline]
            buffer.removeSubrange(...newline)
            if let value = try? JSONSerialization.jsonObject(with: line) as? [String: Any],
               let identifier = value["id"] as? Int, let callback = pending.removeValue(forKey: identifier) {
                deliveries.append((value, callback))
            }
        }
        lock.unlock()
        DispatchQueue.main.async {
            for (value, callback) in deliveries {
                if value["ok"] as? Bool == true { callback(.success(value)) }
                else { callback(.failure(ABError.message(value["error"] as? String ?? "优化失败，原稿已保留"))) }
            }
        }
    }

    private func failAll(_ error: Error) {
        lock.lock(); let callbacks = Array(pending.values); pending.removeAll(); buffer = Data(); lock.unlock()
        DispatchQueue.main.async { callbacks.forEach { $0(.failure(error)) } }
    }

    func close() {
        try? input?.close()
        if process?.isRunning == true { process?.terminate() }
        input = nil; process = nil
    }
}
