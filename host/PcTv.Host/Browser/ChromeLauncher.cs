using System.Diagnostics;
using System.Runtime.InteropServices;
using PcTv.Host.Display;

namespace PcTv.Host.Browser;

/// <summary>
/// Owns the TV Chrome process: launches it kiosk on the virtual monitor, verifies/forces its window
/// placement, and relaunches it if it exits. Uses its own exe + profile so it never touches the owner's browser.
/// </summary>
public sealed class ChromeLauncher(Config cfg)
{
    public static string ProfileDir => Path.Combine(Config.DataDir, "chrome-profile");

    public static string[] BuildArgs(Config cfg, MonitorInfo mon, string url) =>
    [
        $"--user-data-dir={ProfileDir}",
        $"--remote-debugging-port={cfg.ChromeDebugPort}",
        "--remote-allow-origins=http://127.0.0.1",
        "--kiosk",
        $"--window-position={mon.X},{mon.Y}",
        $"--window-size={mon.Width},{mon.Height}",
        "--force-device-scale-factor=2",
        "--enable-spatial-navigation",
        "--autoplay-policy=no-user-gesture-required",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-features=Translate",
        url,
    ];

    /// <summary>Runs until cancelled: launch, place, wait for exit, relaunch.</summary>
    public async Task RunAsync(MonitorInfo mon, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Process? proc = null;
            try
            {
                var psi = new ProcessStartInfo(cfg.ChromePath) { UseShellExecute = false };
                foreach (var a in BuildArgs(cfg, mon, cfg.HomeUrl)) psi.ArgumentList.Add(a);
                proc = Process.Start(psi) ?? throw new InvalidOperationException("Chrome did not start");
                HostStatus.ChromeText = $"Starting (pid {proc.Id})";
                HostStatus.Log($"chrome: started pid {proc.Id}");

                await PlaceAsync(proc, mon, ct);
                await proc.WaitForExitAsync(ct);
                HostStatus.ChromeText = "Exited, restarting";
                HostStatus.Log($"chrome: exited with code {proc.ExitCode}, restarting");
            }
            catch (OperationCanceledException)
            {
                try { if (proc is { HasExited: false }) proc.Kill(true); } catch { }
                return;
            }
            catch (Exception e)
            {
                HostStatus.ChromeText = $"Error: {e.Message}";
                HostStatus.Log($"chrome: {e.Message}");
            }
            try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Waits for the window, then makes sure it sits on the virtual monitor; retries SetWindowPos.</summary>
    async Task PlaceAsync(Process proc, MonitorInfo mon, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 40; attempt++) // ~20 s
        {
            await Task.Delay(500, ct);
            proc.Refresh();
            var h = proc.MainWindowHandle;
            if (h == IntPtr.Zero || !GetWindowRect(h, out var r)) continue;
            if (OnMonitor(r, mon))
            {
                HostStatus.ChromeText = $"Running on {mon.DeviceName}";
                HostStatus.Log($"chrome: window on {mon.DeviceName}");
                return;
            }
            HostStatus.Log($"chrome: window at ({r.Left},{r.Top},{r.Right},{r.Bottom}), moving to {mon.DeviceName}");
            SetWindowPos(h, IntPtr.Zero, mon.X, mon.Y, mon.Width, mon.Height, 0x0004 /*NOZORDER*/ | 0x0010 /*NOACTIVATE*/);
        }
        HostStatus.ChromeText = "Window not placed";
        HostStatus.Log("chrome: could not place window on the virtual monitor");
    }

    /// <summary>True if the window's centre lies inside the monitor.</summary>
    public static bool OnMonitor(RECT r, MonitorInfo mon)
    {
        int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
        return cx >= mon.X && cx < mon.X + mon.Width && cy >= mon.Y && cy < mon.Y + mon.Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
