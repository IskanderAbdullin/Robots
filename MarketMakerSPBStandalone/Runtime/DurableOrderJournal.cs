using System.Text.Json;
using MarketMakerSPBStandalone.Core;

namespace MarketMakerSPBStandalone.Runtime;

/// <summary>Append-only intent and report log. Crash-safe intent must precede the actual send.</summary>
public sealed class DurableOrderJournal : IDisposable
{
    private readonly FileStream _file;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DurableOrderJournal(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        _file = new FileStream(full, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 4096, options: FileOptions.WriteThrough);
    }

    public void RecordIntent(OrderCommand command)
    {
        if (command.Kind != CommandKind.Place || command.Price <= 0 || command.Lots <= 0)
            throw new ArgumentException("Invalid placement intent.", nameof(command));
        Append(new JournalEvent("place", command.ClientId, command.Side, command.Price,
            command.Lots, null, 0));
    }

    public void RecordCancellation(OrderCommand command)
    {
        if (command.Kind != CommandKind.Cancel) throw new ArgumentException("Expected cancellation.", nameof(command));
        Append(new JournalEvent("cancel", command.ClientId, command.Side, command.Price, 0, null, 0));
    }

    public void RecordReport(string id, ManagedOrderState state, long cumulativeFilled) =>
        Append(new JournalEvent("report", id, null, 0, 0, state, cumulativeFilled));

    private void Append(JournalEvent evt)
    {
        var line = JsonSerializer.Serialize(evt, Json) + "\n";
        lock (_gate)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(line);
            _file.Write(bytes);
            _file.Flush(flushToDisk: true);
        }
    }

    /// <summary>Read only a journal that is not currently open for writing.</summary>
    public static OrderReconciler Recover(string path)
    {
        var engine = new OrderReconciler();
        if (!File.Exists(path)) return engine;
        var reports = new List<JournalEvent>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JournalEvent evt;
            try { evt = JsonSerializer.Deserialize<JournalEvent>(line, Json)
                ?? throw new InvalidDataException("Null record."); }
            catch (JsonException error)
            {
                // A torn last line means we cannot prove the order state. Stop, do not trade.
                throw new InvalidDataException("Damaged journal; manual reconciliation required.", error);
            }
            if (evt.Kind == "place")
            {
                if (!ids.Add(evt.ClientId)) throw new InvalidDataException("Duplicated client id.");
                if (evt.Side == null) throw new InvalidDataException("Side missing.");
                engine.RestoreUnknown(evt.ClientId, evt.Side.Value, evt.Price, evt.Lots);
            }
            else if (evt.Kind == "report") reports.Add(evt);
            else if (evt.Kind != "cancel") throw new InvalidDataException("Unexpected journal record.");
        }
        foreach (var evt in reports)
        {
            if (evt.State == null) throw new InvalidDataException("Report state missing.");
            engine.OnBrokerReport(evt.ClientId, evt.State.Value, evt.Filled);
        }
        // Even previously WORKING orders are unknown until broker audit after restart.
        foreach (var order in engine.Orders)
            if (!order.Terminal) engine.MarkUnknown(order.ClientId);
        return engine;
    }

    public void Dispose() => _file.Dispose();
}

public sealed record JournalEvent(
    string Kind,
    string ClientId,
    Side? Side,
    decimal Price,
    long Lots,
    ManagedOrderState? State,
    long Filled);
