using System.Diagnostics;

namespace PcTv.Host;

/// <summary>Checks prerequisites and prints how to fix each. Grows with each milestone.</summary>
public static class Doctor
{
    public static int Run(Config cfg)
    {
        int bad = 0;
        void Check(string name, bool ok, string fix)
        {
            Console.WriteLine($"[{(ok ? " ok " : "FAIL")}] {name}");
            if (!ok) { Console.WriteLine($"       -> {fix}"); bad++; }
        }

        Check($"config: {Config.ConfigPath}", File.Exists(Config.ConfigPath), "run `pctv run` once to create it");
        Check($"ffmpeg ({cfg.FfmpegPath})", FfmpegHas(cfg.FfmpegPath, "-encoders", "hevc_nvenc") && FfmpegHas(cfg.FfmpegPath, "-filters", "ddagrab"),
            "install the gyan.dev 'full' FFmpeg build and set FfmpegPath in pctv.json");
        Check($"TV Chrome ({cfg.ChromePath})", File.Exists(cfg.ChromePath),
            "unpack Chrome for Testing / Chromium there and set ChromePath in pctv.json");
        var mons = PcTv.Host.Display.VirtualMonitor.Enumerate();
        var vm = PcTv.Host.Display.VirtualMonitor.Pick(mons, cfg.VirtualMonitorName, cfg.Width, cfg.Height);
        Check($"virtual monitor ({cfg.Width}x{cfg.Height}, non-primary)", vm is not null,
            "install a virtual display driver and add a " + cfg.Width + "x" + cfg.Height + " display (Settings > System > Display), or set VirtualMonitorName; found: " +
            string.Join("; ", mons.Select(m => $"{m.DeviceName} {m.Width}x{m.Height}")));
        if (vm is not null) Console.WriteLine($"       {vm}");
        return bad == 0 ? 0 : 1;
    }

    static bool FfmpegHas(string exe, string listArg, string token)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, $"-hide_banner {listArg}")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return o.Contains(token);
        }
        catch { return false; }
    }
}
