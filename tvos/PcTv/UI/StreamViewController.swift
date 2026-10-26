import UIKit

/// M1: connects to a hard-coded host and renders its stream. No pairing or input yet.
/// The host must run with "RequirePairing": false in pctv.json until M7.
final class StreamViewController: UIViewController {
    // TODO(M7): replaced by discovery. Set this to the PC's LAN IP.
    static let hostAddress = "192.168.1.100"
    static let port: UInt16 = 47800

    private let renderer = VideoRenderer()
    private let player = PCMPlayer()
    private var conn: Connection?
    private let status = UILabel()

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
        renderer.layer.frame = view.bounds
        view.layer.addSublayer(renderer.layer)
        status.textColor = .white
        status.frame = CGRect(x: 40, y: 40, width: 1200, height: 60)
        view.addSubview(status)
        connect()
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        renderer.layer.frame = view.bounds
    }

    private func connect() {
        status.text = "Connecting to \(Self.hostAddress)…"
        let c = Connection(host: Self.hostAddress, port: Self.port)
        conn = c
        c.onMessage = { [weak self] type, payload in self?.handle(type, payload) }
        c.onClose = { [weak self] _ in
            self?.status.text = "Disconnected, retrying"
            DispatchQueue.main.asyncAfter(deadline: .now() + 2) { self?.connect() }
        }
        c.start { [weak c] in
            let tvId = UIDevice.current.identifierForVendor?.uuidString ?? "appletv"
            c?.send(Protocol.json(.hello, [
                "v": 1, "tvId": tvId,
                "display": ["w": 3840, "h": 2160, "fps": 60],
                "codecs": ["hevc"], "audio": ["pcm_s16le_48k_2ch"],
            ]))
        }
    }

    private var videoCount = 0, audioCount = 0

    private func handle(_ type: MsgType, _ p: Data) {
        switch type {
        case .welcome:
            print("[pctv] WELCOME: \(String(data: p, encoding: .utf8) ?? "?")")
            if let o = try? JSONSerialization.jsonObject(with: p) as? [String: Any], let e = o["error"] as? String {
                status.text = "Host refused: \(e)"
            } else {
                status.text = ""
                do { try player.start(); print("[pctv] audio engine started") }
                catch { print("[pctv] audio start failed: \(error)") }
            }
        case .video:
            videoCount += 1
            renderer.enqueue(accessUnit: p.dropFirst(8))
            if videoCount == 1 || videoCount % 60 == 0 {
                print("[pctv] video #\(videoCount) bytes=\(p.count) layerStatus=\(renderer.layer.status.rawValue) err=\(String(describing: renderer.layer.error))")
            }
        case .audio:
            audioCount += 1
            player.enqueue(pcm: p.dropFirst(8))
            if audioCount == 1 || audioCount % 200 == 0 { print("[pctv] audio #\(audioCount)") }
        case .ping: conn?.send(Protocol.frame(.pong, p))
        default: break
        }
    }
}
