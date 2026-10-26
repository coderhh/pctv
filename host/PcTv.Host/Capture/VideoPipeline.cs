using System.Diagnostics;

namespace PcTv.Host.Capture;

public readonly record struct VideoFrame(long PtsMicros, byte[] AnnexB);

/// <summary>Swap point for the capture backend (ddagrab today, WGC fallback later).</summary>
public interface IVideoSource : IAsyncDisposable
{
    IAsyncEnumerable<VideoFrame> ReadAsync(CancellationToken ct);
}

/// <summary>Spawns ffmpeg per session and reads Annex-B HEVC from its stdout.</summary>
public sealed class FfmpegVideoSource(Config cfg, string inputArgs, string frameArgs) : IVideoSource
{
    Process? _proc;

    public static FfmpegVideoSource TestPattern(Config cfg) =>
        new(cfg, $"-re -f lavfi -i testsrc2=size={cfg.Width}x{cfg.Height}:rate={cfg.Fps}", "-pix_fmt yuv420p");

    /// <summary>
    /// Desktop Duplication of one DXGI output. Frames stay on the GPU (d3d11) into NVENC, so no pix_fmt conversion here.
    /// An adapter other than 0 needs an explicit D3D11 device for the filter graph.
    /// </summary>
    public static FfmpegVideoSource Ddagrab(Config cfg, Display.MonitorInfo mon)
    {
        string dev = mon.AdapterIndex == 0 ? "" : $"-init_hw_device d3d11va=dx:{mon.AdapterIndex} -filter_hw_device dx ";
        return new(cfg, $"{dev}-f lavfi -i ddagrab=output_idx={mon.OutputIndex}:framerate={cfg.Fps}:draw_mouse=0", "");
    }

    public static string BuildEncodeArgs(Config cfg, string frameArgs)
    {
        string bitrate = $"{cfg.BitrateMbps}M";
        return $"{frameArgs} -c:v hevc_nvenc -preset p1 -tune ull -rc cbr -b:v {bitrate} -maxrate {bitrate} -bufsize 1M" +
               " -bf 0 -g 600 -forced-idr 1 -aud 1 -profile:v main -flush_packets 1 -f hevc -";
    }

    public async IAsyncEnumerable<VideoFrame> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var psi = new ProcessStartInfo(cfg.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in ("-hide_banner -loglevel warning " + inputArgs + " " + BuildEncodeArgs(cfg, frameArgs))
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            psi.ArgumentList.Add(a);

        _proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start ffmpeg");
        // Surface ffmpeg's own warnings/errors in the host log; an unread stderr pipe could also block ffmpeg.
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await _proc.StandardError.ReadLineAsync()) is not null) HostStatus.Log($"ffmpeg: {line}");
        });
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
