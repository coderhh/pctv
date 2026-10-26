using PcTv.Host.Browser;
using Xunit;

namespace PcTv.Host.Tests;

public class InputTests
{
    [Fact]
    public void Coords_map_corners_and_clamp()
    {
        Assert.Equal((0.0, 0.0), Coords.ToCss(0, 0, 1920, 1080));
        Assert.Equal((1919.0, 1079.0), Coords.ToCss(1, 1, 1920, 1080));
        Assert.Equal((1919.0, 0.0), Coords.ToCss(5, -3, 1920, 1080));
        Assert.Equal(959.5, Coords.ToCss(0.5, 0.5, 1920, 1080).X, 3);
    }

    [Theory]
    [InlineData("ArrowUp", 38)]
    [InlineData("Enter", 13)]
    [InlineData("Escape", 27)]
    [InlineData("Space", 32)]
    [InlineData("Tab", 9)]
    [InlineData("Backspace", 8)]
    [InlineData("MediaPlayPause", 179)]
    [InlineData("arrowleft", 37)]
    public void Named_keys_have_virtual_key_codes(string name, int vk)
    {
        Assert.True(KeyMap.TryGet(name, out var k));
        Assert.Equal(vk, k.Vk);
    }

    [Fact]
    public void Space_types_a_space_and_unknown_keys_are_rejected()
    {
        Assert.True(KeyMap.TryGet("Space", out var k));
        Assert.Equal(" ", k.Name);
        Assert.Equal(" ", k.Text);
        Assert.False(KeyMap.TryGet("Hyperspace", out _));
    }

    [Theory]
    [InlineData("https://www.youtube.com/", true)]
    [InlineData("http://192.168.1.2/x", true)]
    [InlineData("file:///D:/x.html", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("chrome://settings", false)]
    [InlineData("not a url", false)]
    public void Only_web_and_file_urls_may_be_navigated(string url, bool ok) =>
        Assert.Equal(ok, BrowserInput.IsAllowedUrl(url));
}
