namespace PcTv.Host;

/// <summary>Thread-safe snapshot of what the host is doing; written by Server, read by the UI.</summary>
public static class HostStatus
{
    static readonly object Lock = new();
    static readonly Queue<string> Lines = new();

    public static volatile bool Listening;
    public static int Port;
    public static string HostId = "";
    public static volatile bool TestPattern;
    public static volatile string? TvId;
    public static volatile string? Remote;
    public static long VideoFrames, VideoBytes, AudioChunks;
    public static volatile string? Error;
    public static volatile string MonitorText = "n/a";
    public static volatile string ChromeText = "n/a";
    /// <summary>Set by the browser host once the virtual monitor is found; read by the capture pipeline.</summary>
    public static volatile PcTv.Host.Display.MonitorInfo? Monitor;

    public static bool Connected => Remote is not null;

    public static void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {msg}";
        Console.WriteLine(line);
        lock (Lock)
        {
            Lines.Enqueue(line);
            while (Lines.Count > 200) Lines.Dequeue();
            try { File.AppendAllText(Path.Combine(Config.DataDir, "host.log"), line + Environment.NewLine); } catch { }
        }
    }

    public static string[] RecentLog()
    {
        lock (Lock) return Lines.ToArray();
    }

    public static void ResetSession()
    {
        Interlocked.Exchange(ref VideoFrames, 0);
        Interlocked.Exchange(ref VideoBytes, 0);
        Interlocked.Exchange(ref AudioChunks, 0);
    }
}
