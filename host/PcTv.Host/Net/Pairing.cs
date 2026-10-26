using System.Security.Cryptography;
using System.Text.Json;

namespace PcTv.Host.Net;

/// <summary>
/// Paired TVs persisted in the data dir. `pctv pair &lt;PIN&gt;` writes pending-pin.txt;
/// the running server compares it against PAIR_BEGIN.pin.
/// </summary>
public sealed class Pairing
{
    readonly string _tokensPath = Path.Combine(Config.DataDir, "paired.json");
    readonly string _pinPath = Path.Combine(Config.DataDir, "pending-pin.txt");
    readonly object _lock = new();

    public static void SetPendingPin(string pin)
    {
        Directory.CreateDirectory(Config.DataDir);
        File.WriteAllText(Path.Combine(Config.DataDir, "pending-pin.txt"), pin.Trim());
    }

    Dictionary<string, string> Load() =>
        File.Exists(_tokensPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_tokensPath)) ?? new()
            : new();

    public bool IsValid(string tvId, string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        lock (_lock)
            return Load().TryGetValue(tvId, out var t) &&
                   CryptographicOperations.FixedTimeEquals(
                       System.Text.Encoding.UTF8.GetBytes(t), System.Text.Encoding.UTF8.GetBytes(token));
    }

    /// <summary>Returns a new token if the PIN matches the one the owner entered, else null.</summary>
    public string? TryPair(string tvId, string pin)
    {
        lock (_lock)
        {
            if (!File.Exists(_pinPath)) return null;
            if (File.ReadAllText(_pinPath).Trim() != pin.Trim()) return null;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var all = Load();
            all[tvId] = token;
            File.WriteAllText(_tokensPath, JsonSerializer.Serialize(all));
            File.Delete(_pinPath);
            return token;
        }
    }
}
