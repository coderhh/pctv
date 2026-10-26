using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PcTv.Host.Capture;

namespace PcTv.Host.Net;

/// <summary>TCP listener; one TV session at a time (a new connection replaces the old one).</summary>
public sealed class Server(Config cfg, Pairing pairing, bool testPattern)
{
    readonly string _hostId = LoadOrCreateHostId();
    CancellationTokenSource? _sessionCts;

    public string HostId => _hostId;

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, cfg.Port);
        listener.Start();
        Console.WriteLine($"pctv listening on :{cfg.Port} (host id {_hostId}){(testPattern ? " [test pattern]" : "")}");
        using var stopReg = ct.Register(listener.Stop);
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            client.NoDelay = true;
            _sessionCts?.Cancel();
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = HandleAsync(client, _sessionCts.Token);
        }
    }

    async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint;
        Console.WriteLine($"[{remote}] connected");
        try
        {
            using var _c = client;
            using var _r = ct.Register(client.Close);
            var s = client.GetStream();
            var writeLock = new SemaphoreSlim(1, 1);
            async Task Send(MsgType t, object body)
            {
                await writeLock.WaitAsync(ct);
                try { await Protocol.WriteJsonAsync(s, t, body, ct); } finally { writeLock.Release(); }
            }

            bool welcomed = false;
            string tvId = "";
            while (await Protocol.ReadAsync(s, ct) is { } msg)
            {
                switch (msg.Type)
                {
                    case MsgType.Hello:
                    {
                        using var doc = msg.Json();
                        var root = doc.RootElement;
                        int v = root.TryGetProperty("v", out var ve) ? ve.GetInt32() : 0;
                        if (v != 1) { await Send(MsgType.Welcome, new { v = 1, error = "version" }); return; }
                        tvId = root.GetProperty("tvId").GetString() ?? "";
                        string? token = root.TryGetProperty("token", out var te) ? te.GetString() : null;
                        if (cfg.RequirePairing && !pairing.IsValid(tvId, token))
                        {
                            await Send(MsgType.Welcome, new { v = 1, error = "unpaired" });
                            break; // stay connected so the TV can PAIR_BEGIN
                        }
                        welcomed = true;
                        await Send(MsgType.Welcome, new
                        {
                            v = 1,
                            hostId = _hostId,
                            name = cfg.HostName,
                            video = new { codec = "hevc", w = cfg.Width, h = cfg.Height, fps = cfg.Fps, bitrate = cfg.BitrateMbps * 1_000_000 },
                            audio = new { codec = "pcm_s16le", rate = 48000, channels = 2 },
                            viewport = new { w = cfg.Width / 2, h = cfg.Height / 2 },
                        });
                        await Send(MsgType.Apps, LoadApps());
                        Console.WriteLine($"[{remote}] session started for tv {tvId}");
                        _ = StreamAsync(s, writeLock, ct);
                        break;
                    }
                    case MsgType.PairBegin:
                    {
                        using var doc = msg.Json();
                        var id = doc.RootElement.GetProperty("tvId").GetString() ?? "";
                        var pin = doc.RootElement.GetProperty("pin").GetString() ?? "";
                        var token = pairing.TryPair(id, pin);
                        await Send(MsgType.PairResult, token is null ? new { ok = false } : new { ok = true, token });
                        Console.WriteLine($"[{remote}] pairing {(token is null ? "failed" : "succeeded")} for tv {id}");
                        break;
                    }
                    case MsgType.Ping:
                        await writeLock.WaitAsync(ct);
                        try { await Protocol.WriteAsync(s, MsgType.Pong, msg.Payload, ct); } finally { writeLock.Release(); }
                        break;
                    default:
                        if (!welcomed) break; // ignore everything else until a valid HELLO
                        break; // INPUT / TEXT_COMMIT / STATS handled from M1+
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or EndOfStreamException)
        {
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[{remote}] error: {e.Message}");
        }
        Console.WriteLine($"[{remote}] disconnected");
    }

    /// <summary>Pumps VIDEO and AUDIO until the session ends. Real capture arrives in M2/M4.</summary>
    async Task StreamAsync(Stream s, SemaphoreSlim writeLock, CancellationToken ct)
    {
        if (!testPattern) return;

        async Task SendTimed(MsgType type, long pts, byte[] data)
        {
            var payload = new byte[8 + data.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(payload, (ulong)pts);
            data.CopyTo(payload, 8);
            await writeLock.WaitAsync(ct);
            try { await Protocol.WriteAsync(s, type, payload, ct); } finally { writeLock.Release(); }
        }

        async Task Video()
        {
            await using var src = FfmpegVideoSource.TestPattern(cfg);
            await foreach (var f in src.ReadAsync(ct)) await SendTimed(MsgType.Video, f.PtsMicros, f.AnnexB);
        }
        async Task Audio()
        {
            await foreach (var c in new ToneAudioSource().ReadAsync(ct)) await SendTimed(MsgType.Audio, c.PtsMicros, c.Pcm);
        }

        try { await Task.WhenAll(Video(), Audio()); }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
        catch (Exception e) { Console.Error.WriteLine($"stream error: {e.Message}"); }
    }

    static object LoadApps()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "apps.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path)) : Array.Empty<object>();
    }

    static string LoadOrCreateHostId()
    {
        var p = Path.Combine(Config.DataDir, "host-id.txt");
        Directory.CreateDirectory(Config.DataDir);
        if (File.Exists(p)) return File.ReadAllText(p).Trim();
        var id = Guid.NewGuid().ToString();
        File.WriteAllText(p, id);
        return id;
    }
}
