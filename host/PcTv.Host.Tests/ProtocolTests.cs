using PcTv.Host.Net;
using Xunit;

namespace PcTv.Host.Tests;

public class ProtocolTests
{
    [Fact]
    public async Task Frame_round_trips()
    {
        var ms = new MemoryStream();
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        await Protocol.WriteAsync(ms, MsgType.Video, payload);
        ms.Position = 0;

        var msg = await Protocol.ReadAsync(ms);
        Assert.NotNull(msg);
        Assert.Equal(MsgType.Video, msg!.Value.Type);
        Assert.Equal(payload, msg.Value.Payload);
        Assert.Null(await Protocol.ReadAsync(ms)); // clean EOF
    }

    [Fact]
    public async Task Length_is_big_endian()
    {
        var ms = new MemoryStream();
        await Protocol.WriteAsync(ms, MsgType.Ping, new byte[0x102]);
        var b = ms.ToArray();
        Assert.Equal(new byte[] { 0x40, 0, 0, 0, 0x01, 0x02 }, b[..6]);
    }

    [Fact]
    public async Task Oversized_payload_is_rejected()
    {
        var ms = new MemoryStream(new byte[] { 0x10, 0, 0xFF, 0xFF, 0xFF, 0xFF });
        await Assert.ThrowsAsync<InvalidDataException>(() => Protocol.ReadAsync(ms));
    }
}
