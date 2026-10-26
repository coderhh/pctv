import Foundation

enum AnnexB {
    /// Splits an Annex-B access unit into NAL units (start codes removed).
    static func nalUnits(_ au: Data) -> [Data] {
        let bytes = [UInt8](au)
        var starts: [(code: Int, nal: Int)] = []
        var i = 0
        while i + 2 < bytes.count {
            if bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 1 {
                starts.append((i, i + 3))
                i += 3
            } else {
                i += 1
            }
        }
        var out: [Data] = []
        for (k, s) in starts.enumerated() {
            var end = k + 1 < starts.count ? starts[k + 1].code : bytes.count
            while end > s.nal && bytes[end - 1] == 0 { end -= 1 } // trailing zero of a 4-byte start code
            if end > s.nal { out.append(Data(bytes[s.nal ..< end])) }
        }
        return out
    }

    /// HEVC NAL unit type from the first header byte.
    static func hevcType(_ nal: Data) -> Int { Int((nal[nal.startIndex] >> 1) & 0x3F) }
}
