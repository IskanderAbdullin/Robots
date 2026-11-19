using System.Text.Json;

namespace MarketMakerSPBStandalone.Core;

public sealed record ExecutedFill(
    string ClientId, string TradeId, Side Side, decimal Price, long Lots,
    DateTimeOffset TimeUtc, decimal Commission = 0m);

/// <summary>
/// Increments in signed cash and exposure relative to holding the starting account inventory.
/// Marks are informational, not tax P&L; commission must be supplied or remains zero.
/// </summary>
public sealed class FillLedger
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<ExecutedFill> _fills = new();
    public IReadOnlyList<ExecutedFill> Fills => _fills;
    public long NetLots { get; private set; }
    public decimal CashFlowPerUnit { get; private set; }
    public decimal Commission { get; private set; }

    public bool Contains(ExecutedFill fill) => _seen.Contains(Key(fill));

    private static string Key(ExecutedFill fill) => fill.ClientId + "\u001f" + fill.TradeId;

    public bool Apply(ExecutedFill fill)
    {
        if (string.IsNullOrWhiteSpace(fill.ClientId) || string.IsNullOrWhiteSpace(fill.TradeId) ||
            fill.Price <= 0 || fill.Lots <= 0 || fill.Commission < 0)
            throw new ArgumentException("Invalid fill.");
        if (!_seen.Add(Key(fill))) return false;
        long sign = fill.Side == Side.Buy ? 1 : -1;
        NetLots = checked(NetLots + checked(sign * fill.Lots));
        CashFlowPerUnit -= sign * fill.Price * fill.Lots;
        Commission += fill.Commission;
        _fills.Add(fill);
        return true;
    }

    public decimal IncrementalMarkedResult(decimal markPrice, decimal lotMultiplier)
    {
        if (markPrice <= 0 || lotMultiplier <= 0) throw new ArgumentOutOfRangeException(nameof(markPrice));
        return (CashFlowPerUnit + NetLots * markPrice) * lotMultiplier - Commission;
    }
}

/// <summary>Durable ledger of unique trade IDs, independent from the order-intent journal.</summary>
public sealed class DurableFillLedger : IDisposable
{
    private readonly FileStream _file;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public FillLedger State { get; }

    public DurableFillLedger(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        State = Recover(path);
        _file = new FileStream(full, FileMode.Append, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough);
    }

    public void Record(ExecutedFill fill)
    {
        lock (_lock)
        {
            if (State.Contains(fill)) return;
            // Crash-safe idempotent accounting: write and fsync before mutable state.
            byte[] line = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fill, Json) + "\n");
            _file.Write(line);
            _file.Flush(flushToDisk: true);
            State.Apply(fill);
        }
    }

    public static FillLedger Recover(string path)
    {
        var ledger = new FillLedger();
        if (!File.Exists(path)) return ledger;
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            ExecutedFill fill;
            try { fill = JsonSerializer.Deserialize<ExecutedFill>(line, Json)
                ?? throw new InvalidDataException("Null fill."); }
            catch (JsonException error)
            {
                throw new InvalidDataException("Damaged fill journal; stop until repaired.", error);
            }
            ledger.Apply(fill);
        }
        return ledger;
    }

    public void Dispose() => _file.Dispose();
}
