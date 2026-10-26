using System.Diagnostics;

namespace PcTv.Host.Capture;

public readonly record struct VideoFrame(long PtsMicros, byte[] AnnexB);

/// <summary>Swap point for the capture backend (ddagrab today, WGC fallback later).</summary>
public interface IVideoSource : IAsyncDisposable
{
    IAsyncEnumerable<VideoFrame> ReadAsync(CancellationToken ct);
}

/// <summary>Spawns ffmpeg per session and reads Annex-B HEVC from its stdout.</summary>
public sealed class FfmpegVideoSource(Config cfg, string inputArgs) : IVideoSource
{
    Process? _proc;

    public static FfmpegVideoSource TestPattern(Config cfg) =>
        new(cfg, $"-re -f lavfi -i testsrc2=size={cfg.Width}x{cfg.Height}:rate={cfg.Fps}");

    public async IAsyncEnumerable<VideoFrame> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        string bitrate = $"{cfg.BitrateMbps}M";
        var psi = new ProcessStartInfo(cfg.FfmpegPath)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in ("-hide_banner -loglevel warning " + inputArgs +
                 $" -pix_fmt yuv420p -c:v hevc_nvenc -preset p1 -tune ull -rc cbr -b:v {bitrate} -maxrate {bitrate} -bufsize 1M" +
                 " -bf 0 -g 600 -forced-idr 1 -aud 1 -profile:v main -flush_packets 1 -f hevc -").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            psi.ArgumentList.Add(a);

        _proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start ffmpeg");
        var stdout = _proc.StandardOutput.BaseStream;
        var splitter = new AccessUnitSplitter();
        var buf = new byte[256 * 1024];
        long t0 = Stopwatch.GetTimestamp();
        while (!ct.IsCancellationRequested)
        {
            int n = await stdout.ReadAsync(buf, ct);
            if (n == 0) yield break;
            foreach (var au in splitter.Push(buf.AsSpan(0, n)))
                yield return new VideoFrame((long)Stopwatch.GetElapsedTime(t0).TotalMicroseconds, au);
        }
    }

    public ValueTask DisposeAsync()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch { }
        _proc?.Dispose();
        return ValueTask.CompletedTask;
    }
}
