using System.Buffers.Binary;
using System.Text.Json;

namespace PcTv.Host.Net;

public enum MsgType : byte
{
    Hello = 0x01,
    Welcome = 0x02,
    PairBegin = 0x03,
    PairResult = 0x04,
    Video = 0x10,
    Audio = 0x11,
    Input = 0x20,
    TextCommit = 0x21,
    TextFocus = 0x30,
    Apps = 0x31,
    Ping = 0x40,
    Pong = 0x41,
    Stats = 0x42,
}

public readonly record struct Message(MsgType Type, byte Flags, byte[] Payload)
{
    public JsonDocument Json() => JsonDocument.Parse(Payload);
}

/// <summary>Framing: u8 type, u8 flags, u32 BE length, payload. See docs/protocol.md.</summary>
public static class Protocol
{
    public const int HeaderSize = 6;
    public const int MaxPayload = 16 * 1024 * 1024;

    public static async Task WriteAsync(Stream s, MsgType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        var header = new byte[HeaderSize];
        header[0] = (byte)type;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(2), (uint)payload.Length);
        await s.WriteAsync(header, ct);
        await s.WriteAsync(payload, ct);
        await s.FlushAsync(ct);
    }

    public static Task WriteJsonAsync(Stream s, MsgType type, object body, CancellationToken ct = default) =>
        WriteAsync(s, type, JsonSerializer.SerializeToUtf8Bytes(body), ct);

    /// <summary>Returns null on clean EOF at a message boundary.</summary>
    public static async Task<Message?> ReadAsync(Stream s, CancellationToken ct = default)
    {
        var header = new byte[HeaderSize];
        if (!await ReadFullAsync(s, header, ct)) return null;
        uint len = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(2));
        if (len > MaxPayload) throw new InvalidDataException($"payload too large: {len}");
        var payload = new byte[len];
        if (len > 0 && !await ReadFullAsync(s, payload, ct)) throw new EndOfStreamException();
        return new Message((MsgType)header[0], header[1], payload);
    }

    static async Task<bool> ReadFullAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int got = 0;
        while (got < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(got), ct);
            if (n == 0) return got == 0 ? false : throw new EndOfStreamException();
            got += n;
        }
        return true;
    }
}
