import Foundation
import Network

/// TCP connection speaking the PCTV framing. Callbacks arrive on the main queue.
final class Connection {
    var onMessage: ((MsgType, Data) -> Void)?
    var onClose: ((Error?) -> Void)?

    private let conn: NWConnection
    private var buffer = Data()
    private let queue = DispatchQueue(label: "pctv.net")

    init(host: String, port: UInt16) {
        let tcp = NWProtocolTCP.Options()
        tcp.noDelay = true
        conn = NWConnection(host: NWEndpoint.Host(host), port: NWEndpoint.Port(rawValue: port)!,
                            using: NWParameters(tls: nil, tcp: tcp))
    }

    func start(onReady: @escaping () -> Void) {
        conn.stateUpdateHandler = { [weak self] state in
            switch state {
            case .ready: DispatchQueue.main.async { onReady() }; self?.receive()
            case .failed(let e): self?.finish(e)
            case .cancelled: self?.finish(nil)
            default: break
            }
        }
        conn.start(queue: queue)
    }

    func send(_ data: Data) { conn.send(content: data, completion: .contentProcessed { _ in }) }
    func cancel() { conn.cancel() }

    private func finish(_ e: Error?) { DispatchQueue.main.async { self.onClose?(e) } }

    private func receive() {
        conn.receive(minimumIncompleteLength: 1, maximumLength: 1 << 20) { [weak self] data, _, isDone, error in
            guard let self else { return }
            if let data { self.buffer.append(data); self.drain() }
            if isDone || error != nil { self.finish(error); return }
            self.receive()
        }
    }

    private func drain() {
        while buffer.count >= 6 {
            let base = buffer.startIndex
            let len = Int(buffer.subdata(in: base + 2 ..< base + 6).reduce(UInt32(0)) { ($0 << 8) | UInt32($1) })
            guard buffer.count >= 6 + len else { return }
            let type = buffer[base]
            let payload = buffer.subdata(in: base + 6 ..< base + 6 + len)
            buffer.removeSubrange(base ..< base + 6 + len)
            if let t = MsgType(rawValue: type) {
                DispatchQueue.main.async { self.onMessage?(t, payload) }
            }
        }
    }
}
