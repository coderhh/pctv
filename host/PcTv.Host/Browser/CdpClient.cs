using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PcTv.Host.Browser;

/// <summary>Minimal Chrome DevTools Protocol client over the browser-level websocket (flattened sessions).</summary>
public sealed class CdpClient : IAsyncDisposable
{
    readonly ClientWebSocket _ws = new();
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    readonly SemaphoreSlim _sendLock = new(1, 1);
    readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _nextId;

    /// <summary>method, params, sessionId (null for browser-level events)</summary>
    public event Action<string, JsonElement, string?>? Event;

    /// <summary>Completes when the websocket closes or fails.</summary>
    public Task Closed => _closed.Task;

    public static async Task<CdpClient> ConnectAsync(int port, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/version", ct));
        var url = doc.RootElement.GetProperty("webSocketDebuggerUrl").GetString()!;
        var c = new CdpClient();
        await c._ws.ConnectAsync(new Uri(url), ct);
        _ = c.ReceiveLoopAsync();
        return c;
    }

    public async Task<JsonElement> SendAsync(string method, object? args = null, string? sessionId = null, int timeoutMs = 3000)
    {
        int id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var msg = new Dictionary<string, object?> { ["id"] = id, ["method"] = method, ["params"] = args ?? new { } };
        if (sessionId is not null) msg["sessionId"] = sessionId;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(msg);
        await _sendLock.WaitAsync();
        try { await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { _pending.TryRemove(id, out _); throw; }
        finally { _sendLock.Release(); }

        using var cts = new CancellationTokenSource(timeoutMs);
        using var reg = cts.Token.Register(() => tcs.TrySetException(new TimeoutException($"CDP {method} timed out")));
        try { return await tcs.Task; }
        finally { _pending.TryRemove(id, out _); }
    }

    async Task ReceiveLoopAsync()
    {
        var buf = new byte[64 * 1024];
        try
        {
            while (_ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(buf, CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buf, 0, r.Count);
                } while (!r.EndOfMessage);

                using var doc = JsonDocument.Parse(ms.ToArray());
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl))
                {
                    if (_pending.TryGetValue(idEl.GetInt32(), out var tcs))
                    {
                        if (root.TryGetProperty("error", out var err))
                            tcs.TrySetException(new InvalidOperationException($"{err.GetProperty("message").GetString()} ({err})"));
                        else
                            tcs.TrySetResult(root.TryGetProperty("result", out var res) ? res.Clone() : default);
                    }
                }
                else if (root.TryGetProperty("method", out var m))
                {
                    string? sid = root.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
                    var p = root.TryGetProperty("params", out var pe) ? pe.Clone() : default;
                    try { Event?.Invoke(m.GetString()!, p, sid); } catch { }
                }
            }
        }
        catch { }
        finally
        {
            foreach (var t in _pending.Values) t.TrySetException(new IOException("CDP connection closed"));
            _closed.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_ws.State == WebSocketState.Open) await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch { }
        _ws.Dispose();
    }
}
