using PcTv.Host.Browser;
using PcTv.Host.Display;
using Xunit;

namespace PcTv.Host.Tests;

public class MonitorTests
{
    static MonitorInfo M(string n, int w, int h, bool primary, int x = 0, string id = "") => new(n, x, 0, w, h, primary, 0, 0, "gpu", "", id);

    [Fact]
    public void Picks_non_primary_with_matching_size()
    {
        var list = new[] { M("D1", 2560, 1440, true), M("D2", 2560, 1440, false, -2560), M("D3", 3840, 2160, false, 2560, @"MONITOR\MTT1337\{x}") };
        Assert.Equal("D3", VirtualMonitor.Pick(list, null, 3840, 2160)!.DeviceName);
    }

    [Fact]
    public void Never_auto_picks_a_physical_monitor()
    {
        // A real second 4K screen (e.g. Dell) must not be taken over when no virtual driver is present.
        var list = new[] { M("D1", 3840, 2160, true, 0, @"MONITOR\MSI5CE8\x"), M("D2", 3840, 2160, false, -3840, @"MONITOR\DEL4278\x") };
        Assert.Null(VirtualMonitor.Pick(list, null, 3840, 2160));
    }

    [Fact]
    public void Never_picks_primary_or_wrong_size()
    {
        var list = new[] { M("D1", 3840, 2160, true, 0, @"MONITOR\MTT1337\x"), M("D2", 2560, 1440, false, 0, @"MONITOR\MTT1337\x") };
        Assert.Null(VirtualMonitor.Pick(list, null, 3840, 2160));
    }

    [Fact]
    public void Configured_name_wins()
    {
        var list = new[] { M("D1", 2560, 1440, true), M("D2", 2560, 1440, false) };
        Assert.Equal("D2", VirtualMonitor.Pick(list, @"d2", 3840, 2160)!.DeviceName);
        Assert.Null(VirtualMonitor.Pick(list, "D9", 3840, 2160));
    }

    [Fact]
    public void Chrome_args_place_window_on_monitor()
    {
        var args = ChromeLauncher.BuildArgs(new Config(), M("D3", 3840, 2160, false, 2560), "https://x/");
        Assert.Contains("--window-position=2560,0", args);
        Assert.Contains("--window-size=3840,2160", args);
        Assert.Contains("--kiosk", args);
        Assert.Equal("https://x/", args[^1]);
    }

    [Fact]
    public void OnMonitor_uses_window_centre()
    {
        var mon = M("D3", 3840, 2160, false, 2560);
        Assert.True(ChromeLauncher.OnMonitor(new ChromeLauncher.RECT { Left = 2560, Top = 0, Right = 6400, Bottom = 2160 }, mon));
        Assert.False(ChromeLauncher.OnMonitor(new ChromeLauncher.RECT { Left = 0, Top = 0, Right = 2560, Bottom = 1440 }, mon));
    }
}
