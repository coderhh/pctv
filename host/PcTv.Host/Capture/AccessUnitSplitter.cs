namespace PcTv.Host.Capture;

/// <summary>
/// Splits an Annex-B HEVC byte stream into access units. Each AU starts at an
/// access unit delimiter NAL (type 35, first header byte 0x46) — ffmpeg emits these with `-aud 1`.
/// Data before the first AUD is discarded.
/// </summary>
public sealed class AccessUnitSplitter
{
    byte[] _buf = new byte[1 << 20];
    int _len;
    int _auStart = -1; // start of the AU being accumulated, -1 until the first AUD
    int _scan;         // next index to test for an AUD start code

    /// <summary>Appends data; returns every AU completed by it (an AU completes when the next AUD arrives).</summary>
    public List<byte[]> Push(ReadOnlySpan<byte> data)
    {
        if (_len + data.Length > _buf.Length)
            Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + data.Length));
        data.CopyTo(_buf.AsSpan(_len));
        _len += data.Length;

        var result = new List<byte[]>();
        int i = _scan;
        for (; i + 3 < _len; i++)
        {
            if (_buf[i] != 0 || _buf[i + 1] != 0 || _buf[i + 2] != 1 || _buf[i + 3] != 0x46) continue;
            int start = (i > _auStart + 0 && i > 0 && _buf[i - 1] == 0 && i - 1 > _auStart) ? i - 1 : i; // 4-byte start code
            if (_auStart >= 0) result.Add(_buf.AsSpan(_auStart, start - _auStart).ToArray());
            _auStart = start;
            i += 3;
        }
        _scan = i;

        // Compact: drop consumed bytes before the current AU (or everything but a possible partial start code).
        int keepFrom = _auStart >= 0 ? _auStart : Math.Max(0, _len - 4);
        if (keepFrom > 0)
        {
            Buffer.BlockCopy(_buf, keepFrom, _buf, 0, _len - keepFrom);
            _len -= keepFrom;
            _scan -= keepFrom;
            if (_auStart >= 0) _auStart = 0;
            if (_scan < 0) _scan = 0;
        }
        return result;
    }
}
