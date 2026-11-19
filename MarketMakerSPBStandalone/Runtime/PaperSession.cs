using MarketMakerSPBStandalone.Core;

namespace MarketMakerSPBStandalone.Runtime;

/// <summary>Offline only. Never communicates with a broker or a real exchange.</summary>
public sealed class PaperBroker
{
    private readonly Dictionary<string, PaperOrder> _orders = new(StringComparer.Ordinal);
    public readonly record struct Report(string ClientId, ManagedOrderState State, long Filled);
    private sealed class PaperOrder
    {
        public required OrderCommand Command;
        public long Filled;
        public ManagedOrderState State = ManagedOrderState.Working;
    }

    public Report Submit(OrderCommand command)
    {
        if (command.Kind != CommandKind.Place || command.Lots <= 0 || command.Price <= 0)
            throw new ArgumentException("Invalid paper order.", nameof(command));
        if (_orders.TryGetValue(command.ClientId, out var duplicate))
        {
            if (duplicate.Command != command) throw new InvalidOperationException("Idempotency key collision.");
            return Snapshot(command.ClientId);
        }
        _orders.Add(command.ClientId, new PaperOrder { Command = command });
        return Snapshot(command.ClientId);
    }

    public Report Cancel(string id)
    {
        if (!_orders.TryGetValue(id, out var order)) throw new InvalidOperationException("Unknown paper order.");
        if (order.State is not (ManagedOrderState.Done or ManagedOrderState.Cancelled))
            order.State = ManagedOrderState.Cancelled;
        return Snapshot(id);
    }

    public Report Fill(string id, long additionalLots)
    {
        if (!_orders.TryGetValue(id, out var order) || additionalLots <= 0 ||
            order.State is ManagedOrderState.Cancelled or ManagedOrderState.Done ||
            additionalLots > order.Command.Lots - order.Filled)
            throw new InvalidOperationException("Invalid paper execution.");
        order.Filled += additionalLots;
        order.State = order.Filled == order.Command.Lots ? ManagedOrderState.Done : ManagedOrderState.Partial;
        return Snapshot(id);
    }

    public Report Snapshot(string id)
    {
        if (!_orders.TryGetValue(id, out var order)) throw new InvalidOperationException("Unknown paper order.");
        return new Report(id, order.State, order.Filled);
    }

    public bool TrySnapshot(string id, out Report report)
    {
        if (_orders.ContainsKey(id)) { report = Snapshot(id); return true; }
        report = default;
        return false;
    }
}

/// <summary>
/// Minimal paper-mode orchestration. Not ready for a live adapter: live operation needs
/// buying-power/position reservations and streaming resynchronization as specified in SPEC.
/// </summary>
public sealed class PaperSession : IDisposable
{
    private readonly DurableOrderJournal _journal;
    private readonly PaperBroker _broker;
    public OrderReconciler Orders { get; }

    public PaperSession(string journalPath, PaperBroker broker)
    {
        _broker = broker;
        Orders = DurableOrderJournal.Recover(journalPath);
        _journal = new DurableOrderJournal(journalPath);
    }

    public void Audit()
    {
        foreach (var order in Orders.Orders.Where(x => !x.Terminal))
        {
            // Missing order does NOT prove rejection; retain UNKNOWN and block new orders.
            if (!_broker.TrySnapshot(order.ClientId, out var report)) continue;
            Report(report);
        }
    }

    public IReadOnlyList<OrderCommand> Tick(IReadOnlyList<Quote> desired, MarketSafety safety)
    {
        var commands = Orders.Reconcile(desired, safety);
        foreach (var command in commands)
        {
            if (command.Kind == CommandKind.Place) _journal.RecordIntent(command);
            else _journal.RecordCancellation(command);
            try
            {
                var report = command.Kind == CommandKind.Place
                    ? _broker.Submit(command)
                    : _broker.Cancel(command.ClientId);
                Report(report);
            }
            catch (Exception)
            {
                // A transport failure leaves the broker status ambiguous.
                Orders.MarkUnknown(command.ClientId);
                throw;
            }
        }
        return commands;
    }

    public void Report(PaperBroker.Report report)
    {
        // Durable before applying state, otherwise restart may reuse a stale reservation.
        _journal.RecordReport(report.ClientId, report.State, report.Filled);
        Orders.OnBrokerReport(report.ClientId, report.State, report.Filled);
    }

    public void Dispose() => _journal.Dispose();
}
