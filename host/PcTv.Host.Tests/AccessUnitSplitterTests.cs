using PcTv.Host.Capture;
using Xunit;

namespace PcTv.Host.Tests;

public class AccessUnitSplitterTests
{
    // AUD (00 00 00 01 46 01 50) + a slice-ish NAL, repeated; payload byte marks the AU.
    static byte[] Au(byte mark) => [0, 0, 0, 1, 0x46, 0x01, 0x50, 0, 0, 1, 0x26, 0x01, mark, mark];

    [Fact]
    public void Splits_on_aud_and_emits_previous_au()
    {
        var stream = Au(1).Concat(Au(2)).Concat(Au(3)).ToArray();
        var aus = new AccessUnitSplitter().Push(stream);
        Assert.Equal(2, aus.Count); // third is incomplete until the next AUD
        Assert.Equal(Au(1), aus[0]);
        Assert.Equal(Au(2), aus[1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Chunking_does_not_change_result(int chunk)
    {
        var stream = Enumerable.Range(1, 6).SelectMany(m => Au((byte)m)).Concat(Au(7)).ToArray();
        var sp = new AccessUnitSplitter();
        var all = new List<byte[]>();
        for (int i = 0; i < stream.Length; i += chunk)
            all.AddRange(sp.Push(stream.AsSpan(i, Math.Min(chunk, stream.Length - i))));
        Assert.Equal(6, all.Count);
        for (int m = 1; m <= 6; m++) Assert.Equal(Au((byte)m), all[m - 1]);
    }
}
