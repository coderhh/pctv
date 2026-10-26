import AVFoundation
import CoreMedia

/// Annex-B HEVC access units → AVSampleBufferDisplayLayer.
final class VideoRenderer {
    let layer = AVSampleBufferDisplayLayer()
    private var vps: Data?, sps: Data?, pps: Data?
    private var format: CMVideoFormatDescription?
    var onFailure: (() -> Void)?
    var formatReady: Bool { format != nil }

    init() { layer.videoGravity = .resizeAspect }

    func enqueue(accessUnit: Data) {
        var slices: [Data] = []
        for nal in AnnexB.nalUnits(accessUnit) {
            switch AnnexB.hevcType(nal) {
            case 32: updateParam(&vps, nal)
            case 33: updateParam(&sps, nal)
            case 34: updateParam(&pps, nal)
            case 0...31: slices.append(nal)
            default: break // AUD, SEI
            }
        }
        guard let format, !slices.isEmpty else { return }

        var avcc = Data()
        for s in slices {
            var len = UInt32(s.count).bigEndian
            avcc.append(Data(bytes: &len, count: 4))
            avcc.append(s)
        }
        var block: CMBlockBuffer?
        guard CMBlockBufferCreateWithMemoryBlock(allocator: nil, memoryBlock: nil, blockLength: avcc.count,
                blockAllocator: nil, customBlockSource: nil, offsetToData: 0, dataLength: avcc.count,
                flags: 0, blockBufferOut: &block) == noErr, let block else { return }
        let copied = avcc.withUnsafeBytes {
            CMBlockBufferReplaceDataBytes(with: $0.baseAddress!, blockBuffer: block, offsetIntoDestination: 0, dataLength: avcc.count)
        }
        guard copied == noErr else { return }

        var sample: CMSampleBuffer?
        var size = avcc.count
        guard CMSampleBufferCreateReady(allocator: nil, dataBuffer: block, formatDescription: format,
                sampleCount: 1, sampleTimingEntryCount: 0, sampleTimingArray: nil,
                sampleSizeEntryCount: 1, sampleSizeArray: &size, sampleBufferOut: &sample) == noErr, let sample else { return }
        if let att = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: true),
           CFArrayGetCount(att) > 0 {
            let d = unsafeBitCast(CFArrayGetValueAtIndex(att, 0), to: CFMutableDictionary.self)
            CFDictionarySetValue(d, Unmanaged.passUnretained(kCMSampleAttachmentKey_DisplayImmediately).toOpaque(),
                                 Unmanaged.passUnretained(kCFBooleanTrue).toOpaque())
        }

        if layer.status == .failed { layer.flush(); onFailure?() }
        layer.enqueue(sample)
    }

    private func updateParam(_ slot: inout Data?, _ nal: Data) {
        if slot != nal {
            slot = nal
            rebuildFormat()
        }
    }

    private func rebuildFormat() {
        guard let vps, let sps, let pps else { return }
        let sets = [vps, sps, pps].map { [UInt8]($0) }
        var fmt: CMVideoFormatDescription?
        let status = sets[0].withUnsafeBufferPointer { a in sets[1].withUnsafeBufferPointer { b in sets[2].withUnsafeBufferPointer { c in
            let ptrs: [UnsafePointer<UInt8>] = [a.baseAddress!, b.baseAddress!, c.baseAddress!]
            let sizes = sets.map { $0.count }
            return CMVideoFormatDescriptionCreateFromHEVCParameterSets(allocator: nil, parameterSetCount: 3,
                parameterSetPointers: ptrs, parameterSetSizes: sizes, nalUnitHeaderLength: 4,
                extensions: nil, formatDescriptionOut: &fmt)
        } } }
        print("[pctv] HEVC format description status=\(status)")
        if status == noErr { format = fmt }
    }
}
