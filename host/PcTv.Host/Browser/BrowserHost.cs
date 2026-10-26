using PcTv.Host.Display;

namespace PcTv.Host.Browser;

/// <summary>Real-mode startup: find the virtual monitor, then keep TV Chrome running on it.</summary>
public static class BrowserHost
{
    public static async Task RunAsync(Config cfg, CancellationToken ct)
    {
        if (!File.Exists(cfg.ChromePath))
        {
            HostStatus.ChromeText = "Chrome not found";
            HostStatus.Error = $"TV Chrome missing: {cfg.ChromePath}";
            HostStatus.Log($"chrome: not found at {cfg.ChromePath} (set ChromePath in pctv.json)");
            return;
        }

        // The virtual monitor may appear/disappear (driver toggled); poll until present.
        MonitorInfo? mon = null;
        while (!ct.IsCancellationRequested && mon is null)
        {
            var all = VirtualMonitor.Enumerate();
            mon = VirtualMonitor.Pick(all, cfg.VirtualMonitorName, cfg.Width, cfg.Height);
            if (mon is null)
            {
                HostStatus.MonitorText = $"No {cfg.Width}x{cfg.Height} virtual monitor found";
                HostStatus.Log($"monitor: none matching ({all.Count} outputs: {string.Join("; ", all.Select(m => $"{m.DeviceName} {m.Width}x{m.Height}"))})");
                try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { return; }
            }
        }
        if (mon is null) return;
        HostStatus.Monitor = mon;
        HostStatus.MonitorText = $"{mon.DeviceName} {mon.Width}x{mon.Height} (adapter {mon.AdapterIndex}, output {mon.OutputIndex})";
        HostStatus.Log($"monitor: using {mon}");

        await new ChromeLauncher(cfg).RunAsync(mon, ct);
    }
}
