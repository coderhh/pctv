import Foundation

/// Mirrors docs/protocol.md.
enum MsgType: UInt8 {
    case hello = 0x01, welcome = 0x02, pairBegin = 0x03, pairResult = 0x04
    case video = 0x10, audio = 0x11
    case input = 0x20, textCommit = 0x21
    case textFocus = 0x30, apps = 0x31
    case ping = 0x40, pong = 0x41, stats = 0x42
}

enum Protocol {
    static func frame(_ type: MsgType, _ payload: Data) -> Data {
        var d = Data([type.rawValue, 0])
        var len = UInt32(payload.count).bigEndian
        d.append(Data(bytes: &len, count: 4))
        d.append(payload)
        return d
    }

    static func json(_ type: MsgType, _ obj: [String: Any]) -> Data {
        frame(type, (try? JSONSerialization.data(withJSONObject: obj)) ?? Data())
    }
}
