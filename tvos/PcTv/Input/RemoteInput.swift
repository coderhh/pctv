import GameController
import UIKit

/// Siri Remote → pointer-mode events (see PLAN.md §7.3). Touch surface via GCMicroGamepad with absolute
/// dpad values; the clickpad centre moves the cursor, the outer ring scrolls. Emits INPUT JSON dictionaries.
final class RemoteInput {
    /// Called with ready-to-send INPUT message bodies.
    var onEvent: (([String: Any]) -> Void)?
    /// Called with the cursor position, normalized 0...1, whenever it changes.
    var onCursor: ((CGPoint) -> Void)?

    // Tunables
    static let ringRadius: Float = 0.75      // touch radius beyond which a touch is a ring (scroll) gesture
    static let cursorGain: CGFloat = 0.9     // full pad width ≈ 90% of the screen at base speed
    static let scrollPerRadian: CGFloat = 700 // CSS px per radian of circular swipe
    static let arrowScroll: CGFloat = 320    // CSS px per ring click
    static let longPress: TimeInterval = 0.6

    private(set) var cursor = CGPoint(x: 0.5, y: 0.5)
    private var last: (x: Float, y: Float)?
    private var ringTouch = false
    private var dirty = false
    private var flushTimer: Timer?
    private var selectDown: Date?

    init() {
        NotificationCenter.default.addObserver(forName: .GCControllerDidConnect, object: nil, queue: .main) { [weak self] n in
            if let c = n.object as? GCController { self?.configure(c) }
        }
        GCController.controllers().forEach(configure)
        // Coalesce cursor moves to at most 60 per second.
        flushTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 60, repeats: true) { [weak self] _ in self?.flush() }
    }

    deinit { flushTimer?.invalidate() }

    private func configure(_ c: GCController) {
        guard let pad = c.microGamepad else { return }
        pad.reportsAbsoluteDpadValues = true
        pad.allowsRotation = false
        pad.dpad.valueChangedHandler = { [weak self] _, x, y in
            DispatchQueue.main.async { self?.touch(x: x, y: y) }
        }
    }

    // MARK: touch surface

    /// (0, 0) is reported when the finger lifts, so it ends the current gesture.
    func touch(x: Float, y: Float) {
        if x == 0 && y == 0 { last = nil; return }
        defer { last = (x, y) }
        guard let prev = last else {
            ringTouch = hypot(x, y) > Self.ringRadius // the gesture's mode is decided where it starts
            return
        }
        if ringTouch {
            let delta = Self.angleDelta(from: atan2(prev.y, prev.x), to: atan2(y, x))
            // Clockwise (angle decreasing, y up) scrolls down.
            let dy = -delta * Self.scrollPerRadian
            if abs(dy) > 0.5 { onEvent?(["t": "scroll", "dx": 0, "dy": Double(dy)]) }
        } else {
            let dx = CGFloat(x - prev.x), dy = CGFloat(prev.y - y) // screen y grows downward
            let speed = hypot(dx, dy)
            let accel = 1 + min(speed * 10, 2.5)
            move(by: CGPoint(x: dx * Self.cursorGain * accel / 2, y: dy * Self.cursorGain * accel / 2))
        }
    }

    /// Shortest signed angle from a to b, in (-π, π].
    static func angleDelta(from a: Float, to b: Float) -> CGFloat {
        var d = b - a
        while d > .pi { d -= 2 * .pi }
        while d <= -.pi { d += 2 * .pi }
        return CGFloat(d)
    }

    private func move(by d: CGPoint) {
        cursor.x = min(max(cursor.x + d.x, 0), 1)
        cursor.y = min(max(cursor.y + d.y, 0), 1)
        dirty = true
        onCursor?(cursor)
    }

    private func flush() {
        guard dirty else { return }
        dirty = false
        onEvent?(["t": "move", "x": Double(cursor.x), "y": Double(cursor.y)])
    }

    // MARK: presses

    /// Returns true if the press was handled.
    func pressBegan(_ type: UIPress.PressType) -> Bool {
        switch type {
        case .select: selectDown = Date(); return true
        case .upArrow: onEvent?(["t": "scroll", "dx": 0, "dy": -Double(Self.arrowScroll)]); return true
        case .downArrow: onEvent?(["t": "scroll", "dx": 0, "dy": Double(Self.arrowScroll)]); return true
        case .leftArrow: onEvent?(["t": "scroll", "dx": -Double(Self.arrowScroll), "dy": 0]); return true
        case .rightArrow: onEvent?(["t": "scroll", "dx": Double(Self.arrowScroll), "dy": 0]); return true
        case .menu: onEvent?(["t": "back"]); return true
        default: return false
        }
    }

    func pressEnded(_ type: UIPress.PressType) {
        guard type == .select, let down = selectDown else { return }
        selectDown = nil
        flush() // make sure the pointer is where the user sees the cursor before clicking
        let long = Date().timeIntervalSince(down) >= Self.longPress
        onEvent?(["t": "click", "button": long ? "right" : "left", "count": 1])
    }

    func pressCancelled(_ type: UIPress.PressType) { if type == .select { selectDown = nil } }
}
