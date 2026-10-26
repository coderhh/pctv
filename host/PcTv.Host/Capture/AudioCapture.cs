using System.Diagnostics;

namespace PcTv.Host.Capture;

public readonly record struct AudioChunk(long PtsMicros, byte[] Pcm);

public interface IAudioSource
{
    /// <summary>10 ms chunks of s16le 48 kHz stereo.</summary>
    IAsyncEnumerable<AudioChunk> ReadAsync(CancellationToken ct);
}

/// <summary>Test pattern audio: a quiet 440 Hz sine, paced in real time.</summary>
public sealed class ToneAudioSource : IAudioSource
{
    public const int Rate = 48000, FramesPerChunk = 480;

    public async IAsyncEnumerable<AudioChunk> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        long t0 = Stopwatch.GetTimestamp();
        long frame = 0;
        while (!ct.IsCancellationRequested)
        {
            var pcm = new byte[FramesPerChunk * 4];
            for (int i = 0; i < FramesPerChunk; i++, frame++)
            {
                short v = (short)(Math.Sin(2 * Math.PI * 440 * frame / Rate) * 6000);
                BitConverter.TryWriteBytes(pcm.AsSpan(i * 4), v);
                BitConverter.TryWriteBytes(pcm.AsSpan(i * 4 + 2), v);
            }
            long pts = frame * 1_000_000 / Rate - 10_000;
            var due = TimeSpan.FromMicroseconds(frame * 1_000_000.0 / Rate) - Stopwatch.GetElapsedTime(t0);
            if (due > TimeSpan.Zero) await Task.Delay(due, ct);
            yield return new AudioChunk(pts, pcm);
        }
    }
}
