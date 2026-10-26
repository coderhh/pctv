using System.Text.Json;

namespace PcTv.Host.Browser;

/// <summary>Named keys → CDP key event fields.</summary>
public static class KeyMap
{
    public readonly record struct Key(string Name, string Code, int Vk, string? Text = null);

    static readonly Dictionary<string, Key> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ArrowUp"] = new("ArrowUp", "ArrowUp", 38),
        ["ArrowDown"] = new("ArrowDown", "ArrowDown", 40),
        ["ArrowLeft"] = new("ArrowLeft", "ArrowLeft", 37),
        ["ArrowRight"] = new("ArrowRight", "ArrowRight", 39),
        ["Enter"] = new("Enter", "Enter", 13, "\r"),
        ["Escape"] = new("Escape", "Escape", 27),
        ["Space"] = new(" ", "Space", 32, " "),
        ["Tab"] = new("Tab", "Tab", 9),
        ["Backspace"] = new("Backspace", "Backspace", 8),
        ["MediaPlayPause"] = new("MediaPlayPause", "MediaPlayPause", 179),
    };

    public static bool TryGet(string name, out Key key) => Keys.TryGetValue(name, out key);
}

/// <summary>Coordinate mapping from the TV's normalized 0..1 space to CSS pixels.</summary>
public static class Coords
{
    public static (double X, double Y) ToCss(double nx, double ny, double vw, double vh) =>
        (Math.Clamp(nx, 0, 1) * (vw - 1), Math.Clamp(ny, 0, 1) * (vh - 1));
}

/// <summary>
/// Tracks the active page target and turns INPUT messages into CDP Input.* / Page.* calls.
/// Never uses OS-level input, so the owner's mouse and keyboard are untouched.
/// </summary>
public sealed class BrowserInput
{
    readonly object _lock = new();
    readonly List<(string TargetId, string SessionId)> _pages = []; // creation order; last = active
    CdpClient? _cdp;
    double _vw = 1920, _vh = 1080, _x, _y;
    DateTime _viewportAt = DateTime.MinValue;

    string? _activeSession;
    DateTime _pickedAt = DateTime.MinValue;

    string? Session { get { lock (_lock) return _activeSession ?? (_pages.Count > 0 ? _pages[^1].SessionId : null); } }

    /// <summary>
    /// Browsers create extra page targets (prerender, tab swaps); the one the user sees is the one whose
    /// document is visible. Re-checked at most once a second.
    /// </summary>
    async Task PickActiveAsync(CdpClient cdp, bool force = false)
    {
        if (!force && (DateTime.UtcNow - _pickedAt).TotalSeconds < 1) return;
        _pickedAt = DateTime.UtcNow;
        List<(string TargetId, string SessionId)> pages;
        lock (_lock) pages = [.. _pages];
        string? best = null;
        foreach (var (_, sid) in pages) // later pages win ties
        {
            try
            {
                var r = await cdp.SendAsync("Runtime.evaluate", new { expression = "document.visibilityState", returnByValue = true }, sid, 1000);
                if (r.GetProperty("result").GetProperty("value").GetString() == "visible") best = sid;
            }
            catch { }
        }
        lock (_lock)
        {
            if (best is not null && best != _activeSession) { _activeSession = best; _viewportAt = DateTime.MinValue; HostStatus.Log("cdp: visible page changed"); }
        }
    }

    /// <summary>Starts target discovery on a fresh connection and tracks pages until it closes.</summary>
    public async Task RunAsync(CdpClient cdp, CancellationToken ct)
    {
        lock (_lock) { _cdp = cdp; _pages.Clear(); }
        cdp.Event += OnEvent;
        try
        {
            await cdp.SendAsync("Target.setDiscoverTargets", new { discover = true });
            HostStatus.CdpText = "Connected";
            HostStatus.Log("cdp: connected");
            _ = CloseRestoredTabsAsync(cdp);
            await Task.WhenAny(cdp.Closed, Task.Delay(Timeout.Infinite, ct));
        }
        finally
        {
            cdp.Event -= OnEvent;
            lock (_lock) { _cdp = null; _pages.Clear(); }
            HostStatus.CdpText = "Disconnected";
            HostStatus.Log("cdp: disconnected");
        }
    }

    /// <summary>The TV profile restores last session's tabs after a crash/kill; keep only the visible one.</summary>
    async Task CloseRestoredTabsAsync(CdpClient cdp)
    {
        try
        {
            await Task.Delay(3000); // let session restore and attachments settle
            await PickActiveAsync(cdp, force: true);
            string? keep = Session;
            List<(string TargetId, string SessionId)> pages;
            lock (_lock) pages = [.. _pages];
            if (keep is null || pages.Count < 2) return;
            foreach (var (targetId, sid) in pages.Where(p => p.SessionId != keep))
                await cdp.SendAsync("Target.closeTarget", new { targetId });
            HostStatus.Log($"cdp: closed {pages.Count - 1} restored tab(s)");
        }
        catch (Exception e) { HostStatus.Log($"cdp: tab cleanup failed: {e.Message}"); }
    }

    void OnEvent(string method, JsonElement p, string? eventSession)
    {
        switch (method)
        {
            case "Target.targetCreated":
            {
                var info = p.GetProperty("targetInfo");
                if (info.GetProperty("type").GetString() == "page")
                    _ = AttachAsync(info.GetProperty("targetId").GetString()!);
                break;
            }
            case "Target.targetDestroyed":
            {
                var id = p.GetProperty("targetId").GetString();
                lock (_lock)
                {
                    var gone = _pages.FirstOrDefault(x => x.TargetId == id).SessionId;
                    _pages.RemoveAll(x => x.TargetId == id);
                    if (gone is not null && gone == _activeSession) { _activeSession = null; _pickedAt = DateTime.MinValue; }
                }
                break;
            }
        }
    }

    async Task AttachAsync(string targetId)
    {
        var cdp = _cdp;
        if (cdp is null) return;
        try
        {
            var r = await cdp.SendAsync("Target.attachToTarget", new { targetId, flatten = true });
            var sid = r.GetProperty("sessionId").GetString()!;
            lock (_lock) { _pages.RemoveAll(x => x.TargetId == targetId); _pages.Add((targetId, sid)); }
            _viewportAt = DateTime.MinValue;
            _pickedAt = DateTime.MinValue;
            HostStatus.Log($"cdp: attached page {targetId[..Math.Min(8, targetId.Length)]}");
        }
        catch (Exception e) { HostStatus.Log($"cdp: attach failed: {e.Message}"); }
    }

    async Task RefreshViewportAsync(CdpClient cdp, string sid)
    {
        if ((DateTime.UtcNow - _viewportAt).TotalSeconds < 3) return;
        // innerWidth/innerHeight include scrollbars, matching what the TV sees in the video; clientWidth would not.
        var r = await cdp.SendAsync("Runtime.evaluate",
            new { expression = "[innerWidth, innerHeight]", returnByValue = true }, sid, 1000);
        var v = r.GetProperty("result").GetProperty("value");
        _vw = v[0].GetDouble();
        _vh = v[1].GetDouble();
        _viewportAt = DateTime.UtcNow;
    }

    public (double W, double H) Viewport => (_vw, _vh);

    /// <summary>Handles one INPUT JSON message. Throws nothing the caller needs to care about.</summary>
    public async Task HandleAsync(JsonElement msg)
    {
        var cdp = _cdp;
        if (cdp is null) return; // browser not ready; drop input
        await PickActiveAsync(cdp);
        var sid = Session;
        if (sid is null) return;
        try
        {
            string t = msg.GetProperty("t").GetString() ?? "";
            switch (t)
            {
                case "move":
                    await RefreshViewportAsync(cdp, sid);
                    (_x, _y) = Coords.ToCss(msg.GetProperty("x").GetDouble(), msg.GetProperty("y").GetDouble(), _vw, _vh);
                    await Mouse(cdp, sid, "mouseMoved", "none", 0, 0);
                    break;

                case "click":
                {
                    string button = msg.TryGetProperty("button", out var b) ? b.GetString() ?? "left" : "left";
                    int count = msg.TryGetProperty("count", out var c) ? Math.Max(1, c.GetInt32()) : 1;
                    int mask = button == "right" ? 2 : 1;
                    await Mouse(cdp, sid, "mousePressed", button, mask, count);
                    await Mouse(cdp, sid, "mouseReleased", button, 0, count);
                    break;
                }

                case "scroll":
                    await cdp.SendAsync("Input.dispatchMouseEvent", new
                    {
                        type = "mouseWheel", x = _x, y = _y,
                        deltaX = msg.GetProperty("dx").GetDouble(), deltaY = msg.GetProperty("dy").GetDouble(),
                    }, sid);
                    break;

                case "key":
                    if (KeyMap.TryGet(msg.GetProperty("key").GetString() ?? "", out var k))
                    {
                        var down = new Dictionary<string, object>
                        {
                            ["type"] = k.Text is null ? "rawKeyDown" : "keyDown", ["key"] = k.Name, ["code"] = k.Code,
                            ["windowsVirtualKeyCode"] = k.Vk, ["nativeVirtualKeyCode"] = k.Vk,
                        };
                        if (k.Text is not null) down["text"] = k.Text; // null is rejected by CDP
                        await cdp.SendAsync("Input.dispatchKeyEvent", down, sid);
                        await cdp.SendAsync("Input.dispatchKeyEvent", new
                        {
                            type = "keyUp", key = k.Name, code = k.Code, windowsVirtualKeyCode = k.Vk, nativeVirtualKeyCode = k.Vk,
                        }, sid);
                    }
                    break;

                case "back":
                {
                    var h = await cdp.SendAsync("Page.getNavigationHistory", null, sid);
                    int cur = h.GetProperty("currentIndex").GetInt32();
                    if (cur > 0)
                    {
                        int entry = h.GetProperty("entries")[cur - 1].GetProperty("id").GetInt32();
                        await cdp.SendAsync("Page.navigateToHistoryEntry", new { entryId = entry }, sid);
                    }
                    break;
                }

                case "navigate":
                {
                    var url = msg.GetProperty("url").GetString() ?? "";
                    if (IsAllowedUrl(url)) await cdp.SendAsync("Page.navigate", new { url }, sid);
                    break;
                }
            }
        }
        catch (Exception e) when (e is TimeoutException or IOException or InvalidOperationException or System.Net.WebSockets.WebSocketException)
        {
            HostStatus.Log($"input {msg.GetRawText()}: {e.Message}");
        }
    }

    public static bool IsAllowedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "file";

    Task Mouse(CdpClient cdp, string sid, string type, string button, int buttons, int clickCount) =>
        cdp.SendAsync("Input.dispatchMouseEvent",
            new { type, x = _x, y = _y, button, buttons, clickCount, pointerType = "mouse" }, sid);
}
