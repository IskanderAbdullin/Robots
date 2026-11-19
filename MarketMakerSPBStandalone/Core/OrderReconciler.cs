namespace MarketMakerSPBStandalone.Core;

public enum ManagedOrderState { PendingNew, Working, Partial, CancelPending, Unknown, Done, Cancelled, Rejected }
public sealed class ManagedOrder
{
    public required string ClientId { get; init; }
    public required Side Side { get; init; }
    public required decimal Price { get; init; }
    public required long Lots { get; init; }
    public long FilledLots { get; private set; }
    public ManagedOrderState State { get; private set; } = ManagedOrderState.PendingNew;
    public bool Terminal => State is ManagedOrderState.Done or ManagedOrderState.Cancelled or ManagedOrderState.Rejected;
    public long Outstanding => Terminal ? 0 : Math.Max(0, Lots - FilledLots);

    public void Apply(ManagedOrderState state, long cumulativeFilled)
    {
        if (cumulativeFilled < 0 || cumulativeFilled > Lots)
            throw new ArgumentOutOfRangeException(nameof(cumulativeFilled));
        // Event replay must never roll back confirmed executions.
        FilledLots = Math.Max(FilledLots, cumulativeFilled);
        if (Terminal) return; // late events do not revive a terminal order
        if (state == ManagedOrderState.Done && FilledLots < Lots)
            throw new InvalidOperationException("Done without full execution");
        // Acknowledge broker events only. Unknown never means cancelled.
        State = state;
    }
}
public enum CommandKind { Place, Cancel }
public sealed record OrderCommand(CommandKind Kind, string ClientId, Side Side, decimal Price, long Lots);
public sealed record MarketSafety(bool Enabled, bool Connected, bool TradingAllowed,
    bool BookFresh, bool PortfolioFresh, bool OrdersSynchronized)
{
    public bool CanCreate => Enabled && Connected && TradingAllowed &&
        BookFresh && PortfolioFresh && OrdersSynchronized;
}
/// <summary>
/// Single-writer incremental reconciler. Call from an event-loop; send returned commands
/// only after intent has been journaled durably. Failed submissions MUST become Unknown.
/// </summary>
public sealed class OrderReconciler
{
    private readonly Dictionary<string, ManagedOrder> _orders = new();
    private bool _failureStop;
    /// <summary>Any definitive broker rejection stops new quotes until manual intervention.</summary>
    public bool FailureStop => _failureStop;
    public IReadOnlyCollection<ManagedOrder> Orders => _orders.Values;
    public ManagedOrder? Find(string id) => _orders.GetValueOrDefault(id);

    public void OnBrokerReport(string id, ManagedOrderState state, long cumulativeFilled)
    {
        if (!_orders.TryGetValue(id, out var order))
            throw new InvalidOperationException("Unknown id requires broker reconciliation: " + id);
        order.Apply(state, cumulativeFilled);
        if (state == ManagedOrderState.Rejected) _failureStop = true;
    }

    /// <summary>On ambiguous transport errors, block until authoritative broker audit.</summary>
    public void MarkUnknown(string id)
    {
        if (!_orders.TryGetValue(id, out var order))
            throw new InvalidOperationException("Unknown local order: " + id);
        if (!order.Terminal) order.Apply(ManagedOrderState.Unknown, order.FilledLots);
    }

    /// <summary>Reload a durable order intent in UNKNOWN state pending broker reconciliation.</summary>
    public void RestoreUnknown(string id, Side side, decimal price, long lots, long filledLots = 0)
    {
        if (string.IsNullOrWhiteSpace(id) || price <= 0 || lots <= 0 || filledLots < 0 || filledLots > lots)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (_orders.ContainsKey(id)) throw new InvalidOperationException("Duplicate order id.");
        var order = new ManagedOrder { ClientId = id, Side = side, Price = price, Lots = lots };
        order.Apply(ManagedOrderState.Unknown, filledLots);
        _orders.Add(id, order);
    }

    public IReadOnlyList<OrderCommand> Reconcile(
        IReadOnlyList<Quote> desired, MarketSafety safety)
    {
        var commands = new List<OrderCommand>();
        if (_failureStop) safety = safety with { Enabled = false };
        // Safe shutdown: cancel every known live order; unknown/pending cannot be assumed cancellable.
        foreach (var order in _orders.Values)
        {
            if (order.Terminal || order.State == ManagedOrderState.CancelPending) continue;
            if (!safety.Enabled || !safety.Connected || !safety.TradingAllowed ||
                !safety.BookFresh || !safety.PortfolioFresh || !safety.OrdersSynchronized ||
                order.State == ManagedOrderState.Working || order.State == ManagedOrderState.Partial)
            {
                var target = desired.Where(q => q.Side == order.Side && q.Price == order.Price)
                    .Sum(q => q.Lots);
                var sameSide = _orders.Values.Where(x => !x.Terminal && x.Side == order.Side &&
                    x.Price == order.Price).Sum(x => x.Outstanding);
                var invalid = !safety.CanCreate || target == 0 || sameSide > target;
                // Never cancel an unchanged useful quote merely to move it in a pro-rata book.
                if (invalid && safety.Connected &&
                    order.State is (ManagedOrderState.Working or ManagedOrderState.Partial))
                {
                    order.Apply(ManagedOrderState.CancelPending, order.FilledLots);
                    commands.Add(new OrderCommand(CommandKind.Cancel, order.ClientId, order.Side, order.Price, 0));
                }
            }
        }
        if (!safety.CanCreate) return commands;
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            var active = _orders.Values.Where(o => !o.Terminal && o.Side == side).ToList();
            // Pending, unknown and cancellation-in-flight block ALL new orders on this side.
            if (active.Any(o => o.State is ManagedOrderState.PendingNew or ManagedOrderState.Unknown or ManagedOrderState.CancelPending))
                continue;
            var targets = desired.Where(x => x.Side == side && x.Lots > 0 && x.Price > 0).ToList();
            var activePrices = active.Select(o => o.Price).Distinct().ToHashSet();
            var targetPrices = targets.Select(o => o.Price).ToHashSet();
            if (activePrices.Any(x => !targetPrices.Contains(x))) continue; // cancellation barrier
            foreach (var quote in targets)
            {
                var current = active.Where(o => o.Price == quote.Price).Sum(o => o.Outstanding);
                var delta = Math.Max(0, quote.Lots - current);
                if (delta == 0) continue;
                // Prevent self-cross even with unmatched live orders, pending and cancels.
                if (_orders.Values.Any(o => !o.Terminal && o.Side != side &&
                    (side == Side.Buy ? quote.Price >= o.Price : quote.Price <= o.Price)))
                    continue;
                var id = Guid.NewGuid().ToString("D");
                var managed = new ManagedOrder { ClientId = id, Side = side, Price = quote.Price, Lots = delta };
                _orders.Add(id, managed);
                commands.Add(new OrderCommand(CommandKind.Place, id, side, quote.Price, delta));
                // Reserve newly-submitted amount immediately; a second pass won't duplicate.
            }
        }
        return commands;
    }
}
