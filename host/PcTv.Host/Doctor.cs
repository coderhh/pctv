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
        Check($"ffmpeg ({cfg.FfmpegPath})", FfmpegHas(cfg.FfmpegPath, "hevc_nvenc") && FfmpegHas(cfg.FfmpegPath, "ddagrab"),
            "install the gyan.dev 'full' FFmpeg build and set FfmpegPath in pctv.json");
        Check($"TV Chrome ({cfg.ChromePath})", File.Exists(cfg.ChromePath),
            "unpack Chrome for Testing / Chromium there and set ChromePath in pctv.json");
        return bad == 0 ? 0 : 1;
    }

    static bool FfmpegHas(string exe, string token)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, "-hide_banner -encoders -filters")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return o.Contains(token);
        }
        catch { return false; }
    }
}
