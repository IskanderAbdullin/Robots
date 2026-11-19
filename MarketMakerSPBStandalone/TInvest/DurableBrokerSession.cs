using MarketMakerSPBStandalone.Core;

namespace MarketMakerSPBStandalone.TInvest;

/// <summary>Authenticated order API; implementations must make failed/timeout sends explicitly ambiguous.</summary>
public interface IOrderGateway
{
    Task<TInvestOrderReport> PostAsync(OrderCommand command, CancellationToken token = default);
    Task RequestCancelAsync(string exchangeId, string clientId, CancellationToken token = default);
    Task<TInvestOrderReport> ReadStateAsync(string exchangeId, string clientId, CancellationToken token = default);
    Task<IReadOnlyList<TInvestOrderReport>> ReadActiveAsync(CancellationToken token = default);
    Task<TInvestBuyingPower> ReadBuyingPowerAsync(CancellationToken token = default);
}

/// <summary>
/// Serializes engine decisions with broker reports, journals placement intent before sending,
/// and treats all RPC failures as potentially executed. No background threads are started.
/// </summary>
public sealed class DurableBrokerSession : IDisposable
{
    private readonly IOrderGateway _broker;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Runtime.DurableOrderJournal _journal;
    private readonly Dictionary<string, string> _exchangeIds = new(StringComparer.Ordinal);
    public OrderReconciler Orders { get; }
    public bool Audited { get; private set; }

    public DurableBrokerSession(string journalPath, IOrderGateway broker)
    {
        _broker = broker;
        Orders = Runtime.DurableOrderJournal.Recover(journalPath);
        _journal = new Runtime.DurableOrderJournal(journalPath);
    }

    public async Task<bool> AuditAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            Audited = false;
            var active = await _broker.ReadActiveAsync(token);
            // Until foreign activity is attributed to account reservations, do not trade in
            // the same instrument alongside another robot or a human's active orders.
            if (active.Any(x => !Orders.Orders.Any(o => o.ClientId == x.ClientId)))
                return false;
            foreach (var order in Orders.Orders.Where(x => !x.Terminal))
            {
                // A missing active order is NOT proof that it was cancelled or rejected.
                // Read the authoritative order state using its idempotency key.
                try
                {
                    var state = await _broker.ReadStateAsync(
                        _exchangeIds.GetValueOrDefault(order.ClientId) ?? "", order.ClientId, token);
                    ApplyReport(state);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { return false; }
            }
            Audited = Orders.Orders.All(o => o.Terminal ||
                o.State is ManagedOrderState.Working or ManagedOrderState.Partial);
            return Audited;
        }
        finally { _gate.Release(); }
    }

    public async Task OnStreamReportAsync(TInvestOrderReport report,
        CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (Orders.Find(report.ClientId) == null) { Audited = false; return; }
            ApplyReport(report);
        }
        finally { _gate.Release(); }
    }

    private void ApplyReport(TInvestOrderReport report)
    {
        if (!string.IsNullOrWhiteSpace(report.ExchangeId))
            _exchangeIds[report.ClientId] = report.ExchangeId;
        _journal.RecordReport(report.ClientId, report.State, report.FilledLots);
        Orders.OnBrokerReport(report.ClientId, report.State, report.FilledLots);
    }

    public async Task<IReadOnlyList<OrderCommand>> TickAsync(
        IReadOnlyList<Quote> targets, MarketSafety safety, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            // A caller cannot simply set OrdersSynchronized=true without successful audit.
            safety = safety with { OrdersSynchronized = safety.OrdersSynchronized && Audited };
            var commands = Orders.Reconcile(targets, safety);
            foreach (var command in commands)
            {
                if (command.Kind == CommandKind.Place) _journal.RecordIntent(command);
                else _journal.RecordCancellation(command);
                try
                {
                    if (command.Kind == CommandKind.Place)
                    {
                        var state = await _broker.PostAsync(command, token);
                        ApplyReport(state);
                    }
                    else
                    {
                        await _broker.RequestCancelAsync(
                            _exchangeIds.GetValueOrDefault(command.ClientId) ?? "",
                            command.ClientId, token);
                        // RPC only means accepted: the side stays CancelPending.
                    }
                }
                catch (Exception)
                {
                    // Even an interrupted request can have reached the exchange.
                    Orders.MarkUnknown(command.ClientId);
                    Audited = false;
                    throw;
                }
            }
            return commands;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _journal.Dispose();
        _gate.Dispose();
    }
}
