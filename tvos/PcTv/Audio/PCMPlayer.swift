import AVFoundation

/// s16le 48 kHz stereo → AVAudioEngine. Starts playback after a small jitter buffer has queued.
final class PCMPlayer {
    private let engine = AVAudioEngine()
    private let node = AVAudioPlayerNode()
    private let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 48000, channels: 2, interleaved: false)!
    private var queuedFrames = 0
    private let startThreshold = 48000 * 70 / 1000 // ~70 ms

    init() {
        engine.attach(node)
        engine.connect(node, to: engine.mainMixerNode, format: format)
    }

    func start() throws {
        try AVAudioSession.sharedInstance().setCategory(.playback)
        try AVAudioSession.sharedInstance().setActive(true)
        try engine.start()
    }

    func stop() { node.stop(); engine.stop() }

    func enqueue(pcm: Data) {
        let frames = pcm.count / 4
        guard frames > 0, let buf = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(frames)) else { return }
        buf.frameLength = AVAudioFrameCount(frames)
        let bytes = [UInt8](pcm)
        let l = buf.floatChannelData![0], r = buf.floatChannelData![1]
        for i in 0..<frames {
            let lo = Int16(bitPattern: UInt16(bytes[4 * i]) | UInt16(bytes[4 * i + 1]) << 8)
            let ro = Int16(bitPattern: UInt16(bytes[4 * i + 2]) | UInt16(bytes[4 * i + 3]) << 8)
            l[i] = Float(lo) / 32768
            r[i] = Float(ro) / 32768
        }
        queuedFrames += frames
        node.scheduleBuffer(buf) { [weak self] in DispatchQueue.main.async { self?.queuedFrames -= frames } }
        if !node.isPlaying && queuedFrames >= startThreshold { node.play() }
    }
}
