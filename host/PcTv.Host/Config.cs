using System.Text.Json;

namespace PcTv.Host;

public sealed class Config
{
    public int Port { get; set; } = 47800;
    public string HostName { get; set; } = Environment.MachineName;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string ChromePath { get; set; } = @"C:\pctv\chrome\chrome.exe";
    public int ChromeDebugPort { get; set; } = 9222;
    public string HomeUrl { get; set; } = "https://www.youtube.com/";
    public string? VirtualMonitorName { get; set; }
    public int Width { get; set; } = 3840;
    public int Height { get; set; } = 2160;
    public int Fps { get; set; } = 60;
    public int BitrateMbps { get; set; } = 30;
    public string AudioDevice { get; set; } = "CABLE Output";
    public bool RequirePairing { get; set; } = true;

    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("PCTV_DATA_DIR") is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pctv");

    public static string ConfigPath => Path.Combine(DataDir, "pctv.json");

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>Loads pctv.json from the data dir, writing defaults on first run.</summary>
    public static Config Load()
    {
        Directory.CreateDirectory(DataDir);
        if (!File.Exists(ConfigPath))
        {
            var def = new Config();
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(def, Opts));
            return def;
        }
        return JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath), Opts) ?? new Config();
    }
}
