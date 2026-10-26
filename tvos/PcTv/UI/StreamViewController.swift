import UIKit

/// M1-M3: connects to a host, renders its stream, and sends pointer input from the Siri Remote.
/// Remote: drag clickpad = move cursor, circular swipe on the outer ring = scroll, click = left click,
/// long click = right click, ring clicks = scroll, Menu = browser back. No pairing yet.
/// The host must run with "RequirePairing": false in pctv.json until M7.
///
/// Host address, in order: env var PCTV_HOST (Xcode scheme → Run → Arguments → Environment Variables),
/// then the address saved from the on-screen prompt. Press Play/Pause to change it. Accepts "ip" or "ip:port".
final class StreamViewController: UIViewController {
    private static let defaultsKey = "pctv.host"
    private static let defaultPort: UInt16 = 47800

    private let renderer = VideoRenderer()
    private let player = PCMPlayer()
    private var conn: Connection?
    private let status = UILabel()
    private var videoCount = 0, audioCount = 0
    private let debugLabel = UILabel()
    private var tick = 0
    private var generation = 0 // invalidates reconnect timers from older connections
    private let remote = RemoteInput()
    private let cursorLayer = CursorLayer()
    private var welcomed = false

    private var savedHost: String? {
        get { UserDefaults.standard.string(forKey: Self.defaultsKey) }
        set { UserDefaults.standard.set(newValue, forKey: Self.defaultsKey) }
    }

    private var hostSpec: String? {
        if let e = ProcessInfo.processInfo.environment["PCTV_HOST"], !e.isEmpty { return e }
        return savedHost
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
        renderer.layer.frame = view.bounds
        view.layer.addSublayer(renderer.layer)
        status.textColor = .white
        status.numberOfLines = 0
        status.frame = CGRect(x: 40, y: 40, width: 1400, height: 120)
        view.addSubview(status)

        // Debug line: if "tick" stops, the main thread is hung; if "rx" stops, the network side is stuck.
        debugLabel.textColor = .yellow
        debugLabel.font = .monospacedSystemFont(ofSize: 24, weight: .regular)
        debugLabel.frame = CGRect(x: 40, y: view.bounds.height - 90, width: view.bounds.width - 80, height: 50)
        debugLabel.autoresizingMask = [.flexibleTopMargin, .flexibleWidth]
        view.addSubview(debugLabel)
        // The debug line is only shown when PCTV_DEBUG is set in the scheme's environment variables.
        debugLabel.isHidden = ProcessInfo.processInfo.environment["PCTV_DEBUG"] == nil

        view.layer.addSublayer(cursorLayer)
        cursorLayer.place(normalized: remote.cursor, in: view.bounds)
        remote.onCursor = { [weak self] p in
            guard let self else { return }
            self.cursorLayer.place(normalized: p, in: self.view.bounds)
        }
        remote.onEvent = { [weak self] body in
            guard let self, self.welcomed else { return }
            self.conn?.send(Protocol.json(.input, body))
        }
        Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            guard let self else { return }
            self.tick += 1
            let c = self.conn?.debugCounts ?? (bytes: 0, messages: 0)
            self.debugLabel.text = "tick \(self.tick) | rx \(c.bytes / 1024) KB, \(c.messages) msgs | video \(self.videoCount) audio \(self.audioCount) | layer \(self.renderer.layer.status.rawValue) | \(self.renderer.formatReady ? "fmt ok" : "no fmt")"
        }
    }

    override func viewDidAppear(_ animated: Bool) {
        super.viewDidAppear(animated)
        if hostSpec == nil { promptForHost() } else { connect() }
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        renderer.layer.frame = view.bounds
    }

    override func pressesBegan(_ presses: Set<UIPress>, with event: UIPressesEvent?) {
        var unhandled = Set<UIPress>()
        for p in presses {
            if p.type == .playPause { promptForHost() } // M6 maps this to MediaPlayPause
            else if !remote.pressBegan(p.type) { unhandled.insert(p) }
        }
        if !unhandled.isEmpty { super.pressesBegan(unhandled, with: event) }
    }

    override func pressesEnded(_ presses: Set<UIPress>, with event: UIPressesEvent?) {
        presses.forEach { remote.pressEnded($0.type) }
        super.pressesEnded(presses, with: event)
    }

    override func pressesCancelled(_ presses: Set<UIPress>, with event: UIPressesEvent?) {
        presses.forEach { remote.pressCancelled($0.type) }
        super.pressesCancelled(presses, with: event)
    }

    private func promptForHost() {
        guard presentedViewController == nil else { return }
        let alert = UIAlertController(title: "PC address", message: "IP address of the PC running the PCTV host, e.g. 192.168.1.20", preferredStyle: .alert)
        alert.addTextField { $0.placeholder = "192.168.1.20"; $0.text = self.savedHost; $0.keyboardType = .numbersAndPunctuation }
        alert.addAction(UIAlertAction(title: "Connect", style: .default) { [weak self] _ in
            guard let self else { return }
            let text = alert.textFields?.first?.text?.trimmingCharacters(in: .whitespaces) ?? ""
            if !text.isEmpty { self.savedHost = text }
            self.conn?.cancel()
            self.connect()
        })
        alert.addAction(UIAlertAction(title: "Cancel", style: .cancel))
        present(alert, animated: true)
    }

    private func parseHost() -> (String, UInt16)? {
        guard let spec = hostSpec else { return nil }
        let parts = spec.split(separator: ":")
        guard let h = parts.first else { return nil }
        return (String(h), parts.count > 1 ? UInt16(parts[1]) ?? Self.defaultPort : Self.defaultPort)
    }

    private func connect() {
        guard let (host, port) = parseHost() else { promptForHost(); return }
        generation += 1
        let gen = generation
        videoCount = 0; audioCount = 0
        welcomed = false
        status.text = "Connecting to \(host):\(port)…   (Play/Pause: change address)"
        let c = Connection(host: host, port: port)
        conn = c
        c.onMessage = { [weak self] type, payload in self?.handle(type, payload) }
        c.onClose = { [weak self] _ in
            guard let self, gen == self.generation else { return }
            self.status.text = "Disconnected, retrying \(host):\(port)…   (Play/Pause: change address)"
            DispatchQueue.main.asyncAfter(deadline: .now() + 2) { [weak self] in
                if let self, gen == self.generation { self.connect() }
            }
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

    private func handle(_ type: MsgType, _ p: Data) {
        switch type {
        case .welcome:
            print("[pctv] WELCOME: \(String(data: p, encoding: .utf8) ?? "?")")
            if let o = try? JSONSerialization.jsonObject(with: p) as? [String: Any], let e = o["error"] as? String {
                status.text = "Host refused: \(e)"
            } else {
                status.text = ""
                welcomed = true
                player.start()
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
