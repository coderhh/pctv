import UIKit

/// The pointer, drawn locally above the video (the host does not capture the cursor).
final class CursorLayer: CALayer {
    override init() {
        super.init()
        bounds = CGRect(x: 0, y: 0, width: 36, height: 36)
        cornerRadius = 18
        backgroundColor = UIColor(white: 1, alpha: 0.85).cgColor
        borderColor = UIColor.black.cgColor
        borderWidth = 3
        shadowColor = UIColor.black.cgColor
        shadowOpacity = 0.5
        shadowRadius = 4
        zPosition = 1000
    }

    override init(layer: Any) { super.init(layer: layer) }
    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    /// Moves the cursor to a normalized (0...1) position within `area`, without implicit animation.
    func place(normalized p: CGPoint, in area: CGRect) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        position = CGPoint(x: area.minX + p.x * area.width, y: area.minY + p.y * area.height)
        CATransaction.commit()
    }
}
